// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import { assertInInjectionContext, inject, Injector, signal, untracked, type Signal, type WritableSignal } from '@angular/core';
import {
  TmDisplayNames,
  TmEntities,
  TmLocalization,
  TmPermissions,
  TmTelemetry,
  type TmColumnInput,
  type TmDataType,
  type TmEntityDescriptor,
  type TmId,
  type TmJson,
  type TmPathResolution,
  type TmPlanRow,
  type TmPropertyDescriptor,
  type TmQueryRowSet,
  type TmQueryRowSetColumn,
  type TmValueDescriptor,
} from '@tellma/core';

// Select plans (spec 0029 §6): column keys expanded into the terms a query
// sends and read back from the row set it returns. Navigations and groups
// select every admitted slot, so a language switch re-renders the held rows
// with no request; only the order terms of a group or a navigation follow
// the active slot. A key the plan cannot serve, because it does not resolve
// or the user may not read it, is dropped rather than sent (§6.4), so a
// shared URL or a stale preference never makes the page fail.

/** What a column shows: a scalar, a multilingual group, a related row, a blob, or an expression. */
export type TmColumnKind = 'value' | 'text' | 'navigation' | 'image' | 'file' | 'expression';

/** The filter control a column's type takes; `null` on a column with no filter. */
export type TmFilterKind = 'text' | 'number' | 'date' | 'instant' | 'enum' | 'bool' | 'navigation';

/** Why a key was left out of a plan: it does not resolve, or the user may not read it. */
export type TmDroppedReason = 'unknown' | 'unreachable';

/** Where a plan's keys came from, as the `tm.entity.column-dropped` telemetry reports it. */
export type TmColumnSource = 'url' | 'preference' | 'declaration';

/** A key a plan left out, and why. */
export interface TmDroppedKey {
  /** The key, as given. */
  readonly key: string;
  /** Why it was left out. */
  readonly reason: TmDroppedReason;
}

/**
 * One shown column of a select plan: what it reads, how it is labelled,
 * filtered and ordered, and how a row's cell is read.
 */
export interface TmColumnPlan {
  /** The column's key in canonical case, such as `Parent.Code`; an expression's text. */
  readonly key: string;
  /** What the column shows. */
  readonly kind: TmColumnKind;
  /** The column input the plan was given, with its display, width and picker filter. */
  readonly input: TmColumnInput<string>;
  /** The key read against the entity; `null` for an expression. */
  readonly resolution: TmPathResolution | null;
  /**
   * The type facts the cell formats by: the property of a value, the first
   * slot of a group, an id for a navigation or a blob, and an expression's
   * once a row set types it. `null` for an expression no row set has typed.
   */
  readonly descriptor: TmValueDescriptor | null;
  /** The cell's type; `null` until a row set types an expression. */
  readonly type: TmDataType | null;
  /** The column's slash label, such as "Parent / Code". */
  readonly label: Signal<string>;
  /** The filter control the column takes; `null` for none. */
  readonly filter: TmFilterKind | null;
  /**
   * The order terms that sort by the column, in the active slot.
   *
   * @param direction - The direction.
   * @returns The terms, such as `coalesce(Name2, Name) desc`; `null` when the column is not orderable.
   */
  orderBy(direction: 'asc' | 'desc'): string | null;
  /**
   * A row's cell value: a navigation's id, a group's shown slot, a blob's
   * id, and any other column's value as the wire sent it.
   *
   * @param row - The row.
   * @returns The value; `null` when the row has none.
   */
  value(row: TmPlanRow): unknown;
}

/** The query terms a list of column keys expands to, and how its row set is read back. */
export interface TmSelectPlan {
  /** The terms, comma separated, for a query's select: the root's key first, each term once. */
  readonly select: string;
  /** The shown columns, the keys in order; never an extra path or a dropped key. */
  readonly columns: readonly TmColumnPlan[];
  /** The keys and extra paths left out, and why. */
  readonly dropped: readonly TmDroppedKey[];
  /**
   * Reads a row set the plan's select returned: one row per wire row,
   * which reads any selected term by path and each column's value by its
   * key. An expression column takes its type from the row set.
   *
   * @param set - The row set.
   * @returns The rows, in order.
   */
  rows(set: TmQueryRowSet): readonly TmPlanRow[];
}

