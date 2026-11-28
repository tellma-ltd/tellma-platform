// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import { TmDecimals, type TmDataType } from '@tellma/core';

import { tmQxLex, ɵtmQxIsBareName } from './tm-qx-lex';

/** Angular's development-mode flag: a production build folds it to false, and the messages it guards drop out. */
declare const ngDevMode: unknown;

// The client's Queryex builder and printer (spec 0029 §8.2): the filters and
// order terms the client writes are trees, printed exactly as the engine's
// canonical printer prints them (spec 0008 §5.2), so no client code
// concatenates query text. The engine writes a golden of trees and their
// printed text that the spec of this file holds the two printers to.

/** A binary operator, as Queryex spells it. */
export type TmQxBinaryOp = 'or' | 'and' | '=' | '!=' | '<' | '<=' | '>' | '>=' | '+' | '-' | '||' | '*' | '/' | '%';

/** A Queryex expression tree. */
export type TmQx =
  | {
      /** A path of names, such as `Parent.Code`. */
      readonly kind: 'path';
      /** The names, in order. */
      readonly segments: readonly string[];
    }
  | {
      /** A string literal. */
      readonly kind: 'string';
      /** The value. */
      readonly value: string;
    }
  | {
      /** A number literal. */
      readonly kind: 'number';
      /** The value as decimal text, such as `100000` or `0.25`. */
      readonly value: string;
    }
  | {
      /** `true` or `false`. */
      readonly kind: 'bool';
      /** The value. */
      readonly value: boolean;
    }
  | {
      /** `null`. */
      readonly kind: 'null';
    }
  | {
      /** A parameter, such as `@From`. */
      readonly kind: 'param';
      /** Its name, without `@`. */
      readonly name: string;
    }
  | {
      /** A function call. */
      readonly kind: 'call';
      /** The function's name. */
      readonly name: string;
      /** Its arguments, in order. */
      readonly args: readonly TmQx[];
    }
  | {
      /** `not` or the arithmetic negation. */
      readonly kind: 'not' | 'negate';
      /** The operand. */
      readonly operand: TmQx;
    }
  | {
      /** An operator between two operands. */
      readonly kind: 'binary';
      /** The operator. */
      readonly op: TmQxBinaryOp;
      /** The left operand. */
      readonly left: TmQx;
      /** The right operand. */
      readonly right: TmQx;
    }
  | {
      /** A membership test, `value in (items)`. */
      readonly kind: 'in';
      /** The value tested. */
      readonly value: TmQx;
      /** The items, at least one. */
      readonly items: readonly TmQx[];
    }
  | {
      /** An absence test, `value is null` or `value is not null`. */
      readonly kind: 'is-null';
      /** The value tested. */
      readonly value: TmQx;
      /** Whether it tests presence instead. */
      readonly negated: boolean;
    }
  | {
      /** Server-supplied text, printed in parentheses. */
      readonly kind: 'raw';
      /** The text. */
      readonly text: string;
    };

/** How tightly an operator binds, as the engine ranks them. */
// The ranks are spec 0008 §5.2's.
const POWER = {
  or: 20,
  and: 30,
  not: 40,
  comparison: 50,
  additive: 60,
  multiplicative: 70,
  negate: 80,
  atomic: 100,
} as const;

/** Each binary operator's binding power. */
const BINARY_POWER: Readonly<Record<TmQxBinaryOp, number>> = {
  or: POWER.or,
  and: POWER.and,
  '=': POWER.comparison,
  '!=': POWER.comparison,
  '<': POWER.comparison,
  '<=': POWER.comparison,
  '>': POWER.comparison,
  '>=': POWER.comparison,
  '+': POWER.additive,
  '-': POWER.additive,
  '||': POWER.additive,
  '*': POWER.multiplicative,
  '/': POWER.multiplicative,
  '%': POWER.multiplicative,
};

/** The significant digits a JavaScript number may carry into a literal: what a double holds exactly. */
const NUMBER_DIGITS = 15;

