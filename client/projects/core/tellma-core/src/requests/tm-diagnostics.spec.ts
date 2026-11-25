// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import { TestBed } from '@angular/core/testing';
import { ɵtmModalOpened } from '@tellma/core-ui/overlay';
import { TmToasts } from '@tellma/core-ui/toast';
import type { MockInstance } from 'vitest';

import { tmAppRig, tmSettle, tmSettleUntil } from '../routes/tm-app.testing.util';
import golden from './diagnostics-text-golden.json';
import { type TmDiagnosticsInput, tmDiagnosticsText } from './tm-diagnostics';
import { type TmRequestRecord, TmRequestLog } from './tm-request-log';

// Copy diagnostics (spec 0026 §8.3): the text pinned by goldens, which hold
// Latin digits, local times with the offset and no bidi control in every UI
// language; its cost; and the copy right after launch, before the browser is
// ever idle, from a button and from the global shortcut on an Arabic layout,
// in a field and over a modal, written synchronously, with its toasts
// (spec 0028 §8.1) and its event.

/** A request in the log, from its start before `now` and its outcome. */
function request(now: number, ago: number, patch: Partial<TmRequestRecord> & Pick<TmRequestRecord, 'traceId'>): TmRequestRecord {
  return {
    route: 'centers/query',
    tenantId: 17,
    method: 'POST',
    background: false,
    startedAt: now - ago,
    durationMs: null,
    outcome: 'pending',
    slow: false,
    ...patch,
  };
}

/** The moment of the Arabic and tenantless readings: 11:03:12 UTC. */
const NOW = Date.UTC(2026, 9, 20, 11, 3, 12);

/** Farah's reading, in Arabic, while a save hangs: the shape support reads. */
const ARABIC: TmDiagnosticsInput = {
  now: NOW,
  offset: () => 180,
  // A bidi mark in a name never reaches the text.
  distribution: 'Acme‏',
  ui: '1.4.0+3f9c2ab1e5d4',
  server: '1.4.0+3f9c2ab1e5d4',
  platform: '0.9.0+77e01d4',
  features: [],
  tenantId: 17,
  tabId: 'k3J7q2M9',
  locale: 'ar-SA',
  calendar: 'uq',
  online: true,
  session: 'active',
  leader: true,
  hub: 'connected',
  page: { routeTemplate: '/:t/centers/:id', kind: 'details', status: 'ready', errors: 1 },
  dialogs: [],
  requests: [
    request(NOW, 6_200, { traceId: '4bf92f3577b34da6a3ce929d0e0e4736', route: 'centers/save', slow: true }),
    request(NOW, 14_000, { traceId: '0af7651916cd43dd8448eb211c80319c', durationMs: 4_810, outcome: 'ok', status: 200, slow: true }),
    request(NOW, 31_000, { traceId: 'a1b2c3d4e5f60718293a4b5c6d7e8f90', durationMs: 310, outcome: 'cancelled' }),
    request(NOW, 32_000, { traceId: '9e8d7c6b5a4938271605f4e3d2c1b0a9', route: 'client/sync', durationMs: 120, outcome: 'ok', status: 200 }),
  ],
};

/** The English reading: west of UTC across midnight, with features, a dialog and failures. */
const ENGLISH: TmDiagnosticsInput = (() => {
  const now = Date.UTC(2026, 9, 20, 2, 10, 0);
  return {
    ...ARABIC,
    now,
    offset: () => -210,
    distribution: 'Acme',
    ui: '1.4.0+3f9c2ab',
    server: '1.5.0+9e8d7c6',
    platform: '0.9.0',
    features: [
      { key: 'core', version: '1.4.0' },
      { key: 'gl', version: '1.4.0' },
    ],
    locale: 'en-US',
    calendar: 'gc',
    online: false,
    session: 'expired',
    leader: false,
    hub: 'reconnecting',
    page: { routeTemplate: '/:t/centers', kind: 'search', status: 'error', errors: 2 },
    dialogs: [{ routeTemplate: '/:t/centers/:id', kind: 'details', status: 'loading', errors: 0 }],
    requests: [
      request(now, 2_000, { traceId: '4bf92f3577b34da6a3ce929d0e0e4736', route: 'centers/save', durationMs: 1_200, outcome: 'error', status: 500, code: 'server' }),
      request(now, 10_000, { traceId: '0af7651916cd43dd8448eb211c80319c', route: '/bff/user', method: 'GET', tenantId: null, durationMs: 40, outcome: 'error' }),
    ],
  };
})();

