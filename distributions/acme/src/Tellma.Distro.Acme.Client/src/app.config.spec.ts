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

  it("composes Acme's own feature last, under the server feature's name", () => {
    const config = tmAppConfigOf(appConfig);

    expect(config?.features.at(-1)).toBe(acmeFeature);
    expect(acmeFeature.key).toBe('acme');
  });
});
