// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import {
  assertInInjectionContext,
  ChangeDetectionStrategy,
  Component,
  computed,
  inject,
  Injector,
  input,
  type InputSignal,
  type Signal,
} from '@angular/core';
import { TmDecimals, TmLocalization, type TmDataType, type TmDecimal, type TmId } from '@tellma/core';
import { TmL10n } from '@tellma/core-ui';
import { tmStripBidiControls, ɵtmInstantOfWallTime } from '@tellma/core-ui/l10n';

import { ɵTM_FILTER_STRINGS_EN } from './tm-entity-strings';
import { ɵtmFormatExact, TmFormats } from './tm-formats';
import { tmQx, tmQxPrint, ɵtmQxIsDate, type TmQx } from './tm-qx';
import { ɵtmGroupSlots, ɵtmPlanEnumValues, type TmColumnPlan, type TmFilterKind } from './tm-select-plan';

// Column filters (spec 0029 §13): the culture-free `f.{key}` value of the
// search URL (spec 0024 §9.5), the Queryex leaf it becomes, and the
// sentence a filter chip shows. Each value is one leaf, built as a tree and
// printed, never concatenated; a value the column cannot take yields no
// leaf, and the host drops it.

/** A column filter's operator. */
export type TmFilterOp =
  | 'contains'
  | 'starts-with'
  | 'ends-with'
  | 'eq'
  | 'ne'
  | 'lt'
  | 'le'
  | 'gt'
  | 'ge'
  | 'between'
  | 'before'
  | 'after'
  | 'in'
  | 'is';

/** The number of values at and above which an enum filters through `tm-entity-multi-picker` rather than `tm-multi-select`. */
export const TM_ENUM_PICKER_THRESHOLD = 50;

/** A column filter: its operator and its values, culture-free (Latin digits, ISO dates). */
export interface TmFilterSpec {
  /** The operator. */
  readonly op: TmFilterOp;
  /** The values: one for most operators, two for `between`, one or more for `in`. */
  readonly values: readonly string[];
}

/** A filter's sentence, such as "Name contains jed", and its parts. */
export interface TmFilterDescription {
  /** The whole sentence as plain text, for an accessible name. */
  readonly text: string;
  /** The sentence in order: the message's words, and each argument (a label or a value) as its own part. */
  readonly parts: readonly {
    /** The part's text. */
    readonly text: string;
    /** Whether the part is an argument, isolated in a `<bdi>` when rendered. */
    readonly argument: boolean;
  }[];
}

/** How a filter's description is made beyond the column and the filter. */
export interface TmDescribeFilterOptions {
  /** The injector the description reads the formats through; the current injection context when omitted. */
  readonly injector?: Injector;
}

/** Each kind's operators, the default first. Internal: the filter control offers them. */
export const ɵTM_FILTER_OPS: Readonly<Record<TmFilterKind, readonly TmFilterOp[]>> = {
  text: ['contains', 'starts-with', 'ends-with', 'eq', 'ne'],
  number: ['between', 'lt', 'le', 'eq', 'ge', 'gt'],
  date: ['between', 'before', 'after'],
  instant: ['between', 'before', 'after'],
  enum: ['in'],
  bool: ['is'],
  navigation: ['in'],
};

/** Every operator. */
const OPS: ReadonlySet<string> = new Set(Object.values(ɵTM_FILTER_OPS).flat());

/** The operators whose value is a list. */
const LIST_OPS: ReadonlySet<TmFilterOp> = new Set(['in', 'between']);

/**
 * Reads an `f.` value, `{op}:{value}`. A list operator's value is split on
 * its commas, `\,` and `\\` escaping a comma and a backslash; any other
 * operator takes the whole remainder as its one value.
 *
 * @param value - The value, such as `between:1,5`.
 * @returns The filter, or `null` when the value is malformed.
 *
 * @example
 * ```ts
 * tmParseFilter('between:1,5'); // { op: 'between', values: ['1', '5'] }
 * tmParseFilter('contains:a,b'); // { op: 'contains', values: ['a,b'] }
 * ```
 */
