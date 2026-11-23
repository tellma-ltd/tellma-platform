// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import { InjectionToken } from '@angular/core';

import type { TmLazyComponent } from '../declarations/tm-declaration-types';

// The help seam (spec 0026 §9.8). Page contexts, navigation entries, entity
// declarations and fields carry a help key; the help drawer, which arrives
// later, resolves a key to its article through this token. It is declared
// here and provided by nothing yet.

/**
 * Resolves a help key, such as `gl.center` or `gl.center.fields.code`, and a
 * language code to the component of its article, or `null` when it has none.
 * Reserved for the help drawer, which provides it; nothing provides it yet,
 * so inject it optionally.
 *
 * @example
 * ```ts
 * const articles = inject(TM_HELP_ARTICLES, { optional: true });
 * const article = articles && (await articles('gl.center', 'en'));
 * ```
 */
export const TM_HELP_ARTICLES = new InjectionToken<(key: string, language: string) => Promise<TmLazyComponent | null>>(
  'TM_HELP_ARTICLES',
);
