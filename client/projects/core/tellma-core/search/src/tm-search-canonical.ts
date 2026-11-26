// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import { Injector, signal, untracked, type Signal } from '@angular/core';
import {
  TmLocalization,
  TmTelemetry,
  TmUrls,
  type TmColumnInput,
  type TmColumnPlan,
  type TmColumnSource,
  type TmSearchQuery,
  type TmTelemetryItem,
} from '@tellma/core';
import { tmFilterLeaf, tmFormatFilter, tmParseFilter, tmSelectPlan } from '@tellma/core/entity';
import { tmStripBidiControls } from '@tellma/core-ui/l10n';

import {
  TM_SEARCH_DEFAULT_PAGE_SIZE,
  tmSearchColumnInput,
  tmSearchDefaultColumns,
  tmSearchFirstTab,
  tmSearchIsActivatable,
  tmSearchIsExpression,
  tmSearchIsTree,
  tmSearchMaxPage,
  tmSearchPageSizes,
  tmSearchPathKeys,
  tmSearchSameColumns,
  tmSearchWidthView,
  type TmSearchSetting,
  type TmSearchState,
} from './tm-search-model';

// The canonical query (spec 0030 §4.1, §4.3): the query state with every
// value the entity cannot have dropped and every code default left out, so
// equal states print one URL. Column keys take the entity's canonical case.
// A column key that does not resolve or that the user may not read drops
// through the select plan, which reports it; an `f.` value its control
// rejects, and an order on a column that cannot order, drop here with
// `reason: 'invalid-value'`, and an order on an expression column with
// `reason: 'expression'`. Each drop is reported once, where the value enters
// the state: a step that only re-reads a state plans under a quiet setting.

/** A query field's mutable form, for building the canonical query in the codec's order. */
type TmMutableQuery = { -readonly [K in keyof TmSearchQuery]: TmSearchQuery[K] };

/** Why a query value dropped, beyond the select plan's reasons. */
type TmSearchDropReason = 'invalid-value' | 'expression';

/** Telemetry that accepts every item and keeps none, for planning a state again without reporting its drops again. */
class TmSearchQuietTelemetry extends TmTelemetry {
  /** Always empty: nothing is recorded. */
  override readonly recent: Signal<readonly TmTelemetryItem[]> = signal<readonly TmTelemetryItem[]>([]).asReadonly();

  /** Accepts the event and drops it. */
  override event(): void {
    // Quiet by design: the drop was reported when the state took it.
  }

  /** Accepts the timing and drops it. */
  override timing(): void {
    // Quiet by design.
  }

  /** Accepts the error and drops it. */
  override error(): void {
    // Quiet by design.
  }

  /** Has nothing to send. */
  override flush(): void {
    // Quiet by design.
  }
}

/** The quiet injector over each page's injector, made once. */
const QUIET_INJECTORS = new WeakMap<Injector, Injector>();

/**
 * The page's setting with telemetry that reports nothing, for a step that
 * plans values the state already holds, or plans a value provisionally, so
 * a drop is reported once.
 *
 * @param setting - The page's setting.
 * @returns The quiet setting.
 */
export function tmSearchQuiet(setting: TmSearchSetting): TmSearchSetting {
  let injector = QUIET_INJECTORS.get(setting.injector);
  if (injector === undefined) {
    injector = Injector.create({ providers: [{ provide: TmTelemetry, useClass: TmSearchQuietTelemetry }], parent: setting.injector });
    QUIET_INJECTORS.set(setting.injector, injector);
  }
  return { ...setting, injector };
}

/**
 * The code default columns as the user may read them: those the access
 * drops left out, planned quietly, as their drops are reported where the
 * columns apply.
 *
 * @param setting - The page's setting.
 * @returns The columns.
 */
export function tmSearchShownDefaults(setting: TmSearchSetting): readonly TmColumnInput<string>[] {
  return tmSearchPlanColumns(tmSearchQuiet(setting), tmSearchDefaultColumns(setting.descriptor, setting.declaration), 'declaration');
}

/**
 * One column key planned against the page's entity: the column it shows,
 * or `null` when it does not resolve or the user may not read it. The plan
 * reports such a key as dropped, under the given source.
 *
 * @param setting - The page's setting.
 * @param key - The column key.
 * @param source - Where the key came from.
 * @returns The column, or `null`.
 */
export function tmSearchColumnPlan(setting: TmSearchSetting, key: string, source: TmColumnSource): TmColumnPlan | null {
  return tmSelectPlan(setting.descriptor, [key], [], { injector: setting.injector, source }).columns[0] ?? null;
}

