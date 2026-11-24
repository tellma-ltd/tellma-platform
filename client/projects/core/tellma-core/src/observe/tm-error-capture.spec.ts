// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import { TmApiError } from '../api/tm-api-error';
import { TmErrorCapture, tmErrorClass, tmRedactMessage, tmReduceStack } from './tm-error-capture';
import type { TmErrorRecord } from './tm-error-handler';

/** The app's origin in these specs. */
const ORIGIN = 'https://acme.example';

/** A trace id. */
const TRACE_ID = '4bf92f3577b34da6a3ce929d0e0e4736';

/** A V8 stack whose first frame is the app's. */
const V8_STACK = [
  "TypeError: Cannot read properties of undefined (reading 'Name')",
  '    at TmCenters.load (https://acme.example/chunk-K3J7Q2M9.js:1:2345)',
  '    at https://acme.example/main-ABCD1234.js:2:10',
].join('\n');

/**
 * Gives an error a stack of the spec's choosing.
 *
 * @param error - The error.
 * @param stack - Its stack.
 * @returns The error.
 */
function withStack<T extends Error>(error: T, stack: string): T {
  Object.defineProperty(error, 'stack', { value: stack });
  return error;
}

/**
 * Captures an error and expects a record.
 *
 * @param capture - The capture.
 * @param error - The error.
 * @param source - How it arrived.
 * @returns The record.
 */
function recorded(capture: TmErrorCapture, error: unknown, source: TmErrorRecord['source'] = 'render'): TmErrorRecord {
  const outcome = capture.capture(error, source, 1_000, '/:t/centers/:id', [TRACE_ID]);
  if (typeof outcome === 'string') {
    throw new Error(`Expected a record, got ${outcome}`);
  }
  return outcome;
}

describe('tmRedactMessage', () => {
  // The worked examples of the redaction, row by row.
  it.each([
    ["Cannot read properties of undefined (reading 'Name')", "Cannot read properties of undefined (reading 'Name')"],
    ['Invalid value "Farah Nasser" at row 42', 'Invalid value "…" at row #'],
    ['Failed to load https://acme.app.tellma.com/17/centers?q=jed', 'Failed to load <url>'],
    ['القيمة غير صالحة', '…'],
  ])('ships %j as %j', (raw, shipped) => {
    expect(tmRedactMessage(raw)).toBe(shipped);
  });

  it('masks e-mail addresses in any script', () => {
    expect(tmRedactMessage('No account for farah.nasser@acme.example')).toBe('No account for <email>');
    expect(tmRedactMessage('No account for فرح@مثال.السعودية today')).toBe('No account for <email> today');
  });

  it('masks absolute URLs, www hosts and paths that carry a query or a fragment', () => {
    expect(tmRedactMessage('GET http://localhost:5000/api/x failed')).toBe('GET <url> failed');
    expect(tmRedactMessage('See www.acme.example/help for more')).toBe('See <url> for more');
    expect(tmRedactMessage('Navigating to /17/centers?q=jed failed')).toBe('Navigating to <url> failed');
    expect(tmRedactMessage('Navigating to /17/centers#d=abc failed')).toBe('Navigating to <url> failed');
    expect(tmRedactMessage('No route matches /centers')).toBe('No route matches /centers');
  });

  it('masks a URL before the e-mail address inside it', () => {
    expect(tmRedactMessage('Fetching https://acme.example/u?mail=farah@acme.example')).toBe('Fetching <url>');
  });

  it('masks quoted text in every quote style unless it is an identifier', () => {
    expect(tmRedactMessage('Unknown “Farah Nasser”')).toBe('Unknown "…"');
    expect(tmRedactMessage('Unknown «Farah Nasser»')).toBe('Unknown "…"');
    expect(tmRedactMessage("Unknown 'Farah Nasser'")).toBe('Unknown "…"');
    expect(tmRedactMessage('Unknown "$scope_1"')).toBe('Unknown "$scope_#"');
    expect(tmRedactMessage('Unknown ""')).toBe('Unknown "…"');
  });

  it('leaves an apostrophe inside a word alone', () => {
    expect(tmRedactMessage("Can't parse 'Farah Nasser'")).toBe('Can\'t parse "…"');
  });

  it('masks each run of digits', () => {
    expect(tmRedactMessage('row 42 of 1,200')).toBe('row # of #,#');
  });

  it('masks each run of non-ASCII text, spaces between its words included', () => {
    expect(tmRedactMessage('Value القيمة غير at 3')).toBe('Value … at #');
    expect(tmRedactMessage('Name Ωmega and Ünit')).toBe('Name …mega and …nit');
  });

  it('caps the message at 200 characters after the masks', () => {
    expect(tmRedactMessage('x'.repeat(300))).toHaveLength(200);
    expect(tmRedactMessage(`${'1 '.repeat(150)}`)).toBe('# '.repeat(100));
  });
});