export function tmParseFilter(value: string): TmFilterSpec | null {
  const colon = typeof value === 'string' ? value.indexOf(':') : -1;
  const op = colon < 0 ? '' : value.slice(0, colon);
  const rest = colon < 0 ? '' : value.slice(colon + 1);
  if (!OPS.has(op) || rest === '') {
    return null;
  }
  if (!LIST_OPS.has(op as TmFilterOp)) {
    return { op: op as TmFilterOp, values: [rest] };
  }

  // --- Split on unescaped commas; a backslash escapes only `,` and `\` ---
  const values: string[] = [''];
  for (let index = 0; index < rest.length; index++) {
    const char = rest[index]!;
    if (char === '\\') {
      const next = rest[++index];
      if (next !== ',' && next !== '\\') {
        return null;
      }
      values[values.length - 1] += next;
    } else if (char === ',') {
      values.push('');
    } else {
      values[values.length - 1] += char;
    }
  }
  return values.every((item) => item !== '') && (op === 'in' || values.length === 2) ? { op: op as TmFilterOp, values } : null;
}

/**
 * Writes a filter as its `f.` value, escaping a list's commas and
 * backslashes.
 *
 * @param spec - The filter.
 * @returns The value, such as `in:Service,Sale`.
 */
export function tmFormatFilter(spec: TmFilterSpec): string {
  const value = LIST_OPS.has(spec.op) ? spec.values.map((item) => item.replace(/[\\,]/g, (char) => `\\${char}`)).join(',') : (spec.values[0] ?? '');
  return `${spec.op}:${value}`;
}

/**
 * The day after an ISO date.
 *
 * @param date - The date, `yyyy-MM-dd`.
 * @returns The next date.
 */
function nextDay(date: string): string {
  const day = new Date(0);
  // Set as a whole, so the years before 100 are not read as 19xx.
  day.setUTCFullYear(Number(date.slice(0, 4)), Number(date.slice(5, 7)) - 1, Number(date.slice(8, 10)) + 1);
  return day.toISOString().slice(0, 10);
}

/**
 * The bounds of whole days in a column's type: the date itself for a
 * date, its midnight for a date-time, and its first instant in the zone for
 * an instant (midnight, or the change itself when midnight does not exist).
 *
 * @param type - The column's type.
 * @param date - The day, `yyyy-MM-dd`.
 * @param timeZone - The display zone.
 * @returns The day's start as a literal.
 */
function dayStart(type: TmDataType, date: string, timeZone: string): TmQx {
  switch (type) {
    case 'datetime':
      return tmQx.literal('datetime', `${date}T00:00:00`);
    case 'instant':
      return tmQx.literal('instant', ɵtmInstantOfWallTime({ date, time: '00:00' }, timeZone));
    default:
      return tmQx.literal('date', date);
  }
}

/**
 * The tree of one filter on one column, or `null` when the values do not
 * fit the operator.
 *
 * @param column - The column.
 * @param kind - Its filter kind.
 * @param spec - The filter.
 * @param timeZone - The display zone.
 * @returns The tree, or `null`.
 */
