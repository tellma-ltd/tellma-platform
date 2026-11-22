// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import { signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { TmFakeApi, type TmFakeApiCall } from '@tellma/core-testing';

import { TmApi } from '../api/tm-api';
import { TmApiError } from '../api/tm-api-error';
import type { TmMeLocaleResult } from '../state/launch/tm-launch-docs';
import { TmCacheRefresher } from '../state/sync/tm-cache-refresher';
import { TmLocaleWrite } from './tm-locale-write';

// The locale write alone, over the fake API and a refresher that only
// knows the routed tenant: each test scripts the answers, holds them in
// flight as it needs, and moves the clock past the coalescing delay.

/** A write's answer the test settles when it chooses. */
interface Held {
  /** Lands the write. */
  readonly land: () => void;
  /** Fails the write. */
  readonly fail: (error: unknown) => void;
}

/** What a test drives and inspects. */
interface Rig {
  readonly write: TmLocaleWrite;
  readonly api: TmFakeApi;
  readonly adopt: ReturnType<typeof vi.fn>;
  readonly routed: ReturnType<typeof signal<number | null>>;
}

/** The write result the server answers with. */
const RESULT: TmMeLocaleResult = { modifiedAt: '2026-11-02T00:00:00.0000000+00:00', preferencesTag: 'P2' };

/** Creates the writer in tenant 17. */
function rig(): Rig {
  const routed = signal<number | null>(17);
  const adopt = vi.fn();
  const refresher = {
    ɵpartition: () => {
      const tenantId = routed();
      return tenantId === null ? null : { kind: 'tenant', sub: 'u1', tenantId };
    },
    adopt,
  };
  TestBed.configureTestingModule({
    providers: [TmFakeApi, { provide: TmApi, useExisting: TmFakeApi }, { provide: TmCacheRefresher, useValue: refresher }],
  });
  const api = TestBed.inject(TmFakeApi);
  api.respond('users/me/locale', RESULT);
  const write = TestBed.inject(TmLocaleWrite);
  TestBed.tick();
  return { write, api, adopt, routed };
}

/**
 * Holds every later write of the route in flight.
 *
 * @param api - The fake API.
 * @returns The held answers, in order.
 */
function hold(api: TmFakeApi): Held[] {
  const held: Held[] = [];
  api.respond(
    'users/me/locale',
    () =>
      new Promise((resolve, reject) => {
        held.push({ land: () => resolve(RESULT), fail: reject });
      }),
  );
  return held;
}

/** The writes and sends of the locale route, as tenant and body. */
function sent(api: TmFakeApi): { readonly kind: string; readonly tenantId: number | undefined; readonly body: unknown }[] {
  return api
    .calls()
    .filter((call: TmFakeApiCall) => call.route === 'users/me/locale')
    .map((call) => ({ kind: call.kind, tenantId: (call.options as { readonly tenantId?: number } | undefined)?.tenantId, body: call.body }));
}

/** Moves the clock past the coalescing delay, and lets the answers and effects run. */
async function later(ms = 600): Promise<void> {
  await vi.advanceTimersByTimeAsync(ms);
  await settle();
}

/** Lets the pending answers and effects run. */
async function settle(): Promise<void> {
  for (let i = 0; i < 5; i++) {
    await Promise.resolve();
    TestBed.tick();
  }
}

beforeEach(() => {
  vi.useFakeTimers({ toFake: ['setTimeout', 'clearTimeout'] });
});

afterEach(() => {
  vi.useRealTimers();
});

describe('TmLocaleWrite', () => {
  it("never merges one tenant's members into another's, even behind a write in flight", async () => {
    const { write, api, routed } = rig();
    const held = hold(api);
    write.queue(17, { region: 'US' });
    await later();
    // A second change in tenant 17 waits behind its write.
    write.queue(17, { digits: 'Latin' });

    // The tab enters tenant 18 and changes a member there.
    routed.set(18);
    await settle();
    write.queue(18, { language: 'ar' });
    await later();

    // Tenant 17's first write lands; its queued change follows, to tenant 17.
    held[0].land();
    await settle();
    expect(sent(api)).toEqual([
      { kind: 'write', tenantId: 17, body: { region: 'US' } },
      { kind: 'write', tenantId: 18, body: { language: 'ar' } },
      { kind: 'write', tenantId: 17, body: { digits: 'Latin' } },
    ]);
    held.slice(1).forEach((answer) => answer.land());
    await settle();
    expect(write.state()).toBe('saved');
  });

  it("keeps an offline write's members for its tenant while another tenant writes", async () => {
    const { write, api } = rig();
    api.respond('users/me/locale', (call: TmFakeApiCall) => {
      if ((call.options as { readonly tenantId?: number }).tenantId === 17 && !api.online()) {
        throw new TmApiError({ kind: 'offline' });
      }
      return RESULT;
    });
    api.setOnline(false);
    await settle();
    write.queue(17, { region: 'US' });
    await later();
    expect(write.state()).toBe('saving');

    write.queue(18, { language: 'ar' });
    await later();
    api.setOnline(true);
    await settle();

    expect(sent(api)).toEqual([
      { kind: 'write', tenantId: 17, body: { region: 'US' } },
      { kind: 'write', tenantId: 18, body: { language: 'ar' } },
      { kind: 'write', tenantId: 17, body: { region: 'US' } },
    ]);
    expect(write.state()).toBe('saved');
  });

  it('lets a later write supersede an offline wait, so the changes queued during it follow at once', async () => {
    const { write, api, adopt } = rig();
    api.respond('users/me/locale', () => {
      throw new TmApiError({ kind: 'offline' });
    });
    write.queue(17, { region: 'US' });
    await later();

    // The server answers again without the tab seeing an offline spell.
    const held = hold(api);
    write.queue(17, { timeZone: 'Africa/Cairo' });
    await later();
    write.queue(17, { digits: 'Latin' });
    await later();
    held[0].land();
    await settle();
    held[1].land();
    await settle();

    expect(sent(api).map((call) => call.body)).toEqual([
      { region: 'US' },
      { region: 'US', timeZone: 'Africa/Cairo' },
      { digits: 'Latin' },
    ]);
    expect(adopt).toHaveBeenCalledTimes(2);
    expect(write.state()).toBe('saved');
  });

  it('sends each queue to its own tenant when the page hides, and then shows nothing saving', async () => {
    const { write, api } = rig();
    write.queue(18, { language: 'ar' });
    await settle();
    window.dispatchEvent(new Event('pagehide'));

    expect(sent(api)).toEqual([{ kind: 'send', tenantId: 18, body: { language: 'ar' } }]);
    expect(write.state()).toBe('idle');
    // The timer it replaced never fires a second copy.
    await later();
    expect(sent(api)).toHaveLength(1);
  });

  it('keeps saving after a pagehide while a write is still in flight, until it lands', async () => {
    const { write, api } = rig();
    const held = hold(api);
    write.queue(17, { region: 'US' });
    await later();
    write.queue(17, { digits: 'Latin' });
    window.dispatchEvent(new Event('pagehide'));
    expect(write.state()).toBe('saving');

    held[0].land();
    await settle();
    expect(sent(api).map((call) => call.kind)).toEqual(['write', 'send']);
    expect(write.state()).toBe('saved');
  });
});
