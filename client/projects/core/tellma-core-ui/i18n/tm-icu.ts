// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import { ɵtmIntl as intl, ɵtmWarnOnce as warnOnce } from './tm-intl';
import {
  tmDefaultUiArgumentFormatter,
  type TmUiArgumentFormatter,
} from './tm-ui-argument-formatter';

/*
 * An ICU MessageFormat interpreter: messages are parsed once into a small
 * tree and evaluated against their parameters by walking it. Nothing is
 * compiled to JavaScript source, so formatting works under a Content
 * Security Policy without 'unsafe-eval' and under Trusted Types — the
 * policy the app's web host serves (spec 0024 §6). The dialect is that of
 * the MessageFormat engine the library used before (the messageformat
 * project's default, non-strict mode): plural, selectordinal and select
 * with nesting, `offset:` and `=N` cases, `#`, typed `number`, `date` and
 * `time` arguments (formatted by a pluggable argument formatter), and ICU
 * apostrophe quoting.
 *
 * The parser is a set of closures over one cursor rather than a class, so
 * the minifier can shorten every internal name: this file ships in the
 * library's primary bundle.
 */

/** A parameter reference: `{name}` (type null) or `{name, type, style}`. */
interface IcuArgument {
  readonly kind: 'argument';
  readonly name: string;
  readonly type: string | null;
  readonly style: string;
}

/** A `plural`, `selectordinal` or `select` block and its cases, keyed as written (`one`, `=0`). */
interface IcuChoice {
  readonly kind: 'plural' | 'selectordinal' | 'select';
  readonly name: string;
  readonly offset: number;
  readonly cases: ReadonlyMap<string, readonly IcuNode[]>;
}

/** The `#` inside a plural case: the plural's value minus its offset, as a number. */
interface IcuPound {
  readonly kind: 'pound';
}

/** One node of a parsed message: literal text, an argument, a choice, or `#`. */
type IcuNode = string | IcuArgument | IcuChoice | IcuPound;

/** The one `#` node every parse shares. */
const POUND: IcuPound = { kind: 'pound' };

/** A message that cannot be parsed; it renders as its raw text. */
class IcuSyntaxError extends Error {}

