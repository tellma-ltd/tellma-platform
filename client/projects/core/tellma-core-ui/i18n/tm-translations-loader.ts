// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import type { Translation, TranslocoLoader, TranslocoLoaderData } from '@jsverse/transloco';

/**
 * A language's strings as a nested table: each leaf is an ICU message, each
 * branch a namespace (`{ tmUi: { errors: { required: '…' } } }`).
 */
export type TmUiTranslationTable = { readonly [key: string]: string | TmUiTranslationTable };

/**
 * Loads one language's strings, passed as `provideTellmaUi({ translations })`.
 *
 * `scope` is null for the root load, whose table carries everything the
 * language shows at the root (an app's string pack, `tmUi` and the app's
 * own namespaces); a scoped load carries one lazily loaded scope.
 *
 * Every loaded table is MERGED into the language's existing strings, never
 * replacing them, so the library's built-in English and an installed
 * locale pack survive any load order. When a language other than English
 * loads, English loads through the same loader as its per-key fallback.
 *
 * The library itself never calls the loader: the app activates a language
 * by awaiting `TranslocoService.load(lang)` and then calling
 * `setActiveLang(lang)`, so a switch is one synchronous re-render with no
 * key shown untranslated in between.
 */
export type TmUiTranslationsLoader = (request: {
  readonly lang: string;
  readonly scope: string | null;
}) => Promise<TmUiTranslationTable>;

/**
 * Adapts a {@link TmUiTranslationsLoader} to Transloco's loader contract.
 * Internal to the library.
 *
 * Transloco names a scoped load by its path (`reports/ar`) and passes the
 * scope beside it; the loader receives the language and the scope apart.
 */
export function ɵtmTranslocoLoader(loader: TmUiTranslationsLoader): TranslocoLoader {
  return {
    getTranslation(path: string, data?: TranslocoLoaderData): Promise<Translation> {
      const scope = data?.scope ?? null;
      const lang = scope === null ? path : path.slice(path.lastIndexOf('/') + 1);
      return loader({ lang, scope });
    },
  };
}
