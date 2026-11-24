// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import { TmLocalization, type TmId, type TmJsonObject } from '@tellma/core';
import { tmFilterLeaf, tmParseFilter, tmSelectPlan, type TmSelectPlan } from '@tellma/core/entity';

import { tmSearchColumnPlan, tmSearchQuiet } from './tm-search-canonical';
import {
  tmSearchEffective,
  tmSearchIsActivatable,
  tmSearchMaxPage,
  tmSearchTilePaths,
  tmSearchTreeKeys,
  type TmSearchSetting,
  type TmSearchState,
} from './tm-search-model';

// The rows request of a query state (spec 0030 §5.1, §16.2, §16.4): the
// select plan over the shown columns with the tile's and the tree's paths,
// one Queryex leaf per source in a fixed order (the tab, the advanced
// filter, the custom filters, the column filters, the fixed leaves), the
// named filters in the same order, the order, the page and the flags. Each
// leaf's and each name's origin is kept beside the body, so a diagnostic on
// `filters[i]` or `namedFilters[i]` maps back to its control (§5.3).

/** Where one of a request's leaves or named filters came from. */
export type TmSearchClauseOrigin =
  | {
      /** The shown tab's. */
      readonly kind: 'tab';
      /** The tab's key. */
      readonly key: string;
    }
  | {
      /** The advanced filter, verbatim. */
      readonly kind: 'advanced';
    }
  | {
      /** A custom filter's, or its chosen option's. */
      readonly kind: 'custom';
      /** The filter's key. */
      readonly key: string;
      /** The filter's value: `1` for a switch, else the option's value. */
      readonly value: string;
    }
  | {
      /** A column filter's leaf. */
      readonly kind: 'column';
      /** The column's key. */
      readonly key: string;
    }
  | {
      /** A fixed leaf: a picker's filter, or a drill-through list's. */
      readonly kind: 'fixed';
      /** The leaf's place among the fixed leaves. */
      readonly index: number;
    };

/** What a query state reads: the call, its body, and where each leaf and named filter came from. */
export interface TmSearchRequest {
  /** The tree's form: a browse tree loads by parent, a search tree reads matches with their ancestors; `null` for flat rows. */
  readonly tree: 'browse' | 'search' | null;
  /** The route, such as `centers/query` or `centers/get-by-parent-ids`. */
  readonly route: string;
  /** The body. */
  readonly body: TmJsonObject;
  /** The origin of each of the body's `filters`, in order. */
  readonly filters: readonly TmSearchClauseOrigin[];
  /** The origin of each of the body's `namedFilters`, in order. */
  readonly namedFilters: readonly TmSearchClauseOrigin[];
}

/** What a request reads beyond the state. */
export interface TmSearchRequestOptions {
  /** A browse tree's remembered expansion, oldest first; the newest within the entity's limit are sent. */
  readonly expanded?: Iterable<TmId>;
}

/**
 * The select plan of a state: the shown columns, with the tile's paths and
 * the tree's keys read beside them but never shown. A tile path or tree key
 * that does not resolve or that the user may not read is reported under
 * the declaration, where it comes from; a shown column the user can no
 * longer read, under the columns' source.
 *
 * @param setting - The page's setting.
 * @param state - The state.
 * @returns The plan.
 */
export function tmSearchPlan(setting: TmSearchSetting, state: TmSearchState): TmSelectPlan {
  const { descriptor, injector } = setting;
  // The declaration's paths are checked on their own, so each drop is reported under its own source.
  const extra = [...tmSearchTilePaths(setting), ...tmSearchTreeKeys(descriptor)];
  const dropped = new Set(tmSelectPlan(descriptor, [], extra, { injector, source: 'declaration' }).dropped.map((drop) => drop.key));
  const kept = extra.filter((path) => !dropped.has(path));
  return tmSelectPlan(descriptor, state.columns, kept, { injector, source: state.columnsFrom });
}

/**
 * The rows request of a state.
 *
 * - **Route.** `{segment}/query`; a browse tree, one with no text, no leaf
 *   and no named filter, `{segment}/get-by-parent-ids` with the roots and
 *   the remembered expansion's children, unpaged.
 * - **Leaves**, in order: the tab's, the advanced filter, the custom
 *   filters in declaration order, the column filters in key order, the
 *   fixed leaves. A source that names a named filter adds it to
 *   `namedFilters` in the same order instead.
 * - **Order.** The order column's in the active language, else the
 *   declaration's order, else the entity's default order.
 * - **Paging.** The page and the size, with the count; the last page within
 *   the entity's skip window takes what is left of it.
 * - **Flags.** Inactive rows when asked for, and always in a drill-through
 *   list; a search tree's ancestors.
 *
 * The state is canonical, so its column keys were reported when they
 * dropped; one the user can no longer read is left out without being
 * reported again.
 *
 * @param setting - The page's setting.
 * @param state - The state.
 * @param plan - The state's select plan.
 * @param options - The remembered expansion of a browse tree.
 * @returns The request.
 * @throws Error when the state is a drill-through list's whose payload is
 *   missing or malformed, which reads nothing.
 */