/** How a plan is made beyond its keys. */
export interface TmSelectPlanOptions {
  /** The injector the plan reads the registry through; the current injection context when omitted. */
  readonly injector?: Injector;
  /** Where the keys came from, for the dropped keys' telemetry; `declaration` when omitted. */
  readonly source?: TmColumnSource;
}

/** How a navigation column's display name reads its related row. Internal. */
export interface ɵTmPlanNames {
  /** The related entity. */
  readonly entity: string;
  /** Each term the display name reads: its path in the row set, and its wire name in the related row. */
  readonly members: readonly { readonly path: string; readonly json: string }[];
}

/** A column's private facts, for the formats and the grid columns of this entry point. */
const NAMES = new WeakMap<TmColumnPlan, ɵTmPlanNames>();

/** A blob column's navigation path, such as `Image`, by column. */
const BLOB_PATHS = new WeakMap<TmColumnPlan, string>();

/**
 * How a navigation column's display name reads its related row. Internal.
 *
 * @param column - The column.
 * @returns The terms, or `undefined` for a column that is not a navigation.
 */
export function ɵtmPlanNames(column: TmColumnPlan): ɵTmPlanNames | undefined {
  return NAMES.get(column);
}

/**
 * A blob column's navigation path, such as `Image` or `Parent.Image`. Internal.
 *
 * @param column - The column.
 * @returns The path, or `undefined` for a column that is not a blob.
 */
export function ɵtmPlanBlobPath(column: TmColumnPlan): string | undefined {
  return BLOB_PATHS.get(column);
}

/** The value type an expression's row set column carries, by its Queryex type. */
function expressionType(column: TmQueryRowSetColumn): TmDataType | null {
  switch (column.type) {
    case 'Bool':
      return 'bool';
    case 'Numeric':
      return (column.scale ?? 0) > 0 ? 'decimal' : 'integer';
    case 'String':
    case 'HierarchyId':
      return 'string';
    case 'Guid':
      return 'guid';
    case 'Date':
      return 'date';
    case 'DateTime':
      return 'datetime';
    case 'DateTimeOffset':
      return 'instant';
    default:
      return null;
  }
}

/** The filter control a value's type takes. */
function filterOf(descriptor: TmValueDescriptor): TmFilterKind | null {
  if (descriptor.array) {
    return null;
  }
  switch (descriptor.type) {
    case 'string':
      return 'text';
    case 'integer':
    case 'decimal':
      return 'number';
    case 'date':
    case 'datetime':
      return 'date';
    case 'instant':
      return 'instant';
    case 'enum':
      return 'enum';
    case 'bool':
      return 'bool';
    default:
      return null;
  }
}

/** Whether a cell holds no value, so a group shows its first slot instead. */
function empty(value: unknown): boolean {
  return value === null || value === undefined || value === '';
}

/** The terms a column reads, how it orders, and how its value is read, before it becomes a column. */
interface TmColumnSpec {
  /** The canonical key. */
  readonly key: string;
  /** The kind. */
  readonly kind: TmColumnKind;
  /** The resolution, or `null` for an expression. */
  readonly resolution: TmPathResolution | null;
  /** The type facts; an expression's is typed by its row set. */
  readonly descriptor: TmValueDescriptor | null;
  /** The label. */
  readonly label: Signal<string>;
  /** The select terms. */
  readonly terms: readonly string[];
  /** The ascending order terms, in the active slot; `null` when not orderable. */
  readonly order: () => readonly string[] | null;
  /** Reads a row's value. */
  readonly value: (row: TmPlanRow) => unknown;
  /** A navigation's display-name terms. */
  readonly names?: ɵTmPlanNames;
  /** A blob's navigation path. */
  readonly blobPath?: string;
}

