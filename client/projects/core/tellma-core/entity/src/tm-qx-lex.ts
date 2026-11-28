// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

// The client's Queryex lexer (spec 0029 §8.1): text read exactly as the
// engine's lexer reads it (spec 0008 §4), for highlighting, parameters and
// safe insertion. The client never parses. Spans are UTF-16 offsets, like
// the engine's, and every rule below mirrors a rule of the engine's scanner,
// down to where a malformed run ends; the engine writes a corpus of texts
// that the spec of this file holds the two to.

/** What a token is: a literal, a name, a reserved word, a parameter, an operator or separator, or a run that could not be read. */
export type TmQxTokenKind = 'number' | 'string' | 'identifier' | 'keyword' | 'parameter' | 'punctuator' | 'error';

/** One token of a Queryex text. */
export interface TmQxToken {
  /** What the token is. */
  readonly kind: TmQxTokenKind;
  /** Where it begins, in UTF-16 code units. */
  readonly start: number;
  /** How many code units it covers. */
  readonly length: number;
  /**
   * What it denotes: an identifier's name without brackets, a string's value
   * with its doubled quotes collapsed, a parameter's name without `@`, and
   * the text as written for any other token.
   */
  readonly text: string;
  /** Set on an identifier the next token opens a call after, which is highlighted as a function. */
  readonly call?: true;
}

/** One lexical problem, with the engine's code. */
export interface TmQxLexDiagnostic {
  /**
   * `QX1001` an unterminated string, `QX1002` an unterminated bracketed
   * name, `QX1003` a malformed number, `QX1004` a number beyond 38 digits,
   * `QX1005` an unexpected character.
   */
  readonly code: 'QX1001' | 'QX1002' | 'QX1003' | 'QX1004' | 'QX1005';
  /** Where the problem begins, in UTF-16 code units. */
  readonly start: number;
  /** How many code units it covers. */
  readonly length: number;
}

/** What a text lexes to. */
export interface TmQxLexResult {
  /** The tokens, in order, the runs that could not be read among them as `error` tokens. */
  readonly tokens: readonly TmQxToken[];
  /** The problems, in order. */
  readonly diagnostics: readonly TmQxLexDiagnostic[];
}

/** The reserved words, matched without regard to ASCII case. */
const KEYWORDS: ReadonlySet<string> = new Set(['and', 'or', 'not', 'in', 'is', 'null', 'true', 'false', 'asc', 'desc']);

/** The most significant digits a number literal may carry. */
const MAX_SIGNIFICANT_DIGITS = 38;

/**
 * The engine's white space: the space, line and paragraph separators, and
 * the controls tab, line feed, vertical tab, form feed, carriage return and
 * next line. The byte-order mark is not among them.
 */
const WHITESPACE = /^[\p{Zs}\p{Zl}\p{Zp}\t\n\v\f\r\x85]$/u;

/** A letter, of any script and any plane. */
const LETTER = /^\p{L}$/u;

/** A decimal digit, of any script. */
const DIGIT = /^\p{Nd}$/u;

/** A name that is not reserved and needs no brackets: a letter or `_`, then letters, digits and `_`, tested per code unit. */
const BARE_NAME = /^[\p{L}_][\p{L}\p{Nd}_]*$/u;

/** A scan in progress: the text, its position and the problems found so far. */
interface TmQxScan {
  /** The text. */
  readonly text: string;
  /** The problems reported so far. */
  readonly diagnostics: TmQxLexDiagnostic[];
  /** The scanning position. */
  index: number;
}

/**
 * Whether a reserved word is spelled, ignoring ASCII case only: the engine
 * matches its keywords ordinally, so no non-ASCII letter folds into one.
 *
 * @param name - The name.
 * @returns Whether it is reserved.
 */
export function ɵtmQxIsKeyword(name: string): boolean {
  return name.length <= 5 && /^[a-z]+$/i.test(name) && KEYWORDS.has(name.toLowerCase());
}

/**
 * Whether a name prints bare: not reserved, and a letter or `_` followed by
 * letters, digits and `_`, each tested as one UTF-16 code unit as the
 * engine's printer tests it, so a letter beyond the basic plane brackets.
 *
 * @param name - The name.
 * @returns Whether it needs no brackets.
 */
