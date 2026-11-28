// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import { existsSync, readFileSync } from 'node:fs';
import { join } from 'node:path';

import { TM_BOOT_STRING_KEYS, tmRenderBootScript, type TmBootScriptConfig } from '../build/boot-script.js';
import { TmBuildFailure } from '../common/problems.js';
import type { TmEmittedMetadata } from '../emit/emitted-metadata.js';

// The distribution's tm-boot.js (spec 0024 §4.2): @tellma/core's template
// with TM_BOOT built from the emitted metadata. The languages and their
// directions come from the Core chunk, the first being the default; the
// boot page's strings come from the string packs. English is always
// embedded, as every language's fallback. The distribution's display name
// fills a language's missing Distribution_DisplayName: English's always, and
// another language's only when English's differs from it, since that
// language would otherwise fall back to English's.

/** The template's path inside `@tellma/core`. */
export const TM_BOOT_TEMPLATE = 'boot/tm-boot.template.js';

/**
 * Builds the boot configuration from the emitted metadata.
 *
 * @param metadata - The emitted metadata.
 * @returns The configuration.
 * @throws TmBuildFailure when the English string pack lacks a boot string.
 */
export function tmBootConfig(metadata: TmEmittedMetadata): TmBootScriptConfig {
  const { languages, distribution } = metadata.core.value;
  const strings: Record<string, Readonly<Record<string, string>>> = {};
  const codes = ['en', ...languages.map((language) => language.code).filter((code) => code !== 'en')];
  for (const code of codes) {
    const pack = metadata.strings.get(code)?.value ?? {};
    const table: Record<string, string> = {};
    for (const key of TM_BOOT_STRING_KEYS) {
      if (typeof pack[key] === 'string' && pack[key] !== '') {
        table[key] = pack[key];
      }
    }
    if (code !== 'en' && table['Distribution_DisplayName'] === undefined && strings['en']!['Distribution_DisplayName'] !== distribution.displayName) {
      table['Distribution_DisplayName'] = distribution.displayName;
    }
    if (code === 'en') {
      table['Distribution_DisplayName'] ??= distribution.displayName;
      const missing = TM_BOOT_STRING_KEYS.filter((key) => table[key] === undefined);
      if (missing.length > 0) {
        throw new TmBuildFailure(
          `The English string pack lacks ${missing.join(', ')}: the boot page needs every boot string in English, its fallback.`,
        );
      }
    }
    // A language with no boot string of its own adds nothing: English covers it.
    if (code === 'en' || Object.keys(table).length > 0) {
      strings[code] = table;
    }
  }
  return {
    v: 1,
    languages: languages.map(({ code, dir }) => ({ code, dir })),
    defaultLanguage: languages[0]!.code,
    strings,
  };
}

/**
 * Renders the distribution's `tm-boot.js`.
 *
 * @param coreDir - The `@tellma/core` package's folder.
 * @param config - The boot configuration.
 * @returns The script's text.
 * @throws TmBuildFailure when the template is missing or does not render.
 */
export function tmBuildBootScript(coreDir: string, config: TmBootScriptConfig): string {
  const template = join(coreDir, ...TM_BOOT_TEMPLATE.split('/'));
  if (!existsSync(template)) {
    throw new TmBuildFailure(`${template} does not exist; @tellma/core ships it, so reinstall or rebuild the package.`);
  }
  try {
    return tmRenderBootScript(readFileSync(template, 'utf8'), config);
  } catch (error) {
    throw new TmBuildFailure(`tm-boot.js could not be written: ${error instanceof Error ? error.message : String(error)}`, {
      cause: error,
    });
  }
}