/** A column of a plan, its type a signal so a row set can type an expression. */
class TmPlanColumn implements TmColumnPlan {
  readonly key: string;
  readonly kind: TmColumnKind;
  readonly input: TmColumnInput<string>;
  readonly resolution: TmPathResolution | null;
  readonly label: Signal<string>;
  readonly filter: TmFilterKind | null;
  /** The type facts; an expression's moves when a row set types it. */
  readonly #descriptor: WritableSignal<TmValueDescriptor | null>;
  readonly #spec: TmColumnSpec;

  /**
   * A column from its spec.
   *
   * @param spec - The column's terms and readers.
   * @param input - The input the plan was given.
   */
  constructor(spec: TmColumnSpec, input: TmColumnInput<string>) {
    this.key = spec.key;
    this.kind = spec.kind;
    this.input = input;
    this.resolution = spec.resolution;
    this.label = spec.label;
    this.#spec = spec;
    this.#descriptor = signal(spec.descriptor, {
      equal: (a, b) => a === b || (a !== null && b !== null && a.type === b.type && a.scale === b.scale),
    });
    // Only a scalar, a group and a navigation filter; an expression never does.
    this.filter =
      spec.kind === 'navigation' ? 'navigation' : (spec.kind === 'value' || spec.kind === 'text') && spec.descriptor ? filterOf(spec.descriptor) : null;
  }

  /** {@inheritDoc TmColumnPlan.descriptor} */
  get descriptor(): TmValueDescriptor | null {
    return this.#descriptor();
  }

  /** {@inheritDoc TmColumnPlan.type} */
  get type(): TmDataType | null {
    return this.#descriptor()?.type ?? null;
  }

  /** {@inheritDoc TmColumnPlan.orderBy} */
  orderBy(direction: 'asc' | 'desc'): string | null {
    const terms = this.#spec.order();
    return terms === null ? null : terms.map((term) => (direction === 'desc' ? `${term} desc` : term)).join(', ');
  }

  /** {@inheritDoc TmColumnPlan.value} */
  value(row: TmPlanRow): unknown {
    return this.#spec.value(row);
  }

  /**
   * Types an expression from its row set column. Internal: `rows` calls it.
   *
   * @param column - The row set's column.
   */
  ɵtype(column: TmQueryRowSetColumn): void {
    const type = expressionType(column);
    const descriptor: TmValueDescriptor | null =
      type && { type, nullable: column.nullable, ...(type === 'decimal' && { scale: column.scale ?? 0 }) };
    // A host may read rows inside a computed; the type is the row set's fact,
    // not a dependency of that computation, so the write is untracked.
    untracked(() => this.#descriptor.set(descriptor));
  }
}

/** A row's cells and the term index they are read through. */
class TmRow implements TmPlanRow {
  readonly id: TmId;
  readonly #cells: readonly TmJson[];
  readonly #index: ReadonlyMap<string, number>;

  /**
   * A row over its cells.
   *
   * @param cells - The cells, one per row set column.
   * @param index - Each term's cell, by exact and by lower-case path.
   * @param key - The root key's term.
   */
  constructor(cells: readonly TmJson[], index: ReadonlyMap<string, number>, key: string) {
    this.#cells = cells;
    this.#index = index;
    this.id = this.get(key) as TmId;
  }

