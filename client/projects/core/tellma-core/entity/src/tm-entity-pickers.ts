// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import { computed, DestroyRef, inject, Injectable, Injector, untracked, type Signal } from '@angular/core';
import {
  TmApi,
  TmApiError,
  TmDisplayNames,
  TmEntities,
  TmLaunchCache,
  TmLocalization,
  TmPermissions,
  TmTelemetry,
  tmRowsToObjects,
  type TmCachedEntitySet,
  type TmDisplayNameParts,
  type TmEntityDescriptor,
  type TmId,
  type TmJson,
  type TmJsonObject,
  type TmPickerDeclaration,
  type TmPropertyDescriptor,
  type TmQueryRowSet,
} from '@tellma/core';
import type { TmLabelResolution, TmPasteContext } from '@tellma/core-ui/contracts';
import type { TmEntityPickerPage, TmEntitySearchFn, TmEntitySearchResult } from '@tellma/core-ui/entity-picker';
import { tmStripBidiControls } from '@tellma/core-ui/l10n';
import { tmSearchNormalize } from '@tellma/core/search-normalize';

import { ɵtmFindable, ɵtmSearchMatches, ɵTM_SEARCH_MATCH, type ɵTmFindablePlan, type ɵTmPickerTerm, type ɵTmSearchColumn } from './tm-findable';
import { TmFormats } from './tm-formats';
import { TmLazyPickerPage, ɵtmRegisterPickerStrings, type TmEntityPickerContext } from './tm-lazy-picker-page';
import { tmSelectPlan, ɵtmDisplayNameOrder, type TmColumnPlan } from './tm-select-plan';

// The generic entity picker binding (spec 0029 §14): one binding, built from
// the target's descriptor, turns the component library's entity picker into
// an entity picker. It searches the target's `query` route (or, for a
// cacheable target with no filter, its cached list in memory), names rows
// by their display names, describes them on a second line, and offers the
// target's pages in modals. Fields, grid cells and filters all spread it.

/** The rows a picker shows of one search. */
const PAGE = 20;

/** The separator between a description's parts. */
const DESCRIPTION_SEPARATOR = ' · ';

/** The most labels a binding memoizes before it starts again. */
const LABELS = 1000;

/**
 * What an entity picker's inputs, and a grid entity column's, are bound to:
 * the search, the item accessors and the target's pages. A template binds
 * each member to the input of the same name; the pages, the create label
 * and the description follow the user's grants and the entity model.
 */
export interface TmEntityPickerBinding {
  /** Searches the target: its query route, or its cached list. */
  readonly search: TmEntitySearchFn<TmJson>;
  /** A row's id. */
  readonly itemId: (item: TmJson) => TmId;
  /** A row's display name in the active language. */
  readonly itemLabel: (item: TmJson) => string;
  /** A row's second line: its description's parts joined by " · "; absent when the target shows none. */
  readonly itemDescription?: (item: TmJson) => string | null;
  /** A row's display name as its parts, the prefix first, so each renders in its own isolate. */
  readonly itemLabelParts: (item: TmJson) => readonly string[];
  /** The display name of a chosen id; `null` while the row is unknown, which starts its read. */
  readonly displayWith: (id: TmId) => string | null;
  /** The Create row's label, such as "New customer…"; absent when Create is not offered. */
  readonly createLabel?: string;
  /** The Edit row's label; the picker's own when absent. */
  readonly editLabel?: string;
  /** The advanced search page; absent when the user may not query the target. */
  readonly advancedSearch?: TmEntityPickerPage;
  /** The page that creates a record; absent when it is not offered. */
  readonly create?: TmEntityPickerPage;
  /** The page that edits the chosen record; absent when it is not offered. */
  readonly edit?: TmEntityPickerPage;
}

/** How a binding searches and what it offers. */
export interface TmEntityPickerOptions {
  /** The field's Queryex filter, applied beside the entity's declared picker filter. */
  readonly filter?: Signal<string | null>;
  /** Whether the picker offers Create, where the entity and the user's grants allow it; `true` when omitted. */
  readonly create?: boolean;
  /** Whether the binding serves a multi-picker, which offers no Edit and opens advanced search on the whole selection. */
  readonly multiple?: boolean;
}

