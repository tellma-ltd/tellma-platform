// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import { computed, inject, Injectable, Injector, untracked, type Signal } from '@angular/core';
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
  type TmDataType,
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

/** The display name's fixed separator between its prefix and its name. */
const SEPARATOR = ' - ';

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

/** One column the display name, the search or the paste reads: its path name, wire name and type. */
export interface ɵTmPickerTerm {
  /** The property's name, as Queryex spells it: `Name2`. */
  readonly name: string;
  /** The property's wire name: `name2`. */
  readonly json: string;
  /** The property's type; `null` when unknown. */
  readonly type: TmDataType | null;
}

/**
 * What the findable rule reads of an entity: its key, its display name's
 * admitted slots in slot order (or its one scalar source), and its prefix.
 * Internal.
 */
export interface ɵTmFindablePlan {
  /** The key. */
  readonly key: ɵTmPickerTerm;
  /** The name's admitted slots, or the scalar source alone. */
  readonly slots: readonly ɵTmPickerTerm[];
  /** The prefix, when the rule declares one. */
  readonly prefix: ɵTmPickerTerm | null;
}

/** How the findable rule compares text: loosely for a search, exactly for a paste. */
export interface ɵTmFindableMatch {
  /** Whether a row's text equals the given text. */
  readonly equal: (value: string, text: string) => boolean;
  /** Whether a row's text starts with the given text; equality for a paste. */
  readonly startsWith: (value: string, text: string) => boolean;
  /** Whether the bare id matches, as a search's does; a paste matches `#{id}` alone. */
  readonly bareId: boolean;
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
  /** The text columns a cached search contains-matches: the name's slots, a text prefix, natural keys and searchable columns. */
  readonly text: readonly ɵTmPickerTerm[];
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
 * The digits of an integer written in Latin, Arabic-Indic or Extended
 * Arabic-Indic digits, as decimal text with no leading zeros.
 *
 * @param text - The text.
 * @param native - Whether Arabic-Indic digits read as Latin ones.
 * @returns The canonical digits, or `null` for text that is no integer.
 */
function integerOf(text: string, native: boolean): string | null {
  const latin = native
    ? text.replace(/[٠-٩۰-۹]/g, (digit) => String((digit.charCodeAt(0) - 0x0660) % 0x90))
    : text;
  return /^-?[0-9]+$/.test(latin) ? latin.replace(/^(-?)0+(?=\d)/, '$1').replace(/^-0$/, '0') : null;
}

/**
 * A wire value's integer text: a number, or the decimal text of a `long`.
 *
 * @param value - The wire value.
 * @returns The canonical digits, or `null` for a value that is no integer.
 */
function integerValue(value: unknown): string | null {
  if (typeof value === 'number') {
    return Number.isSafeInteger(value) ? String(value) : null;
  }
  return typeof value === 'string' ? integerOf(value, false) : null;
}

/**
 * Whether a wire value is text the comparison accepts.
 *
 * @param value - The wire value.
 * @param text - The text.
 * @param compare - The comparison.
 * @returns Whether it matches.
 */
function textMatches(value: unknown, text: string, compare: (value: string, text: string) => boolean): boolean {
  return typeof value === 'string' && compare(value, text);
}

/**
 * The findable rule as a row predicate: text holding the
 * separator splits at its first one, the prefix equal to the left part and
 * the name in an admitted slot starting with (for a paste, equal to) the
 * right part; an integer source or prefix equal to the text, native digits
 * normalized; `#{id}`, and for a search the bare id, equal to the key. A
 * date source is never findable. Internal: the cached search and the paste
 * resolver share it.
 *
 * @param plan - What the rule reads of the entity.
 * @param text - The search or pasted text, without bidi controls and trimmed.
 * @param match - How text compares.
 * @returns Whether a row is found by the text.
 */
export function ɵtmFindable(plan: ɵTmFindablePlan, text: string, match: ɵTmFindableMatch): (row: TmJsonObject) => boolean {
  const tests: ((row: TmJsonObject) => boolean)[] = [];

  // --- The id: `#{id}`, or for a search the id itself, in Latin digits as the server reads it ---
  const idText = text.startsWith('#') ? text.slice(1) : match.bareId ? text : null;
  const id = idText === null ? null : integerOf(idText, false);
  if (id !== null) {
    tests.push((row) => integerValue(row[plan.key.json]) === id);
  }

  // --- An integer source or prefix: equal to the text, native digits normalized ---
  const number = integerOf(text, true);
  const source = plan.slots[0];
  const prefix = plan.prefix;
  if (number !== null && source?.type === 'integer') {
    tests.push((row) => integerValue(row[source.json]) === number);
  }
  if (number !== null && prefix?.type === 'integer') {
    tests.push((row) => integerValue(row[prefix.json]) === number);
  }

  // --- The split: "prefix - name", over a text source's admitted slots ---
  const at = text.indexOf(SEPARATOR);
  if (at >= 0 && prefix !== null && source?.type === 'string') {
    const left = text.slice(0, at).trim();
    const right = text.slice(at + SEPARATOR.length).trim();
    const leftNumber = prefix.type === 'integer' ? integerOf(left, true) : null;
    const prefixTest =
      left === ''
        ? null
        : prefix.type === 'integer'
          ? leftNumber === null
            ? null
            : (row: TmJsonObject) => integerValue(row[prefix.json]) === leftNumber
          : prefix.type === 'string' || prefix.type === null
            ? (row: TmJsonObject) => textMatches(row[prefix.json], left, match.equal)
            : null;
    if (prefixTest !== null) {
      tests.push((row) => prefixTest(row) && plan.slots.some((slot) => textMatches(row[slot.json], right, match.startsWith)));
    }
  }
  return (row) => tests.some((test) => test(row));
}

/** Each locale's exact-match collator: case-insensitive, accent-sensitive, as the database compares. */
const EXACT = new Map<string, Intl.Collator>();

/**
 * The paste's comparison in a locale: equal ignoring case, not accents.
 *
 * @param locale - The locale.
 * @returns The match.
 */
function exactMatch(locale: string): ɵTmFindableMatch {
  let collator = EXACT.get(locale);
  if (collator === undefined) {
    EXACT.set(locale, (collator = new Intl.Collator(locale, { usage: 'search', sensitivity: 'accent' })));
  }
  const equal = (value: string, text: string): boolean => collator.compare(value, text) === 0;
  return { equal, startsWith: equal, bareId: false };
}

/**
 * A search's comparison: the folded texts, as the platform's client-side
 * matching folds them. Internal: the cached search uses it.
 */
export const ɵTM_SEARCH_MATCH: ɵTmFindableMatch = {
  equal: (value, text) => tmSearchNormalize(value) === tmSearchNormalize(text),
  startsWith: (value, text) => tmSearchNormalize(value).startsWith(tmSearchNormalize(text)),
  bareId: true,
};

/**
 * Whether a row is the one a pasted name names: a text slot of the display
 * name, or a text prefix, equal to the name, or the findable rule with
 * equality in place of a prefix match; case is ignored, accents are not.
 * Internal: the paste resolver uses it.
 *
 * @param plan - What the rule reads of the entity.
 * @param text - The pasted name, without bidi controls and trimmed.
 * @param locale - The locale whose collation compares text.
 * @returns The row predicate.
 */
export function ɵtmPasteMatch(plan: ɵTmFindablePlan, text: string, locale: string): (row: TmJsonObject) => boolean {
  const match = exactMatch(locale);
  const findable = ɵtmFindable(plan, text, match);
  const prefix = plan.prefix;
  return (row) =>
    plan.slots.some((slot) => slot.type === 'string' && textMatches(row[slot.json], text, match.equal)) ||
    (prefix?.type === 'string' && textMatches(row[prefix.json], text, match.equal)) ||
    findable(row);
}

/**
 * A Queryex string literal: the text in single quotes, a quote written
 * twice.
 *
 * @param text - The text.
 * @returns The literal.
 */
function quote(text: string): string {
  return `'${text.replaceAll("'", "''")}'`;
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

  /**
   * Each descriptor's plan, a descriptor being made once per entity and
   * shape; it is made again when the grants that admit its columns move.
   */
  readonly #plans = new WeakMap<TmEntityDescriptor, Signal<TmPickerPlan>>();

  /** Each row's folded text columns, by plan, for the cached search. */
  readonly #folded = new WeakMap<TmPickerPlan, WeakMap<object, readonly string[]>>();

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
   * The paste resolver of a grid's entity column: one query for the pasted
   * labels, each matched against the display name's columns of every
   * admitted slot, a text prefix, and the findable rule with equality in
   * place of a prefix match. A label one row matches resolves to it; none,
   * or several, is an error. Internal: `tmGridColumnFor` binds it.
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
      await this.#entities.load();
      return this.#resolveLabels(this.#plan(entity), labels, untracked(filter)?.trim() || null, context.signal);
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

    // --- The text a cached search contains-matches, each column once ---
    const text = new Map<string, ɵTmPickerTerm>();
    const addText = (property: TmPropertyDescriptor | undefined): void => {
      if (property?.type === 'string' && !text.has(property.name)) {
        text.set(property.name, term(property));
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
      // The binding works before the Entities chunk: its first search waits for it.
      return this.#entities.load().then(() => this.#search(entity, text, signal, fieldFilter));
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
   * Searches a cached list: its active rows whose text columns contain the
   * folded text, or that the findable rule finds, ordered by display name.
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
      matched = rows.filter((row) => (folded !== '' && this.#foldedText(plan, row).some((text) => text.includes(folded))) || findable(row));
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
   * @returns The folded texts.
   */
  #foldedText(plan: TmPickerPlan, row: TmJsonObject): readonly string[] {
    let byRow = this.#folded.get(plan);
    if (byRow === undefined) {
      this.#folded.set(plan, (byRow = new WeakMap()));
    }
    let texts = byRow.get(row);
    if (texts === undefined) {
      texts = plan.text.flatMap((column) => {
        const value = row[column.json];
        return typeof value === 'string' && value !== '' ? [tmSearchNormalize(value)] : [];
      });
      byRow.set(row, texts);
    }
    return texts;
  }

  /**
   * Resolves pasted labels with as few queries as the filter length allows:
   * one, unless the labels are too many for one filter.
   *
   * @param plan - The target's plan.
   * @param labels - The pasted labels.
   * @param fieldFilter - The column's filter.
   * @param signal - Aborts the resolution.
   * @returns Each label's resolution.
   */
  async #resolveLabels(
    plan: TmPickerPlan,
    labels: readonly string[],
    fieldFilter: string | null,
    signal: AbortSignal,
  ): Promise<ReadonlyMap<string, TmLabelResolution<TmId>>> {
    const resolutions = new Map<string, TmLabelResolution<TmId>>();
    const texts = new Map<string, string[]>();
    for (const label of labels) {
      const text = tmStripBidiControls(label).trim();
      if (text === '') {
        resolutions.set(label, { error: 'notFound' });
      } else {
        texts.set(text, [...(texts.get(text) ?? []), label]);
      }
    }

    // --- The texts in groups whose filter fits beside the other leaves ---
    const leaves = [plan.filter, fieldFilter].filter((filter): filter is string => filter !== null);
    const room = plan.descriptor.limits.maxFilterLength - leaves.reduce((length, leaf) => length + leaf.length + 5, 0);
    const groups: string[][] = [];
    let group: string[] = [];
    for (const text of texts.keys()) {
      if (group.length > 0 && pasteFilter(plan, [...group, text]).length > room) {
        groups.push(group);
        group = [];
      }
      group.push(text);
    }
    if (group.length > 0) {
      groups.push(group);
    }

    // --- One query per group; each text keeps the rows it alone matches ---
    const locale = untracked(this.#localization.locale);
    const reads = groups.map(async (texts) => {
      const filter = pasteFilter(plan, texts);
      const rows =
        filter === ''
          ? []
          : tmRowsToObjects(
              plan.descriptor,
              (
                await this.#api.read<{ readonly rows: TmQueryRowSet }>(
                  `${plan.segment}/query`,
                  { select: plan.select, filters: [...leaves, filter], take: plan.descriptor.limits.maxTake },
                  { signal },
                )
              ).rows,
            );
      this.#names.remember(plan.descriptor.name, rows);
      return { texts, rows };
    });
    for (const { texts: resolved, rows } of await Promise.all(reads)) {
      for (const text of resolved) {
        const pasted = ɵtmPasteMatch(plan, text, locale);
        const ids = new Map<string, TmId>();
        for (const row of rows) {
          if (pasted(row)) {
            const id = row[plan.key.json] as TmId;
            ids.set(String(id), id);
          }
        }
        const resolution: TmLabelResolution<TmId> =
          ids.size === 1 ? { value: [...ids.values()][0]! } : { error: ids.size === 0 ? 'notFound' : 'ambiguous' };
        for (const label of texts.get(text)!) {
          resolutions.set(label, resolution);
        }
      }
    }
    return resolutions;
  }
}

/**
 * The one filter leaf of a paste's labels: every admitted text slot and a
 * text prefix `in` the labels, an integer source or prefix `in` the labels
 * that read as integers, the key `in` the `#{id}` labels, and each label
 * holding the separator as its prefix and name halves.
 *
 * @param plan - The target's plan.
 * @param texts - The labels, without bidi controls and trimmed.
 * @returns The leaf; empty when no label can match.
 */
function pasteFilter(plan: ɵTmFindablePlan, texts: readonly string[]): string {
  const terms: string[] = [];
  const strings = texts.map(quote).join(', ');
  const numbers = [...new Set(texts.map((text) => integerOf(text, true)).filter((text) => text !== null))].join(', ');
  const ids = [...new Set(texts.filter((text) => text.startsWith('#')).map((text) => integerOf(text.slice(1), false)).filter((id) => id !== null))];
  const source = plan.slots[0];
  const prefix = plan.prefix;

  // --- The display name's columns, each slot's and the prefix's ---
  for (const slot of plan.slots) {
    if (slot.type === 'string') {
      terms.push(`${slot.name} in (${strings})`);
    }
  }
  if (prefix?.type === 'string') {
    terms.push(`${prefix.name} in (${strings})`);
  }
  if (numbers !== '' && source?.type === 'integer') {
    terms.push(`${source.name} in (${numbers})`);
  }
  if (numbers !== '' && prefix?.type === 'integer') {
    terms.push(`${prefix.name} in (${numbers})`);
  }
  if (ids.length > 0) {
    terms.push(`${plan.key.name} in (${ids.join(', ')})`);
  }

  // --- The split, with equality in place of a prefix match ---
  if (prefix !== null && source?.type === 'string') {
    for (const text of texts) {
      const at = text.indexOf(SEPARATOR);
      const left = at < 0 ? '' : text.slice(0, at).trim();
      const right = text.slice(at + SEPARATOR.length).trim();
      const side = left === '' ? null : prefix.type === 'integer' ? integerOf(left, true) : prefix.type === 'string' ? quote(left) : null;
      if (side !== null) {
        const names = plan.slots.filter((slot) => slot.type === 'string').map((slot) => `${slot.name} = ${quote(right)}`);
        terms.push(`(${prefix.name} = ${side} and (${names.join(' or ')}))`);
      }
    }
  }
  return terms.join(' or ');
}
