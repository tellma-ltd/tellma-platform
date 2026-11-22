// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import { computed, DOCUMENT, inject, Injectable, Injector, signal, untracked, type Signal, type WritableSignal } from '@angular/core';
import { tmFormatDate, tmFormatNumber, tmStripBidiControls, type TmCalendar } from '@tellma/core-ui/l10n';

import { TmApi } from '../api/tm-api';
import type { TmDecimal, TmId, TmJson, TmJsonObject } from '../json/tm-json';
import { TmLocalization } from '../l10n/tm-localization';
import { TmTelemetry } from '../observe/tm-telemetry';
import { TmLaunchCache } from '../state/launch/tm-launch-cache';
import { TmCacheRefresher } from '../state/sync/tm-cache-refresher';
import { TmEntities } from './tm-entities';
import type { TmEntityDescriptor } from './tm-entity-descriptors';
import type { TmDataType, TmDisplayNameRule, TmEntitySummary } from './tm-entity-metadata';
import { TmPermissions } from './tm-permissions';
import { tmRowsToObjects, ɵtmJsonName, type TmQueryRowSet } from './tm-rows-to-objects';

// Display names (spec 0029 §5): one string per row in the user's language,
// rendered by the rule the server declares and by the same steps the server
// takes (§5.2; display-name-golden.json pins both sides). The registry also
// holds the rows the app has read, per tenant, so a navigation, a tile or the
// bell names a row it only has the id of, and fetches the unknown ones in the
// background, a frame's ids at a time.

/** The fixed separator between a prefix and a name; never localized. */
const SEPARATOR = ' - ';

/** The most rows the registry holds per entity; the least recently used go first. */
const ROWS_PER_ENTITY = 5000;

/** Marks an id the server did not return, so its rows show the fallback. */
const MISSING = Symbol('missing');

/** The placeholder the fallback message is rendered with, then replaced by the id. */
const ID_PLACEHOLDER = '';

/**
 * A display name as its two parts, for markup that isolates each in a
 * `<bdi>`: `{ prefix: '1101', name: 'Cash' }` renders "1101 - Cash".
 */
export interface TmDisplayNameParts {
  /** The prefix, set only when the row has both a prefix and a name. */
  readonly prefix: string | null;
  /** The name; else the prefix; else the fallback, such as "#7". */
  readonly name: string;
}

/** One property a display name reads: its path, its wire name and its type. */
interface ɵTmDisplayNameTerm {
  /** The property's name, as paths spell it: `Name2`. */
  readonly name: string;
  /** The property's wire name: `name2`. */
  readonly json: string;
  /** The property's type; `null` before the Entities chunk loads, when the wire value's own type decides. */
  readonly type: TmDataType | null;
}

/**
 * How one entity's display names render under one shape. Internal: the
 * registry builds one per entity and shape, and the shared golden builds
 * them from its rules.
 */
export interface ɵTmDisplayNamePlan {
  /** The key. */
  readonly key: ɵTmDisplayNameTerm;
  /** The name's slots in order: a group's admitted twins, or the one scalar source. */
  readonly names: readonly ɵTmDisplayNameTerm[];
  /** The prefix, when the rule declares one. */
  readonly prefix: ɵTmDisplayNameTerm | null;
  /** The columns a display name reads: the key, the names and the prefix, each once. */
  readonly columns: readonly string[];
}

/** How a display name's values format: the active locale and calendar. */
export interface ɵTmDisplayNameFormat {
  /** The locale, such as `ar-SA`. */
  readonly locale: string;
  /** The display calendar. */
  readonly calendar: TmCalendar;
}

/**
 * A display-name plan from its terms. Internal.
 *
 * @param key - The key.
 * @param names - The name's slots in order.
 * @param prefix - The prefix, or `null`.
 * @returns The plan.
 */