/** Decimal wire text: an optional minus, digits, and a point with digits after it. */
const DECIMAL = /^-?\d+(?:\.\d+)?$/;

/** An ISO date. */
const ISO_DATE = /^(\d{4})-(\d{2})-(\d{2})$/;

/** An ISO date-time with no offset; the seconds and their fraction are optional. */
const ISO_DATETIME = /^(\d{4}-\d{2}-\d{2})(?:T(\d{2}):(\d{2})(?::(\d{2})(\.\d{1,7})?)?)?$/;

/** An ISO instant: a date-time and its offset, `Z` or `±HH:mm`. */
const ISO_INSTANT = /^(\d{4}-\d{2}-\d{2})T(\d{2}):(\d{2})(?::(\d{2})(\.\d{1,7})?)?(?:Z|[+-](\d{2}):(\d{2}))$/;

/**
 * Whether text is a real calendar date, `yyyy-MM-dd`.
 *
 * @param text - The text.
 * @returns Whether it is.
 */
export function ɵtmQxIsDate(text: string): boolean {
  const match = ISO_DATE.exec(text);
  if (match === null) {
    return false;
  }
  const [year, month, day] = [Number(match[1]), Number(match[2]), Number(match[3])];
  const leap = year % 4 === 0 && (year % 100 !== 0 || year % 400 === 0);
  const days = [31, leap ? 29 : 28, 31, 30, 31, 30, 31, 31, 30, 31, 30, 31][month - 1];
  return days !== undefined && day >= 1 && day <= days;
}

/**
 * Whether a time of day is in range.
 *
 * @param hours - The hours, as written.
 * @param minutes - The minutes, as written.
 * @param seconds - The seconds, as written, when given.
 * @returns Whether each is in range.
 */
function isTime(hours: string, minutes: string, seconds: string | undefined): boolean {
  return Number(hours) <= 23 && Number(minutes) <= 59 && (seconds === undefined || Number(seconds) <= 59);
}

/**
 * Throws unless a name can be written: it must not be empty, and no
 * bracket can escape a `]`.
 *
 * @param name - The name.
 * @param what - What it names, for the message.
 * @returns The name.
 */
function checkName(name: string, what: string): string {
  if (typeof name !== 'string' || name === '') {
    throw new TypeError(typeof ngDevMode !== 'undefined' && ngDevMode ? `A Queryex ${what} needs a name.` : '');
  }
  if (name.includes(']')) {
    throw new TypeError(typeof ngDevMode !== 'undefined' && ngDevMode ? `The Queryex ${what} "${name}" contains "]", which no name can be written with.` : '');
  }
  return name;
}

/**
 * Throws unless text is server-supplied Queryex that can stand in
 * parentheses: it lexes cleanly and its parentheses balance.
 *
 * @param text - The text.
 * @returns The text.
 */
function checkRaw(text: string): string {
  if (typeof text !== 'string') {
    throw new TypeError(typeof ngDevMode !== 'undefined' && ngDevMode ? 'Raw Queryex must be text.' : '');
  }
  const { tokens, diagnostics } = tmQxLex(text);
  let depth = 0;
  for (const token of tokens) {
    if (token.kind === 'punctuator' && (token.text === '(' || token.text === ')')) {
      depth += token.text === '(' ? 1 : -1;
      if (depth < 0) {
        break;
      }
    }
  }
  if (tokens.length === 0 || diagnostics.length > 0 || depth !== 0) {
    throw new TypeError(typeof ngDevMode !== 'undefined' && ngDevMode ? `"${text}" is not Queryex that can be grouped in parentheses.` : '');
  }
  return text;
}

/**
 * The number node for decimal text, a negative one as a negation, as the
 * engine reads `-5`.
 *
 * @param text - Decimal wire text.
 * @returns The node.
 */
