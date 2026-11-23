// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import { computed, inject, Injectable, untracked, type Signal } from '@angular/core';
import {
  TmDisplayNames,
  TmEntities,
  TmLocalization,
  type TmDecimal,
  type TmId,
  type TmJsonObject,
  type TmPlanRow,
  type TmPropertyDescriptor,
  type TmValueDescriptor,
} from '@tellma/core';
import { TmL10n, type TmZoneLabelMode } from '@tellma/core-ui';
import { tmFormatDateTime, tmStripBidiControls, type TmDateStyle } from '@tellma/core-ui/l10n';

import { ɵTM_VALUE_STRINGS_EN } from './tm-entity-strings';
import { ɵtmPlanNames, type TmColumnPlan } from './tm-select-plan';

// Typed values as text (spec 0029 §12): one format per value descriptor, the
// same on every surface, following the language, the regional formats, the
// calendar and the display zone as they switch. Decimals and longs travel
// as decimal text and are formatted from that text, never through a
// double, so a 28-digit amount prints every digit it has.

/** How a surface shows a value: a details view, or a grid cell. */
export type TmValueStyle = 'details' | 'grid';

/** The text formats a decimal's exact formatter is cached by. */
const FORMATTERS = new Map<string, Intl.NumberFormat>();

/** The list formatters, by locale. */
const LISTS = new Map<string, Intl.ListFormat>();

/**
 * An exact number formatter: `Intl.NumberFormat` formats decimal text at
 * full precision, where a double would round past 15 digits.
 *
 * @param locale - The locale.
 * @param decimals - The fraction digits to show, at least and at most; free when omitted.
 * @param grouping - Whether the digits are grouped.
 * @returns The formatter.
 */
function formatter(locale: string, decimals: number | undefined, grouping: boolean): Intl.NumberFormat {
  const key = `${locale}\n${decimals ?? ''}\n${grouping}`;
  let format = FORMATTERS.get(key);
  if (format === undefined) {
    format = new Intl.NumberFormat(locale, {
      ...(decimals === undefined ? { maximumFractionDigits: 20 } : { minimumFractionDigits: decimals, maximumFractionDigits: decimals }),
      ...(!grouping && { useGrouping: false }),
    });
    FORMATTERS.set(key, format);
  }
  return format;
}

/**
 * Formats a number or decimal text exactly in a locale, its digits and
 * grouping, with no bidi control.
 *
 * @param value - The number or decimal text.
 * @param locale - The locale.
 * @param decimals - The fraction digits to show; free when omitted.
 * @param grouping - Whether the digits are grouped; they are when omitted.
 * @returns The text, or `null` when the value is no number.
 */
export function ɵtmFormatExact(value: unknown, locale: string, decimals?: number, grouping = true): string | null {
  if (typeof value === 'number' ? !Number.isFinite(value) : typeof value !== 'string' || !/^-?\d+(?:\.\d+)?$/.test(value)) {
    return null;
  }
  // The ES2023 signature takes decimal text as an exact decimal; the
  // project's library target predates it.
  const format = formatter(locale, decimals, grouping).format as (value: number | string) => string;
  return tmStripBidiControls(format(value as number | TmDecimal));
}

/**
 * Formats typed values as text: numbers in the locale's digits, decimals
 * exactly at their scale, dates in the active calendar, instants in the
 * display zone, enum values by their labels and ids by display names. A
 * caller reading inside a template or a `computed()` re-renders on every
 * switch of language, formats, calendar or zone.
 *
 * @example
 * ```ts
 * const formats = inject(TmFormats);
 * formats.text('1234.5', { type: 'decimal', nullable: false, scale: 2 }); // "1,234.50"
 * formats.cell(plan.columns[0], rows[0]);
 * ```
 */
@Injectable({ providedIn: 'root' })
export class TmFormats {
  readonly #l10n = inject(TmL10n);
  readonly #localization = inject(TmLocalization);
  readonly #entities = inject(TmEntities);
  readonly #names = inject(TmDisplayNames);

  /** Registers the entry point's strings, which every typed value reads through this service. */
  constructor() {
    this.#localization.ɵregisterStrings('value', ɵTM_VALUE_STRINGS_EN);
  }

  /** The related rows a navigation cell names, by row and column, so a row's display name is computed once. */
  readonly #related = new WeakMap<TmPlanRow, Map<TmColumnPlan, TmJsonObject>>();

  /** The zone suffixes made so far, by descriptor or column. */
  readonly #suffixes = new WeakMap<object, Signal<string>>();

  /**
   * A value as text. A `null` is empty in a grid and "—" in a details view;
   * an instant carries the display zone's offset when it differs from the
   * device's at that instant.
   *
   * @param value - The value, as the wire sends it.
   * @param of - The value's type facts.
   * @param style - The surface; `details` when omitted.
   * @returns The text.
   */
  text(value: unknown, of: TmValueDescriptor, style: TmValueStyle = 'details'): string {
    return this.#text(value, of, style, 'auto');
  }

  /**
   * A row's cell as text: a group's shown slot, a navigation's display
   * name, a file's name, and any other value as `text` formats it in a
   * grid. An instant's zone label follows its column header's (`column`
   * mode): a cell is labelled only when its offset differs from the
   * header's.
   *
   * @param column - The column.
   * @param row - The row.
   * @returns The text; empty for an image, which a thumbnail shows.
   */
  cell(column: TmColumnPlan, row: TmPlanRow): string {
    const value = column.value(row);
    switch (column.kind) {
      case 'text':
        return empty(value) ? '' : String(value);
      case 'navigation': {
        const names = ɵtmPlanNames(column);
        return empty(value) || names === undefined ? '' : this.#names.of(names.entity, this.ɵrelated(column, row))();
      }
      case 'image':
        return '';
      case 'file': {
        const name = row.get(`${column.key}.FileName`);
        return empty(name) ? '' : String(name);
      }
      default: {
        const of = column.descriptor;
        // The key property of the column's entity is an id, written ungrouped as a foreign key is.
        const end = column.resolution?.end;
        const key = end?.kind === 'value' && this.#entities.get(column.resolution!.entity).key.property === end.property.name;
        return of === null ? (empty(value) ? '' : String(value)) : this.#text(value, of, 'grid', 'column', key);
      }
    }
  }