export function ɵtmQxIsBareName(name: string): boolean {
  if (name === '' || ɵtmQxIsKeyword(name)) {
    return false;
  }
  for (let index = 0; index < name.length; index++) {
    // A surrogate code unit is never a letter on its own.
    const unit = name.charCodeAt(index);
    if (unit >= 0xd800 && unit <= 0xdfff) {
      return false;
    }
  }
  return BARE_NAME.test(name);
}

/**
 * The code point at an index and the code units it takes, or `null` for a
 * lone surrogate, which is never a letter.
 *
 * @param text - The text.
 * @param index - The index.
 * @returns The character and its width, or `null`.
 */
function runeAt(text: string, index: number): { readonly char: string; readonly width: number } | null {
  const unit = text.charCodeAt(index);
  if (unit >= 0xd800 && unit <= 0xdbff) {
    const next = text.charCodeAt(index + 1);
    return next >= 0xdc00 && next <= 0xdfff ? { char: text.slice(index, index + 2), width: 2 } : null;
  }
  return unit >= 0xdc00 && unit <= 0xdfff ? null : { char: text[index]!, width: 1 };
}

/**
 * The width of the identifier start at an index, or 0 when none starts there.
 *
 * @param text - The text.
 * @param index - The index.
 * @returns The code units the starting character takes, or 0.
 */
function identifierStart(text: string, index: number): number {
  if (text[index] === '_') {
    return 1;
  }
  const rune = runeAt(text, index);
  return rune !== null && LETTER.test(rune.char) ? rune.width : 0;
}

/**
 * The width of the identifier part at an index, or 0 when the identifier
 * ends there.
 *
 * @param text - The text.
 * @param index - The index.
 * @returns The code units the character takes, or 0.
 */
function identifierPart(text: string, index: number): number {
  if (text[index] === '_') {
    return 1;
  }
  const rune = runeAt(text, index);
  return rune !== null && (LETTER.test(rune.char) || DIGIT.test(rune.char)) ? rune.width : 0;
}

/**
 * Where a name's remaining parts end.
 *
 * @param text - The text.
 * @param from - The index after the name's first character.
 * @returns The index after its last part.
 */
function identifierEnd(text: string, from: number): number {
  let index = from;
  let width = index < text.length ? identifierPart(text, index) : 0;
  while (width > 0) {
    index += width;
    width = index < text.length ? identifierPart(text, index) : 0;
  }
  return index;
}

/**
 * Whether one code unit is a decimal digit of any script, as the engine's
 * single-character test reads it: a surrogate never is.
 *
 * @param char - The code unit, or `undefined` past the end.
 * @returns Whether it is.
 */
function isDigitUnit(char: string | undefined): boolean {
  if (char === undefined) {
    return false;
  }
  const unit = char.charCodeAt(0);
  return (unit < 0xd800 || unit > 0xdfff) && DIGIT.test(char);
}

/**
 * Whether one code unit is one of the ten digits a number is written with.
 *
 * @param char - The code unit, or `undefined` past the end.
 * @returns Whether it is.
 */
function isPlainDigit(char: string | undefined): boolean {
  return char !== undefined && char >= '0' && char <= '9';
}

/**
 * Reports a problem.
 *
 * @param scan - The scan.
 * @param code - The code.
 * @param start - Where it begins.
 * @param length - How many code units it covers.
 */
function report(scan: TmQxScan, code: TmQxLexDiagnostic['code'], start: number, length: number): void {
  scan.diagnostics.push({ code, start, length });
}

/**
 * Reads a string literal, collapsing its doubled quotes.
 *
 * @param scan - The scan, at the opening quote.
 * @returns The token, or `null` when it never closes.
 */
function scanString(scan: TmQxScan): TmQxToken | null {
  const { text } = scan;
  const start = scan.index;
  let value = '';
  let index = start + 1;
  while (index < text.length) {
    if (text[index] !== "'") {
      value += text[index];
      index++;
    } else if (text[index + 1] === "'") {
      value += "'";
      index += 2;
    } else {
      scan.index = index + 1;
      return { kind: 'string', start, length: scan.index - start, text: value };
    }
  }
  report(scan, 'QX1001', start, text.length - start);
  scan.index = text.length;
  return null;
}

/**
 * Reads a bracketed name, which escapes every reserved word.
 *
 * @param scan - The scan, at the opening bracket.
 * @returns The name, or `null` when it never closes or is empty.
 */