function decimal(text: string): TmQx {
  const negative = text.startsWith('-');
  const magnitude = negative ? text.slice(1) : text;
  // The engine reads at most 38 significant digits, leading zeros aside (QX1004).
  if (tmQxLex(magnitude).diagnostics.length > 0) {
    throw new RangeError(typeof ngDevMode !== 'undefined' && ngDevMode ? `${text} has more digits than a Queryex number can hold.` : '');
  }
  // Negative zero is zero.
  return negative && !/^0+(?:\.0+)?$/.test(magnitude) ? { kind: 'negate', operand: { kind: 'number', value: magnitude } } : { kind: 'number', value: magnitude };
}

/**
 * The significant digits of canonical decimal text: from the first nonzero
 * digit to the last.
 *
 * @param text - The text.
 * @returns The count.
 */
function significantDigits(text: string): number {
  const digits = text.replace(/[-.]/g, '').replace(/^0+/, '').replace(/0+$/, '');
  return digits.length;
}

/**
 * A number literal from a JavaScript number or decimal text.
 *
 * @param value - The number, or decimal text such as a `TmDecimal`.
 * @returns The node: a number, or the negation of one.
 */
function num(value: number | string): TmQx {
  if (typeof value === 'number') {
    if (!Number.isFinite(value)) {
      throw new RangeError(typeof ngDevMode !== 'undefined' && ngDevMode ? `${String(value)} is not a number Queryex can write.` : '');
    }
    // The shortest text that reads back as the double, its exponent expanded.
    const text = TmDecimals.parse(String(value));
    if (text === null || significantDigits(text) > NUMBER_DIGITS) {
      throw new RangeError(typeof ngDevMode !== 'undefined' && ngDevMode ? `${String(value)} has more significant digits than a number holds exactly; pass its decimal text instead.` : '');
    }
    return decimal(text);
  }
  if (typeof value !== 'string' || !DECIMAL.test(value)) {
    throw new TypeError(typeof ngDevMode !== 'undefined' && ngDevMode ? `"${String(value)}" is not decimal text.` : '');
  }
  return decimal(value);
}

/**
 * A date-time literal's text, `yyyy-MM-ddTHH:mm:ss` with any fraction
 * kept; a date alone is its midnight.
 *
 * @param value - The date-time.
 * @returns The text.
 */
function dateTimeText(value: unknown): string {
  const match = typeof value === 'string' ? ISO_DATETIME.exec(value) : null;
  if (match === null || !ɵtmQxIsDate(match[1]!) || (match[2] !== undefined && !isTime(match[2], match[3]!, match[4]))) {
    throw new TypeError(typeof ngDevMode !== 'undefined' && ngDevMode ? `"${String(value)}" is not a date-time, yyyy-MM-ddTHH:mm:ss.` : '');
  }
  const [, date, hours = '00', minutes = '00', seconds = '00', fraction = ''] = match;
  return `${date}T${hours}:${minutes}:${seconds}${fraction}`;
}

/**
 * An instant literal's text: ISO with its offset as given, or a `Date` or
 * epoch milliseconds written in UTC with the `+00:00` offset.
 *
 * @param value - The instant.
 * @returns The text.
 */
function instantText(value: unknown): string {
  if (typeof value === 'string') {
    const match = ISO_INSTANT.exec(value);
    if (match === null || !ɵtmQxIsDate(match[1]!) || !isTime(match[2]!, match[3]!, match[4]) || Number(match[6] ?? 0) > 23 || Number(match[7] ?? 0) > 59) {
      throw new TypeError(typeof ngDevMode !== 'undefined' && ngDevMode ? `"${value}" is not an instant with its offset, such as 2026-01-31T21:00:00+00:00.` : '');
    }
    return value;
  }
  const at = value instanceof Date ? value.getTime() : typeof value === 'number' ? value : Number.NaN;
  const iso = Number.isFinite(at) ? new Date(at).toISOString() : '';
  if (!/^\d{4}-/.test(iso)) {
    throw new TypeError(typeof ngDevMode !== 'undefined' && ngDevMode ? `${String(value)} is not an instant Queryex can write.` : '');
  }
  // `2026-01-31T21:00:00.000Z` → `2026-01-31T21:00:00+00:00`, a fraction kept only when it has one.
  return `${iso.slice(0, 19)}${iso.slice(19, 23) === '.000' ? '' : iso.slice(19, 23)}+00:00`;
}

