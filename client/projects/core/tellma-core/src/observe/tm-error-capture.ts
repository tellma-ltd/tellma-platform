// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import { TmApiError } from '../api/tm-api-error';
import type { TmErrorRecord, TmErrorSource } from './tm-error-handler';

// What the telemetry pipeline does with one error (spec 0026 §4.1-§4.3): the
// filters for global errors, the class and the fingerprint, the redaction of
// the message and the reduction of the stack, the record the tab keeps for
// the dump, and the throttles on what is sent. It is part of the pipeline's
// lazy chunk: only the pipeline imports it.

/** What a module that failed to load throws, in each engine. */
const CHUNK_LOAD = /Failed to fetch dynamically imported module|error loading dynamically imported module|Importing a module script failed/i;

/** The browser's notice that a resize observer's callbacks did not settle in one frame. */
const RESIZE_OBSERVER = /ResizeObserver loop/;

/** An identifier, which a quoted text may be and still ship. */
const IDENTIFIER = /^[A-Za-z_$][A-Za-z0-9_$]{0,63}$/;

/** The characters a telemetry value may hold; anything else in a fingerprint becomes `_`. */
const NOT_VALUE = /[^A-Za-z0-9_.:/@+-]/g;

/** An absolute URL, a `www.` host, or a path that carries a query or a fragment. */
const URLS = /\b[a-z][a-z0-9+.-]*:\/\/[^\s'"<>]+|\bwww\.[^\s'"<>]+|(?<![\w/.])\/[^\s'"<>?#]*[?#][^\s'"<>]*/gi;

/** An e-mail address, in any script. */
const EMAILS = /[^\s@<>'"“”«»(),;[\]]+@[^\s@<>'"“”«»(),;[\]]+\.[^\s@<>'"“”«»(),;[\].]+/g;

/**
 * A quoted text in single, double, curly or angle quotes. A single quote
 * opens only after a non-letter, so an apostrophe never opens one.
 */
const QUOTED = /(?<![\p{L}\p{N}])'([^'\n]*)'|"([^"\n]*)"|“([^”\n]*)”|«([^»\n]*)»/gu;

/** A run of digits. */
const DIGITS = /[0-9]+/g;

/** A run of non-ASCII characters, with the spaces between such runs, so a phrase becomes one mark. */
const NON_ASCII = /\P{ASCII}+(?:\s+\P{ASCII}+)*/gu;

/** A V8 stack frame, `at fn (url:1:2)`. */
const V8_FRAME = /^\s*at\s/;

/** A frame location at the end of a line, `:1:2` or `:1`. */
const POSITION = /(?::\d+){1,2}$/;

/** An absolute URL in a stack frame, with its position if it has one. */
const FRAME_URL = /\b[a-z][a-z0-9+.-]*:\/\/[^\s()]+/gi;

/** The most characters of a message that ship. */
const MESSAGE_CHARS = 200;

/** The most frames of a stack that ship. */
const STACK_FRAMES = 30;

/** The most characters of a stack that ship. */
const STACK_CHARS = 4096;

/** The most characters of a fingerprint, as of any telemetry value. */
const FINGERPRINT_CHARS = 64;

/** How many error records the tab keeps for the dump. */
const RECORDS = 50;

/** The throttles' window: 10 minutes. */
const THROTTLE_MS = 10 * 60 * 1000;

/** The most error items sent per window. */
const THROTTLE_ITEMS = 30;

/**
 * Masks what a message may quote of the user's data, then caps it at 200
 * characters: URLs become `<url>`, e-mail addresses `<email>`, quoted text
 * that is not an identifier `"…"`, each run of digits `#`, and each run of
 * non-ASCII text `…`.
 *
 * @param message - The raw message.
 * @returns The message as it may leave the tab.
 *
 * @example
 * ```ts
 * tmRedactMessage('Invalid value "Farah Nasser" at row 42'); // 'Invalid value "…" at row #'
 * ```
 */
export function tmRedactMessage(message: string): string {
  return message
    .replace(URLS, '<url>')
    .replace(EMAILS, '<email>')
    .replace(QUOTED, (quoted, ...inner: (string | undefined)[]) =>
      IDENTIFIER.test(inner.slice(0, 4).find((text) => text !== undefined) ?? '') ? quoted : '"…"',
    )
    .replace(DIGITS, '#')
    .replace(NON_ASCII, '…')
    .slice(0, MESSAGE_CHARS);
}

/** A stack reduced to what may leave the tab. */
export interface TmReducedStack {
  /** The frames, one per line, each URL reduced to its file name; at most 30 frames and 4,096 characters. */
  readonly text: string;
  /** Whether the first frame that names a script names one of this origin; `null` when no frame names a script. */
  readonly topSameOrigin: boolean | null;
  /** The first same-origin frame's position, such as `chunk-K3J7Q2M9.js:1:2345`; `null` when there is none. */
  readonly firstSameOrigin: string | null;
}