export function ɵtmDisplayNamePlan(
  key: ɵTmDisplayNameTerm,
  names: readonly ɵTmDisplayNameTerm[],
  prefix: ɵTmDisplayNameTerm | null,
): ɵTmDisplayNamePlan {
  const columns = [...new Set([key.name, ...names.map((term) => term.name), ...(prefix ? [prefix.name] : [])])];
  return { key, names, prefix, columns };
}

/**
 * The plan of an entity's rule under a shape: from the descriptor once the
 * Entities chunk is loaded, whose properties name the twins the group has;
 * before that from the summary alone, by the conventions twins and wire
 * names follow. Internal.
 *
 * @param rule - The entity's display-name rule.
 * @param shape - The tenant's shape.
 * @param descriptor - The entity's descriptor, or `null` before the load.
 * @returns The plan.
 */
export function ɵtmDisplayNamePlanOf(
  rule: TmDisplayNameRule,
  shape: 1 | 2 | 3,
  descriptor: TmEntityDescriptor | null,
): ɵTmDisplayNamePlan {
  const term = (name: string): ɵTmDisplayNameTerm => {
    const property = descriptor?.property(name);
    return property
      ? { name: property.name, json: property.json, type: property.type }
      : { name, json: ɵtmJsonName(name), type: null };
  };

  // --- The name's slots: a group's twins in slot order, stopping at the first it lacks ---
  let names: ɵTmDisplayNameTerm[];
  if (!rule.multilingual) {
    names = [term(rule.source)];
  } else if (descriptor) {
    names = [];
    for (let slot = 1; slot <= shape; slot++) {
      const twin = descriptor.properties.find((property) => property.multilingual?.group === rule.source && property.multilingual.slot === slot);
      if (twin === undefined) {
        break;
      }
      names.push({ name: twin.name, json: twin.json, type: twin.type });
    }
  } else {
    names = [1, 2, 3].slice(0, shape).map((slot) => term(slot === 1 ? rule.source : `${rule.source}${slot}`));
  }
  return ɵtmDisplayNamePlan(term(descriptor?.key.property ?? 'Id'), names, rule.prefix === undefined ? null : term(rule.prefix));
}

/**
 * The paths a plan reads, for a query's select, under the navigation the
 * entity is reached through. Internal.
 *
 * @param plan - The plan.
 * @param navigation - The navigation path, such as `Parent`; absent from the root.
 * @returns The paths, such as `['Parent.Id', 'Parent.Name']`.
 */
export function ɵtmDisplayNameSelect(plan: ɵTmDisplayNamePlan, navigation?: string): readonly string[] {
  return navigation ? plan.columns.map((column) => `${navigation}.${column}`) : plan.columns;
}

/** Each locale's digits and decimal separator, for writing decimal text exactly. */
const DIGITS = new Map<string, { readonly zero: number; readonly point: string }>();

/**
 * Writes decimal text, such as a `long` or a `decimal` wire value, in the
 * locale's digits without grouping, digit for digit, so no precision is lost.
 *
 * @param text - The decimal text.
 * @param locale - The locale.
 * @returns The text in the locale's digits.
 */
function localDigits(text: string, locale: string): string {
  let digits = DIGITS.get(locale);
  if (digits === undefined) {
    const half = tmFormatNumber(1.5, locale);
    digits = { zero: tmFormatNumber(0, locale).codePointAt(0) ?? 48, point: half.slice(1, -1) || '.' };
    DIGITS.set(locale, digits);
  }
  const { zero, point } = digits;
  return text.replace(/[0-9.]/g, (character) => (character === '.' ? point : String.fromCodePoint(zero + character.charCodeAt(0) - 48)));
}

/**
 * Formats one value of a display name, its name or its prefix: numbers in the
 * locale's digits and never grouped, dates in the active calendar's numeric
 * style, text as it is; no bidi control survives.
 *
 * @param value - The wire value.
 * @param type - The property's type, or `null` when the wire value's own type decides.
 * @param format - The locale and calendar.
 * @returns The text, or `null` when the value is empty.
 */