describe('tmReduceStack', () => {
  it('drops the message lines of a V8 stack and reduces each URL to its file name', () => {
    expect(tmReduceStack(V8_STACK, ORIGIN)).toEqual({
      text: 'at TmCenters.load (chunk-K3J7Q2M9.js:1:2345)\nat main-ABCD1234.js:2:10',
      topSameOrigin: true,
      firstSameOrigin: 'chunk-K3J7Q2M9.js:1:2345',
    });
  });

  it('drops a V8 message that names a URL', () => {
    const stack = 'Error: Failed to load https://acme.example/17/x?q=1\n    at load (https://acme.example/main.js:1:1)';
    expect(tmReduceStack(stack, ORIGIN).text).toBe('at load (main.js:1:1)');
  });

  it('reduces a Firefox stack', () => {
    const stack = 'load@https://acme.example/chunk-K3.js:1:2345\n@https://acme.example/main.js:2:10\n';
    expect(tmReduceStack(stack, ORIGIN)).toEqual({ text: 'load@chunk-K3.js:1:2345\n@main.js:2:10', topSameOrigin: true, firstSameOrigin: 'chunk-K3.js:1:2345' });
  });

  it('reduces a Safari stack', () => {
    const stack = 'load@https://acme.example/chunk-K3.js:1:2345\nmodule code@https://acme.example/main.js:2:10\nglobal code';
    expect(tmReduceStack(stack, ORIGIN).text).toBe('load@chunk-K3.js:1:2345\nmodule code@main.js:2:10');
  });

  it('drops the query and fragment of a frame URL', () => {
    expect(tmReduceStack('    at f (https://acme.example/chunk.js?v=3:1:2)', ORIGIN).text).toBe('at f (chunk.js:1:2)');
  });

  it('masks non-ASCII text in a frame', () => {
    expect(tmReduceStack('    at فرح (https://acme.example/a.js:1:1)', ORIGIN).text).toBe('at … (a.js:1:1)');
  });

  it('tells a foreign top frame from the first same-origin one', () => {
    const stack = '    at chrome-extension://abcdef/content.js:1:1\n    at f (https://acme.example/main.js:3:4)';
    expect(tmReduceStack(stack, ORIGIN)).toEqual({ text: 'at content.js:1:1\nat f (main.js:3:4)', topSameOrigin: false, firstSameOrigin: 'main.js:3:4' });
  });

  it('keeps at most 30 frames', () => {
    const stack = Array.from({ length: 40 }, (_, index) => `    at f${index} (https://acme.example/main.js:${index}:1)`).join('\n');
    const text = tmReduceStack(stack, ORIGIN).text;
    expect(text.split('\n')).toHaveLength(30);
    expect(text.split('\n').at(-1)).toBe('at f29 (main.js:29:1)');
  });

  it('keeps whole frames within 4,096 characters', () => {
    const name = 'f'.repeat(180);
    const stack = Array.from({ length: 25 }, () => `    at ${name} (https://acme.example/main.js:1:1)`).join('\n');
    const text = tmReduceStack(stack, ORIGIN).text;
    expect(text.length).toBeLessThanOrEqual(4096);
    expect(text.split('\n').every((frame) => frame === `at ${name} (main.js:1:1)`)).toBe(true);
    expect(text.split('\n')).toHaveLength(20);
  });

  it('reduces an empty stack to nothing', () => {
    expect(tmReduceStack('', ORIGIN)).toEqual({ text: '', topSameOrigin: null, firstSameOrigin: null });
  });
});