function leafOf(column: TmColumnPlan, kind: TmFilterKind, spec: TmFilterSpec, timeZone: string): TmQx | null {
  const { op, values } = spec;
  const single = values.length === 1 ? values[0]! : null;
  const type = column.descriptor?.type ?? 'string';

  switch (kind) {
    // --- Text: every admitted slot of a group, `or`-joined, `ne` `and`-joined ---
    case 'text': {
      if (single === null || single === '') {
        return null;
      }
      const slots = column.kind === 'text' && column.resolution !== null ? ɵtmGroupSlots(column.resolution) : [column.key];
      const value = tmQx.str(single);
      const leaves = slots.map((slot) => {
        const path = tmQx.path(slot);
        switch (op) {
          case 'contains':
            return tmQx.call('contains', path, value);
          case 'starts-with':
            return tmQx.call('startsWith', path, value);
          case 'ends-with':
            return tmQx.call('endsWith', path, value);
          case 'ne':
            return tmQx.ne(path, value);
          default:
            return tmQx.eq(path, value);
        }
      });
      return op === 'ne' ? tmQx.and(...leaves) : tmQx.or(...leaves);
    }

    // --- Numbers: exact decimal text, a range in order ---
    case 'number': {
      const path = tmQx.path(column.key);
      if (op === 'between') {
        if (values.length !== 2 || !values.every((item) => TmDecimals.isDecimal(item))) {
          return null;
        }
        const [from, to] = values as [TmDecimal, TmDecimal];
        return TmDecimals.compare(from, to) > 0 ? null : tmQx.and(tmQx.ge(path, tmQx.literal(type, from)), tmQx.le(path, tmQx.literal(type, to)));
      }
      if (single === null || !TmDecimals.isDecimal(single)) {
        return null;
      }
      const bound = tmQx.literal(type, single);
      const compare = { lt: tmQx.lt, le: tmQx.le, eq: tmQx.eq, ge: tmQx.ge, gt: tmQx.gt }[op as 'lt' | 'le' | 'eq' | 'ge' | 'gt'];
      return compare(path, bound);
    }

    // --- Days: a date compares as itself, a date-time and an instant by day starts ---
    case 'date':
    case 'instant': {
      if (!values.every((item) => ɵtmQxIsDate(item))) {
        return null;
      }
      const path = tmQx.path(column.key);
      const days = kind === 'instant' ? 'instant' : type;
      if (op === 'between') {
        const [from, to] = values;
        if (values.length !== 2 || from! > to!) {
          return null;
        }
        return days === 'date'
          ? tmQx.and(tmQx.ge(path, dayStart(days, from!, timeZone)), tmQx.le(path, dayStart(days, to!, timeZone)))
          : tmQx.and(tmQx.ge(path, dayStart(days, from!, timeZone)), tmQx.lt(path, dayStart(days, nextDay(to!), timeZone)));
      }
      if (single === null) {
        return null;
      }
      if (op === 'before') {
        return tmQx.lt(path, dayStart(days, single, timeZone));
      }
      // After a date-time's or an instant's day is from the next day's start.
      return days === 'date' ? tmQx.gt(path, dayStart(days, single, timeZone)) : tmQx.ge(path, dayStart(days, nextDay(single), timeZone));
    }

    // --- Enum values the enum holds, each once ---
    case 'enum': {
      const known = ɵtmPlanEnumValues(column);
      if (values.length === 0 || (known !== undefined && !values.every((item) => known.has(item)))) {
        return null;
      }
      return tmQx.in(tmQx.path(column.key), [...new Set(values)].map((item) => tmQx.str(item)));
    }

    // --- True or false ---
    case 'bool':
      return single === 'true' || single === 'false' ? tmQx.eq(tmQx.path(column.key), tmQx.bool(single === 'true')) : null;

    // --- Related rows, by the id the navigation holds ---
    case 'navigation': {
      const fk = column.resolution?.fkPath;
      if (fk === undefined || values.length === 0 || !values.every((item) => /^\d+$/.test(item))) {
        return null;
      }
      return tmQx.in(tmQx.path(fk), [...new Set(values)].map((item) => tmQx.num(item)));
    }
  }
}

/**
 * The Queryex leaf of a column filter: a group reads every admitted slot,
 * an instant filters whole days in the display zone and a date-time whole
 * days with no zone, and a navigation filters by the id it holds.
 *
 * @param column - The column.
 * @param spec - The filter.
 * @param context - The display zone, `TmLocalization.timeZone()`.
 * @returns The leaf, or `null` when the column takes no such filter or the values are invalid.
 *
 * @example
 * ```ts
 * tmFilterLeaf(name, { op: 'contains', values: ['jed'] }, { timeZone: 'Asia/Riyadh' });
 * // "contains(Name, 'jed') or contains(Name2, 'jed')" in a two-language tenant
 * ```
 */
export function tmFilterLeaf(column: TmColumnPlan, spec: TmFilterSpec, context: { readonly timeZone: string }): string | null {
  const kind = column.filter;
  if (kind === null || !ɵTM_FILTER_OPS[kind].includes(spec.op)) {
    return null;
  }
  try {
    const tree = leafOf(column, kind, spec, context.timeZone);
    return tree === null ? null : tmQxPrint(tree);
  } catch (error) {
    // A literal the builder refuses is an invalid value here.
    if (error instanceof TypeError || error instanceof RangeError) {
      return null;
    }
    throw error;
  }
}

/** The first of the private-use characters that stand in for a description's arguments while its message is formatted. */
const PLACEHOLDER = 0xe000;

/** The list formats of a description's `in` values, by locale. */
const LISTS = new Map<string, Intl.ListFormat>();

/**
 * The sentence a column filter's chip shows, in the active language: one
 * whole-sentence message per operator, `tm.filter.describe.<op>`, with the
 * column's label and the values formatted as the column shows them
 * (numbers in the locale's digits, days in the active calendar, enum
 * values and related rows by their labels), an `in` list joined as
 * alternatives in the locale with each value its own part.
 *
 * @param column - The column.
 * @param spec - The filter.
 * @param options - The injector; the current injection context when omitted.
 * @returns The description, which follows the language, the formats and the labels.
 *
 * @example
 * ```ts
 * tmDescribeFilter(type, { op: 'in', values: ['Service', 'Sale'] })().text; // 'Type: Service or Sale'
 * ```
 */