/** The reading outside a tenant, before any metadata, page or request. */
const TENANTLESS: TmDiagnosticsInput = {
  ...ARABIC,
  offset: () => 0,
  distribution: 'Tellma',
  ui: '',
  server: null,
  platform: '',
  tenantId: null,
  locale: 'en-GB',
  calendar: 'gc',
  hub: 'disabled',
  page: null,
  requests: [],
};

describe('tmDiagnosticsText', () => {
  it('matches the goldens in Arabic, in English and outside a tenant', () => {
    expect(tmDiagnosticsText(ARABIC, 10).split('\n')).toEqual(golden.ar);
    expect(tmDiagnosticsText(ENGLISH, 10).split('\n')).toEqual(golden.en);
    expect(tmDiagnosticsText(TENANTLESS, 10).split('\n')).toEqual(golden.tenantless);
  });

  it('holds only Latin digits and no bidi control character', () => {
    for (const input of [ARABIC, ENGLISH, TENANTLESS]) {
      const text = tmDiagnosticsText(input, 10);
      expect(text).not.toMatch(/[٠-٩۰-۹]/);
      expect(text).not.toMatch(/[؜‎‏‪-‮⁦-⁩]/);
    }
  });

  it('keeps the newest requests up to the count', () => {
    const lines = tmDiagnosticsText(ARABIC, 2).split('\n');
    expect(lines.slice(4)).toEqual(golden.ar.slice(4, 6));
    expect(tmDiagnosticsText(ARABIC, 0).split('\n').at(-1)).toBe('No requests');
  });

  it('builds the text of 100 requests in under 2 ms', () => {
    const requests = Array.from({ length: 100 }, (_, i) =>
      request(NOW, i * 1_000, { traceId: i.toString(16).padStart(32, '0'), durationMs: i * 37, outcome: i % 7 === 0 ? 'error' : 'ok', status: 200, slow: i % 5 === 0 }),
    );
    const input = { ...ARABIC, requests };
    // The best of several runs after a warm-up, so a busy machine's pause does not count.
    for (let i = 0; i < 5; i++) {
      tmDiagnosticsText(input, 100);
    }
    let best = Infinity;
    for (let i = 0; i < 10; i++) {
      const started = performance.now();
      tmDiagnosticsText(input, 100);
      best = Math.min(best, performance.now() - started);
    }
    expect(best).toBeLessThan(2);
  });
});