describe('tmErrorClass', () => {
  it('names an error by its name, or its constructor when the name is not an identifier', () => {
    expect(tmErrorClass(new TypeError('x'))).toBe('TypeError');
    expect(tmErrorClass(Object.assign(new Error('x'), { name: 'AcmeError' }))).toBe('AcmeError');
    expect(tmErrorClass(Object.assign(new RangeError('x'), { name: 'not a name' }))).toBe('RangeError');
    expect(tmErrorClass({ name: 'not a name' })).toBe('Object');
  });

  it('names a request error TmApiError', () => {
    expect(tmErrorClass(new TmApiError({ kind: 'server', status: 500 }))).toBe('TmApiError');
  });

  it("names a failed dynamic import ChunkLoadError, in each engine's wording", () => {
    expect(tmErrorClass(new TypeError('Failed to fetch dynamically imported module: https://acme.example/chunk-X.js'))).toBe('ChunkLoadError');
    expect(tmErrorClass(new TypeError('error loading dynamically imported module: https://acme.example/chunk-X.js'))).toBe('ChunkLoadError');
    expect(tmErrorClass(new TypeError('Importing a module script failed.'))).toBe('ChunkLoadError');
  });

  it('names a value that is not an object by its type', () => {
    expect(tmErrorClass('boom')).toBe('string');
    expect(tmErrorClass(42)).toBe('number');
    expect(tmErrorClass(undefined)).toBe('undefined');
    expect(tmErrorClass(null)).toBe('null');
  });
});