export function tmSearchRequest(setting: TmSearchSetting, state: TmSearchState, plan: TmSelectPlan, options: TmSearchRequestOptions = {}): TmSearchRequest {
  // A list without its fixed filter would read every row: the page shows the link as not valid instead.
  if (state.payloadInvalid) {
    throw new Error('A drill-through list whose link has no valid filter reads nothing.');
  }
  const { descriptor, declaration, mode } = setting;
  const quiet = tmSearchQuiet(setting);
  const { query } = state;
  const effective = tmSearchEffective(setting, query);
  const filters: string[] = [];
  const filterOrigins: TmSearchClauseOrigin[] = [];
  const named: string[] = [];
  const namedOrigins: TmSearchClauseOrigin[] = [];
  const apply = (source: { readonly queryex?: string; readonly named?: string }, origin: TmSearchClauseOrigin): void => {
    if (source.queryex !== undefined) {
      filters.push(source.queryex);
      filterOrigins.push(origin);
    } else if (source.named !== undefined) {
      named.push(source.named);
      namedOrigins.push(origin);
    }
  };

  // --- The leaves and the named filters, in their order ---
  if (effective.tab !== null) {
    apply(effective.tab, { kind: 'tab', key: effective.tab.key });
  }
  if (query.filter !== undefined && mode !== 'list') {
    apply({ queryex: query.filter }, { kind: 'advanced' });
  }
  for (const declared of declaration.filters ?? []) {
    const value = query.custom?.[declared.key];
    const source = value === undefined ? undefined : 'options' in declared ? declared.options.find((option) => option.value === value) : value === '1' ? declared : undefined;
    if (source !== undefined) {
      apply(source, { kind: 'custom', key: declared.key, value: value! });
    }
  }
  const timeZone = setting.injector.get(TmLocalization).timeZone();
  const columnFilters = query.columnFilters ?? {};
  for (const key of Object.keys(columnFilters).sort((a, b) => (a < b ? -1 : a > b ? 1 : 0))) {
    const value = columnFilters[key]!;
    // A column the user can no longer read since the state resolved is left out.
    const column = tmSearchColumnPlan(quiet, key, 'url');
    const spec = tmParseFilter(value);
    const leaf = column && spec && tmFilterLeaf(column, spec, { timeZone });
    if (leaf) {
      apply({ queryex: leaf }, { kind: 'column', key });
    }
  }
  const fixed = mode === 'list' ? (state.listFilter ? [state.listFilter] : []) : mode === 'picker' ? (setting.fixedLeaves ?? []) : [];
  fixed.forEach((leaf, index) => apply({ queryex: leaf }, { kind: 'fixed', index }));

  // --- The order, the flags and the tree's form ---
  const orderBy = orderOf(quiet, query.order);
  const includeInactive = mode === 'list' || (query.inactive === true && tmSearchIsActivatable(descriptor));
  const tree = !effective.tree ? null : effective.q === '' && filters.length === 0 && named.length === 0 ? 'browse' : 'search';
  const segment = setting.segment ?? descriptor.segment ?? '';

  // --- A browse tree: the roots and the remembered expansion's children, unpaged ---
  if (tree === 'browse') {
    const expanded = [...(options.expanded ?? [])];
    const parentIds = expanded.slice(Math.max(0, expanded.length - descriptor.limits.maxIds));
    return {
      tree,
      route: `${segment}/get-by-parent-ids`,
      body: { parentIds, includeRoots: true, ...(orderBy && { orderBy }), includeInactive, select: plan.select },
      filters: [],
      namedFilters: [],
    };
  }

  // --- A query: the page of matches, counted ---
  const skip = (effective.page - 1) * effective.size;
  const take = effective.page < tmSearchMaxPage(descriptor, effective.size) ? effective.size : Math.min(effective.size, descriptor.limits.maxSkipWindow - skip);
  const body: TmJsonObject = {
    select: plan.select,
    ...(effective.q !== '' && { search: effective.q }),
    ...(filters.length > 0 && { filters }),
    ...(named.length > 0 && { namedFilters: named }),
    ...(orderBy && { orderBy }),
    skip,
    take,
    includeCount: true,
    ...(includeInactive && { includeInactive: true }),
    ...(tree === 'search' && { includeAncestors: true }),
  };
  return { tree, route: `${segment}/query`, body, filters: filterOrigins, namedFilters: namedOrigins };
}

/**
 * The order terms of a state: the order column's in the active language,
 * else the declaration's order, else the entity's default order.
 *
 * @param setting - The page's setting.
 * @param order - The query's order, `Key` or `Key:desc`.
 * @returns The terms, or `undefined` when the entity has no default order.
 */
function orderOf(setting: TmSearchSetting, order: string | undefined): string | undefined {
  const terms = (input: string | undefined, source: 'url' | 'declaration'): string | null => {
    if (input === undefined) {
      return null;
    }
    const desc = input.endsWith(':desc');
    return tmSearchColumnPlan(setting, desc ? input.slice(0, -5) : input, source)?.orderBy(desc ? 'desc' : 'asc') ?? null;
  };
  return terms(order, 'url') ?? terms(setting.declaration.orderBy, 'declaration') ?? (setting.descriptor.defaultOrderBy || undefined);
}
