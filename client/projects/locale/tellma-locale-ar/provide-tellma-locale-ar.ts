import {
  inject,
  makeEnvironmentProviders,
  provideEnvironmentInitializer,
  type EnvironmentProviders,
} from '@angular/core';
import { TranslocoService } from '@jsverse/transloco';

import { TM_FONT_SUBSETS, TM_UI_I18N_SCOPE } from '@tellma/core-ui';

import { TM_FONTS_ARABIC } from './font-manifest.generated';
import { TM_LOCALE_AR_STRINGS } from './strings-ar';

/**
 * Installs the Arabic locale pack — THE reference
 * template every later pack (`@tellma/locale-am`, …) copies. One provider
 * contributes the three pieces by three standard mechanisms:
 *
 *  (a) the locale's library strings, merged under the tmUi namespace of the
 *      'ar' language resources (and 'ar' registered as an available lang);
 *  (b) its font-subset manifest entries into the TM_FONT_SUBSETS multi
 *      token — the injected value becomes the union of the core + packs;
 *  (c) its @font-face rules ship as a static stylesheet asset
 *      (`@tellma/locale-ar/fonts/fonts.css`) the distribution includes in
 *      its styles; faces fetch on demand via unicode-range.
 *
 * No build-time scan, no central registry: installing the pack and calling
 * this provider next to `provideTellmaUi()` is the whole wiring. Without
 * the pack, Arabic keys fall back to English (never a raw key) and no
 * Arabic font is ever fetched.
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
    { provide: TM_FONT_SUBSETS, useValue: TM_FONTS_ARABIC, multi: true },
  ]);
}