/**
 * The columns of a list that the entity can show the user, each path in its
 * canonical case and read as its declared column. A key that does not
 * resolve or that the user may not read drops, and the select plan reports
 * it under the given source.
 *
 * @param setting - The page's setting.
 * @param inputs - The column inputs.
 * @param source - Where they came from.
 * @returns The columns.
 */
export function tmSearchPlanColumns(
  setting: TmSearchSetting,
  inputs: readonly TmColumnInput<string>[],
  source: TmColumnSource,
): readonly TmColumnInput<string>[] {
  const plan = tmSelectPlan(setting.descriptor, inputs, [], { injector: setting.injector, source });
  return plan.columns.map((column) => (tmSearchIsExpression(column.input) ? column.input : tmSearchColumnInput(setting.declaration, column.key)));
}

/**
 * Reports a query value that dropped as `tm.entity.column-dropped`.
 *
 * @param setting - The page's setting.
 * @param reason - Why it dropped.
 */
function drop(setting: TmSearchSetting, reason: TmSearchDropReason): void {
  const telemetry = setting.injector.get(TmTelemetry);
  untracked(() => telemetry.event('tm.entity.column-dropped', { reason, source: 'url' }));
}

/**
 * The search text as a query carries it: bidi controls stripped, trimmed,
 * and cut to the entity's longest search without splitting a character.
 *
 * @param text - The text.
 * @param max - The longest search, in UTF-16 code units.
 * @returns The text.
 */
export function tmSearchText(text: string, max: number): string {
  const trimmed = tmStripBidiControls(text).trim();
  if (trimmed.length <= max) {
    return trimmed;
  }
  // A cut between a surrogate pair's halves drops the high half too.
  const cut = trimmed.slice(0, max);
  const last = cut.charCodeAt(cut.length - 1);
  return (last >= 0xd800 && last <= 0xdbff ? cut.slice(0, -1) : cut).trimEnd();
}

/**
 * The column filters a query keeps, by canonical key in ordinal order:
 * each key that resolves to a column that filters, with a value its control
 * accepts, rewritten in its canonical form.
 *
 * @param setting - The page's setting.
 * @param filters - The filters, by key.
 * @returns The kept filters, or `undefined` for none.
 */
function columnFilters(setting: TmSearchSetting, filters: Readonly<Record<string, string>>): Readonly<Record<string, string>> | undefined {
  const timeZone = setting.injector.get(TmLocalization).timeZone();
  const kept: [string, string][] = [];
  for (const key of Object.keys(filters).sort(ordinal)) {
    const column = tmSearchColumnPlan(setting, key, 'url');
    if (column === null) {
      continue;
    }
    const spec = tmParseFilter(filters[key]!);
    if (spec === null || column.filter === null || tmFilterLeaf(column, spec, { timeZone }) === null) {
      drop(setting, 'invalid-value');
      continue;
    }
    // Two spellings of one key keep the first, in the codec's order.
    if (!kept.some(([other]) => other === column.key)) {
      kept.push([column.key, tmFormatFilter(spec)]);
    }
  }
  kept.sort(([a], [b]) => ordinal(a, b));
  return kept.length > 0 ? Object.fromEntries(kept) : undefined;
}

/**
 * The custom filters a query keeps, in key order: each declared switch that
 * is on and each declared option group set to one of its options.
 *
 * @param setting - The page's setting.
 * @param custom - The custom filters' values, by key.
 * @returns The kept values, or `undefined` for none.
 */
function customFilters(setting: TmSearchSetting, custom: Readonly<Record<string, string>>): Readonly<Record<string, string>> | undefined {
  const kept: [string, string][] = [];
  for (const declared of setting.declaration.filters ?? []) {
    const value = custom[declared.key];
    if (value === undefined) {
      continue;
    }
    const valid = 'options' in declared ? declared.options.some((option) => option.value === value) : value === '1';
    if (valid) {
      kept.push([declared.key, value]);
    }
  }
  kept.sort(([a], [b]) => ordinal(a, b));
  return kept.length > 0 ? Object.fromEntries(kept) : undefined;
}

/**
 * The order a query keeps: a key that resolves to a column that can order,
 * in its canonical case, with its direction. An expression column never
 * orders, as an order travels in the URL and the URL carries paths only.
 *
 * @param setting - The page's setting.
 * @param order - The order, `Key` or `Key:desc`.
 * @param columns - The shown columns.
 * @returns The kept order, or `undefined`.
 */