function formatValue(value: TmJson | undefined, type: TmDataType | null, format: ɵTmDisplayNameFormat): string | null {
  let text: string | null;
  if (value === null || value === undefined || typeof value === 'object') {
    text = null;
  } else if ((type === 'date' || type === 'datetime') && typeof value === 'string') {
    // A wall-clock date-time names its date, as the server's does.
    text = tmFormatDate(value.slice(0, 10), format.locale, { calendar: format.calendar, dateStyle: 'numeric' });
  } else if (typeof value === 'number' && (type === 'integer' || type === 'decimal' || type === null)) {
    text = tmFormatNumber(value, format.locale, { grouping: false });
  } else if (typeof value === 'string' && (type === 'integer' || type === 'decimal') && /^-?\d+(\.\d+)?$/.test(value)) {
    text = localDigits(value, format.locale);
  } else {
    text = String(value);
  }
  text = text === null ? null : tmStripBidiControls(text);
  return text === '' ? null : text;
}

/**
 * A row's display name as its parts: the active slot's name, else
 * slot 1's; the prefix only beside a name; the fallback when the row has
 * neither. Internal.
 *
 * @param plan - The entity's plan.
 * @param row - The wire row.
 * @param slot - The active content slot.
 * @param format - The locale and calendar.
 * @param fallback - Writes the fallback for an id, written in Latin digits as the server writes it.
 * @returns The parts.
 */
export function ɵtmDisplayNameParts(
  plan: ɵTmDisplayNamePlan,
  row: TmJson,
  slot: 1 | 2 | 3,
  format: ɵTmDisplayNameFormat,
  fallback: (id: string) => string,
): TmDisplayNameParts {
  const object: TmJsonObject = typeof row === 'object' && row !== null && !Array.isArray(row) ? (row as TmJsonObject) : {};
  const read = (term: ɵTmDisplayNameTerm | undefined): string | null => (term ? formatValue(object[term.json], term.type, format) : null);

  // --- Steps 1-3: the name, the prefix, and their text ---
  const name = (slot <= plan.names.length ? read(plan.names[slot - 1]) : null) ?? read(plan.names[0]);
  const prefix = read(plan.prefix ?? undefined);
  if (name !== null) {
    return { prefix, name };
  }
  if (prefix !== null) {
    return { prefix: null, name: prefix };
  }
  const id = object[plan.key.json];
  return { prefix: null, name: tmStripBidiControls(fallback(typeof id === 'number' || typeof id === 'string' ? String(id) : '')) };
}

/**
 * The text of a display name's parts: the prefix, the fixed separator and
 * the name, or the name alone.
 *
 * @param parts - The parts.
 * @returns The text.
 */
export function ɵtmDisplayNameText(parts: TmDisplayNameParts): string {
  return parts.prefix === null ? parts.name : parts.prefix + SEPARATOR + parts.name;
}

/**
 * The text key of an id: its decimal digits, whether it arrives as a number
 * for an `int` key or as decimal text for a `long` one, so either form finds
 * the same row.
 *
 * @param id - The id.
 * @returns The key, or `null` for a value that is no integer id.
 */
function idKey(id: unknown): string | null {
  if (typeof id === 'number') {
    return Number.isSafeInteger(id) ? String(id) : null;
  }
  return typeof id === 'string' && /^-?\d+$/.test(id) ? id.replace(/^(-?)0+(?=\d)/, '$1') : null;
}

/** One entity's held rows, least recently used first, and the signal their readers follow. */
interface TmNameBucket {
  /** The rows by id key; `MISSING` for an id the server did not return. */
  readonly rows: Map<string, TmJsonObject | typeof MISSING>;
  /** Moves when rows are added. */
  readonly version: WritableSignal<number>;
}

/** A display name's parts and text, memoized per row object. */
interface TmRowNames {
  /** The parts. */
  readonly parts: Signal<TmDisplayNameParts>;
  /** The text. */
  readonly text: Signal<string>;
}