/** Everything a binding reads of its target's descriptor, made once per descriptor. */
interface TmPickerPlan extends ɵTmFindablePlan {
  /** The target's descriptor. */
  readonly descriptor: TmEntityDescriptor;
  /** The target's API segment. */
  readonly segment: string;
  /** The query's select: the key, the display name's columns and the description's. */
  readonly select: string;
  /** The description's columns, in order; empty for none. */
  readonly description: readonly TmColumnPlan[];
  /** The text columns a cached search matches: the name's slots, a text prefix, natural keys and searchable columns, a prefix-searchable one from its start. */
  readonly text: readonly ɵTmSearchColumn[];
  /** The declared picker filter; `null` for none. */
  readonly filter: string | null;
  /** The active flag's wire name, for an activatable target; `null` otherwise. */
  readonly active: string | null;
  /** The results' order in the active slot. */
  readonly order: (slot: number) => string;
  /** Whether the target's list is cached on the client. */
  readonly cacheable: boolean;
}

/**
 * The display name's parts as the picker's array form: `[prefix, name]`,
 * or `[name]` without a prefix.
 *
 * @param parts - The parts.
 * @returns The array.
 */
export function ɵtmLabelParts(parts: TmDisplayNameParts): readonly string[] {
  return parts.prefix === null ? [parts.name] : [parts.prefix, parts.name];
}

/**
 * Whether a failure is the search's own abort, which reports nothing.
 *
 * @param error - The failure.
 * @param signal - The search's signal.
 * @returns Whether it is.
 */
function aborted(error: unknown, signal: AbortSignal): boolean {
  return (
    signal.aborted ||
    (error instanceof DOMException && error.name === 'AbortError') ||
    (error instanceof TmApiError && error.kind === 'cancelled')
  );
}

/**
 * Wire objects at a path: a nested object's member, or `undefined`.
 *
 * @param value - The object.
 * @param member - The member's wire name.
 * @returns The member's value.
 */
function member(value: unknown, member: string): unknown {
  return typeof value === 'object' && value !== null && !Array.isArray(value) ? (value as TmJsonObject)[member] : undefined;
}

/**
 * Entity picker bindings, made from the target's descriptor: searching its
 * query route, or the cached list of a cacheable target with no filter,
 * naming and describing its rows, and offering its pages in modals.
 *
 * @example
 * ```ts
 * readonly parent = inject(TmEntityPickers).binding('gl.Center', { filter: signal("CenterType = 'Abstract'") });
 * ```
 * ```html
 * <tm-entity-picker [(value)]="parentId" [search]="parent.search" [itemId]="parent.itemId"
 *                   [itemLabel]="parent.itemLabel" [itemLabelParts]="parent.itemLabelParts"
 *                   [itemDescription]="parent.itemDescription" [displayWith]="parent.displayWith"
 *                   [advancedSearch]="parent.advancedSearch" [create]="parent.create" [edit]="parent.edit"
 *                   [createLabel]="parent.createLabel" />
 * ```
 */
@Injectable({ providedIn: 'root' })
export class TmEntityPickers {
  readonly #injector = inject(Injector);
  readonly #api = inject(TmApi);
  readonly #entities = inject(TmEntities);
  readonly #names = inject(TmDisplayNames);
  readonly #permissions = inject(TmPermissions);
  readonly #localization = inject(TmLocalization);
  readonly #launch = inject(TmLaunchCache);
  readonly #telemetry = inject(TmTelemetry);
  readonly #formats = inject(TmFormats);
  /** The app's lifetime: a search or a paste that outlives it reads nothing. */
  readonly #app = inject(DestroyRef);

  /**
   * Each descriptor's plan, a descriptor being made once per entity and
   * shape; it is made again when the grants that admit its columns move.
   */
  readonly #plans = new WeakMap<TmEntityDescriptor, Signal<TmPickerPlan>>();

