// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import { assertInInjectionContext, inject, Injector } from '@angular/core';
import { TmDecimals, TmEntities, TmLocalization, type TmJson, type TmPlanRow, type TmValueDescriptor } from '@tellma/core';
import { TM_PARSE_ERROR, type TmParseContext, type TmParseError } from '@tellma/core-ui/contracts';
import type { TmGridColumnAlign } from '@tellma/core-ui/grid';
import type { TmGridColumnType } from '@tellma/core-ui/grid-engine';
import { TM_NUMBER_MAX_DIGITS, tmNumberDigitCount, tmParseNumber } from '@tellma/core-ui/l10n';

import { TmFormats } from './tm-formats';
import type { TmColumnPlan } from './tm-select-plan';

// A grid column from a select plan's column (spec 0029 §12.2's last column):
// the grid type, alignment, width, display and parse each type takes, so a
// search grid and a form's child grid bind every column the same way. A
// search grid's rows are plan rows, read by the column's key; a child
// grid's rows are the form model's wire objects, read by JSON names.

/**
 * The `tm-grid-column` inputs a column binds, by input name: a host binds
 * each member it is given to the input of the same name.
 */
export interface TmGridColumnConfig<T = unknown> {
  /** The row member the cell reads and edits. */
  readonly key: string;
  /** The grid's column type. */
  readonly type: TmGridColumnType;
  /** Reads a cell's value when the cell has no plain member. */
  readonly value: (row: T) => unknown;
  /** The cell's text, and its edit text. */
  readonly format: (value: unknown, row: T) => string;
  /** Reads edit text back into a value. */
  readonly parse: (text: string, context: TmParseContext) => unknown;
  /** An enum's values, in order. */
  readonly options: readonly unknown[];
  /** An enum value's label. */
  readonly optionLabel: (option: never) => string;
  /** An enum value's stored value. */
  readonly optionValue: (option: never) => unknown;
  /** Whether the column's cells, or one row's cell, are read-only. */
  readonly readonly: boolean | ((row: T) => boolean);
  /** The column's width in pixels. */
  readonly width: number;
  /** The cell's alignment. */
  readonly align: TmGridColumnAlign;
  /** The fewest fraction digits a number shows. */
  readonly minDecimals: number;
  /** The most fraction digits a number shows and accepts. */
  readonly maxDecimals: number;
  /** Whether the header sorts by the column. */
  readonly sortable: boolean;
}

/** How a grid column is made from a plan's column. */
export interface TmGridColumnOptions {
  /** Whether the grid edits the column's cells; a read-only column when omitted. */
  readonly editable?: boolean;
  /**
   * What the grid's rows are: a plan's rows, read by the column's key, or
   * the form model's wire objects, read by JSON names. `model` when
   * `editable` is given, else `plan`.
   */
  readonly rows?: 'plan' | 'model';
  /** The injector the column reads the formats through; the current injection context when omitted. */
  readonly injector?: Injector;
}

/** Each value type's default width, in pixels. */
const WIDTHS: Readonly<Record<string, number>> = {
  string: 200,
  integer: 120,
  decimal: 140,
  float: 140,
  bool: 90,
  date: 120,
  datetime: 170,
  instant: 170,
  enum: 140,
  guid: 280,
  time: 100,
};

/** The types a grid cell edits through the binding of this entry point; the rest are read-only. */
const EDITABLE = new Set(['string', 'integer', 'decimal', 'bool', 'date', 'enum', 'guid', 'time']);

/**
 * Whether a model row is new, so a write-once value is still editable: no
 * id yet, or the negative id a new child row carries.
 *
 * @param row - The model row.
 * @returns Whether it is new.
 */
function isNew(row: unknown): boolean {
  const id = (row as { readonly id?: unknown } | null)?.id;
  return id === null || id === undefined || (typeof id === 'number' && id < 0) || (typeof id === 'string' && id.startsWith('-'));
}

/**
 * The `tm-grid-column` inputs a select plan's column binds: its grid type,
 * alignment, width and sort, and its display and parse. A decimal or a
 * `long` displays and parses exactly, at its scale; a number keeps its
 * physical right alignment in either direction. Only a scalar at the
 * row's own level edits; groups, navigations, blobs, instants and
 * expressions stay read-only, and a write-once value edits only in a new
 * row. A host binds `header` to the column's label itself, which follows
 * the language.
 *
 * @param column - The plan's column.
 * @param options - Whether the grid edits it, and what its rows are.
 * @returns The inputs to bind.
 *
 * @example
 * ```html
 * @for (column of plan.columns; track column.key) {
 *   @let config = tmGridColumnFor(column);
 *   <tm-grid-column [key]="config.key" [type]="config.type" [header]="column.label()" [format]="config.format" />
 * }
 * ```
 */
