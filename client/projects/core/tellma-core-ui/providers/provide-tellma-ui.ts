// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import {
  inject,
  isDevMode,
  makeEnvironmentProviders,
  provideEnvironmentInitializer,
  type EnvironmentProviders,
} from '@angular/core';
import { Directionality } from '@angular/cdk/bidi';
import {
  provideTransloco,
  TRANSLOCO_LOADER,
  TRANSLOCO_TRANSPILER,
  TranslocoService,
} from '@jsverse/transloco';

import { ɵTmLiveDirectionality } from '../i18n/tm-live-directionality';
import { ɵTmMessageFormatTranspiler } from '../i18n/tm-message-transpiler';
import { ɵtmTranslocoLoader, type TmUiTranslationsLoader } from '../i18n/tm-translations-loader';
import { TM_UI_I18N_SCOPE } from '../i18n/tm-ui-translate';
import { TM_UI_STRINGS_EN } from '../i18n/strings-en';
import { provideTellmaForms, type TmFormsOptions } from '../forms/provide-tellma-forms';
import { TmTheme, ɵTM_THEME_OPTIONS, type TmThemeOptions } from '../theme/tm-theme';

/** Options for `provideTellmaUi()`. */
export interface TmUiOptions {
  /** Forms customization, forwarded to `provideTellmaForms()`. */
  readonly forms?: TmFormsOptions;
  /**
   * The languages the distribution can activate at boot (default ['en']).
   * Later changes go through `TranslocoService.setAvailableLangs`, which is
   * how locale packs add theirs, so listing packs here is unnecessary — list
   * additional languages only for langs served without a pack (they render
   * English until one lands).
   */
  readonly availableLangs?: readonly string[];
  /**
   * Loads a language's strings; registered as the Transloco loader. Every
   * loaded table merges into the language's existing strings, so the
   * library's English and installed locale packs survive any load order,
   * and loading a language other than English also loads English, through
   * the same loader, as its per-key fallback. Without a loader the library
   * renders English plus the installed locale packs.
   */
  readonly translations?: TmUiTranslationsLoader;
  /**
   * Starts {@link TmTheme} at boot from `initial`, so the operating
   * system's preference is followed from the first frame. Without it the
   * theme is created on first inject, starting from `'system'`.
   */
  readonly theme?: TmThemeOptions;
}

/**
 * The umbrella a distribution calls once: composes
 * `provideTellmaForms()` with the default Transloco-backed i18n runtime —
 * `fallbackLang: 'en'` (whole pack absent → English) plus per-key
 * fall-through (`useFallbackTranslation`, pack installed but key missing →
 * English), ICU MessageFormat interpreted without `eval` (so it runs under a
 * strict Content Security Policy), the packaged English library strings,
 * and a root `Directionality` that follows `<html dir>` live. A
 * distribution on the defaults writes ZERO other config; locale packs add
 * their strings by calling their own `provideTellmaLocale*()` next to this.
 *
 * The app switches language and direction at run time by loading the
 * language, activating it, and flipping `document.documentElement.dir` and
 * `lang`: every library surface follows, open overlays included.
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
    // ICU MessageFormat, formatted in the formatting locale rather than the
    // language tag — see the transpiler's own docs.
    { provide: TRANSLOCO_TRANSPILER, useClass: ɵTmMessageFormatTranspiler },
    // The app's string tables. Transloco merges every loaded table into the
    // language's existing strings and, with useFallbackTranslation, loads
    // English beside any other language through the same loader.
    ...(options.translations
      ? [{ provide: TRANSLOCO_LOADER, useValue: ɵtmTranslocoLoader(options.translations) }]
      : []),
    // A root Directionality that follows <html dir> and <body dir> live, so
    // every root-injected surface follows a runtime direction switch.
    { provide: Directionality, useClass: ɵTmLiveDirectionality },
    // The theme starts at boot only when the app seeds it; otherwise it is
    // created, with 'system', by whoever injects it first.
    ...(options.theme
      ? [
          { provide: ɵTM_THEME_OPTIONS, useValue: options.theme },
          provideEnvironmentInitializer(() => {
            inject(TmTheme);
          }),
        ]
      : []),
    provideEnvironmentInitializer(() => {
      // The library's built-in English strings, merged under the tmUi
      // namespace of the English language resources.
      inject(TranslocoService).setTranslation({ [TM_UI_I18N_SCOPE]: TM_UI_STRINGS_EN }, 'en', {
        merge: true,
      });
    }),
    // The core's Latin/Mono font-subset manifest entries; locale packs add
    // theirs through the same multi token (§7.1).
    provideTellmaForms(options.forms),
  ]);
}
