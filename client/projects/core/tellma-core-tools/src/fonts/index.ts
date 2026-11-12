/**
 * Font preloading for a Tellma distribution's built output: which font
 * files hold the faces of Latin and the offered scripts, and the step that
 * preloads them in the built `index.html`. `tellma-client build` runs it.
 *
 * ```js
 * import { tmFontFragments, tmInjectFontPreloads } from '@tellma/core-tools/fonts';
 *
 * tmInjectFontPreloads('dist/acme/browser', tmFontFragments(['en', 'ar']));
 * ```
 *
 * @packageDocumentation
 */

// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import { existsSync, readdirSync, readFileSync, writeFileSync } from 'node:fs';
import { join } from 'node:path';

// Font preloading (spec 0024 §2.6, step 3). The application builder copies
// each font file a stylesheet references into media/ under a hashed name;
// this module finds those names in the built stylesheets and preloads the
// faces a first paint needs: Latin, the universal fallback, and the script
// of each offered language. Other faces stay on demand through their
// unicode-range. Fontsource names each file after its subset
// (`noto-sans-arabic-arabic-wght-normal-<hash>.woff2`), so a subset's name
// fragment, such as `arabic`, finds it; the `-ext` subsets are never
// preloaded, since they hold characters few pages show.

/**
 * The font subset name of each writing system, by its ISO 15924 code: the
 * fragment of a font file's name that marks the faces for that script.
 */
export const TM_FONT_SCRIPT_FRAGMENTS: Readonly<Record<string, string>> = Object.freeze({
  Latn: 'latin',
  Arab: 'arabic',
  Ethi: 'ethiopic',
  Cyrl: 'cyrillic',
  Grek: 'greek',
  Hebr: 'hebrew',
  Deva: 'devanagari',
  Beng: 'bengali',
  Thai: 'thai',
  Armn: 'armenian',
  Geor: 'georgian',
  Taml: 'tamil',
  Hans: 'chinese-simplified',
  Hant: 'chinese-traditional',
  Jpan: 'japanese',
  Kore: 'korean',
});

/**
 * The font name fragments for a set of languages: Latin always, then the
 * script of each language, by its likely script when the code names none.
 *
 * @param languages - The offered language codes, such as `en` and `ar`.
 * @returns The fragments, without repeats, Latin first.
 *
 * @example
 * ```ts
 * tmFontFragments(['en', 'ar', 'am']); // ['latin', 'arabic', 'ethiopic']
 * ```
 */
export function tmFontFragments(languages: readonly string[]): string[] {
  const fragments = new Set<string>(['latin']);
  for (const code of languages) {
    let script: string | undefined;
    try {
      script = new Intl.Locale(code).maximize().script;
    } catch {
      script = undefined;
    }
    const fragment = script === undefined ? undefined : TM_FONT_SCRIPT_FRAGMENTS[script];
    if (fragment !== undefined) {
      fragments.add(fragment);
    }
  }
  return [...fragments];
}

/**
 * Whether a font file's name marks it as one of the fragments' faces: the
 * fragment between hyphens, and not its `-ext` subset.
 *
 * @param url - The font's URL or file name.
 * @param fragments - The fragments, such as `latin` and `arabic`.
 * @returns True when the file holds a preloaded face.
 */
export function tmIsFontForFragments(url: string, fragments: readonly string[]): boolean {
  const name = (url.split('/').pop() ?? url).toLowerCase();
  return fragments.some((fragment) => {
    const at = name.indexOf(`-${fragment.toLowerCase()}-`);
    return at >= 0 && !name.startsWith('ext-', at + fragment.length + 2);
  });
}

/**
 * The woff2 files the built stylesheets reference, as URLs relative to the
 * output root, such as `media/noto-sans-latin-wght-normal-ABCD1234.woff2`.
 *
 * @param browserDir - The deployable output.
 * @returns The URLs, in ordinal order.
 */
export function tmBuiltFontUrls(browserDir: string): string[] {
  const urls = new Set<string>();
  for (const file of readdirSync(browserDir).filter((name) => name.endsWith('.css'))) {
    const css = readFileSync(join(browserDir, file), 'utf8');
    for (const match of css.matchAll(/url\(["']?(?:\.\/)?(media\/[^)"']+\.woff2)["']?\)/g)) {
      urls.add(match[1]!);
    }
  }
  return [...urls].sort();
}

/**
 * Adds `<link rel="preload" as="font" crossorigin>` for each matching font
 * to the built `index.html`, before `</head>`. The `crossorigin` attribute is
 * required: a font fetch is a CORS request even from the same origin, and a
 * preload in another mode would download the file twice. A URL already
 * preloaded is skipped, so the step can run twice.
 *
 * @param browserDir - The deployable output, holding `index.html`.
 * @param fragments - The font name fragments to preload, such as `latin` and `arabic`.
 * @returns The URLs preloaded.
 * @throws Error when no built stylesheet references a matching font, or `index.html` is missing.
 */
export function tmInjectFontPreloads(browserDir: string, fragments: readonly string[]): string[] {
  const urls = tmBuiltFontUrls(browserDir).filter((url) => tmIsFontForFragments(url, fragments));
  if (urls.length === 0) {
    throw new Error(`No built stylesheet in ${browserDir} references a font for: ${fragments.join(', ')}.`);
  }
  const indexPath = join(browserDir, 'index.html');
  if (!existsSync(indexPath)) {
    throw new Error(`${indexPath} does not exist.`);
  }
  const html = readFileSync(indexPath, 'utf8');
  const links = urls
    .filter((url) => !html.includes(`href="${url}"`))
    .map((url) => `<link rel="preload" href="${url}" as="font" type="font/woff2" crossorigin>`)
    .join('');
  writeFileSync(indexPath, html.replace(/<\/head>/i, `${links}</head>`));
  return urls;
}