export function tmGridColumnFor(column: TmColumnPlan, options: TmGridColumnOptions = {}): Partial<TmGridColumnConfig> {
  if (options.injector === undefined) {
    assertInInjectionContext(tmGridColumnFor);
  }
  const injector = options.injector ?? inject(Injector);
  const formats = injector.get(TmFormats);
  const entities = injector.get(TmEntities);
  const model = (options.rows ?? (options.editable === undefined ? 'plan' : 'model')) === 'model';
  const of: TmValueDescriptor | null = column.descriptor;
  const type = of?.type ?? null;

  // --- Where a cell's value lives: a plan row's key, or a model row's JSON path ---
  const resolution = column.resolution;
  const json =
    resolution === null
      ? null
      : [
          ...resolution.steps.map((step) => step.json),
          resolution.end.kind === 'value'
            ? resolution.end.property.json
            : resolution.end.kind === 'text'
              ? resolution.end.slots[0]!.json
              : // A related row or a blob is its foreign key's id in a model row.
                (entities.get(resolution.entity).property(resolution.end.navigation.foreignKey)?.json ?? resolution.end.navigation.json),
        ];
  const at = (row: unknown, path: readonly string[]): unknown => {
    let value: unknown = row;
    for (const segment of path) {
      value = value === null || typeof value !== 'object' ? undefined : (value as Record<string, TmJson>)[segment];
    }
    return value ?? null;
  };
  let read = (row: unknown): unknown => at(row, json ?? []);
  if (resolution?.end.kind === 'text' && json !== null) {
    // A group shows the active slot, else the first, as a plan's cell does.
    const l10n = injector.get(TmLocalization);
    const prefix = json.slice(0, -1);
    const slots = [...resolution.end.slots].sort((a, b) => (a.multilingual?.slot ?? 1) - (b.multilingual?.slot ?? 1));
    read = (row) => {
      const active = slots[l10n.contentSlot() - 1];
      const value = active === undefined ? null : at(row, [...prefix, active.json]);
      return value === null || value === '' ? at(row, [...prefix, slots[0]!.json]) : value;
    };
  }
  const config: { -readonly [K in keyof TmGridColumnConfig]?: TmGridColumnConfig[K] } = model
    ? json !== null && json.length === 1 && column.kind === 'value'
      ? { key: json[0]! }
      : { value: read }
    : { key: column.key, sortable: column.orderBy('asc') !== null };

  // --- What only a scalar at the row's own level, of an editable type, edits ---
  const property = resolution?.end.kind === 'value' && resolution.steps.length === 0 ? resolution.end.property : null;
  const editable = model && options.editable === true && property !== null && EDITABLE.has(property.type);
  const ownership = property?.ownership ?? 'server-owned';
  config.readonly =
    !editable || ownership === 'server-owned' || ownership === 'database-owned' || ownership === 'derived'
      ? true
      : ownership === 'write-once'
        ? (row: unknown) => !isNew(row)
        : false;
  config.width = column.kind === 'image' ? 72 : column.kind === 'navigation' ? 200 : (WIDTHS[type ?? 'string'] ?? 160);

  // --- The display text: the plan's cell, or the value's grid text ---
  const text = model
    ? (value: unknown): string => (of === null ? (value === null || value === undefined ? '' : String(value)) : formats.ɵgridText(value, of))
    : (_value: unknown, row: unknown): string => formats.cell(column, row as TmPlanRow);

  // --- The grid type each value type takes ---
  if (column.kind === 'value' || column.kind === 'expression') {
    switch (type) {
      case 'integer':
      case 'decimal':
        Object.assign(config, { type: 'number', align: 'right', minDecimals: of?.scale ?? 0, maxDecimals: of?.scale ?? 0 });
        // A decimal or a long is decimal text: shown and parsed exactly, never through the grid's double.
        if (type === 'decimal' || of?.long) {
          config.format = text;
          config.parse = (input, context) => parseDecimal(input, context, of?.scale ?? 0);
        }
        return config;
      case 'float':
        Object.assign(config, { type: 'number', align: 'right', format: text, readonly: true });
        return config;
      case 'bool':
        config.type = 'boolean';
        return config;
      case 'date':
        config.type = 'date';
        return config;
      case 'enum': {
        const values = (of?.enum !== undefined && entities.enum(of.enum)?.values) || [];
        const label = (value: unknown): string => formats.ɵgridText(value, of!);
        Object.assign(config, {
          type: of?.array ? 'text' : 'enum',
          ...(!of?.array && {
            options: values,
            optionValue: (option: { readonly value: string }) => option.value,
            optionLabel: (option: { readonly value: string }) => label(option.value),
          }),
          format: text,
          ...(of?.array && { readonly: true }),
        });
        return config;
      }
      case 'string':
      case 'guid':
      case 'time':
        config.type = 'text';
        return config;
      default:
        // Instants, date-times, JSON, binary and an untyped expression show their text alone.
        Object.assign(config, { type: 'text', format: text, readonly: true });
        return config;
    }
  }

  // --- Groups, navigations and blobs show the plan's cell, read-only ---
  Object.assign(config, { type: column.kind === 'image' ? 'custom' : 'text', format: text, readonly: true });
  return config;
}

/**
 * Reads a number cell's edit text into decimal text, rounded to the
 * column's scale and held to the digits a double carries.
 *
 * @param text - The edit text.
 * @param context - The parse context.
 * @param scale - The column's fraction digits.
 * @returns The decimal text, `null` for empty text, or the parse error.
 */
function parseDecimal(text: string, context: TmParseContext, scale: number): TmJson | TmParseError {
  const number = tmParseNumber(text, context.locale, { sourceLocale: context.sourceLocale, minDecimals: scale, maxDecimals: scale });
  if (number === null || number === TM_PARSE_ERROR) {
    return number;
  }
  const rounded = Number(number.toFixed(scale));
  return tmNumberDigitCount(rounded) > TM_NUMBER_MAX_DIGITS ? TM_PARSE_ERROR : TmDecimals.fromNumber(rounded);
}