  /** Each row's folded text columns, by plan, for the cached search. */
  readonly #folded = new WeakMap<TmPickerPlan, WeakMap<object, readonly (string | null)[]>>();

  /** Registers the pickers' strings, which the binding and its pages read. */
  constructor() {
    ɵtmRegisterPickerStrings(this.#localization);
  }

  /**
   * A picker binding for an entity: its search, its rows' names and
   * descriptions, and the pages the user may open. Advanced search is
   * offered when the user may query the entity; Create and Edit when they
   * may save it and it has a details page, Create unless turned off by the
   * options or the entity's declaration, and Edit only to a single picker.
   * The search, the description and the pages need `TmEntities.load()`;
   * a search before the load waits for it.
   *
   * @param entity - The entity's name, such as `gl.Center`.
   * @param options - The field's filter, and whether it offers Create and serves a multi-picker.
   * @returns The binding.
   * @throws Error for an entity the metadata does not list.
   */
  binding(entity: string, options: TmEntityPickerOptions = {}): TmEntityPickerBinding {
    const summary = this.#entities.summary(entity);
    if (summary === undefined) {
      throw new Error(`The metadata lists no entity '${entity}'.`);
    }
    const name = summary.name;
    const names = this.#names;
    const allows = (operation: 'query' | 'save' | 'details'): boolean => this.#permissions.allows(name, operation)();
    const fieldFilter = (): string | null => untracked(() => options.filter?.())?.trim() || null;

    // --- What the user is offered: their grants, the details page and the declaration ---
    const offersCreate = (): boolean =>
      options.create !== false && this.#picker(name)?.create !== false && allows('save') && allows('details');
    const offersEdit = (): boolean => options.multiple !== true && allows('save') && allows('details');

    // --- The pages, one shim each; the context is read when the picker launches it ---
    const newLabel = this.#localization.ɵtext(summary.newLabelKey, undefined, summary.labelFallback);
    const createLabel = computed(() => this.#localization.ɵtext('tm.picker.newLabel', { label: newLabel() })());
    const page = (kind: TmEntityPickerContext['kind']): TmEntityPickerPage => ({
      component: TmLazyPickerPage,
      size: 'lg',
      get title(): string | undefined {
        return kind === 'create' ? untracked(newLabel) : undefined;
      },
      get context(): TmEntityPickerContext {
        return { entity: name, filter: fieldFilter(), kind };
      },
    });
    const pages = { search: page('search'), create: page('create'), edit: page('edit') };

    // --- Labels: memoized per id, so a chosen value's name follows the language ---
    const labels = new Map<string, Signal<string | null>>();
    const describe = (item: TmJson): string | null => this.#describe(name, item);
    const hasDescription = (): boolean => this.#hasDescription(name);

    const binding: TmEntityPickerBinding = {
      search: (text, signal) => this.#search(name, text, signal, fieldFilter()),
      itemId: (item) => {
        const plan = this.#entities.loaded() ? this.#plan(name) : null;
        return member(item, plan?.key.json ?? 'id') as TmId;
      },
      itemLabel: (item) => names.of(name, item)(),
      get itemDescription(): ((item: TmJson) => string | null) | undefined {
        // A target with nothing to describe shows one-line rows.
        return hasDescription() ? describe : undefined;
      },
      itemLabelParts: (item) => ɵtmLabelParts(names.parts(name, item)()),
      displayWith: (id) => {
        const key = String(id);
        let label = labels.get(key);
        if (label === undefined) {
          if (labels.size >= LABELS) {
            labels.clear();
          }
          labels.set(key, (label = names.label(name, id)));
        }
        const text = label();
        if (text === null) {
          untracked(() => names.resolve(name, [id]));
        }
        return text;
      },
      get createLabel(): string | undefined {
        return offersCreate() ? createLabel() : undefined;
      },
      get advancedSearch(): TmEntityPickerPage | undefined {
        return allows('query') ? pages.search : undefined;
      },
      get create(): TmEntityPickerPage | undefined {
        return offersCreate() ? pages.create : undefined;
      },
      get edit(): TmEntityPickerPage | undefined {
        return offersEdit() ? pages.edit : undefined;
      },
    };
    return binding;
  }

  /**
   * The paste resolver of a grid's entity column: the pasted labels in as
   * few queries as the server's filter limits allow, one for most pastes,
   * each label matched against the display name's columns of every admitted
   * slot, a text prefix, and the findable rule with equality in place of a
   * prefix match. A label one row matches resolves to it; none, or several,
   * is an error. Its code loads on the first paste. Internal:
   * `tmGridColumnFor` binds it.
   *
   * @param entity - The entity's name.
   * @param filter - The column's Queryex filter.
   * @returns The resolver.
   */
  ɵpasteResolver(
    entity: string,
    filter: Signal<string | null>,
  ): (labels: string[], context: TmPasteContext) => Promise<ReadonlyMap<string, TmLabelResolution<TmId>>> {
    return async (labels, context) => {
      // The resolver's code loads on the first paste, beside the Entities chunk.
      const [{ ɵtmResolveLabels }] = await Promise.all([import('./tm-entity-paste'), this.#entities.load()]);
      // An app that ended meanwhile reads nothing: its injector would throw.
      if (this.#app.destroyed) {
        return new Map();
      }
      const plan = this.#plan(entity);
      const leaves = [plan.filter, untracked(filter)?.trim() || null].filter((leaf): leaf is string => leaf !== null);

      // --- The room the paste's own leaf has beside the other leaves ---
      const room = plan.descriptor.limits.maxFilterLength - leaves.reduce((length, leaf) => length + leaf.length + 5, 0);
      const read = async (pasted: string): Promise<readonly TmJsonObject[]> => {
        const body = { select: plan.select, filters: [...leaves, pasted], take: plan.descriptor.limits.maxTake };
        const page = await this.#api.read<{ readonly rows: TmQueryRowSet }>(`${plan.segment}/query`, body, { signal: context.signal });
        // An app that ended during the read remembers nothing.
        if (this.#app.destroyed) {
          return [];
        }
        const rows = tmRowsToObjects(plan.descriptor, page.rows);
        this.#names.remember(plan.descriptor.name, rows);
        return rows;
      };
      return ɵtmResolveLabels(plan, labels, room, plan.descriptor.limits.maxTake, untracked(this.#localization.locale), read);
    };
  }

  /**
   * An entity's declared picker options.
   *
   * @param entity - The entity's name.
   * @returns The options, or `undefined` when no feature declares any.
   */
  #picker(entity: string): TmPickerDeclaration<string> | undefined {
    return this.#entities.declaration(entity)?.['picker'] as TmPickerDeclaration<string> | undefined;
  }

  /**
   * A target's plan, made once per descriptor.
   *
   * @param entity - The entity's name.
   * @returns The plan.
   */
  #plan(entity: string): TmPickerPlan {
    const descriptor = untracked(() => this.#entities.get(entity));
    let plan = this.#plans.get(descriptor);
    if (plan === undefined) {
      plan = computed(() => this.#makePlan(descriptor));
      this.#plans.set(descriptor, plan);
    }
    return untracked(plan);
  }

  /**
   * Builds a target's plan: the display name's terms, the description's
   * columns and select, the text the cached search matches, and the order.
   *
   * @param descriptor - The target's descriptor.
   * @returns The plan.
   */
  #makePlan(descriptor: TmEntityDescriptor): TmPickerPlan {
    const summary = this.#entities.summary(descriptor.name)!;
    const rule = summary.displayName;
    const declaration = this.#picker(descriptor.name);
    const term = (property: TmPropertyDescriptor): ɵTmPickerTerm => ({ name: property.name, json: property.json, type: property.type });
    const keyProperty = descriptor.property(descriptor.key.property);

    // --- The display name: the group's admitted twins in slot order, or the source; the prefix ---
    const twins = rule.multilingual
      ? descriptor.properties
          .filter((property) => property.multilingual?.group.toLowerCase() === rule.source.toLowerCase())
          .sort((a, b) => a.multilingual!.slot - b.multilingual!.slot)
      : [];
    const sourceProperty = descriptor.property(rule.source);
    const slots = twins.length > 0 ? twins.map(term) : sourceProperty ? [term(sourceProperty)] : [];
    const prefixProperty = rule.prefix === undefined ? undefined : descriptor.property(rule.prefix);
    const prefix = prefixProperty ? term(prefixProperty) : null;
    const shown = new Set([...slots.map((slot) => slot.name.toLowerCase()), ...(prefix ? [prefix.name.toLowerCase()] : [])]);
    const inName = (name: string): boolean => {
      const property = descriptor.property(name);
      return shown.has(name.toLowerCase()) || (rule.multilingual && property?.multilingual?.group.toLowerCase() === rule.source.toLowerCase());
    };

    // --- The description: the declared paths, else the natural keys the name does not show and a tree's parent ---
    let paths: readonly string[];
    if (declaration?.description === false) {
      paths = [];
    } else if (declaration?.description !== undefined) {
      paths = declaration.description;
    } else {
      const parent = descriptor.capabilities.has('tree') ? descriptor.navigations.find((navigation) => navigation.target === descriptor.name && navigation.name === 'Parent') : undefined;
      paths = [...descriptor.naturalKeys.filter((key) => !inName(key)), ...(parent ? [parent.name] : [])];
    }
    const injector = this.#injector;
    const plan = tmSelectPlan(descriptor, paths, this.#names.select(descriptor.name), { injector });

    // --- The text a cached search matches, each column once: a prefix-searchable one from its
    // start, as the server's search does (QuerySearch), every other one anywhere ---
    const prefixed = new Set(descriptor.prefixSearchable?.map((name) => name.toLowerCase()));
    const text = new Map<string, ɵTmSearchColumn>();
    const addText = (property: TmPropertyDescriptor | undefined): void => {
      if (property?.type === 'string' && !text.has(property.name)) {
        text.set(property.name, { ...term(property), prefix: prefixed.has(property.name.toLowerCase()) });
      }
    };
    [...twins, sourceProperty, prefixProperty].forEach(addText);
    [...descriptor.naturalKeys, ...descriptor.searchable].forEach((name) => addText(descriptor.property(name)));

    // --- The order: the declared one, else the display name's ---
    const [orderPath, direction] = (declaration?.orderBy ?? '').split(':');
    const orderColumn = orderPath ? tmSelectPlan(descriptor, [orderPath], [], { injector }).columns[0] : undefined;
    const entities = this.#entities;
    const order = (slot: number): string =>
      orderColumn?.orderBy(direction === 'desc' ? 'desc' : 'asc') ?? ɵtmDisplayNameOrder(descriptor, entities, slot).join(', ');

    return {
      descriptor,
      segment: summary.segment ?? '',
      key: keyProperty ? term(keyProperty) : { name: descriptor.key.property, json: 'id', type: 'integer' },
      slots,
      prefix,
      select: plan.select,
      description: plan.columns,
      text: [...text.values()],
      filter: declaration?.filter?.trim() || null,
      active: descriptor.capabilities.has('activatable') ? (descriptor.property('IsActive')?.json ?? 'isActive') : null,
      order,
      cacheable: descriptor.capabilities.has('cacheable'),
    };
  }

  /**
   * Whether a target's rows have a description; `false` until the Entities
   * chunk loads.
   *
   * @param entity - The entity's name.
   * @returns Whether they have.
   */
  #hasDescription(entity: string): boolean {
    return this.#entities.loaded() && this.#plan(entity).description.length > 0;
  }

  /**
   * A row's description: each description column's text, the empty ones
   * left out, joined by " · ".
   *
   * @param entity - The entity's name.
   * @param item - The wire row.
   * @returns The description, or `null` when every part is empty.
   */
  #describe(entity: string, item: TmJson): string | null {
    if (!this.#entities.loaded()) {
      return null;
    }
    const parts: string[] = [];
    for (const column of this.#plan(entity).description) {
      const text = this.#describeColumn(column, item);
      if (text !== '') {
        parts.push(text);
      }
    }
    return parts.length > 0 ? parts.join(DESCRIPTION_SEPARATOR) : null;
  }

  /**
   * One description column's text from a wire row: a value as a grid
   * writes it, a group's active slot else its first, a related row's
   * display name.
   *
   * @param column - The column.
   * @param item - The wire row.
   * @returns The text; empty when the row has none.
   */
  #describeColumn(column: TmColumnPlan, item: TmJson): string {
    const resolution = column.resolution;
    if (resolution === null) {
      return '';
    }
    let owner: unknown = item;
    for (const step of resolution.steps) {
      owner = member(owner, step.json);
    }
    const end = resolution.end;
    switch (end.kind) {
      case 'value':
        return this.#formats.text(member(owner, end.property.json), end.property, 'grid');
      case 'text': {
        const slots = [...end.slots].sort((a, b) => (a.multilingual?.slot ?? 1) - (b.multilingual?.slot ?? 1));
        const active = member(owner, slots[this.#localization.contentSlot() - 1]?.json ?? slots[0]!.json);
        const value = active === null || active === undefined || active === '' ? member(owner, slots[0]!.json) : active;
        return typeof value === 'string' ? tmStripBidiControls(value) : '';
      }
      case 'navigation': {
        // A selected related row names itself; a cached row holds only its key.
        const related = member(owner, end.navigation.json);
        if (typeof related === 'object' && related !== null) {
          // A row with no related row selects its columns as nulls.
          const target = this.#entities.get(end.navigation.target);
          const id = member(related, target.property(target.key.property)?.json ?? 'id');
          return id === null || id === undefined ? '' : this.#names.of(end.navigation.target, related as TmJsonObject)();
        }
        const key = this.#entities.get(resolution.entity).property(end.navigation.foreignKey)?.json;
        const id = key === undefined ? undefined : member(owner, key);
        if (typeof id !== 'number' && typeof id !== 'string') {
          return '';
        }
        const label = this.#names.label(end.navigation.target, id as TmId)();
        if (label === null) {
          untracked(() => this.#names.resolve(end.navigation.target, [id as TmId]));
        }
        return label ?? '';
      }
      default:
        return '';
    }
  }

  /**
   * Searches a target: its cached list in memory, for a cacheable target
   * with no filter whose list is loaded, else its query route, 20 rows at a
   * time.
   *
   * @param entity - The entity's name.
   * @param text - The query text.
   * @param signal - Aborts the search.
   * @param fieldFilter - The field's filter.
   * @returns The rows.
   */
  #search(entity: string, text: string, signal: AbortSignal, fieldFilter: string | null): TmEntitySearchResult<TmJson> | Promise<TmEntitySearchResult<TmJson>> {
    if (!untracked(this.#entities.loaded)) {
      // The binding works before the Entities chunk: its first search waits for it,
      // and finds nothing once the app ended meanwhile, whose injector would throw.
      return this.#entities.load().then(() => (this.#app.destroyed ? { items: [] } : this.#search(entity, text, signal, fieldFilter)));
    }
    const started = performance.now();
    const plan = this.#plan(entity);
    let query = tmStripBidiControls(text).trim();
    query = query.slice(0, plan.descriptor.limits.maxSearchLength).trim();

    // --- A cacheable target with no filter searches its list, synchronously ---
    if (plan.cacheable && plan.filter === null && fieldFilter === null) {
      const list = untracked(this.#launch.entitySet(plan.descriptor.name));
      if (list !== undefined) {
        const result = untracked(() => this.#searchList(plan, list, query));
        this.#telemetry.timing('tm.entity.picker-search', performance.now() - started, { cached: true, outcome: 'ok' });
        return result;
      }
      // While the list loads, or is refused, the server answers.
    }
    return this.#searchServer(plan, query, fieldFilter, signal, started);
  }

  /**
   * Searches a target's query route: the search text, the declared and the
   * field's filters as separate leaves, the order, and one row more than a
   * page to know whether there are more. The rows are remembered by name.
   *
   * @param plan - The target's plan.
   * @param query - The search text; empty to browse.
   * @param fieldFilter - The field's filter.
   * @param signal - Aborts the search.
   * @param started - When the search started.
   * @returns The rows.
   */
  async #searchServer(
    plan: TmPickerPlan,
    query: string,
    fieldFilter: string | null,
    signal: AbortSignal,
    started: number,
  ): Promise<TmEntitySearchResult<TmJson>> {
    const filters = [plan.filter, fieldFilter].filter((filter): filter is string => filter !== null);
    const body = {
      select: plan.select,
      ...(query !== '' && { search: query }),
      ...(filters.length > 0 && { filters }),
      orderBy: plan.order(untracked(this.#localization.contentSlot)),
      take: PAGE + 1,
    };
    try {
      const page = await this.#api.read<{ readonly rows: TmQueryRowSet }>(`${plan.segment}/query`, body, { signal });
      // An app that ended during the read remembers nothing.
      if (this.#app.destroyed) {
        return { items: [] };
      }
      const items = tmRowsToObjects(plan.descriptor, page.rows);
      this.#names.remember(plan.descriptor.name, items);
      this.#telemetry.timing('tm.entity.picker-search', performance.now() - started, { cached: false, outcome: 'ok' });
      return { items: items.slice(0, PAGE), hasMore: items.length > PAGE };
    } catch (error) {
      if (!aborted(error, signal)) {
        this.#telemetry.timing('tm.entity.picker-search', performance.now() - started, { cached: false, outcome: 'error' });
      }
      throw error;
    }
  }

  /**
   * Searches a cached list: its active rows whose text columns hold the
   * folded text (a prefix-searchable column from its start, any other
   * anywhere), or that the findable rule finds, ordered by display name.
   *
   * @param plan - The target's plan.
   * @param list - The cached list.
   * @param query - The search text; empty to browse.
   * @returns The rows.
   */
  #searchList(plan: TmPickerPlan, list: TmCachedEntitySet, query: string): TmEntitySearchResult<TmJson> {
    const active = plan.active;
    const rows = list.items.filter(
      (item): item is TmJsonObject => typeof item === 'object' && item !== null && !Array.isArray(item) && (active === null || (item as TmJsonObject)[active] !== false),
    );
    let matched: readonly TmJsonObject[] = rows;
    if (query !== '') {
      const folded = tmSearchNormalize(query);
      const findable = ɵtmFindable(plan, query, ɵTM_SEARCH_MATCH);
      matched = rows.filter((row) => ɵtmSearchMatches(plan.text, this.#foldedText(plan, row), folded) || findable(row));
    }

    // --- Ordered by display name, in the locale's collation ---
    const collator = new Intl.Collator(this.#localization.locale());
    const named = matched.map((row) => ({ row, name: this.#names.of(plan.descriptor.name, row)() }));
    named.sort((a, b) => collator.compare(a.name, b.name));
    return { items: named.slice(0, PAGE).map((entry) => entry.row), hasMore: named.length > PAGE };
  }

  /**
   * A cached row's text columns, folded, made once per row.
   *
   * @param plan - The target's plan.
   * @param row - The row.
   * @returns The folded text of each of the plan's text columns, `null` where the row has none.
   */
  #foldedText(plan: TmPickerPlan, row: TmJsonObject): readonly (string | null)[] {
    let byRow = this.#folded.get(plan);
    if (byRow === undefined) {
      this.#folded.set(plan, (byRow = new WeakMap()));
    }
    let texts = byRow.get(row);
    if (texts === undefined) {
      texts = plan.text.map((column) => {
        const value = row[column.json];
        return typeof value === 'string' && value !== '' ? tmSearchNormalize(value) : null;
      });
      byRow.set(row, texts);
    }
    return texts;
  }
}
