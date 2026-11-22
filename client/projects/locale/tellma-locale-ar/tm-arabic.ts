// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import type { TmLanguageDefinition } from '@tellma/core';

// Arabic as a Tellma app's language (spec 0024 §7.3): the platform's `tm`
// table and the component library's Arabic, which the localization service
// merges into the language's strings on Arabic's first use. Both tables live
// in the ./strings entry point, loaded on that first use, so an app's other
// languages never download them (as §7.4 loads a calendar).

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
      import('@tellma/locale-ar/strings').then((tables) => ({ tm: tables.TM_STRINGS_AR, tmUi: tables.TM_LOCALE_AR_STRINGS })),
  };
}
