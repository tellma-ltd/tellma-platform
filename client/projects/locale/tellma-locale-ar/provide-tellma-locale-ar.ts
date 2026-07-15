// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import {
  inject,
  makeEnvironmentProviders,
  provideEnvironmentInitializer,
  type EnvironmentProviders,
} from '@angular/core';
import { TranslocoService } from '@jsverse/transloco';

import { TM_UI_I18N_SCOPE } from '@tellma/core-ui';

import { TM_LOCALE_AR_STRINGS } from './strings-ar';

/**
 * Installs the Arabic locale pack — THE reference
 * template every later pack (`@tellma/locale-am`, …) copies. The provider
 * merges the locale's library strings under the tmUi namespace of the 'ar'
 * language resources (registering 'ar' as an available lang); the pack's
 * @font-face rules ship as a stylesheet (`@tellma/locale-ar/fonts/fonts.css`)
 * the distribution adds to its build's `styles`, and faces fetch on demand
 * via unicode-range.
 *
 * Installing the pack, calling this provider next to `provideTellmaUi()`,
 * and adding the stylesheet is the whole wiring. Without the pack, Arabic
 * keys fall back to English (never a raw key) and no Arabic font is ever
 * fetched.
 */
export function provideTellmaLocaleAr(): EnvironmentProviders {
  return makeEnvironmentProviders([
    provideEnvironmentInitializer(() => {
      const transloco = inject(TranslocoService);
      transloco.setTranslation({ [TM_UI_I18N_SCOPE]: TM_LOCALE_AR_STRINGS }, 'ar', {
        merge: true,
      });
      const langs = (transloco.getAvailableLangs() as (string | { id: string })[]).map((lang) =>
        typeof lang === 'string' ? lang : lang.id,
      );
      if (!langs.includes('ar')) {
        transloco.setAvailableLangs([...langs, 'ar']);
      }
    }),
  ]);
}