describe('Copy diagnostics', () => {
  /** The rig's handles. */
  interface Rig {
    /** The log under test. */
    readonly log: TmRequestLog;
    /** The clipboard's write, stubbed. */
    readonly write: MockInstance<(text: string) => Promise<void>>;
    /** The toasts shown. */
    readonly toasts: MockInstance<TmToasts['show']>;
    /** The `tm.support.diagnostics-copied` events' triggers. */
    readonly copied: () => unknown[];
  }

  const cleanups: (() => void)[] = [];

  afterEach(() => {
    cleanups.splice(0).forEach((cleanup) => cleanup());
  });

  /**
   * Starts the app with the browser never idle, so nothing the startup
   * defers to idle has run, and stubs the clipboard.
   */
  async function rig(write: (text: string) => Promise<void> = () => Promise.resolve()): Promise<Rig> {
    const idle = vi.spyOn(window, 'requestIdleCallback').mockImplementation(() => 0);
    cleanups.push(() => idle.mockRestore());
    const { telemetry } = await tmAppRig();
    const stub = vi.fn(write);
    const spy = vi.spyOn(navigator.clipboard, 'writeText').mockImplementation(stub);
    cleanups.push(() => spy.mockRestore());
    return {
      log: TestBed.inject(TmRequestLog),
      write: stub,
      toasts: vi.spyOn(TestBed.inject(TmToasts), 'show'),
      copied: () => telemetry.recent().filter((item) => item.name === 'tm.support.diagnostics-copied').map((item) => item.props['via']),
    };
  }

  /** Appends an element to the page for one test. */
  function attach<T extends HTMLElement>(element: T): T {
    document.body.append(element);
    cleanups.push(() => element.remove());
    return element;
  }

  /** Presses Mod+Alt+Shift+D on an Arabic layout, where the key types ي. */
  function press(target: EventTarget): KeyboardEvent {
    const event = new KeyboardEvent('keydown', { key: 'ي', code: 'KeyD', ctrlKey: true, altKey: true, shiftKey: true, bubbles: true, cancelable: true });
    target.dispatchEvent(event);
    return event;
  }

  it('writes the text synchronously right after launch, before idle, then toasts and reports the copy once', async () => {
    const { log, write, toasts, copied } = await rig();
    log.copy('button');
    // The write ran inside the call, as the gesture requires.
    expect(write).toHaveBeenCalledOnce();
    const text = write.mock.calls[0][0];
    expect(text.split('\n')[0]).toMatch(/^Acme diagnostics · \d{4}-\d{2}-\d{2} \d{2}:\d{2}:\d{2} [+-]\d{2}:\d{2}$/);
    expect(text).toContain('UI 1.0.0+0123abc · server unknown · platform 1.0.0');
    await tmSettleUntil(() => copied().length > 0);
    expect(toasts).toHaveBeenCalledExactlyOnceWith(expect.objectContaining({ tone: 'success', title: 'Diagnostics copied.', duration: 4_000 }));
    expect(copied()).toEqual(['button']);
  });

  it('toasts a sticky failure once, and reports nothing, when the write is refused or throws', async () => {
    const { log, write, toasts, copied } = await rig(() => Promise.reject(new DOMException('Denied', 'NotAllowedError')));
    log.copy('dialog');
    expect(write).toHaveBeenCalledOnce();
    await tmSettleUntil(() => toasts.mock.calls.length > 0);
    const failure = expect.objectContaining({ tone: 'error', title: "Couldn't copy. Open About, then Diagnostics, to see the details.", duration: 'sticky' });
    await tmSettle();
    expect(toasts).toHaveBeenCalledExactlyOnceWith(failure);

    write.mockImplementation(() => {
      throw new TypeError('No clipboard');
    });
    log.copy('button');
    expect(write).toHaveBeenCalledTimes(2);
    await tmSettleUntil(() => toasts.mock.calls.length > 1);
    await tmSettle();
    expect(toasts).toHaveBeenCalledTimes(2);
    expect(toasts).toHaveBeenLastCalledWith(failure);
    expect(copied()).toEqual([]);
  });

  it('copies with Mod+Alt+Shift+D before idle, on an Arabic layout, in a field and over a modal', async () => {
    const { write, copied } = await rig();
    const field = press(attach(document.createElement('input')));
    expect(field.defaultPrevented).toBe(true);
    expect(write).toHaveBeenCalledOnce();

    const host = attach(document.createElement('div'));
    cleanups.push(ɵtmModalOpened(host));
    press(host);
    expect(write).toHaveBeenCalledTimes(2);
    await tmSettleUntil(() => copied().length === 2);
    expect(copied()).toEqual(['shortcut', 'shortcut']);
  });
});