function scanBracketed(scan: TmQxScan): string | null {
  const { text } = scan;
  const start = scan.index;
  const close = text.indexOf(']', start + 1);
  if (close < 0) {
    report(scan, 'QX1002', start, text.length - start);
    scan.index = text.length;
    return null;
  }
  scan.index = close + 1;
  if (close === start + 1) {
    report(scan, 'QX1005', start, 2);
    return null;
  }
  return text.slice(start + 1, close);
}

/**
 * Reads a parameter: `@` and a name, bare or bracketed.
 *
 * @param scan - The scan, at the `@`.
 * @returns The token, or `null` when no name follows.
 */
function scanParameter(scan: TmQxScan): TmQxToken | null {
  const { text } = scan;
  const start = scan.index;
  if (text[start + 1] === '[') {
    scan.index = start + 1;
    const name = scanBracketed(scan);
    return name === null ? null : { kind: 'parameter', start, length: scan.index - start, text: name };
  }
  const width = start + 1 < text.length ? identifierStart(text, start + 1) : 0;
  if (width === 0) {
    report(scan, 'QX1005', start, 1);
    scan.index = start + 1;
    return null;
  }
  const index = identifierEnd(text, start + 1 + width);
  scan.index = index;
  return { kind: 'parameter', start, length: index - start, text: text.slice(start + 1, index) };
}

/**
 * Reads a name, a keyword when it spells a reserved word.
 *
 * @param scan - The scan, at the name's start.
 * @param width - The code units its first character takes.
 * @returns The token.
 */
function scanIdentifier(scan: TmQxScan, width: number): TmQxToken {
  const { text } = scan;
  const start = scan.index;
  const index = identifierEnd(text, start + width);
  scan.index = index;
  const name = text.slice(start, index);
  return { kind: ɵtmQxIsKeyword(name) ? 'keyword' : 'identifier', start, length: index - start, text: name };
}

/**
 * Reports a malformed number and skips the run it was written as: every
 * following identifier part, digit and point, one code unit at a time.
 *
 * @param scan - The scan.
 * @param start - Where the number began.
 * @param from - Where the malformed part was noticed.
 */
function failNumber(scan: TmQxScan, start: number, from: number): void {
  const { text } = scan;
  let end = from;
  while (end < text.length && (identifierPart(text, end) > 0 || isDigitUnit(text[end]) || text[end] === '.')) {
    // One code unit per step, as the engine skips: a letter beyond the
    // basic plane ends the run between its two halves.
    end++;
  }
  report(scan, 'QX1003', start, end - start);
  scan.index = Math.max(end, start + 1);
}

/**
 * Reads a number: digits, and a point with digits after it.
 *
 * @param scan - The scan, at the first digit.
 * @returns The token, or `null` when the number is malformed or too long.
 */
function scanNumber(scan: TmQxScan): TmQxToken | null {
  const { text } = scan;
  const start = scan.index;
  let index = start;
  while (isPlainDigit(text[index])) {
    index++;
  }
  const integerEnd = index;
  let scale = 0;
  if (text[index] === '.') {
    // A digit is required on both sides of the point.
    if (!isPlainDigit(text[index + 1])) {
      failNumber(scan, start, index + 1);
      return null;
    }
    const fractionStart = ++index;
    while (isPlainDigit(text[index])) {
      index++;
    }
    scale = index - fractionStart;
  }

  // Nothing that could begin a name, nor a digit of another script, may
  // follow a number: exponents, hexadecimal and digit separators are refused.
  if (index < text.length && (identifierStart(text, index) > 0 || isDigitUnit(text[index]))) {
    failNumber(scan, start, index);
    return null;
  }

  // --- At most 38 significant digits, leading zeros aside ---
  let first = start;
  while (first < integerEnd && text[first] === '0') {
    first++;
  }
  if (Math.max(integerEnd - first + scale, 1) > MAX_SIGNIFICANT_DIGITS) {
    report(scan, 'QX1004', start, index - start);
    scan.index = index;
    return null;
  }
  scan.index = index;
  return { kind: 'number', start, length: index - start, text: text.slice(start, index) };
}

/** The operators of two characters, tried before those of one. */
const PAIRS: ReadonlySet<string> = new Set(['!=', '<=', '>=', '||']);

/** The operators and separators of one character. */
const SINGLES: ReadonlySet<string> = new Set(['=', '<', '>', '+', '-', '*', '/', '%', '(', ')', ',']);

