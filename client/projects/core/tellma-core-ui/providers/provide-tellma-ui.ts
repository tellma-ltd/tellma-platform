import {
  inject,
  isDevMode,
  makeEnvironmentProviders,
  provideEnvironmentInitializer,
  type EnvironmentProviders,
} from '@angular/core';
import { provideTransloco, TranslocoService } from '@jsverse/transloco';
import { provideTranslocoMessageformat } from '@jsverse/transloco-messageformat';

import { TM_UI_I18N_SCOPE } from '../i18n/tm-ui-translate';
import { TM_UI_STRINGS_EN } from '../i18n/strings-en';
import { provideTellmaForms, type TmFormsOptions } from '../forms/provide-tellma-forms';
import { TM_FONT_SUBSETS } from '../fonts/font-subsets';
import { TM_FONTS_LATIN } from '../fonts/font-manifest.generated';

/** Options for `provideTellmaUi()`. */
export interface TmUiOptions {
  /** Forms customization, forwarded to `provideTellmaForms()`. */
  readonly forms?: TmFormsOptions;
  /**
   * The languages the distribution can activate (default ['en']). Locale
   * packs extend this at runtime via `setAvailableLangs` in their provider,
   * so listing packs here is unnecessary — list additional languages only
   * for langs served without a pack (they render English until one lands).
   */
  readonly availableLangs?: readonly string[];
}

/**
 * The umbrella a distribution calls once: composes
 * `provideTellmaForms()` with the default Transloco-backed i18n runtime —
 * `fallbackLang: 'en'` (whole pack absent → English) plus per-key
 * fall-through (`useFallbackTranslation`, pack installed but key missing →
 * English), ICU MessageFormat, and the packaged English library strings. A
 * distribution on the defaults writes ZERO other config; locale packs add
 * their strings by calling their own `provideTellmaLocale*()` next to this.
 *
 * (Font preloading is a distribution-shell concern and is not wired here.)
 */
export function provideTellmaUi(options: TmUiOptions = {}): EnvironmentProviders {
  return makeEnvironmentProviders([
    provideTransloco({
      config: {
        defaultLang: 'en',
        fallbackLang: 'en',
        // MUST be non-empty: with the default [] Transloco treats every
        // language string as a scope and mangles keys.
        availableLangs: [...(options.availableLangs ?? ['en'])],
        missingHandler: {
          useFallbackTranslation: true,
          logMissingKey: false,
          allowEmpty: false,
        },
        reRenderOnLangChange: true,
        prodMode: !isDevMode(),
      },
    }),
    provideTranslocoMessageformat(),
    provideEnvironmentInitializer(() => {
      // The library's built-in English strings, merged under the tmUi
      // namespace of the English language resources.
      inject(TranslocoService).setTranslation({ [TM_UI_I18N_SCOPE]: TM_UI_STRINGS_EN }, 'en', {
        merge: true,
      });
    }),
    // The core's Latin/Mono font-subset manifest entries; locale packs add
    // theirs through the same multi token (§7.1).
    { provide: TM_FONT_SUBSETS, useValue: TM_FONTS_LATIN, multi: true },
    provideTellmaForms(options.forms),
  ]);
}
