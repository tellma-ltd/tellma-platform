// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import { TestBed } from '@angular/core/testing';

import { TM_LAUNCHER_PROVIDERS, type TmLauncherItem, type TmLauncherProvider, type TmLauncherSearchContext } from './tm-launcher-providers';

// The launcher's provider seam: no provider by default, and every provider a
// feature registers, in registration order.

/** A provider that answers one row per query. */
class TmEchoSearch implements TmLauncherProvider {
  readonly id = 'echo';
  readonly labelKey = 'Echo';
  readonly order = 10;

  /** Answers the query as one row. */
  search(query: string, context: TmLauncherSearchContext): Promise<readonly TmLauncherItem[]> {
    return Promise.resolve([{ id: query, href: `/${context.tenantId}/echo`, label: query }]);
  }
}

describe('TM_LAUNCHER_PROVIDERS', () => {
  it('lists no provider by default', () => {
    expect(TestBed.inject(TM_LAUNCHER_PROVIDERS)).toEqual([]);
  });

  it('lists every provider the features register', async () => {
    const help: TmLauncherProvider = { id: 'help', labelKey: 'Help', order: 20, minLength: 3, search: () => Promise.resolve([]) };
    TestBed.configureTestingModule({
      providers: [
        { provide: TM_LAUNCHER_PROVIDERS, multi: true, useClass: TmEchoSearch },
        { provide: TM_LAUNCHER_PROVIDERS, multi: true, useValue: help },
      ],
    });
    const providers = TestBed.inject(TM_LAUNCHER_PROVIDERS);
    expect(providers.map((provider) => provider.id)).toEqual(['echo', 'help']);
    const rows = await providers[0]!.search('jed', { tenantId: 17, language: 'en', limit: 5, signal: new AbortController().signal });
    expect(rows).toEqual([{ id: 'jed', href: '/17/echo', label: 'jed' }]);
  });
});
