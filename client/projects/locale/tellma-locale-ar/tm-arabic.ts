// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import type { TmLanguageDefinition } from '@tellma/core';

// Arabic as a Tellma app's language (spec 0024 §7.3): the platform's `tm`
// table and the component library's Arabic, which the localization service
// merges into the language's strings on Arabic's first use. The tables live
// in three entry points, ./platform-strings, ./shell-strings (the shell's part
// of the tm table) and ./strings, all loaded at once on that first use, so an
// app's other languages never download them (as §7.4 loads a calendar); one
// entry point per table keeps each one's growth under its own ceiling.

/**
 * Arabic, right to left, with the platform's and the component library's
 * strings.
 *
 * @returns The language's definition, for `TmAppConfig.languages`.
 *
 * @example
 * ```ts
 * provideTellma({ metadata: tellmaMetadata, languages: [tmEnglish(), tmArabic()], features: [] });
 * ```
 */
export function tmArabic(): TmLanguageDefinition {
  return {
    code: 'ar',
    dir: 'rtl',
    strings: () =>
      Promise.all([
        import('@tellma/locale-ar/platform-strings'),
        import('@tellma/locale-ar/shell-strings'),
        import('@tellma/locale-ar/strings'),
      ]).then(([platform, shell, library]) => ({
        tm: { ...platform.TM_STRINGS_AR, ...shell.TM_SHELL_STRINGS_AR },
        tmUi: library.TM_LOCALE_AR_STRINGS,
      })),
  };
}
