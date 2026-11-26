// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import { tmAppConfigOf } from '@tellma/core';

import { appConfig } from './app.config';
import { acmeFeature } from './app/acme.feature';

// The app configuration as the build's config check reads it back out of
// the providers.
describe('Acme app configuration', () => {
  it('offers English first, then Arabic, in their text directions', () => {
    const config = tmAppConfigOf(appConfig);

    expect(config?.languages.map(({ code, dir }) => ({ code, dir }))).toEqual([
      { code: 'en', dir: 'ltr' },
      { code: 'ar', dir: 'rtl' },
    ]);
  });

  it("ships the platform's and the library's strings for both languages", async () => {
    const [english, arabic] = (await Promise.all(tmAppConfigOf(appConfig)!.languages.map((language) => language.strings()))).map(
      (table) => table.tm['errors'] as Readonly<Record<string, unknown>>,
    );

    expect(english['retry']).toBe('Try again');
    expect((arabic['forbidden'] as Readonly<Record<string, string>>)['title']).toBe('ليست لديك صلاحية لذلك');
  });

  it("declares the customers' details page, whose layout loads on demand", async () => {
    const customer = acmeFeature.entities?.find((entity) => entity.name === 'acme.Customer');
    const load = customer?.['details'] as (() => Promise<{ readonly default: unknown }>) | undefined;

    expect(typeof load).toBe('function');
    expect(typeof (await load!()).default).toBe('function');
  });

  it("places GL's centers under Administration in the launcher", () => {
    const centers = acmeFeature.entities?.find((entity) => entity.name === 'gl.Center');

    expect([centers?.['section'], centers?.['order']]).toEqual(['administration', 30]);
  });

  it("composes Acme's own feature last, under the server feature's name", () => {
    const config = tmAppConfigOf(appConfig);

    expect(config?.features.at(-1)).toBe(acmeFeature);
    expect(acmeFeature.key).toBe('acme');
  });
});