/**
 * Reads an operator or separator.
 *
 * @param scan - The scan.
 * @returns The token, or `null` for a character that is none.
 */
function scanPunctuator(scan: TmQxScan): TmQxToken | null {
  const { text } = scan;
  const start = scan.index;
  const pair = text.slice(start, start + 2);
  if (PAIRS.has(pair)) {
    scan.index = start + 2;
    return { kind: 'punctuator', start, length: 2, text: pair };
  }
  const char = text[start]!;
  if (SINGLES.has(char)) {
    scan.index = start + 1;
    return { kind: 'punctuator', start, length: 1, text: char };
  }
  if (char === '.') {
    // A point before a digit is a number missing its leading digit.
    if (isDigitUnit(text[start + 1])) {
      failNumber(scan, start, start + 1);
      return null;
    }
    scan.index = start + 1;
    return { kind: 'punctuator', start, length: 1, text: '.' };
  }
  report(scan, 'QX1005', start, 1);
  scan.index = start + 1;
  return null;
}

/**
 * Lexes a Queryex text as the engine does. A run that cannot be read
 * becomes an `error` token beside its problem, and lexing goes on after it,
 * so every token around a typo still highlights.
 *
 * @param text - The text.
 * @returns The tokens and the problems.
 *
 * @example
 * ```ts
 * tmQxLex("contains(Name, 'jed')").tokens.map((token) => token.kind);
 * // ['identifier', 'punctuator', 'identifier', 'punctuator', 'string', 'punctuator']
 * ```
 */
export function tmQxLex(text: string): TmQxLexResult {
  const scan: TmQxScan = { text, diagnostics: [], index: 0 };
  const tokens: TmQxToken[] = [];
  while (scan.index < text.length) {
    const char = text[scan.index]!;
    if (WHITESPACE.test(char)) {
      scan.index++;
      continue;
    }

    // --- One token, by its first character ---
    const start = scan.index;
    let token: TmQxToken | null;
    if (char === "'") {
      token = scanString(scan);
    } else if (char === '[') {
      const name = scanBracketed(scan);
      token = name === null ? null : { kind: 'identifier', start, length: scan.index - start, text: name };
    } else if (char === '@') {
      token = scanParameter(scan);
    } else {
      const width = identifierStart(text, start);
      token = width > 0 ? scanIdentifier(scan, width) : isPlainDigit(char) ? scanNumber(scan) : scanPunctuator(scan);
    }

    // --- A run that could not be read is an error token over what was skipped ---
    if (token === null) {
      const length = Math.max(scan.index - start, 1);
      tokens.push({ kind: 'error', start, length, text: text.slice(start, start + length) });
    } else {
      tokens.push(token);
    }
  }

  // --- A name is a call's when the next readable token opens its arguments ---
  let next: TmQxToken | undefined;
  for (let index = tokens.length - 1; index >= 0; index--) {
    const token = tokens[index]!;
    if (token.kind === 'error') {
      continue;
    }
    if (token.kind === 'identifier' && next?.kind === 'punctuator' && next.text === '(') {
      tokens[index] = { ...token, call: true };
    }
    next = token;
  }
  return { tokens, diagnostics: scan.diagnostics };
}

/**
 * The paths a Queryex text reads: each name, with the names its points
 * join to it, that is not a function's. Internal: a select plan checks an
 * expression column's paths before it sends the expression.
 *
 * @param text - The text.
 * @returns The paths, such as `Manager.CreatedAt`, each once, in order.
 */
export function ɵtmQxPaths(text: string): readonly string[] {
  const tokens = tmQxLex(text).tokens.filter((token) => token.kind !== 'error');
  const paths: string[] = [];
  for (let index = 0; index < tokens.length; index++) {
    const token = tokens[index]!;
    // A path starts at a name that does not continue one before it.
    const previous = tokens[index - 1];
    if (token.kind !== 'identifier' || token.call || (previous?.kind === 'punctuator' && previous.text === '.')) {
      continue;
    }
    const segments = [token.text];
    while (tokens[index + 1]?.text === '.' && tokens[index + 1]?.kind === 'punctuator' && tokens[index + 2]?.kind === 'identifier' && !tokens[index + 2]?.call) {
      segments.push(tokens[index + 2]!.text);
      index += 2;
    }
    paths.push(segments.join('.'));
  }
  return [...new Set(paths)];
}