/**
 * Display names of the routed tenant's rows: rendered from a wire row by
 * the entity's rule in the active language, or named by id from the rows
 * this tab has read, with the unknown ones fetched in the background.
 *
 * @example
 * ```ts
 * const names = inject(TmDisplayNames);
 * const title = names.of('gl.Center', center); // "E-01 - Eastern region"
 * names.resolve('gl.Center', [5, 6]);
 * const parent = names.label('gl.Center', 5); // null until the row arrives
 * ```
 */
@Injectable({ providedIn: 'root' })
export class TmDisplayNames {
  readonly #injector = inject(Injector);
  readonly #entities = inject(TmEntities);
  readonly #permissions = inject(TmPermissions);
  readonly #l10n = inject(TmLocalization);
  readonly #telemetry = inject(TmTelemetry);
  readonly #document = inject(DOCUMENT);

  /** Each entity's plan, by name, shape and whether the Entities chunk had loaded. */
  readonly #plans = new Map<string, ɵTmDisplayNamePlan>();

  /** Each row object's names, by entity name. */
  readonly #rows = new WeakMap<object, Map<string, TmRowNames>>();

  /** The held rows of the current scope, by entity name. */
  readonly #buckets = new Map<string, TmNameBucket>();

  /** The user and tenant the held rows were read in. */
  #tenant: string | null = null;

  /** The tenant's content languages when the held rows were read; `undefined` while unknown. */
  #languages: string | undefined;

  /** Counts the scopes, so a read that outlives its scope holds nothing. */
  #generation = 0;

  /** The ids waiting for the next frame, by entity name and id key. */
  #queue = new Map<string, Map<string, TmId>>();

  /** The ids being read now, as entity name and id key. */
  readonly #reading = new Set<string>();

  /** Whether a flush of the queue is scheduled. */
  #scheduled = false;

  /** The fallback message, rendered once with a placeholder for the id. */
  #fallback: Signal<string> | null = null;