/**
 * Builds Queryex trees. Every literal is checked as it is built, so a tree
 * that prints is a tree the engine reads back as built.
 *
 * @example
 * ```ts
 * const p = tmQx.path('CreditLimit');
 * tmQxPrint(tmQx.and(tmQx.ge(p, tmQx.num('100000')), tmQx.le(p, tmQx.num('500000'))));
 * // 'CreditLimit >= 100000 and CreditLimit <= 500000'
 * ```
 */
export const tmQx = /* @__PURE__ */ Object.freeze({
  /**
   * A path such as `Parent.Code`, split on its points.
   *
   * @throws TypeError when a segment is empty or contains `]`.
   */
  path(path: string): TmQx {
    return { kind: 'path', segments: String(path).split('.').map((segment) => checkName(segment, 'path segment')) };
  },

  /** A string literal. */
  str(value: string): TmQx {
    if (typeof value !== 'string') {
      throw new TypeError(typeof ngDevMode !== 'undefined' && ngDevMode ? `${String(value)} is not text.` : '');
    }
    return { kind: 'string', value };
  },

  /**
   * A number literal from a JavaScript number or decimal text, such as a
   * `TmDecimal`; a negative one is the negation of its magnitude. Numbers
   * print without an exponent.
   *
   * @throws RangeError for a number that is not finite or carries more than 15 significant digits.
   * @throws TypeError for text that is not decimal text.
   */
  num(value: number | string): TmQx {
    return num(value);
  },

  /** `true` or `false`. */
  bool(value: boolean): TmQx {
    return { kind: 'bool', value: value === true };
  },

  /** `null`. */
  null(): TmQx {
    return { kind: 'null' };
  },

  /**
   * A parameter, such as `@From`.
   *
   * @throws TypeError when the name is empty or contains `]`.
   */
  param(name: string): TmQx {
    return { kind: 'param', name: checkName(name, 'parameter') };
  },

  /**
   * A function call.
   *
   * @throws TypeError when the name is empty or contains `]`.
   */
  call(name: string, ...args: TmQx[]): TmQx {
    return { kind: 'call', name: checkName(name, 'function'), args };
  },

  /**
   * A literal of a data type: strings, enum values and guids as strings,
   * numbers as `num` writes them, dates `yyyy-MM-dd`, date-times
   * `yyyy-MM-ddTHH:mm:ss`, instants ISO with their offset (a `Date` or epoch
   * milliseconds in UTC), and `null` or `undefined` as `null`.
   *
   * @throws TypeError when the value is not one of the type, or the type has no literal (`time`, `json`, `binary`).
   * @throws RangeError for a number beyond 15 significant digits.
   */
  literal(type: TmDataType, value: unknown): TmQx {
    if (value === null || value === undefined) {
      return { kind: 'null' };
    }
    switch (type) {
      case 'string':
      case 'enum':
      case 'guid':
        return tmQx.str(value as string);
      case 'integer':
        if (typeof value === 'number' ? !Number.isInteger(value) : typeof value !== 'string' || !/^-?\d+$/.test(value)) {
          throw new TypeError(typeof ngDevMode !== 'undefined' && ngDevMode ? `${String(value)} is not a whole number.` : '');
        }
        return num(value as number | string);
      case 'decimal':
      case 'float':
        return num(value as number | string);
      case 'bool':
        if (typeof value !== 'boolean') {
          throw new TypeError(typeof ngDevMode !== 'undefined' && ngDevMode ? `${String(value)} is not true or false.` : '');
        }
        return { kind: 'bool', value };
      case 'date':
        if (typeof value !== 'string' || !ɵtmQxIsDate(value)) {
          throw new TypeError(typeof ngDevMode !== 'undefined' && ngDevMode ? `"${String(value)}" is not a date, yyyy-MM-dd.` : '');
        }
        return { kind: 'string', value };
      case 'datetime':
        return { kind: 'string', value: dateTimeText(value) };
      case 'instant':
        return { kind: 'string', value: instantText(value) };
      default:
        throw new TypeError(typeof ngDevMode !== 'undefined' && ngDevMode ? `A ${type} value has no Queryex literal.` : '');
    }
  },

  /**
   * Server-supplied Queryex, such as a report's drill filter, printed in
   * parentheses. Never user keystrokes.
   *
   * @throws TypeError when the text does not lex cleanly or its parentheses do not balance.
   */
  raw(text: string): TmQx {
    return { kind: 'raw', text: checkRaw(text) };
  },

  /** `not x`. */
  not(x: TmQx): TmQx {
    return { kind: 'not', operand: x };
  },

  /** The operands joined with `and`, nulls skipped; `true` when none is left. */
  and(...xs: (TmQx | null)[]): TmQx {
    return chain('and', xs, true);
  },

  /** The operands joined with `or`, nulls skipped; `false` when none is left. */
  or(...xs: (TmQx | null)[]): TmQx {
    return chain('or', xs, false);
  },

  /** `a = b`. */
  eq(a: TmQx, b: TmQx): TmQx {
    return { kind: 'binary', op: '=', left: a, right: b };
  },

  /** `a != b`. */
  ne(a: TmQx, b: TmQx): TmQx {
    return { kind: 'binary', op: '!=', left: a, right: b };
  },

  /** `a < b`. */
  lt(a: TmQx, b: TmQx): TmQx {
    return { kind: 'binary', op: '<', left: a, right: b };
  },

  /** `a <= b`. */
  le(a: TmQx, b: TmQx): TmQx {
    return { kind: 'binary', op: '<=', left: a, right: b };
  },

  /** `a > b`. */
  gt(a: TmQx, b: TmQx): TmQx {
    return { kind: 'binary', op: '>', left: a, right: b };
  },

  /** `a >= b`. */
  ge(a: TmQx, b: TmQx): TmQx {
    return { kind: 'binary', op: '>=', left: a, right: b };
  },

  /** `value in (items)`; `false` for no items, since nothing is in an empty list. */
  in(value: TmQx, items: readonly TmQx[]): TmQx {
    return items.length === 0 ? { kind: 'bool', value: false } : { kind: 'in', value, items: [...items] };
  },

  /** `value is null`, or `value is not null` when negated. */
  isNull(value: TmQx, negated = false): TmQx {
    return { kind: 'is-null', value, negated };
  },
});

