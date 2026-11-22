// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import type { TmLanguageDefinition } from '@tellma/core';

import { TM_LOCALE_AR_STRINGS } from './strings-ar';
import { TM_STRINGS_AR } from './strings-tm-ar';

// Arabic as a Tellma app's language (spec 0024 §7.3): the platform's `tm`
// table and the component library's Arabic, which the localization service
// merges into the language's strings on Arabic's first use.

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
  return { code: 'ar', dir: 'rtl', strings: async () => ({ tm: TM_STRINGS_AR, tmUi: TM_LOCALE_AR_STRINGS }) };
}