export function tmDescribeFilter(column: TmColumnPlan, spec: TmFilterSpec, options: TmDescribeFilterOptions = {}): Signal<TmFilterDescription> {
  if (options.injector === undefined) {
    assertInInjectionContext(tmDescribeFilter);
  }
  const injector = options.injector ?? inject(Injector);
  const localization = injector.get(TmLocalization);
  const formats = injector.get(TmFormats);
  const l10n = injector.get(TmL10n);
  localization.ɵregisterStrings('filter', ɵTM_FILTER_STRINGS_EN);

  // --- The message, each displayed argument a placeholder the parts split on ---
  const holder = (index: number): string => String.fromCharCode(PLACEHOLDER + index);
  const { op, values } = spec;
  const params: Record<string, string> = { label: holder(0) };
  if (op === 'between') {
    params['from'] = holder(1);
    params['to'] = holder(2);
  } else if (op === 'in') {
    params['values'] = holder(1);
  } else if (op === 'is') {
    params['value'] = values[0] === 'true' ? 'true' : 'false';
  } else {
    params['value'] = holder(1);
  }
  const message = localization.ɵtext(`tm.filter.describe.${op}`, params);

  // --- A value as the column shows it ---
  const shown = (value: string): string => {
    const of = column.descriptor;
    switch (column.filter) {
      case 'number':
      case 'enum':
        return of === null ? value : formats.text(value, of);
      case 'date':
      case 'instant':
        return ɵtmQxIsDate(value) ? formats.text(value, { type: 'date', nullable: false }) : value;
      case 'navigation': {
        // A row not yet named shows its id, which the formats name once it is read.
        const id: TmId = of?.long ? (value as TmDecimal) : Number(value);
        const name = of === null || !/^\d+$/.test(value) ? '' : formats.text(id, of);
        return name === '' ? (ɵtmFormatExact(value, l10n.locale(), 0, false) ?? value) : name;
      }
      default:
        return value;
    }
  };

  return computed(() => {
    const text = message();
    const label = column.label();

    // --- Each placeholder's parts: the label, a value, or a list of values ---
    const argument = (index: number): TmFilterDescription['parts'] => {
      if (index === 0) {
        return [{ text: label, argument: true }];
      }
      if (op === 'in') {
        const locale = l10n.locale();
        let list = LISTS.get(locale);
        if (list === undefined) {
          // A disjunction, not spec 0029 §13.2's `unit` list: the filter is any-of,
          // and a unit list joins with "and" in Arabic and Amharic ("Service or Sale").
          LISTS.set(locale, (list = new Intl.ListFormat(locale, { type: 'disjunction', style: 'short' })));
        }
        return list
          .formatToParts(values.map(shown))
          .map((part) => (part.type === 'element' ? { text: part.value, argument: true } : { text: tmStripBidiControls(part.value), argument: false }));
      }
      const value = op === 'between' ? values[index - 1] : values[0];
      return [{ text: shown(value ?? ''), argument: true }];
    };

    // --- The message split at its placeholders, adjacent words joined ---
    const parts: { text: string; argument: boolean }[] = [];
    const word = (chunk: string): void => {
      const last = parts.at(-1);
      if (chunk === '') {
        return;
      }
      if (last !== undefined && !last.argument) {
        last.text += chunk;
      } else {
        parts.push({ text: chunk, argument: false });
      }
    };
    let chunk = '';
    for (const char of text) {
      const index = char.charCodeAt(0) - PLACEHOLDER;
      if (index >= 0 && index <= 2) {
        word(chunk);
        chunk = '';
        for (const part of argument(index)) {
          if (part.argument) {
            parts.push({ ...part });
          } else {
            word(part.text);
          }
        }
      } else {
        chunk += char;
      }
    }
    word(chunk);
    return { text: parts.map((part) => part.text).join(''), parts };
  });
}

/**
 * A filter's description as inline text, each argument isolated in a
 * `<bdi>` so a Latin value reads correctly in a right-to-left sentence.
 *
 * @example
 * ```html
 * <tm-filter-description [description]="description()" />
 * ```
 */
@Component({
  selector: 'tm-filter-description',
  changeDetection: ChangeDetectionStrategy.OnPush,
  host: { class: 'tm-filter-description' },
  // The parts, on one line with no whitespace between them, so the message's own spacing stands.
  template: `@for (part of description().parts; track $index) {@if (part.argument) {<bdi>{{ part.text }}</bdi>} @else {<ng-container>{{ part.text }}</ng-container>}}`,
})
export class TmFilterDescriptionText {
  /** The description. */
  readonly description: InputSignal<TmFilterDescription> = input.required<TmFilterDescription>();
}