/**
 * Joins operands with `and` or `or`, grouped to the left as the engine
 * reads a chain.
 *
 * @param op - The operator.
 * @param xs - The operands; nulls are skipped.
 * @param empty - The value of no operands.
 * @returns The chain.
 */
function chain(op: 'and' | 'or', xs: readonly (TmQx | null)[], empty: boolean): TmQx {
  const operands = xs.filter((x): x is TmQx => x !== null);
  if (operands.length === 0) {
    return { kind: 'bool', value: empty };
  }
  return operands.slice(1).reduce<TmQx>((left, right) => ({ kind: 'binary', op, left, right }), operands[0]!);
}

/**
 * How tightly a node binds: an operator's power, a negative number's as a
 * negation's, and anything else as an atom.
 *
 * @param node - The node.
 * @returns Its binding power.
 */
function powerOf(node: TmQx): number {
  switch (node.kind) {
    case 'binary':
      return BINARY_POWER[node.op];
    case 'in':
    case 'is-null':
      return POWER.comparison;
    case 'not':
      return POWER.not;
    case 'negate':
      return POWER.negate;
    case 'number':
      return node.value.startsWith('-') ? POWER.negate : POWER.atomic;
    default:
      return POWER.atomic;
  }
}

/**
 * Writes a name, bracketed when it is reserved or breaks the grammar.
 *
 * @param name - The name.
 * @param what - What it names, for a message.
 * @returns The text.
 */
