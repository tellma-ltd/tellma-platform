// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import type { TmUiTranslationsLoader, TmUiTranslationTable } from '@tellma/core-ui';

import type { TmLanguageDefinition } from '../app/tm-app-config';
import type { TmMetadataLoaders } from '../metadata/tm-metadata-loaders';

// The app's Transloco loader (spec 0024 §7.3): one language's dictionary is
// the emitted string pack at the root, then the definition's `tmUi` table,
// then its `tm` table. A pack key that begins `tm.` or `tmUi.` would shadow
// the client tables' keys once Transloco flattens the dictionary, so it is a
// `string-key-collision`: it is dropped, with a development warning. Scoped
// loads have nothing of their own, since every table loads at the root.

/** Angular's development-mode flag, which a production build defines as false. */
declare const ngDevMode: unknown;

/** The client namespaces a pack key may neither be nor begin with. */
const CLIENT_NAMESPACES = ['tm', 'tmUi'] as const;

/**
 * The loader `provideTellmaUi` receives: each language's string pack and
 * client tables, merged into one dictionary.
 *
 * @param metadata - The emitted metadata loaders, which load the string packs.
 * @param languages - The offered languages, which load the client tables.
 * @param devMode - Whether the app runs in development mode, so a collision warns.
 * @returns The loader.
 */
export function tmTranslationsLoader(
  metadata: TmMetadataLoaders,
  languages: readonly TmLanguageDefinition[],
  devMode: () => boolean,
): TmUiTranslationsLoader {
  return async ({ lang, scope }) => {
    if (scope !== null) {
      return {};
    }
    // The pack and the tables of one language load side by side.
    const [pack, tables] = await Promise.all([
      metadata.strings[lang]?.() ?? {},
      languages.find((language) => language.code === lang)?.strings(),
    ]);
    const dictionary: Record<string, string | TmUiTranslationTable> = {};
    for (const [key, message] of Object.entries(pack)) {
      if (CLIENT_NAMESPACES.some((namespace) => key === namespace || key.startsWith(`${namespace}.`))) {
        // The build folds ngDevMode away, and the warning with it.
        if (typeof ngDevMode !== 'undefined' && ngDevMode && devMode()) {
          console.warn(`string-key-collision: the "${lang}" string pack's key "${key}" is in a client namespace and is dropped.`);
        }
      } else {
        dictionary[key] = message;
      }
    }
    if (tables !== undefined) {
      dictionary['tmUi'] = tables.tmUi;
      dictionary['tm'] = tables.tm;
    }
    return dictionary;
  };
}