/** An argument or case name: anything but ICU pattern syntax and pattern whitespace. */
const NAME = /[^\p{Pat_Syn}\p{Pat_WS}]+/uy;
/** A plural case key: an exact `=N` match or a keyword. */
const CASE_KEY = /=\d+|[^\p{Pat_Syn}\p{Pat_WS}]+/uy;
/** A plural offset clause, allowed only before the first case. */
const OFFSET = /offset\s*:\s*(\d+)/y;
/** Whitespace between syntax elements. */
const SPACE = /\s*/y;
/** A message holding none of these characters is plain text and skips the parser. */
const SYNTAX = /[{}']/;

/** Parses one message into its node tree, or throws {@link IcuSyntaxError}. */
function parse(src: string): IcuNode[] {
  let pos = 0;

  /** Fails the parse; the message renders raw. */
  const fail = (what: string): never => {
    throw new IcuSyntaxError(`${what} at ${pos}`);
  };

  /** Consumes one token of a sticky `pattern` at the cursor, or fails. */
  const token = (pattern: RegExp): string => {
    pattern.lastIndex = pos;
    const found = pattern.exec(src) ?? fail('unexpected character');
    pos = pattern.lastIndex;
    return found[0];
  };

  /** Skips whitespace. */
  const space = (): void => {
    token(SPACE);
  };

  /** Consumes `char` at the cursor, or fails. */
  const expect = (char: string): void => {
    if (src[pos++] !== char) {
      fail(`expected "${char}"`);
    }
  };

  /**
   * ICU apostrophe quoting: `''` is one apostrophe; `'` before `{`, `}` or
   * `#` quotes literal text up to the next lone `'`; any other `'` is
   * literal.
   */
  const apostrophe = (inPlural: boolean): string => {
    const start = pos;
    const next = src[pos + 1];
    if (next === "'") {
      pos += 2;
      return "'";
    }
    if (next === '{' || next === '}' || next === '#') {
      let value = next;
      for (let i = pos + 2; i < src.length; i++) {
        if (src[i] !== "'") {
          value += src[i];
        } else if (src[i + 1] === "'") {
          value += "'";
          i++;
        } else {
          pos = i + 1;
          if (value[0] !== '#' || inPlural) {
            return value;
          }
          // Outside a plural `#` needs no quoting, so the quotes are kept as
          // written — and a quoted brace there would be ambiguous.
          return value.includes('{') ? fail('unsupported escape') : src.slice(start, pos);
        }
      }
    }
    // Not a quote, or one that never closes: a plain apostrophe.
    pos++;
    return "'";
  };

  /**
   * Parses message text up to the `}` that closes the enclosing case (or
   * the end of the message at the root). `inPlural` makes `#` the plural's
   * number.
   */
  const body = (inPlural: boolean, atRoot: boolean): IcuNode[] => {
    const nodes: IcuNode[] = [];
    let text = '';
    const flush = (): void => {
      if (text !== '') {
        nodes.push(text);
        text = '';
      }
    };
    while (pos < src.length) {
      const ch = src[pos];
      if (ch === "'") {
        text += apostrophe(inPlural);
      } else if (ch === '{') {
        flush();
        nodes.push(argument(inPlural));
      } else if (ch === '#' && inPlural) {
        flush();
        nodes.push(POUND);
        pos++;
      } else if (ch === '}' && !atRoot) {
        pos++;
        flush();
        return nodes;
      } else {
        // A stray `}` at the root is literal text, as in the previous engine.
        text += ch;
        pos++;
      }
    }
    if (!atRoot) {
      fail('unexpected end');
    }
    flush();
    return nodes;
  };

  /** Parses `{name}`, `{name, type}`, `{name, type, style}` or a choice block. */
  const argument = (inPlural: boolean): IcuNode => {
    pos++;
    space();
    const name = token(NAME);
    space();
    if (src[pos] === '}') {
      pos++;
      return { kind: 'argument', name, type: null, style: '' };
    }
    expect(',');
    space();
    const type = token(NAME);
    space();
    if (type === 'plural' || type === 'selectordinal' || type === 'select') {
      expect(',');
      return choice(name, type, inPlural);
    }
    let style = '';
    if (src[pos] === '}') {
      pos++;
    } else {
      expect(',');
      // The style is literal text up to the argument's closing brace.
      for (const node of body(inPlural, false)) {
        style +=
          typeof node === 'string'
            ? node
            : node.kind === 'pound'
              ? '#'
              : fail('argument in a style');
      }
    }
    return { kind: 'argument', name, type, style: style.trim() };
  };

  /** Parses the cases of a `plural`, `selectordinal` or `select` block. */
  const choice = (name: string, kind: IcuChoice['kind'], inPlural: boolean): IcuChoice => {
    const cases = new Map<string, readonly IcuNode[]>();
    const casesInPlural = kind !== 'select' || inPlural;
    let offset = 0;
    for (space(); src[pos] !== '}'; space()) {
      if (pos >= src.length) {
        fail('unexpected end');
      }
      if (kind !== 'select' && cases.size === 0) {
        OFFSET.lastIndex = pos;
        const clause = OFFSET.exec(src);
        if (clause !== null) {
          offset = Number(clause[1]);
          pos = OFFSET.lastIndex;
          continue;
        }
      }
      const key = token(CASE_KEY);
      if (key[0] === '=' && kind === 'select') {
        fail('exact case in select');
      }
      space();
      expect('{');
      cases.set(key, body(casesInPlural, false));
    }
    pos++;
    if (!cases.has('other')) {
      fail("no 'other' case");
    }
    return { kind, name, offset, cases };
  };

  return body(false, true);
}

/** Parsed messages by source. Bounded by the string tables, which messages come from. */
const parsed = new Map<string, readonly IcuNode[] | IcuSyntaxError>();
/** Walks a parsed message. `pound` is the innermost plural's value minus its offset. */
function render(
  nodes: readonly IcuNode[],
  params: Readonly<Record<string, unknown>>,
  locale: string,
  format: TmUiArgumentFormatter,
  pound: number | null,
): string {
  let out = '';
  for (const node of nodes) {
    if (typeof node === 'string') {
      out += node;
    } else if (node.kind === 'pound') {
      out += pound === null ? '#' : format('number', pound, '', locale);
    } else {
      // Own members only: a name such as `constructor` must never resolve to
      // an inherited Object.prototype member.
      const value = Object.hasOwn(params, node.name) ? params[node.name] : undefined;
      if (node.kind === 'argument') {
        const type = node.type;
        if (value === undefined) {
          // A missing parameter shows as its placeholder, never as "undefined".
          out += `{${node.name}}`;
        } else if (type === 'number' || type === 'date' || type === 'time') {
          out += format(type, value, node.style, locale);
        } else {
          if (type !== null) {
            warnOnce(`Unsupported ICU argument type "${type}"; pass it formatted.`);
          }
          out += String(value);
        }
      } else {
        let chosen: readonly IcuNode[] | undefined;
        let inner = pound;
        if (node.kind === 'select') {
          chosen = node.cases.get(String(value));
        } else {
          // An exact `=N` case matches the value itself; the category, and
          // `#`, use the value minus the offset.
          const n = Number(value);
          inner = n - node.offset;
          chosen =
            node.cases.get(`=${n}`) ??
            node.cases.get(
              intl(
                node.kind,
                locale,
                (tag) =>
                  new Intl.PluralRules(tag, {
                    type: node.kind === 'plural' ? 'cardinal' : 'ordinal',
                  }),
              ).select(inner),
            );
        }
        out += render(chosen ?? node.cases.get('other')!, params, locale, format, inner);
      }
    }
  }
  return out;
}

/**
 * Formats an ICU MessageFormat message with its parameters in `locale` —
 * the locale that picks plural categories and formats every number the
 * message carries; typed arguments (and a plural's `#`) format through
 * `format`. A message that fails to parse renders as its raw text
 * (with a development warning) rather than throwing, so one malformed
 * translation never takes down the view showing it.
 *
 * Internal to the library: the Transloco transpiler and the static
 * English fallback both format through it.
 */
export function ɵtmFormatMessage(
  message: string,
  params: Readonly<Record<string, unknown>>,
  locale: string,
  format: TmUiArgumentFormatter = tmDefaultUiArgumentFormatter,
): string {
  if (!SYNTAX.test(message)) {
    return message;
  }
  let nodes = parsed.get(message);
  if (nodes === undefined) {
    try {
      nodes = parse(message);
    } catch (error) {
      if (!(error instanceof IcuSyntaxError)) {
        throw error;
      }
      nodes = error;
    }
    parsed.set(message, nodes);
  }
  if (nodes instanceof IcuSyntaxError) {
    warnOnce(`Malformed ICU message (${nodes.message}): ${message}`);
    return message;
  }
  return render(nodes, params, locale, format, null);
}
