// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

// Renders a distribution's `tm-boot.js` (spec 0024 §4.2). `@tellma/core`
// ships the boot script as a template, `boot/tm-boot.template.js`, which
// opens with `var TM_BOOT=/*TM_BOOT*/null;`. The build replaces that one
// marker with the distribution's boot configuration as a JSON literal: the
// offered languages, the default one and, per language, the boot page's
// strings and the display name. The configuration is checked first, because
// the boot script trusts it and runs before anything could report a problem.

/** The marker in the template that the boot configuration replaces. */
export const TM_BOOT_MARKER = '/*TM_BOOT*/null';

/** A language the distribution offers, as the boot script reads it. */
export interface TmBootScriptLanguage {
  /** The language code, such as `ar`. */
  readonly code: string;
  /** The language's writing direction. */
  readonly dir: 'ltr' | 'rtl';
}

/** The distribution's boot configuration, which the rendered script carries. */
export interface TmBootScriptConfig {
  /** The configuration's version; always 1. */
  readonly v: 1;
  /** The offered languages. */
  readonly languages: readonly TmBootScriptLanguage[];
  /** The code of the default language, one of `languages`. */
  readonly defaultLanguage: string;
  /** Per language code, the boot page's strings and the display name; English is the fallback. */
  readonly strings: Readonly<Record<string, Readonly<Record<string, string>>>>;
}

/** The boot strings a language may carry; English must carry all of them. */
export const TM_BOOT_STRING_KEYS: readonly string[] = Object.freeze([
  'Distribution_DisplayName',
  'Boot_Title',
  'Boot_Unsupported',
  'Boot_Cookies',
  'Boot_SignInFailed',
  'Boot_LoadFailed',
  'Boot_TryAgain',
]);

/** A BCP 47 language tag, in the plain shape the platform offers: `en`, `ar-SA`, `zh-Hant`. */
const LANGUAGE_TAG = /^[A-Za-z]{2,8}(?:-[A-Za-z0-9]{1,8})*$/;

/** Throws when the boot configuration is not well formed. */
function check(config: TmBootScriptConfig): void {
  const fail = (reason: string): never => {
    throw new Error(`The boot configuration is invalid: ${reason}`);
  };
  if (config.v !== 1) {
    fail('its version must be 1.');
  }
  if (!Array.isArray(config.languages) || config.languages.length === 0) {
    fail('it must offer at least one language.');
  }
  const codes = new Set<string>();
  for (const language of config.languages) {
    if (typeof language.code !== 'string' || !LANGUAGE_TAG.test(language.code)) {
      fail(`the language code '${String(language.code)}' is not a language tag.`);
    }
    if (language.dir !== 'ltr' && language.dir !== 'rtl') {
      fail(`the language '${language.code}' needs the direction 'ltr' or 'rtl'.`);
    }
    if (codes.has(language.code)) {
      fail(`the language '${language.code}' is offered twice.`);
    }
    codes.add(language.code);
  }
  if (!codes.has(config.defaultLanguage)) {
    fail(`the default language '${config.defaultLanguage}' is not offered.`);
  }
  if (typeof config.strings !== 'object' || config.strings === null) {
    fail('it needs the strings per language.');
  }
  // Each table belongs to an offered language (English always may, as the
  // fallback) and holds only boot strings.
  for (const [code, table] of Object.entries(config.strings)) {
    if (code !== 'en' && !codes.has(code)) {
      fail(`the strings name the language '${code}', which is not offered.`);
    }
    for (const [key, value] of Object.entries(table)) {
      if (!TM_BOOT_STRING_KEYS.includes(key) || typeof value !== 'string') {
        fail(`the string '${key}' of '${code}' is not a boot string.`);
      }
    }
  }
  const english = config.strings['en'];
  for (const key of TM_BOOT_STRING_KEYS) {
    if (typeof english?.[key] !== 'string' || english[key] === '') {
      fail(`the English string '${key}', the fallback of every language, is missing.`);
    }
  }
}

/**
 * Renders `tm-boot.js` from the template and the distribution's boot
 * configuration.
 *
 * @param template - The text of `@tellma/core/boot/tm-boot.template.js`.
 * @param config - The distribution's boot configuration.
 * @returns The script's text.
 * @throws Error when the template does not hold the marker exactly once, or the configuration is invalid.
 *
 * @example
 * ```ts
 * const script = tmRenderBootScript(template, {
 *   v: 1,
 *   languages: [{ code: 'en', dir: 'ltr' }],
 *   defaultLanguage: 'en',
 *   strings: { en: englishBootStrings },
 * });
 * ```
 */
export function tmRenderBootScript(template: string, config: TmBootScriptConfig): string {
  const at = template.indexOf(TM_BOOT_MARKER);
  if (at < 0 || template.indexOf(TM_BOOT_MARKER, at + 1) >= 0) {
    throw new Error('The boot script template must hold its configuration marker exactly once.');
  }
  check(config);
  // Only the members the script reads are written. U+2028 and U+2029 are
  // escaped, since engines before ES2019 end a string literal at them.
  const literal = JSON.stringify({
    v: config.v,
    languages: config.languages.map(({ code, dir }) => ({ code, dir })),
    defaultLanguage: config.defaultLanguage,
    strings: config.strings,
  })
    .replace(/\u2028/g, '\\u2028')
    .replace(/\u2029/g, '\\u2029');
  return template.slice(0, at) + literal + template.slice(at + TM_BOOT_MARKER.length);
}