function keptOrder(setting: TmSearchSetting, order: string, columns: readonly TmColumnInput<string>[]): string | undefined {
  const desc = order.endsWith(':desc');
  const key = desc ? order.slice(0, -5) : order;
  if (columns.some((column) => tmSearchIsExpression(column) && column.expression === key)) {
    drop(setting, 'expression');
    return undefined;
  }
  const column = tmSearchColumnPlan(setting, key, 'url');
  if (column === null) {
    return undefined;
  }
  if (column.orderBy('asc') === null) {
    drop(setting, 'invalid-value');
    return undefined;
  }
  return desc ? `${column.key}:desc` : column.key;
}

/**
 * The canonical form of a query state: each value the entity cannot have
 * dropped, each column key in its canonical case, and each code default
 * left out. `cols` holds the shown columns only when they differ from the
 * code default and include no expression column.
 *
 * @param setting - The page's setting.
 * @param query - The query state.
 * @param columns - The shown columns.
 * @returns The canonical query.
 */
export function tmSearchCanonical(setting: TmSearchSetting, query: TmSearchQuery, columns: readonly TmColumnInput<string>[]): TmSearchQuery {
  const { descriptor, declaration, mode } = setting;
  const canonical: TmMutableQuery = {};

  // --- The fields in the codec's order, each checked against the entity ---
  const first = tmSearchFirstTab(declaration);
  if (query.tab !== undefined && query.tab !== first?.key && declaration.tabs?.some((tab) => tab.key === query.tab)) {
    canonical.tab = query.tab;
  }
  const q = tmSearchText(query.q ?? '', descriptor.limits.maxSearchLength);
  if (q !== '') {
    canonical.q = q;
  }
  const filters = query.columnFilters && columnFilters(setting, query.columnFilters);
  if (filters !== undefined) {
    canonical.columnFilters = filters;
  }
  // A drill-through list's `#d=` is its payload, so it has no filter expression and no Active only.
  if (query.filter !== undefined && query.filter.trim() !== '' && mode !== 'list') {
    canonical.filter = query.filter;
  }
  if (query.inactive === true && tmSearchIsActivatable(descriptor) && mode !== 'list') {
    canonical.inactive = true;
  }
  const custom = query.custom && customFilters(setting, query.custom);
  if (custom !== undefined) {
    canonical.custom = custom;
  }
  const kept = query.order === undefined ? undefined : keptOrder(setting, query.order, columns);
  if (kept !== undefined) {
    canonical.order = kept;
  }

  // --- The columns, only when they travel ---
  // The default is compared as the user may read it, so a default column
  // the user's access drops does not pin the others in the URL.
  const paths = tmSearchPathKeys(columns);
  if (paths !== null && paths.length > 0 && !tmSearchSameColumns(columns, tmSearchShownDefaults(setting))) {
    canonical.cols = paths;
  }

  // --- Paging within the entity's limits, then the view and the list ---
  const size = query.size !== undefined && tmSearchPageSizes(descriptor).includes(query.size) ? query.size : TM_SEARCH_DEFAULT_PAGE_SIZE;
  const page = query.page !== undefined && query.page <= tmSearchMaxPage(descriptor, size) ? query.page : 1;
  if (page !== 1) {
    canonical.page = page;
  }
  if (size !== TM_SEARCH_DEFAULT_PAGE_SIZE) {
    canonical.size = size;
  }
  if (query.view !== undefined && query.view !== tmSearchWidthView(setting.width)) {
    canonical.view = query.view;
  }
  if (query.list === true && tmSearchIsTree(descriptor)) {
    canonical.list = true;
  }
  return canonical;
}

/**
 * The canonical URL of a query state: the search page's, or in list mode
 * the drill-through list's with its payload, which carries the fixed filter
 * and the columns. A picker's state has none, and nor has a list's whose
 * payload is not valid: the address bar keeps the link as it came, so it
 * never turns into an unfiltered list's URL.
 *
 * @param setting - The page's setting.
 * @param state - The state.
 * @returns The URL, such as `/17/centers?q=jed`; empty, for nothing to write, in picker mode and for a list's invalid payload.
 */
export function tmSearchUrl(setting: TmSearchSetting, state: TmSearchState): string {
  const urls = setting.urls ?? setting.injector.get(TmUrls);
  if (state.payloadInvalid) {
    return '';
  }
  switch (setting.mode) {
    case 'picker':
      return '';
    case 'list':
      return urls.list(
        setting.segment ?? setting.descriptor.segment ?? '',
        state.query.cols === undefined ? { filter: state.listFilter ?? null } : { filter: state.listFilter ?? null, cols: state.query.cols },
        state.query,
      );
    default:
      return urls.search(setting.descriptor.name, state.query);
  }
}

/** Compares two strings ordinally, as the codec orders `f.*` and `x.*`. */
function ordinal(a: string, b: string): number {
  return a < b ? -1 : a > b ? 1 : 0;
}