  /** The routed tenant's launch documents, resolved at first use. */
  get #launch(): TmLaunchCache {
    return this.#injector.get(TmLaunchCache);
  }

  /** The cache refresher, resolved at first use. */
  get #refresher(): TmCacheRefresher {
    return this.#injector.get(TmCacheRefresher);
  }

  /**
   * A row's display name in the active language.
   *
   * @param entity - The entity's name, such as `gl.Center`.
   * @param row - The wire row, holding the rule's members.
   * @returns The name, which follows the language and the formats.
   */
  of(entity: string, row: TmJson): Signal<string> {
    return this.#names(entity, row).text;
  }

  /**
   * A row's display name as its parts, for `tm-display-name`.
   *
   * @param entity - The entity's name.
   * @param row - The wire row.
   * @returns The parts, which follow the language and the formats.
   */
  parts(entity: string, row: TmJson): Signal<TmDisplayNameParts> {
    return this.#names(entity, row).parts;
  }

  /**
   * The paths a display name reads, for a query's select: the key, the
   * admitted twins or the source, and the prefix, under the routed
   * tenant's shape. It needs `TmEntities.load()`.
   *
   * @param entity - The entity's name.
   * @param navigation - The navigation path the entity is reached through, such as `Parent`.
   * @returns The paths, such as `['Parent.Id', 'Parent.Name', 'Parent.Name2']`.
   * @throws Error for an entity the metadata does not list.
   */
  select(entity: string, navigation?: string): readonly string[] {
    if (!this.#entities.loaded()) {
      this.#entities.ɵbeforeLoad('TmDisplayNames.select');
    }
    const plan = this.#plan(entity);
    if (plan === null) {
      throw new Error(`The metadata lists no entity '${entity}'.`);
    }
    return ɵtmDisplayNameSelect(plan, navigation);
  }

  /**
   * A known row's display name by id: from the entity's cached list when it
   * is cacheable and loaded, else from the rows this tab has read. `null`
   * while the row is unknown; the fallback, such as "#7", for a row the
   * server did not return or an entity the user cannot query.
   *
   * @param entity - The entity's name.
   * @param id - The row's id, a number or decimal text.
   * @returns The name.
   */
  label(entity: string, id: TmId): Signal<string | null> {
    const parts = this.labelParts(entity, id);
    return computed(() => {
      const value = parts();
      return value && ɵtmDisplayNameText(value);
    });
  }

  /**
   * A known row's display name by id, as its parts.
   *
   * @param entity - The entity's name.
   * @param id - The row's id.
   * @returns The parts, or `null` while the row is unknown.
   */
  labelParts(entity: string, id: TmId): Signal<TmDisplayNameParts | null> {
    return computed(() => {
      const summary = this.#entities.summary(entity);
      const key = idKey(id);
      if (summary === undefined || key === null) {
        return this.#fallbackParts(key ?? String(id));
      }

      // --- A cacheable entity's list answers first, in either id form ---
      if (summary.capabilities.includes('cacheable')) {
        const list = this.#launch.entitySet(summary.name)();
        const row = list && (list.byId.get(Number(key)) ?? list.byId.get(key as TmDecimal));
        if (row !== undefined) {
          return this.parts(summary.name, row)();
        }
      }

      // --- Then the rows this tab has read ---
      const bucket = this.#bucket(summary.name);
      bucket.version();
      const row = bucket.rows.get(key);
      if (row !== undefined) {
        // A read refreshes the row's recency.
        bucket.rows.delete(key);
        bucket.rows.set(key, row);
        return row === MISSING ? this.#fallbackParts(key) : this.parts(summary.name, row)();
      }
      // An entity never asked about its rows names them by id once the grants are known.
      return this.#launch.docs.me() !== undefined && !this.#askable(summary) ? this.#fallbackParts(key) : null;
    });
  }

  /**
   * Holds rows the app has read, so their names answer `label`: wire rows,
   * or a query's row set, read as wire objects.
   *
   * @param entity - The entity's name.
   * @param rows - The wire rows, or a row set.
   */
  remember(entity: string, rows: readonly TmJson[] | TmQueryRowSet): void {
    const summary = this.#entities.summary(entity);
    if (summary === undefined) {
      return;
    }
    const objects = Array.isArray(rows) ? rows : tmRowsToObjects(untracked(() => this.#entities.get(summary.name)), rows as TmQueryRowSet);
    this.#hold(summary.name, objects as readonly TmJson[], []);
  }

  /**
   * Holds the related rows a details read or a save returned, by entity name.
   *
   * @param related - The related rows, such as an entities result's `related`.
   */
  rememberRelated(related: Readonly<Record<string, readonly TmJson[]>>): void {
    for (const [entity, rows] of Object.entries(related)) {
      this.remember(entity, rows);
    }
  }

  /**
   * Fetches the display names of ids this tab does not hold, in the
   * background: the ids of one frame are read together, at most 100 per
   * read, and a tenant change aborts the reads. An entity the user cannot
   * query is never asked, nor an id a cacheable entity's loaded list holds.
   *
   * @param entity - The entity's name.
   * @param ids - The ids, numbers or decimal text.
   */
  resolve(entity: string, ids: readonly TmId[]): void {
    const summary = this.#entities.summary(entity);
    if (summary === undefined || summary.segment === null || !summary.operations.includes('query')) {
      return;
    }
    const bucket = untracked(() => this.#bucket(summary.name));
    // A cacheable entity's loaded list already names its rows, as label reads them.
    const list = summary.capabilities.includes('cacheable') ? untracked(this.#launch.entitySet(summary.name)) : undefined;
    let queue = this.#queue.get(summary.name);
    for (const id of ids) {
      const key = idKey(id);
      const listed = key !== null && list && (list.byId.has(Number(key)) || list.byId.has(key as TmDecimal));
      if (key !== null && !listed && !bucket.rows.has(key) && !this.#reading.has(`${summary.name}\n${key}`)) {
        queue ??= new Map();
        queue.set(key, id);
      }
    }
    if (queue === undefined || queue.size === 0) {
      return;
    }
    this.#queue.set(summary.name, queue);

    // --- One flush per frame; a hidden tab paints no frame, so a task stands in ---
    if (!this.#scheduled) {
      this.#scheduled = true;
      const window = this.#document.defaultView;
      const flush = (): void => void this.#flush();
      if (window?.requestAnimationFrame && this.#document.visibilityState === 'visible') {
        window.requestAnimationFrame(flush);
      } else {
        setTimeout(flush);
      }
    }
  }

  /**
   * An entity's plan under the routed tenant's shape.
   *
   * @param entity - The entity's name.
   * @returns The plan, or `null` for an unknown entity.
   */
  #plan(entity: string): ɵTmDisplayNamePlan | null {
    const summary = this.#entities.summary(entity);
    if (summary === undefined) {
      return null;
    }
    const model = this.#entities.ɵmodel();
    const shape = this.#entities.ɵshape();
    const key = `${summary.name}\n${shape}\n${model !== null}`;
    let plan = this.#plans.get(key);
    if (plan === undefined) {
      plan = ɵtmDisplayNamePlanOf(summary.displayName, shape, model && this.#entities.get(summary.name));
      this.#plans.set(key, plan);
    }
    return plan;
  }

  /**
   * A row's names, made once per row object and entity.
   *
   * @param entity - The entity's name.
   * @param row - The wire row.
   * @returns The names.
   */
  #names(entity: string, row: TmJson): TmRowNames {
    const memo = typeof row === 'object' && row !== null ? row : null;
    let byEntity = memo && this.#rows.get(memo);
    let names = byEntity?.get(entity);
    if (names === undefined) {
      const parts = computed(() => {
        const plan = this.#plan(entity);
        const format = { locale: this.#l10n.locale(), calendar: this.#l10n.calendar() };
        return plan
          ? ɵtmDisplayNameParts(plan, row, this.#l10n.contentSlot(), format, (id) => this.#fallbackText(id))
          : this.#fallbackParts('');
      });
      names = { parts, text: computed(() => ɵtmDisplayNameText(parts())) };
      if (memo !== null) {
        byEntity ??= new Map();
        byEntity.set(entity, names);
        this.#rows.set(memo, byEntity);
      }
    }
    return names;
  }

  /**
   * The fallback text for an id, from Core's `Entity_DisplayNameFallback`.
   *
   * @param id - The id, in Latin digits.
   * @returns The text, such as "#7".
   */
  #fallbackText(id: string): string {
    this.#fallback ??= this.#l10n.ɵtext('Entity_DisplayNameFallback', { id: ID_PLACEHOLDER }, `#${ID_PLACEHOLDER}`);
    return this.#fallback().replace(ID_PLACEHOLDER, id);
  }

  /**
   * The fallback as parts.
   *
   * @param id - The id, in Latin digits.
   * @returns The parts.
   */
  #fallbackParts(id: string): TmDisplayNameParts {
    return { prefix: null, name: tmStripBidiControls(this.#fallbackText(id)) };
  }

  /**
   * Whether the registry may ask the server about an entity's rows: it
   * offers `query` and the user may run it.
   *
   * @param summary - The entity's summary.
   * @returns Whether it may.
   */
  #askable(summary: TmEntitySummary): boolean {
    return summary.segment !== null && this.#permissions.allows(summary.name, 'query')();
  }

  /**
   * An entity's held rows in the current scope. The held rows are dropped
   * when the user or tenant changes, and when the tenant's content languages
   * do, since they decide what each name slot holds; any other move of the
   * tenant's documents keeps them.
   *
   * @param entity - The entity's name.
   * @returns The bucket.
   */
  #bucket(entity: string): TmNameBucket {
    const partition = this.#refresher.ɵpartition();
    const tenant = partition && `${partition.sub}\n${partition.tenantId}`;
    const languages = this.#launch.docs.settings()?.languages.map((language) => language.code).join(',');
    if (tenant !== this.#tenant || (languages !== undefined && this.#languages !== undefined && languages !== this.#languages)) {
      this.#buckets.clear();
      this.#queue.clear();
      this.#tenant = tenant;
      this.#languages = undefined;
      this.#generation++;
    }
    this.#languages = languages ?? this.#languages;
    let bucket = this.#buckets.get(entity);
    if (bucket === undefined) {
      bucket = { rows: new Map(), version: signal(0) };
      this.#buckets.set(entity, bucket);
    }
    return bucket;
  }

  /**
   * Holds rows and marks the ids the server did not return.
   *
   * @param entity - The entity's name.
   * @param rows - The wire rows.
   * @param missing - The id keys to mark as missing.
   */
  #hold(entity: string, rows: readonly TmJson[], missing: readonly string[]): void {
    const bucket = untracked(() => this.#bucket(entity));
    const json = untracked(() => this.#plan(entity))?.key.json ?? 'id';
    const set = (key: string, row: TmJsonObject | typeof MISSING): void => {
      bucket.rows.delete(key);
      bucket.rows.set(key, row);
      if (bucket.rows.size > ROWS_PER_ENTITY) {
        bucket.rows.delete(bucket.rows.keys().next().value!);
      }
    };
    for (const row of rows) {
      const key = typeof row === 'object' && row !== null && !Array.isArray(row) ? idKey((row as TmJsonObject)[json]) : null;
      if (key !== null) {
        set(key, row as TmJsonObject);
      }
    }
    for (const key of missing) {
      if (!bucket.rows.has(key)) {
        set(key, MISSING);
      }
    }
    bucket.version.update((version) => version + 1);
  }

  /** Reads the ids queued in the last frame, per entity. */
  async #flush(): Promise<void> {
    this.#scheduled = false;
    const queue = this.#queue;
    this.#queue = new Map();
    const partition = untracked(this.#refresher.ɵpartition);
    if (partition === null) {
      return;
    }
    await Promise.all([...queue].map(([entity, ids]) => this.#read(partition.tenantId, entity, ids)));
  }

  /**
   * Reads one entity's queued ids: the Entities chunk first, for the exact
   * select, then the background queries, which load with the entity model.
   *
   * @param tenantId - The tenant the ids were queued in.
   * @param entity - The entity's name.
   * @param ids - The ids, by id key.
   */
  async #read(tenantId: number, entity: string, ids: ReadonlyMap<string, TmId>): Promise<void> {
    const keys = [...ids.keys()];
    const flying = keys.map((key) => `${entity}\n${key}`);
    flying.forEach((key) => this.#reading.add(key));
    const generation = this.#generation;
    try {
      await this.#entities.load();
      const descriptor = untracked(() => this.#entities.get(entity));
      const plan = untracked(() => this.#plan(entity));
      if (plan === null || !untracked(this.#permissions.allows(entity, 'query'))) {
        return;
      }
      const { ɵtmReadDisplayNames } = await import('./tm-entity-model');
      await ɵtmReadDisplayNames(
        {
          api: this.#injector.get(TmApi),
          descriptor,
          columns: plan.columns,
          key: plan.key,
          keys,
          tenantId,
          signal: untracked(() => this.#refresher.ɵsignal(tenantId)),
        },
        (rows, missing, ms) => {
          // The scope moves at the next read of the held rows, so read them before comparing.
          untracked(() => this.#bucket(descriptor.name));
          if (this.#generation === generation) {
            this.#hold(descriptor.name, rows, missing);
            const count = rows.length + missing.length;
            this.#telemetry.timing('tm.entity.names-resolved', ms, { count: count === 1 ? '1' : count <= 10 ? '2-10' : '11+' });
          }
        },
      );
    } catch {
      // The Entities chunk failed to load: the ids stay unknown, so a later resolve asks again.
    } finally {
      flying.forEach((key) => this.#reading.delete(key));
    }
  }
}
