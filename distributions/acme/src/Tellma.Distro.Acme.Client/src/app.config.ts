// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import type { ApplicationConfig } from '@angular/core';
import { provideTellma, tmEnglish } from '@tellma/core';
import { tmArabic } from '@tellma/locale-ar';

import { tellmaMetadata } from '../generated/tellma-metadata';
import { acmeFeature } from './app/acme.feature';

/**
 * Acme's app configuration: the emitted metadata, the languages the server
 * offers (English, the default, then Arabic) and the features, Acme's last.
 */
export const appConfig: ApplicationConfig = {
  providers: [
    provideTellma({
      metadata: tellmaMetadata,
      languages: [tmEnglish(), tmArabic()],
      features: [acmeFeature],
    }),
  ],
};
