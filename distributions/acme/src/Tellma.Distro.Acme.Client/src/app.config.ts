// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import type { ApplicationConfig } from '@angular/core';
import { provideTellma, type TmLanguageDefinition } from '@tellma/core';

import { tellmaMetadata } from '../generated/tellma-metadata';
import { acmeFeature } from './app/acme.feature';

/**
 * A language Acme offers, with empty client string tables. It stands in for
 * the platform's language definitions, `tmEnglish()` and `tmArabic()`, which
 * load the platform's own tables.
 *
 * @param code - The language's code, without its region: the server owns the region.
 * @param dir - The language's text direction.
 * @returns The language's definition.
 */
function language(code: string, dir: 'ltr' | 'rtl'): TmLanguageDefinition {
  return { code, dir, strings: async () => ({ tm: {}, tmUi: {} }) };
}

/**
 * Acme's app configuration: the emitted metadata, the languages the server
 * offers (English, the default, then Arabic) and the features, Acme's last.
 */
export const appConfig: ApplicationConfig = {
  providers: [
    provideTellma({
      metadata: tellmaMetadata,
      languages: [language('en', 'ltr'), language('ar', 'rtl')],
      features: [acmeFeature],
    }),
  ],
};
