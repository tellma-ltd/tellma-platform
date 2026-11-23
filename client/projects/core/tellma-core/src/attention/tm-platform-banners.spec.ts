// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import { signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { provideTellmaTesting, TmFakeApi } from '@tellma/core-testing';

import { TM_API_HOST } from '../api/tm-api-host';
import { TmFakeApiHost } from '../api/tm-api.testing.util';
import { TmLocalization } from '../l10n/tm-localization';
import { TmBanners } from './tm-banners';
import { tmPlatformBanners } from './tm-platform-banners';

// The platform's offline banner on a fake clock: it shows once the server
// has been unreachable for 5 s and closes when it answers again.

/** Starts the banner sources over the fake API, with the clock faked first. */
function setUp(): { api: TmFakeApi; banners: TmBanners } {
  vi.useFakeTimers({ toFake: ['setTimeout', 'clearTimeout', 'Date', 'performance'] });
  TestBed.configureTestingModule({
    providers: [
      provideTellmaTesting(),
      { provide: TM_API_HOST, useValue: new TmFakeApiHost() },
      // The message is its key, so the test needs no strings.
      { provide: TmLocalization, useValue: { ɵtext: (key: string) => signal(key) } },
      tmPlatformBanners(),
    ],
  });
  const api = TestBed.inject(TmFakeApi);
  const banners = TestBed.inject(TmBanners);
  TestBed.tick();
  return { api, banners };
}

/** Takes the server away or brings it back, and runs the effects. */
function setOnline(api: TmFakeApi, online: boolean): void {
  api.setOnline(online);
  TestBed.tick();
}

describe('the platform banners', () => {
  afterEach(() => {
    vi.useRealTimers();
  });

  it('shows the offline banner once the server has been unreachable for 5 s', async () => {
    const { api, banners } = setUp();
    setOnline(api, false);
    await vi.advanceTimersByTimeAsync(4999);
    expect(banners.ɵhead()).toBeNull();
    await vi.advanceTimersByTimeAsync(1);
    const options = banners.ɵhead()?.options;
    expect(options).toMatchObject({ id: 'tm.offline', level: 1, tone: 'error', icon: 'wifi-off', dismissible: false });
    expect(options?.message()).toBe('tm.attention.banners.offline');
    expect(options?.action).toBeUndefined();
  });

  it('shows nothing when the server answers within 5 s', async () => {
    const { api, banners } = setUp();
    setOnline(api, false);
    await vi.advanceTimersByTimeAsync(4000);
    setOnline(api, true);
    await vi.advanceTimersByTimeAsync(10_000);
    expect(banners.ɵhead()).toBeNull();
  });

  it('closes the offline banner when the server answers again', async () => {
    const { api, banners } = setUp();
    setOnline(api, false);
    await vi.advanceTimersByTimeAsync(5000);
    expect(banners.ɵhead()?.options.id).toBe('tm.offline');
    setOnline(api, true);
    expect(banners.ɵhead()).toBeNull();

    // A second outage shows it again.
    setOnline(api, false);
    await vi.advanceTimersByTimeAsync(5000);
    expect(banners.ɵhead()?.options.id).toBe('tm.offline');
  });
});