/**
 * Reduces a stack to its frames: the message lines a V8 stack starts with
 * are dropped, each frame's URL becomes its file name, and non-ASCII text
 * is masked.
 *
 * @param stack - The raw stack.
 * @param origin - The app's origin, such as `https://acme.app.tellma.com`.
 * @returns The reduced stack and where its frames point.
 *
 * @example
 * ```ts
 * tmReduceStack('TypeError: x\n    at f (https://a.example/chunk-K3.js:1:2)', 'https://a.example').text; // 'at f (chunk-K3.js:1:2)'
 * ```
 */
export function tmReduceStack(stack: string, origin: string): TmReducedStack {
  const lines = stack.split('\n').map((line) => line.trim());
  // A V8 stack repeats the message before its `at` frames; the other engines list frames only.
  const v8 = lines.some((line) => V8_FRAME.test(line));
  const frames: string[] = [];
  let length = 0;
  let topSameOrigin: boolean | null = null;
  let firstSameOrigin: string | null = null;
  for (const line of lines) {
    if (line === '' || (v8 ? !V8_FRAME.test(line) : !line.includes('@') && !POSITION.test(line))) {
      continue;
    }
    // Each URL becomes its file name and position; the first one decides where the frame points.
    let frameOrigin: string | null = null;
    let framePosition: string | null = null;
    const reduced = line
      .replace(FRAME_URL, (url) => {
        const position = POSITION.exec(url)?.[0] ?? '';
        const base = url.slice(0, url.length - position.length);
        const file = (base.split(/[?#]/)[0] ?? '').split('/').pop() ?? '';
        if (frameOrigin === null) {
          frameOrigin = originOf(base);
          framePosition = file + position;
        }
        return file + position;
      })
      .replace(NON_ASCII, '…');
    if (frameOrigin !== null) {
      topSameOrigin ??= frameOrigin === origin;
      if (firstSameOrigin === null && frameOrigin === origin) {
        firstSameOrigin = framePosition;
      }
    }
    // The caps keep whole frames.
    if (frames.length < STACK_FRAMES && length + reduced.length + (frames.length > 0 ? 1 : 0) <= STACK_CHARS) {
      length += reduced.length + (frames.length > 0 ? 1 : 0);
      frames.push(reduced);
    }
  }
  return { text: frames.join('\n'), topSameOrigin, firstSameOrigin };
}

/**
 * The origin of a URL, or `null` when it has none, as an `about:` or
 * `blob:` URL of no origin does.
 *
 * @param url - The URL.
 * @returns The origin.
 */
function originOf(url: string): string | null {
  try {
    const origin = new URL(url).origin;
    return origin === 'null' ? url.slice(0, url.indexOf('://') + 3) : origin;
  } catch {
    return url.slice(0, url.indexOf('://') + 3);
  }
}

/**
 * An error's class: `ChunkLoadError` for a module that failed to load,
 * `TmApiError` for a request error, else the error's name, its
 * constructor's name, or the type of a value that is not an object.
 *
 * @param error - What was thrown or rejected.
 * @returns The class, such as `TypeError`.
 *
 * @example
 * ```ts
 * tmErrorClass(new TypeError('x')); // 'TypeError'
 * ```
 */
export function tmErrorClass(error: unknown): string {
  if (error instanceof TmApiError) {
    return 'TmApiError';
  }
  const message = messageOf(error);
  if (message !== undefined && CHUNK_LOAD.test(message)) {
    return 'ChunkLoadError';
  }
  if (typeof error === 'object' && error !== null) {
    const name: unknown = (error as { readonly name?: unknown }).name;
    if (typeof name === 'string' && IDENTIFIER.test(name)) {
      return name;
    }
    const constructorName: unknown = (error as { readonly constructor?: { readonly name?: unknown } }).constructor?.name;
    return typeof constructorName === 'string' && IDENTIFIER.test(constructorName) ? constructorName : 'Object';
  }
  return error === null ? 'null' : typeof error;
}

/**
 * The message an error carries: an error's `message`, or a thrown string.
 *
 * @param error - What was thrown or rejected.
 * @returns The message, or `undefined` when there is none.
 */
function messageOf(error: unknown): string | undefined {
  if (typeof error === 'string') {
    return error;
  }
  const message: unknown = typeof error === 'object' && error !== null ? (error as { readonly message?: unknown }).message : undefined;
  return typeof message === 'string' ? message : undefined;
}

/**
 * Captures errors for the telemetry pipeline: it filters global noise,
 * classifies, redacts and fingerprints each error, keeps the last 50
 * records, and throttles what is sent.
 */
export class TmErrorCapture {
  readonly #origin: string;

  /** The last records, newest last. */
  readonly #records: TmErrorRecord[] = [];

  /** When each fingerprint was last admitted. */
  readonly #admitted = new Map<string, number>();

  /** When each admitted item of the window was admitted, oldest first. */
  #window: number[] = [];

  /**
   * Creates the capture.
   *
   * @param origin - The app's origin, which tells same-origin frames from foreign ones.
   */
  constructor(origin: string) {
    this.#origin = origin;
  }

  /** The last 50 records, newest last. */
  get records(): readonly TmErrorRecord[] {
    return [...this.#records];
  }

  /**
   * Captures one error: `filtered` for global noise, `foreign` for a global
   * error whose top frame is another origin's script, else its record,
   * which the capture also keeps.
   *
   * @param error - What was thrown or rejected.
   * @param source - How it arrived.
   * @param at - When, in epoch milliseconds.
   * @param routeTemplate - The routed page's template, or empty.
   * @param recentTraceIds - The three most recent requests' trace ids, newest first.
   * @returns The capture's outcome.
   */
  capture(
    error: unknown,
    source: TmErrorSource,
    at: number,
    routeTemplate: string,
    recentTraceIds: readonly string[],
  ): TmErrorRecord | 'filtered' | 'foreign' {
    const api = error instanceof TmApiError ? error : null;
    const message = api === null ? messageOf(error) : undefined;
    // --- The filters, which apply to the global listeners' errors alone ---
    if (source === 'global') {
      if (api?.kind === 'cancelled' || api?.kind === 'unauthenticated' || (message !== undefined && RESIZE_OBSERVER.test(message))) {
        return 'filtered';
      }
    }
    const stackValue: unknown = typeof error === 'object' && error !== null ? (error as { readonly stack?: unknown }).stack : undefined;
    const stack = tmReduceStack(typeof stackValue === 'string' ? stackValue : '', this.#origin);
    if (source === 'global' && this.#foreign(error, stack)) {
      return 'foreign';
    }
    // --- The record ---
    const errorClass = tmErrorClass(error);
    const fingerprint = `${errorClass}@${stack.firstSameOrigin ?? ''}`.replace(NOT_VALUE, '_').slice(0, FINGERPRINT_CHARS);
    const record: TmErrorRecord = {
      source,
      errorClass,
      fingerprint,
      ...(api === null
        ? {}
        : {
            kind: api.kind,
            code: api.code,
            ...(api.status > 0 ? { status: api.status } : {}),
            ...(/^(?!0{32}$)[0-9a-f]{32}$/.test(api.traceId) ? { traceId: api.traceId } : {}),
          }),
      routeTemplate,
      stack: stack.text,
      ...(message === undefined ? {} : { message: tmRedactMessage(message) }),
      at,
      recentTraceIds: [...recentTraceIds],
    };
    this.#records.push(record);
    if (this.#records.length > RECORDS) {
      this.#records.shift();
    }
    return record;
  }

  /**
   * Whether an error item may be sent now: each fingerprint once per 10
   * minutes, and 30 items in all per 10 minutes. An admitted item counts
   * against both.
   *
   * @param fingerprint - The item's fingerprint.
   * @param at - When the error was captured, in epoch milliseconds.
   * @returns `true` when the item may be sent.
   */
  admit(fingerprint: string, at: number): boolean {
    this.#window = this.#window.filter((admitted) => at - admitted < THROTTLE_MS);
    const last = this.#admitted.get(fingerprint);
    if ((last !== undefined && at - last < THROTTLE_MS) || this.#window.length >= THROTTLE_ITEMS) {
      return false;
    }
    // Fingerprints outside the window are forgotten, so the map stays small.
    for (const [known, admitted] of this.#admitted) {
      if (at - admitted >= THROTTLE_MS) {
        this.#admitted.delete(known);
      }
    }
    this.#window.push(at);
    this.#admitted.set(fingerprint, at);
    return true;
  }

  /**
   * Whether a global error came from another origin's script: the event of
   * an error event without an error names a foreign file, or hides it as
   * the browser's cross-origin `Script error.`; else the top script frame
   * is foreign.
   *
   * @param error - The error.
   * @param stack - Its reduced stack.
   * @returns `true` for a foreign error.
   */
  #foreign(error: unknown, stack: TmReducedStack): boolean {
    // An error event without an error reaches the handler wrapped, the event
    // as its cause; the wrapper's own frames are Angular's listener's, so the
    // event alone says where the error came from.
    const cause: unknown = error instanceof Error ? error.cause : undefined;
    if (cause instanceof ErrorEvent && (cause.error === null || cause.error === undefined)) {
      return cause.filename === '' ? cause.message === 'Script error.' : originOf(cause.filename) !== this.#origin;
    }
    return stack.topSameOrigin === false;
  }
}
