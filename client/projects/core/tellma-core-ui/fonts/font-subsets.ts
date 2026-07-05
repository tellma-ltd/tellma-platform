import { InjectionToken } from '@angular/core';

/**
 * One self-hosted font subset (one @font-face) a package contributes
 * (§7.1). The core seeds Latin/Mono; each locale pack contributes its
 * script's subsets through the same multi token.
 */
export interface TmFontSubset {
  /** e.g. 'Noto Sans', 'Noto Sans Arabic' */
  readonly family: string;
  /** unicode-range subset name: 'latin' | 'latin-ext' | 'arabic' | 'ethiopic' | … */
  readonly script: string;
  readonly style: 'normal' | 'italic';
  /** '100 900' for a variable face, '400' for a static weight. */
  readonly weight: string;
  /** Package-relative woff2 URL — resolved by the consuming app's asset pipeline. */
  readonly url: string;
  readonly unicodeRange: string;
}

/**
 * The merged font-subset manifest (`multi: true`): the injected value is the
 * UNION of the core's Latin/Mono entries plus every installed locale pack's
 * entries — no build-time scan, no central registry (§7.1). Each provider
 * contributes an ARRAY of subsets.
 */
export const TM_FONT_SUBSETS = new InjectionToken<readonly (readonly TmFontSubset[])[]>(
  'TM_FONT_SUBSETS',
);

/** A `<link rel="preload">` descriptor the distribution shell injects (§7.1). */
export interface PreloadLink {
  readonly rel: 'preload';
  readonly href: string;
  readonly as: 'font';
  readonly type: 'font/woff2';
  readonly crossorigin: 'anonymous';
}

/** Scripts each language needs beyond the always-preloaded Latin. */
const LOCALE_SCRIPTS: Record<string, readonly string[]> = {
  ar: ['arabic'],
  fa: ['arabic'],
  ur: ['arabic'],
  am: ['ethiopic'],
  ti: ['ethiopic'],
  ru: ['cyrillic', 'cyrillic-ext'],
  uk: ['cyrillic', 'cyrillic-ext'],
  el: ['greek', 'greek-ext'],
  he: ['hebrew'],
  hi: ['devanagari'],
  ja: ['japanese'],
  ko: ['korean'],
  th: ['thai'],
  zh: ['chinese-simplified'],
};

/**
 * Pure helper (§7.1): given the merged `TM_FONT_SUBSETS` manifest and the
 * tenant's resolved locales, returns the `<link rel="preload">` descriptors
 * to inject. Latin is ALWAYS preloaded (the universal fallback); other
 * scripts only when a configured locale needs them — unconfigured scripts
 * are never preloaded and only ever fetch on demand via unicode-range.
 * The runtime injection itself is the distribution shell's job.
 */
export function fontPreloadLinks(
  subsets: readonly (readonly TmFontSubset[])[],
  locales: readonly string[],
): readonly PreloadLink[] {
  const scripts = new Set(['latin', 'latin-ext']);
  for (const locale of locales) {
    const language = locale.toLowerCase().split(/[-_]/)[0];
    for (const script of LOCALE_SCRIPTS[language] ?? []) {
      scripts.add(script);
    }
  }
  const seen = new Set<string>();
  const links: PreloadLink[] = [];
  for (const subset of subsets.flat()) {
    if (scripts.has(subset.script) && !seen.has(subset.url)) {
      seen.add(subset.url);
      links.push({
        rel: 'preload',
        href: subset.url,
        as: 'font',
        type: 'font/woff2',
        crossorigin: 'anonymous',
      });
    }
  }
  return links;
}