  /**
   * The zone label a column header of instants shows while the display
   * zone's offset differs from the device's, such as " (GMT+3)"; empty for
   * any other value and while the offsets agree.
   *
   * @param of - The value's type facts, or a column.
   * @returns The suffix, which follows the zones.
   */
  zoneSuffix(of: TmValueDescriptor | TmColumnPlan): Signal<string> {
    let suffix = this.#suffixes.get(of);
    if (suffix === undefined) {
      suffix = computed(() => {
        return of.type === 'instant' && this.#l10n.zoneMismatch() ? ` (${this.#l10n.zoneLabel()})` : '';
      });
      this.#suffixes.set(of, suffix);
    }
    return suffix;
  }

  /**
   * A value as a grid cell's text, an instant labelled as its column's
   * header is (`column` mode). Internal: the grid columns of this entry
   * point format a form model's cells through it.
   *
   * @param value - The value.
   * @param of - Its type facts.
   * @returns The text.
   */
  ɵgridText(value: unknown, of: TmValueDescriptor): string {
    return this.#text(value, of, 'grid', 'column');
  }

  /**
   * A value as text under a zone label mode.
   *
   * @param value - The value.
   * @param of - Its type facts.
   * @param style - The surface.
   * @param zone - How an instant is labelled.
   * @param id - Whether the value is an entity's key, written ungrouped.
   * @returns The text.
   */
  #text(value: unknown, of: TmValueDescriptor, style: TmValueStyle, zone: TmZoneLabelMode, id = false): string {
    if (empty(value)) {
      return style === 'grid' ? '' : this.#localization.ɵtext('tm.value.none')();
    }

    // --- A list joins its items' texts in the locale ---
    if (of.array && Array.isArray(value)) {
      const item: TmValueDescriptor = { ...of, array: undefined };
      const parts = value.map((element) => this.#text(element, item, 'grid', zone)).filter((part) => part !== '');
      const locale = this.#l10n.locale();
      let list = LISTS.get(locale);
      if (list === undefined) {
        LISTS.set(locale, (list = new Intl.ListFormat(locale, { type: 'unit', style: 'short' })));
      }
      return tmStripBidiControls(list.format(parts));
    }

    // --- An id names its row ---
    if (of.target !== undefined) {
      const id = value as TmId;
      const label = this.#names.label(of.target, id)();
      if (label === null) {
        untracked(() => this.#names.resolve(of.target!, [id]));
        return '';
      }
      return label;
    }

    const dateStyle: TmDateStyle = style === 'grid' ? 'numeric' : 'medium';
    switch (of.type) {
      case 'integer': {
        // An id, a foreign key or an entity's key, is written ungrouped in the
        // locale's digits (spec 0029 §5.2, spec 0010 §D.15).
        const ungrouped = id || (of as Partial<TmPropertyDescriptor>).foreignKeyOf !== undefined;
        return ɵtmFormatExact(value, this.#l10n.locale(), 0, !ungrouped) ?? String(value);
      }
      case 'decimal':
        return ɵtmFormatExact(value, this.#l10n.locale(), of.scale) ?? String(value);
      case 'float':
        return this.#l10n.formatNumber(value);
      case 'bool':
        return this.#localization.ɵtext(value === true ? 'tm.value.yes' : 'tm.value.no')();
      case 'date':
        return this.#l10n.formatDate(String(value), { dateStyle });
      case 'instant':
        return this.#l10n.formatDateTime(String(value), { dateStyle, timeStyle: 'short', zoneLabel: zone });
      case 'datetime':
        // A wall-clock date-time shows as stored: read as UTC, shown in UTC, never labelled.
        return tmFormatDateTime(`${String(value).replace(/(Z|[+-]\d{2}:\d{2})$/, '')}Z`, this.#l10n.locale(), {
          timeZone: 'UTC',
          calendar: this.#l10n.calendar(),
          dateStyle,
          timeStyle: 'short',
          zoneLabel: false,
        });
      case 'enum':
        return (of.enum !== undefined && this.#entities.enum(of.enum)?.label(String(value))()) || String(value);
      case 'json':
        return JSON.stringify(value);
      default:
        return String(value);
    }
  }

  /**
   * The related row a navigation cell's display name reads, made once per
   * row and column from the display name's terms. Internal: `tm-value`
   * renders a navigation cell's parts from it.
   *
   * @param column - The navigation column.
   * @param row - The row.
   * @returns The related row as a wire object.
   */
  ɵrelated(column: TmColumnPlan, row: TmPlanRow): TmJsonObject {
    let byColumn = this.#related.get(row);
    let related = byColumn?.get(column);
    if (related === undefined) {
      const object: Record<string, unknown> = {};
      for (const member of ɵtmPlanNames(column)?.members ?? []) {
        object[member.json] = row.get(member.path) ?? null;
      }
      related = object as TmJsonObject;
      if (byColumn === undefined) {
        this.#related.set(row, (byColumn = new Map()));
      }
      byColumn.set(column, related);
    }
    return related;
  }
}

/** Whether a value is absent: `null`, `undefined` or empty text. */
function empty(value: unknown): boolean {
  return value === null || value === undefined || value === '';
}