function nameText(name: string, what: string): string {
  checkName(name, what);
  return ɵtmQxIsBareName(name) ? name : `[${name}]`;
}

/**
 * Writes an operand, in parentheses when it binds looser than the floor.
 *
 * @param node - The operand.
 * @param floor - The least binding power it may have bare.
 * @returns The text.
 */
function operand(node: TmQx, floor: number): string {
  const text = print(node);
  return powerOf(node) < floor ? `(${text})` : text;
}

/**
 * Writes a node.
 *
 * @param node - The node.
 * @returns The text.
 */
function print(node: TmQx): string {
  switch (node.kind) {
    case 'path':
      if (node.segments.length === 0) {
        throw new TypeError(typeof ngDevMode !== 'undefined' && ngDevMode ? 'A Queryex path needs at least one segment.' : '');
      }
      return node.segments.map((segment) => nameText(segment, 'path segment')).join('.');
    case 'string':
      return `'${node.value.replaceAll("'", "''")}'`;
    case 'number':
      if (!DECIMAL.test(node.value)) {
        throw new TypeError(typeof ngDevMode !== 'undefined' && ngDevMode ? `"${node.value}" is not decimal text.` : '');
      }
      return node.value;
    case 'bool':
      return node.value ? 'true' : 'false';
    case 'null':
      return 'null';
    case 'param':
      return `@${nameText(node.name, 'parameter')}`;
    case 'call':
      return `${nameText(node.name, 'function')}(${node.args.map(print).join(', ')})`;
    case 'not':
      return `not ${operand(node.operand, POWER.not)}`;
    case 'negate':
      return `-${operand(node.operand, POWER.negate)}`;
    case 'binary': {
      // Left-associative: an equal-power right operand is grouped and an
      // equal-power left one is not. Comparisons do not chain, so one
      // inside another is grouped on either side.
      const power = BINARY_POWER[node.op];
      const left = operand(node.left, power === POWER.comparison ? power + 1 : power);
      return `${left} ${node.op} ${operand(node.right, power + 1)}`;
    }
    case 'in':
      if (node.items.length === 0) {
        throw new TypeError(typeof ngDevMode !== 'undefined' && ngDevMode ? 'A Queryex membership test needs at least one item.' : '');
      }
      return `${operand(node.value, POWER.comparison + 1)} in (${node.items.map((item) => operand(item, POWER.additive)).join(', ')})`;
    case 'is-null':
      return `${operand(node.value, POWER.comparison + 1)} is ${node.negated ? 'not ' : ''}null`;
    case 'raw':
      return `(${checkRaw(node.text)})`;
  }
}

/**
 * Prints a Queryex tree as the engine's canonical printer does, with the
 * fewest parentheses its precedence needs: a child that binds looser than
 * its parent, an equal-power right operand (`a - (b - c)`) and a comparison
 * inside a comparison are grouped. Names that are reserved or break the
 * grammar print bracketed (`[not]`); strings double each `'`.
 *
 * @param expression - The tree.
 * @returns The text.
 * @throws TypeError when a name contains `]`, a number node is not decimal text, or raw text cannot be grouped.
 *
 * @example
 * ```ts
 * tmQxPrint(tmQx.or(tmQx.call('contains', tmQx.path('Name'), tmQx.str("O'Neil")), tmQx.eq(tmQx.path('Code'), tmQx.str('7'))));
 * // "contains(Name, 'O''Neil') or Code = '7'"
 * ```
 */
export function tmQxPrint(expression: TmQx): string {
  return print(expression);
}