describe('TmErrorCapture', () => {
  let capture: TmErrorCapture;

  beforeEach(() => {
    capture = new TmErrorCapture(ORIGIN);
  });

  describe('records', () => {
    it("records an error's class, fingerprint, reduced stack and redacted message", () => {
      const error = withStack(new TypeError('Invalid value "Farah Nasser" at row 42'), V8_STACK);
      expect(recorded(capture, error)).toEqual({
        source: 'render',
        errorClass: 'TypeError',
        fingerprint: 'TypeError@chunk-K3J7Q2M9.js:1:2345',
        routeTemplate: '/:t/centers/:id',
        stack: 'at TmCenters.load (chunk-K3J7Q2M9.js:1:2345)\nat main-ABCD1234.js:2:10',
        message: 'Invalid value "…" at row #',
        at: 1_000,
        recentTraceIds: [TRACE_ID],
      });
    });

    it("records a request error's kind, code, status and trace id, never its message", () => {
      const error = withStack(new TmApiError({ kind: 'server', status: 500, code: 'internal', traceId: TRACE_ID, message: 'Name "Farah" failed' }), V8_STACK);
      const record = recorded(capture, error, 'api');
      expect(record).toMatchObject({ source: 'api', errorClass: 'TmApiError', kind: 'server', code: 'internal', status: 500, traceId: TRACE_ID });
      expect(record).not.toHaveProperty('message');
    });

    it('leaves out a request error status of 0 and a trace id that is not one', () => {
      const record = recorded(capture, new TmApiError({ kind: 'offline', code: 'outcome-unknown' }), 'api');
      expect(record).toMatchObject({ kind: 'offline', code: 'outcome-unknown' });
      expect(record).not.toHaveProperty('status');
      expect(record).not.toHaveProperty('traceId');
    });

    it('records a failed dynamic import as ChunkLoadError, its URL masked', () => {
      const error = withStack(new TypeError('Failed to fetch dynamically imported module: https://acme.example/chunk-X.js'), V8_STACK);
      const record = recorded(capture, error, 'global');
      expect(record).toMatchObject({ errorClass: 'ChunkLoadError', message: 'Failed to fetch dynamically imported module: <url>' });
    });

    it('records a thrown string as its own message, with no stack', () => {
      expect(recorded(capture, 'boom 42')).toMatchObject({ errorClass: 'string', fingerprint: 'string@', stack: '', message: 'boom #' });
    });

    it('keeps the last 50 records, newest last, and hands out a copy', () => {
      for (let at = 0; at < 55; at++) {
        capture.capture(new Error('x'), 'render', at, '', []);
      }
      const records = capture.records;
      expect(records).toHaveLength(50);
      expect(records[0]?.at).toBe(5);
      expect(records.at(-1)?.at).toBe(54);
      expect(capture.records).not.toBe(records);
    });
  });

  describe('fingerprints', () => {
    it('are stable across messages, and follow the first same-origin frame', () => {
      const first = recorded(capture, withStack(new TypeError('a'), V8_STACK));
      const second = recorded(capture, withStack(new TypeError('b 12'), V8_STACK.replace('reading', 'setting')));
      const moved = recorded(capture, withStack(new TypeError('a'), V8_STACK.replace(':1:2345', ':1:2346')));
      expect(second.fingerprint).toBe(first.fingerprint);
      expect(moved.fingerprint).toBe('TypeError@chunk-K3J7Q2M9.js:1:2346');
    });

    it('skip foreign frames', () => {
      const stack = '    at chrome-extension://abcdef/content.js:1:1\n    at f (https://acme.example/main.js:3:4)';
      expect(recorded(capture, withStack(new Error('x'), stack)).fingerprint).toBe('Error@main.js:3:4');
    });

    it('hold only value characters, in at most 64', () => {
      const stack = `    at f (https://acme.example/${'chunk~1-'.repeat(10)}.js:1:1)`;
      const { fingerprint } = recorded(capture, withStack(new Error('x'), stack));
      expect(fingerprint).toMatch(/^Error@chunk_1-chunk_1-/);
      expect(fingerprint).toHaveLength(64);
    });
  });

  describe('filters', () => {
    it.each(['cancelled', 'unauthenticated'] as const)('filters a global request error of kind %s, and records it from elsewhere', (kind) => {
      expect(capture.capture(new TmApiError({ kind }), 'global', 0, '', [])).toBe('filtered');
      expect(recorded(capture, new TmApiError({ kind }), 'render').kind).toBe(kind);
    });

    it('filters the global ResizeObserver loop notice', () => {
      const error = new Error('ResizeObserver loop completed with undelivered notifications.');
      expect(capture.capture(error, 'global', 0, '', [])).toBe('filtered');
    });

    it('drops a global error whose top frame is another origin’s script as foreign', () => {
      const stack = '    at chrome-extension://abcdef/content.js:1:1\n    at f (https://acme.example/main.js:3:4)';
      expect(capture.capture(withStack(new Error('x'), stack), 'global', 0, '', [])).toBe('foreign');
      expect(recorded(capture, withStack(new Error('x'), stack), 'render').source).toBe('render');
    });

    it('tells a wrapped error event by the file it names', () => {
      const wrapped = (init: ErrorEventInit): Error => new Error(init.message ?? '', { cause: new ErrorEvent('error', init) });
      expect(capture.capture(wrapped({ message: 'Script error.' }), 'global', 0, '', [])).toBe('foreign');
      expect(capture.capture(wrapped({ message: 'x', filename: 'https://cdn.example/x.js' }), 'global', 0, '', [])).toBe('foreign');
      expect(recorded(capture, wrapped({ message: 'x', filename: 'https://acme.example/main.js' }), 'global').source).toBe('global');
      expect(recorded(capture, wrapped({ message: 'x' }), 'global').source).toBe('global');
    });

    it('keeps a global error with no frames', () => {
      expect(recorded(capture, 'boom', 'global').source).toBe('global');
    });
  });

  describe('throttles', () => {
    const MINUTE = 60_000;

    it('admit each fingerprint once per 10 minutes', () => {
      expect(capture.admit('TypeError@main.js:1:1', 0)).toBe(true);
      expect(capture.admit('TypeError@main.js:1:1', 10 * MINUTE - 1)).toBe(false);
      expect(capture.admit('TypeError@main.js:2:1', 10 * MINUTE - 1)).toBe(true);
      expect(capture.admit('TypeError@main.js:1:1', 10 * MINUTE)).toBe(true);
    });

    it('admit 30 items in all per 10 minutes', () => {
      for (let index = 0; index < 30; index++) {
        expect(capture.admit(`Error@main.js:${index}:1`, index * 1_000)).toBe(true);
      }
      expect(capture.admit('Error@main.js:99:1', 30_000)).toBe(false);
      // The oldest admission leaves the window, which makes room for one more.
      expect(capture.admit('Error@main.js:99:1', 10 * MINUTE)).toBe(true);
      expect(capture.admit('Error@main.js:98:1', 10 * MINUTE)).toBe(false);
    });
  });
});
