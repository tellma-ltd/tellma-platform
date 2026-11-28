// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import type { ApplicationConfig } from '@angular/core';
import { provideTellma, type TmLanguageDefinition } from '@tellma/core';

import { tellmaMetadata } from '../generated/tellma-metadata';
import { fixtureFeature } from './app/fixture.feature';

/**
 * A language the fixture offers. The fixture ships no client strings of its
 * own, so each language loads empty tables.
 *
 * @param code - The language's code.
 * @param dir - The language's text direction.
 * @returns The language's definition.
 */
function language(code: string, dir: 'ltr' | 'rtl'): TmLanguageDefinition {
  return { code, dir, strings: async () => ({ tm: {}, tmUi: {} }) };
}

/** The fixture's app configuration: one `provideTellma` call, as a distribution writes it. */
export const appConfig: ApplicationConfig = {
  providers: [
    provideTellma({
      metadata: tellmaMetadata,
      languages: [language('en', 'ltr'), language('ar', 'rtl')],
      features: [fixtureFeature],
    }),
  ],
};
