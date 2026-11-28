// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import { tmStripBidiControls } from '@tellma/core-ui/l10n';

// The search query's URL codec (spec 0024 §9.5). A search page's whole query
// state rides its URL: every field in the query string, in one canonical
// order with the defaults left out, except the advanced filter, which rides
// the fragment `#d=` as base64url JSON, so no filter text reaches a server,
// a proxy's URL limit, a log or a `Referer`. The drill-through list's fixed
// filter rides the same fragment as its own payload. Parsing is lenient: an
// invalid entry is dropped and reported, and the rest of the query stands.

/** A search page's query state, as its URL carries it. */
export interface TmSearchQuery {
  /** The page's tab: `tab=mine`. */
  readonly tab?: string;
  /** The search text: `q=jed`. */
  readonly q?: string;
  /** One filter per column path, each `{op}:{value}`: `f.CenterType=in:Service,Sale`. */
  readonly columnFilters?: Readonly<Record<string, string>>;
  /** The advanced Queryex filter; it rides the fragment `#d=`, never the query. */
  readonly filter?: string;
  /** Whether inactive rows are included: `inactive=1`. */
  readonly inactive?: boolean;
  /** A distribution's custom filters, by key: `x.overdue=1`. */
  readonly custom?: Readonly<Record<string, string>>;
  /** The sort column, `:desc` for descending: `order=Name:desc`. */
  readonly order?: string;
  /** The shown columns, as path keys: `cols=Code,Name,Parent`. */
  readonly cols?: readonly string[];
  /** The 1-based page: `page=2`. */
  readonly page?: number;
  /** The page size: `size=100`. */
  readonly size?: number;
  /** Grid or tiles: `view=tiles`. */
  readonly view?: 'grid' | 'tiles';
  /** Whether a tree shows as a flat list: `list=1`. */
  readonly list?: boolean;
}

/**
 * The drill-through list's `#d=` payload: the list's fixed filter, and the
 * columns it opens with. Neither travels in the list's query.
 */
export interface TmListPayload {
  /** The list's fixed Queryex filter, or `null` for none. */
  readonly filter: string | null;
  /** The columns the list opens with, as path keys only. */
  readonly cols?: readonly string[];
}

/**
 * The defaults of one search page, which its canonical URL leaves out. A
 * field without a default here is left out only at its code default (the
 * first page, active rows only, a tree as a tree, no text).
 */
export interface TmSearchDefaults {
  /** The page's first tab. */
  readonly tab?: string;
  /** The page's declared columns, as path keys. */
  readonly cols?: readonly string[];
  /** The page size; 50 when unset. */
  readonly size?: number;
  /** The view the page's width picks. */
  readonly view?: 'grid' | 'tiles';
}

/** The page size a search page shows by default. */
const DEFAULT_SIZE = 50;

/** A column path: dotted PascalCase or camelCase identifiers, `Parent.Name`. */
const PATH = /^[A-Za-z][A-Za-z0-9]*(\.[A-Za-z][A-Za-z0-9]*)*$/;

/** A positive integer without leading zeros. */
const POSITIVE = /^[1-9]\d*$/;

/** The column-filter operators whose value is a `,`-separated list. */
const LIST_OPERATORS: ReadonlySet<string> = new Set(['in', 'between']);

/** Every column-filter operator. */
const OPERATORS: ReadonlySet<string> = new Set([
  'contains',
  'starts-with',
  'ends-with',
  'eq',
  'ne',
  'lt',
  'le',
  'gt',
  'ge',
  'between',
  'in',
  'before',
  'after',
  'is',
]);

/** The fragment that carries a payload. */
const FRAGMENT_PREFIX = '#d=';

/** Receives each invalid entry a parse drops, as a developer-facing sentence. */
export type TmQueryProblemSink = (problem: string) => void;

// -----------------------------------------------------------------------------
// Encoding
// -----------------------------------------------------------------------------

/**
 * Encodes one query value: `encodeURIComponent`, with `,`, `:` and `@`
 * restored, so a filter such as `in:Service,Sale` stays readable. Spaces
 * become `%20`.
 *
 * @param value - The raw value.
 * @returns The encoded value.
 */
export function tmEncodeQueryValue(value: string): string {
  return encodeURIComponent(value).replace(/%2C/g, ',').replace(/%3A/g, ':').replace(/%40/g, '@');
}

/**
 * The base64url text, without padding, of a string's UTF-8 bytes.
 *
 * @param text - The text.
 * @returns Its base64url form.
 */