  /** {@inheritDoc TmPlanRow.get} */
  get(path: string): unknown {
    const cell = this.#index.get(path) ?? this.#index.get(path.toLowerCase());
    return cell === undefined ? undefined : (this.#cells[cell] ?? null);
  }
}

/** The members a plan row can never carry as a column getter: its own. */
const RESERVED = new Set(['id', 'get', 'constructor']);

/**
 * Expands column keys into a query's select terms and reads its rows back.
 * Each key is a path read against the entity, or an expression:
 *
 * - a scalar, such as `CreditLimit` or the twin `Name2`, selects itself;
 * - a group anchor, such as `Name`, selects every admitted slot and shows
 *   the active one, else the first;
 * - a navigation, such as `Parent`, selects the related row's id and the
 *   terms of its display name;
 * - an image or a file navigation selects the blob's id and its `Variants`
 *   or `FileName`.
 *
 * The root's key comes first and every term is selected once. Extra paths,
 * such as a tile's, are selected and read but never shown as columns. A key
 * that does not resolve, or that the user may not read, is dropped, listed
 * in `dropped` and reported as `tm.entity.column-dropped`. It needs
 * `TmEntities.load()`.
 *
 * @param root - The descriptor of the entity the query reads.
 * @param keys - The column inputs, in order.
 * @param extra - Paths read beside the columns, such as a tile's.
 * @param options - The injector and the keys' source.
 * @returns The plan.
 *
 * @example
 * ```ts
 * const plan = tmSelectPlan(inject(TmEntities).get('gl.Center'), ['Code', 'Name', 'Parent']);
 * // plan.select === 'Id,Code,Name,Name2,Parent.Id,Parent.Code' in a two-language tenant
 * const rows = plan.rows(result);
 * ```
 */
export function tmSelectPlan(
  root: TmEntityDescriptor,
  keys: readonly TmColumnInput<string>[],
  extra: readonly string[] = [],
  options: TmSelectPlanOptions = {},
): TmSelectPlan {
  if (options.injector === undefined) {
    assertInInjectionContext(tmSelectPlan);
  }
  const injector = options.injector ?? inject(Injector);
  const entities = injector.get(TmEntities);
  const permissions = injector.get(TmPermissions);
  const names = injector.get(TmDisplayNames);
  const l10n = injector.get(TmLocalization);

  const dropped: TmDroppedKey[] = [];
  const terms: string[] = [];
  const seen = new Set<string>();
  const add = (term: string): void => {
    const folded = term.toLowerCase();
    if (!seen.has(folded)) {
      seen.add(folded);
      terms.push(term);
    }
  };

  // --- The root's key first, as the row identity ---
  const keyTerm = root.key.property;
  add(keyTerm);

  // --- Each key and extra path read against the entity, then checked ---
  const read = (key: string): TmColumnSpec | null => {
    const spec = columnSpec(root, key, entities, names, l10n);
    if (spec === null) {
      dropped.push({ key, reason: 'unknown' });
      return null;
    }
    if (!spec.terms.every((term) => permissions.reachable(root.name, term)())) {
      dropped.push({ key, reason: 'unreachable' });
      return null;
    }
    return spec;
  };

  const columns: TmPlanColumn[] = [];
  const keysSeen = new Set<string>();
  for (const input of keys) {
    // An expression is the server's to check: it selects as written.
    if (typeof input === 'object' && 'expression' in input) {
      if (!keysSeen.has(input.expression)) {
        keysSeen.add(input.expression);
        const spec = expressionSpec(input.expression, input.labelKey, input.type, l10n);
        spec.terms.forEach(add);
        columns.push(new TmPlanColumn(spec, input));
      }
      continue;
    }
    const path = typeof input === 'string' ? input : input.path;
    const spec = read(path);
    if (spec === null || keysSeen.has(spec.key)) {
      continue;
    }
    keysSeen.add(spec.key);
    spec.terms.forEach(add);
    const column = new TmPlanColumn(spec, input);
    if (spec.names) {
      NAMES.set(column, spec.names);
    }
    if (spec.blobPath !== undefined) {
      BLOB_PATHS.set(column, spec.blobPath);
    }
    columns.push(column);

    // A custom cell's own paths are read as extra paths are.
    if (typeof input === 'object') {
      for (const other of input.select ?? []) {
        read(other)?.terms.forEach(add);
      }
    }
  }
  for (const path of extra) {
    read(path)?.terms.forEach(add);
  }

  // --- Each drop reported once, under the host's source ---
  if (dropped.length > 0) {
    const telemetry = injector.get(TmTelemetry);
    const source = options.source ?? 'declaration';
    untracked(() => dropped.forEach(({ reason }) => telemetry.event('tm.entity.column-dropped', { reason, source })));
  }

  return {
    select: terms.join(','),
    columns,
    dropped,
    rows: (set) => planRows(set, columns, keyTerm),
  };
}

/**
 * Reads a row set into plan rows, typing the expression columns from it.
 *
 * @param set - The row set.
 * @param columns - The plan's columns.
 * @param keyTerm - The root key's term.
 * @returns The rows.
 */
function planRows(set: TmQueryRowSet, columns: readonly TmPlanColumn[], keyTerm: string): readonly TmPlanRow[] {
  // --- Each term's cell, by its path as the select spelled it, exact and folded ---
  const index = new Map<string, number>();
  set.columns.forEach((column, cell) => {
    const term = column.path && column.path.length > 0 ? column.path.join('.') : column.name;
    for (const name of [term, column.name, term.toLowerCase(), column.name.toLowerCase()]) {
      if (!index.has(name)) {
        index.set(name, cell);
      }
    }
  });

  // --- Expressions take their type from the response ---
  for (const column of columns) {
    if (column.kind === 'expression') {
      const cell = index.get(column.key) ?? index.get(column.key.toLowerCase());
      if (cell !== undefined) {
        column.ɵtype(set.columns[cell]!);
      }
    }
  }

  // --- One prototype per read, with a getter per column key, so a grid reads `row[key]` ---
  class TmKeyedRow extends TmRow {}
  for (const column of columns) {
    if (!RESERVED.has(column.key)) {
      Object.defineProperty(TmKeyedRow.prototype, column.key, {
        get(this: TmPlanRow) {
          return column.value(this);
        },
        enumerable: true,
      });
    }
  }
  return set.rows.map((cells) => new TmKeyedRow(cells, index, keyTerm));
}

/**
 * The spec of an expression column: it selects and orders by itself, and a
 * row set types it.
 *
 * @param expression - The Queryex expression.
 * @param labelKey - The string key of its label.
 * @param type - Its type, when the declaration knows it.
 * @param l10n - The localization.
 * @returns The spec.
 */
function expressionSpec(expression: string, labelKey: string, type: TmDataType | undefined, l10n: TmLocalization): TmColumnSpec {
  return {
    key: expression,
    kind: 'expression',
    resolution: null,
    descriptor: type === undefined ? null : { type, nullable: true },
    label: l10n.ɵtext(labelKey),
    terms: [expression],
    order: () => [expression],
    value: (row) => row.get(expression) ?? null,
  };
}

/**
 * The spec of a path column, or `null` when the path does not resolve or
 * names a property queries cannot read.
 *
 * @param root - The root's descriptor.
 * @param key - The path.
 * @param entities - The entity registry.
 * @param names - The display names.
 * @param l10n - The localization.
 * @returns The spec, or `null`.
 */
function columnSpec(
  root: TmEntityDescriptor,
  key: string,
  entities: TmEntities,
  names: TmDisplayNames,
  l10n: TmLocalization,
): TmColumnSpec | null {
  const resolution = entities.resolve(root.name, key);
  if (resolution === null) {
    return null;
  }
  const { end, steps } = resolution;
  const under = (name: string): string => [...steps.map((step) => step.name), name].join('.');
  const base = { resolution, label: resolution.label };

  switch (end.kind) {
    // --- A scalar or a twin: itself ---
    case 'value': {
      if (!end.property.queryable) {
        return null;
      }
      const path = under(end.property.name);
      return {
        ...base,
        key: path,
        kind: 'value',
        descriptor: end.property,
        terms: [path],
        order: () => [path],
        value: (row) => row.get(path) ?? null,
      };
    }

    // --- A group: every admitted slot; the active one shown and ordered ---
    case 'text': {
      const slots = [...end.slots].sort((a, b) => (a.multilingual?.slot ?? 1) - (b.multilingual?.slot ?? 1)).map((slot) => under(slot.name));
      const shown = (): string | undefined => slots[l10n.contentSlot() - 1];
      return {
        ...base,
        key: slots[0]!,
        kind: 'text',
        descriptor: end.slots.find((slot) => (slot.multilingual?.slot ?? 1) === 1) ?? end.slots[0]!,
        terms: slots,
        order: () => groupOrder(slots, l10n.contentSlot()),
        value: (row) => {
          const active = shown();
          const value = active === undefined ? undefined : row.get(active);
          return empty(value) ? (row.get(slots[0]!) ?? null) : value;
        },
      };
    }

    // --- A related row: its id and its display name's terms ---
    case 'navigation': {
      const path = under(end.navigation.name);
      const target = entities.get(end.navigation.target);
      const select = names.select(target.name, path);
      const idTerm = `${path}.${target.key.property}`;
      const members = select.map((term) => {
        const name = term.slice(path.length + 1);
        return { path: term, json: target.property(name)?.json ?? name.charAt(0).toLowerCase() + name.slice(1) };
      });
      return {
        ...base,
        key: path,
        kind: 'navigation',
        descriptor: {
          type: 'integer',
          nullable: end.navigation.nullable,
          target: target.name,
          ...(target.key.type === 'long' && { long: true }),
        },
        terms: [idTerm, ...select],
        order: () => ɵtmDisplayNameOrder(target, entities, l10n.contentSlot(), path),
        value: (row) => row.get(idTerm) ?? null,
        names: { entity: target.name, members },
      };
    }

    // --- A blob: its id, with its variants or its file name ---
    case 'image':
    case 'file': {
      const path = under(end.navigation.name);
      const idTerm = resolution.fkPath!;
      const nameTerm = `${path}.${end.kind === 'image' ? 'Variants' : 'FileName'}`;
      return {
        ...base,
        key: path,
        kind: end.kind,
        descriptor: { type: 'integer', nullable: end.navigation.nullable },
        terms: [idTerm, nameTerm],
        order: () => (end.kind === 'file' ? [nameTerm] : null),
        value: (row) => row.get(idTerm) ?? null,
        blobPath: path,
      };
    }
  }
}

/**
 * A group's order terms in a slot: `coalesce(NameS, Name)` beyond the first
 * slot, so a row with no text in the slot sorts by the first one, as it
 * shows; the first slot alone otherwise.
 *
 * @param slots - The group's slot paths, the first slot's first.
 * @param slot - The active slot.
 * @returns The terms.
 */
function groupOrder(slots: readonly string[], slot: number): readonly string[] {
  const active = slots[slot - 1];
  return [slot > 1 && active !== undefined ? `coalesce(${active}, ${slots[0]})` : slots[0]!];
}

/**
 * The order terms of an entity's display name: its prefix, then its name,
 * as a group orders when the name is multilingual. Internal: a navigation
 * column sorts by them under its path, and the entity pickers order their
 * results by them from the root.
 *
 * @param target - The entity's descriptor.
 * @param entities - The entity registry.
 * @param slot - The active slot.
 * @param path - The navigation's path; the root's own terms when omitted.
 * @returns The terms.
 */
export function ɵtmDisplayNameOrder(target: TmEntityDescriptor, entities: TmEntities, slot: number, path?: string): readonly string[] {
  const under = (name: string): string => (path ? `${path}.${name}` : name);
  const rule = entities.summary(target.name)?.displayName;
  if (rule === undefined) {
    return [under(target.key.property)];
  }
  const canonical = (name: string): string => under(target.property(name)?.name ?? name);
  const terms: string[] = rule.prefix === undefined ? [] : [canonical(rule.prefix)];
  if (rule.multilingual) {
    const slots = target.properties
      .filter((property: TmPropertyDescriptor) => property.multilingual?.group.toLowerCase() === rule.source.toLowerCase())
      .sort((a, b) => a.multilingual!.slot - b.multilingual!.slot)
      .map((property) => under(property.name));
    terms.push(...(slots.length > 0 ? groupOrder(slots, slot) : [canonical(rule.source)]));
  } else {
    terms.push(canonical(rule.source));
  }
  return [...new Set(terms)];
}