function toBase64Url(text: string): string {
  let binary = '';
  for (const byte of new TextEncoder().encode(text)) {
    binary += String.fromCharCode(byte);
  }
  return btoa(binary).replace(/\+/g, '-').replace(/\//g, '_').replace(/=+$/, '');
}

/**
 * Decodes base64url text, padded or not, into the string its UTF-8 bytes
 * hold.
 *
 * @param data - The base64url text.
 * @returns The string, or `null` when the text is not base64url or its bytes are not UTF-8.
 */
function fromBase64Url(data: string): string | null {
  if (!/^[A-Za-z0-9_-]*$/.test(data) || data.length % 4 === 1) {
    return null;
  }
  try {
    const binary = atob(data.replace(/-/g, '+').replace(/_/g, '/'));
    const bytes = Uint8Array.from(binary, (char) => char.charCodeAt(0));
    return new TextDecoder('utf-8', { fatal: true }).decode(bytes);
  } catch {
    return null;
  }
}

/**
 * The `#d=` fragment of a JSON value.
 *
 * @param value - The payload.
 * @returns The fragment, with its `#`.
 */
function fragmentOf(value: object): string {
  return FRAGMENT_PREFIX + toBase64Url(JSON.stringify(value));
}

/**
 * The JSON object a raw `#d=` fragment carries.
 *
 * @param hash - The raw `location.hash`, with its `#`, or empty.
 * @returns `undefined` when the hash carries no payload, `null` when it
 * carries a malformed one, else the payload.
 */
function payloadOf(hash: string): Readonly<Record<string, unknown>> | null | undefined {
  if (!hash.startsWith(FRAGMENT_PREFIX)) {
    return undefined;
  }
  const text = fromBase64Url(hash.slice(FRAGMENT_PREFIX.length));
  if (text === null) {
    return null;
  }
  try {
    const value: unknown = JSON.parse(text);
    return typeof value === 'object' && value !== null && !Array.isArray(value)
      ? (value as Readonly<Record<string, unknown>>)
      : null;
  } catch {
    return null;
  }
}

// -----------------------------------------------------------------------------
// Printing
// -----------------------------------------------------------------------------

/** A printed URL's query and fragment. */
export interface TmPrintedQuery {
  /** The query string, without its `?`; empty when no field is set. */
  readonly search: string;
  /** The `#d=` fragment, with its `#`; empty when no filter is set. */
  readonly hash: string;
}

/**
 * Prints a search query in its canonical form: the parameters in the
 * codec's order (`tab`, `q`, `f.*` by path, `inactive`, `x.*` by key,
 * `order`, `cols`, `page`, `size`, `view`, `list`), every default left out,
 * and the filter in the fragment.
 *
 * @param query - The query state.
 * @param defaults - The page's own defaults.
 * @returns The query string and the fragment.
 *
 * @example
 * ```ts
 * tmPrintSearchQuery({ q: 'jed', order: 'Code' }); // { search: 'q=jed&order=Code', hash: '' }
 * ```
 */
export function tmPrintSearchQuery(query: TmSearchQuery, defaults: TmSearchDefaults = {}): TmPrintedQuery {
  const parts: string[] = [];
  const add = (name: string, value: string): void => {
    parts.push(`${tmEncodeQueryValue(name)}=${tmEncodeQueryValue(value)}`);
  };

  // --- The fields in the codec's order, each omitted at its default ---
  if (query.tab && query.tab !== defaults.tab) {
    add('tab', query.tab);
  }
  const q = query.q === undefined ? '' : tmStripBidiControls(query.q);
  if (q !== '') {
    add('q', q);
  }
  for (const path of Object.keys(query.columnFilters ?? {}).sort(ordinal)) {
    add(`f.${path}`, query.columnFilters![path]!);
  }
  if (query.inactive) {
    add('inactive', '1');
  }
  for (const key of Object.keys(query.custom ?? {}).sort(ordinal)) {
    add(`x.${key}`, query.custom![key]!);
  }
  if (query.order) {
    add('order', query.order);
  }
  if (query.cols && query.cols.length > 0 && !sameList(query.cols, defaults.cols)) {
    add('cols', query.cols.join(','));
  }
  if (query.page !== undefined && query.page !== 1) {
    add('page', String(query.page));
  }
  if (query.size !== undefined && query.size !== (defaults.size ?? DEFAULT_SIZE)) {
    add('size', String(query.size));
  }
  if (query.view && query.view !== defaults.view) {
    add('view', query.view);
  }
  if (query.list) {
    add('list', '1');
  }

  // --- The filter, in the fragment ---
  return { search: parts.join('&'), hash: query.filter ? fragmentOf({ filter: query.filter }) : '' };
}

/**
 * The fragment of a drill-through list's payload.
 *
 * @param payload - The payload.
 * @returns The `#d=` fragment, with its `#`.
 */
export function tmPrintListPayload(payload: TmListPayload): string {
  // JSON drops a member whose value is undefined, so a payload without
  // columns prints as the filter alone.
  return fragmentOf({ filter: payload.filter, cols: payload.cols });
}

/** Compares two strings ordinally, for the canonical order of `f.*` and `x.*`. */
function ordinal(a: string, b: string): number {
  return a < b ? -1 : a > b ? 1 : 0;
}

/** Whether two column lists hold the same keys in the same order. */
function sameList(a: readonly string[], b: readonly string[] | undefined): boolean {
  return b !== undefined && a.length === b.length && a.every((key, index) => key === b[index]);
}

// -----------------------------------------------------------------------------
// Parsing
// -----------------------------------------------------------------------------

/**
 * Whether a column filter's value, `{op}:{value}`, is well formed: a known
 * operator and a non-empty value; `in` and `between` take a `,`-separated
 * list with `\,` and `\\` as escapes, and `between` exactly two items.
 *
 * @param filter - The parameter's value.
 * @returns Whether it is well formed.
 */
export function tmIsColumnFilter(filter: string): boolean {
  const colon = filter.indexOf(':');
  const op = filter.slice(0, colon);
  const value = filter.slice(colon + 1);
  if (colon < 0 || !OPERATORS.has(op) || value === '') {
    return false;
  }
  if (!LIST_OPERATORS.has(op)) {
    return true;
  }
  // Split on unescaped commas; a backslash escapes only `,` and `\`.
  const items: string[] = [''];
  for (let index = 0; index < value.length; index++) {
    const char = value[index]!;
    if (char === '\\') {
      const next = value[++index];
      if (next !== ',' && next !== '\\') {
        return false;
      }
      items[items.length - 1] += next;
    } else if (char === ',') {
      items.push('');
    } else {
      items[items.length - 1] += char;
    }
  }
  return items.every((item) => item !== '') && (op === 'in' || items.length === 2);
}

/**
 * Whether a string is a column path key, such as `Parent.Name`.
 *
 * @param key - The candidate.
 * @returns Whether it is one.
 */
export function tmIsPathKey(key: string): boolean {
  return PATH.test(key);
}

/**
 * Parses the column list of `cols`, dropping each key that is not a path.
 *
 * @param value - The parameter's value.
 * @param report - Receives each dropped key.
 * @returns The keys, or `undefined` when none is left.
 */
function parseCols(value: string, report: TmQueryProblemSink): readonly string[] | undefined {
  const cols = value.split(',').filter((key) => {
    const valid = PATH.test(key);
    if (!valid) {
      report(`The column '${key}' in 'cols' is not a path and was dropped.`);
    }
    return valid;
  });
  return cols.length > 0 ? cols : undefined;
}

/**
 * Parses a positive integer parameter.
 *
 * @param value - The parameter's value.
 * @returns The number, or `undefined` when it is not a positive integer.
 */
function positive(value: string): number | undefined {
  const number = Number(value);
  return POSITIVE.test(value) && Number.isSafeInteger(number) ? number : undefined;
}

/**
 * Parses a search page's URL into its query state. Parsing is lenient: an
 * invalid parameter is dropped and reported, and the rest stands. A filter
 * whose Queryex is invalid is kept, since the server reports it; only a
 * malformed `#d=` payload is dropped. Parameters the codec does not own are
 * ignored. `q` comes back without bidi control characters.
 *
 * @param params - The URL's query parameters.
 * @param hash - The raw `location.hash`, with its `#`, never the router's
 * decoded fragment.
 * @param report - Receives a sentence for each dropped entry.
 * @returns The query state.
 *
 * @example
 * ```ts
 * tmParseSearchQuery(new URLSearchParams('q=jed&order=Code'), ''); // { q: 'jed', order: 'Code' }
 * ```
 */
export function tmParseSearchQuery(params: URLSearchParams, hash: string, report: TmQueryProblemSink = () => undefined): TmSearchQuery {
  const query: { -readonly [K in keyof TmSearchQuery]: TmSearchQuery[K] } = {};
  const columnFilters: Record<string, string> = {};
  const custom: Record<string, string> = {};
  const drop = (name: string, value: string): void => report(`The parameter '${name}=${value}' is invalid and was dropped.`);

  // --- The query string, one parameter at a time; the first of a name wins ---
  const seen = new Set<string>();
  for (const [name, value] of params) {
    if (seen.has(name)) {
      report(`The parameter '${name}' appears more than once; only the first counts.`);
      continue;
    }
    seen.add(name);
    if (name.startsWith('f.')) {
      const path = name.slice(2);
      if (PATH.test(path) && tmIsColumnFilter(value)) {
        columnFilters[path] = value;
      } else {
        drop(name, value);
      }
    } else if (name.startsWith('x.')) {
      if (name.length > 2 && value !== '') {
        custom[name.slice(2)] = value;
      } else {
        drop(name, value);
      }
    } else {
      parseField(query, name, value, report, drop);
    }
  }
  if (Object.keys(columnFilters).length > 0) {
    query.columnFilters = columnFilters;
  }
  if (Object.keys(custom).length > 0) {
    query.custom = custom;
  }

  // --- The fragment: the advanced filter ---
  const payload = payloadOf(hash);
  if (payload === null || (payload !== undefined && typeof payload['filter'] !== 'string')) {
    report("The '#d=' payload is malformed and was dropped.");
  } else if (payload !== undefined && payload['filter'] !== '') {
    query.filter = payload['filter'] as string;
  }
  return reorder(query);
}

/**
 * Parses one of the plain fields into `query`.
 *
 * @param query - The query being built.
 * @param name - The parameter's name.
 * @param value - Its value.
 * @param report - Receives a sentence for each dropped entry.
 * @param drop - Reports the parameter as invalid.
 */
function parseField(
  query: { -readonly [K in keyof TmSearchQuery]: TmSearchQuery[K] },
  name: string,
  value: string,
  report: TmQueryProblemSink,
  drop: (name: string, value: string) => void,
): void {
  switch (name) {
    case 'tab':
      if (value !== '') {
        query.tab = value;
      } else {
        drop(name, value);
      }
      return;
    case 'q': {
      const q = tmStripBidiControls(value);
      if (q !== '') {
        query.q = q;
      }
      return;
    }
    case 'inactive':
    case 'list':
      if (value === '1') {
        query[name] = true;
      } else if (value !== '0') {
        drop(name, value);
      }
      return;
    case 'order':
      if (PATH.test(value.endsWith(':desc') ? value.slice(0, -5) : value)) {
        query.order = value;
      } else {
        drop(name, value);
      }
      return;
    case 'cols':
      query.cols = parseCols(value, report);
      if (query.cols === undefined) {
        delete query.cols;
      }
      return;
    case 'page':
    case 'size': {
      const number = positive(value);
      if (number !== undefined) {
        query[name] = number;
      } else {
        drop(name, value);
      }
      return;
    }
    case 'view':
      if (value === 'grid' || value === 'tiles') {
        query.view = value;
      } else {
        drop(name, value);
      }
      return;
    default:
      // A parameter another part of the app owns, such as `clone`.
      return;
  }
}

/** The fields of a search query, in the codec's order. */
const FIELD_ORDER: readonly (keyof TmSearchQuery)[] = [
  'tab',
  'q',
  'columnFilters',
  'filter',
  'inactive',
  'custom',
  'order',
  'cols',
  'page',
  'size',
  'view',
  'list',
];

/** The same query with its members in the codec's order, so equal queries print and compare alike. */
function reorder(query: TmSearchQuery): TmSearchQuery {
  const ordered: Record<string, unknown> = {};
  for (const field of FIELD_ORDER) {
    if (query[field] !== undefined) {
      ordered[field] = query[field];
    }
  }
  return ordered as TmSearchQuery;
}

/**
 * Parses a drill-through list's `#d=` payload.
 *
 * @param hash - The raw `location.hash`, with its `#`.
 * @returns The payload, or `null` when the hash carries none or a malformed one.
 */
export function tmParseListPayload(hash: string): TmListPayload | null {
  const payload = payloadOf(hash);
  if (!payload) {
    return null;
  }
  const filter = payload['filter'];
  const cols = payload['cols'];
  if (filter !== null && typeof filter !== 'string') {
    return null;
  }
  if (cols === undefined) {
    return { filter };
  }
  return Array.isArray(cols) && cols.every((key) => typeof key === 'string' && PATH.test(key)) ? { filter, cols: cols as string[] } : null;
}
