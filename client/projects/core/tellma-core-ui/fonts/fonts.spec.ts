import { TestBed } from '@angular/core/testing';

import { provideTellmaUi } from '../providers/provide-tellma-ui';
import { TM_FONTS_LATIN } from './font-manifest.generated';
import { fontPreloadLinks, TM_FONT_SUBSETS, type TmFontSubset } from './font-subsets';

const ARABIC_SUBSET: TmFontSubset = {
  family: 'Noto Sans Arabic',
  script: 'arabic',
  style: 'normal',
  weight: '100 900',
  url: 'fonts/noto-sans-arabic-wght-normal.woff2',
  unicodeRange: 'U+0600-06FF',
};

describe('fontPreloadLinks (§7.1)', () => {
  it('always preloads Latin, even with no locales', () => {
    const links = fontPreloadLinks([TM_FONTS_LATIN], []);
    expect(links.length).toBe(TM_FONTS_LATIN.length);
    expect(links.every((l) => l.as === 'font' && l.type === 'font/woff2')).toBe(true);
    expect(links.every((l) => l.crossorigin === 'anonymous')).toBe(true);
  });

  it('adds a script only when a configured locale needs it', () => {
    const manifest = [TM_FONTS_LATIN, [ARABIC_SUBSET]];
    const latinOnly = fontPreloadLinks(manifest, ['en', 'fr']);
    expect(latinOnly.some((l) => l.href.includes('arabic'))).toBe(false);

    const withArabic = fontPreloadLinks(manifest, ['en', 'ar-SA']);
    expect(withArabic.some((l) => l.href.includes('arabic'))).toBe(true);
  });

  it('never preloads a script no locale needs (unconfigured = no eager download)', () => {
    const manifest = [TM_FONTS_LATIN, [ARABIC_SUBSET]];
    const links = fontPreloadLinks(manifest, ['am']); // Amharic: needs ethiopic, not arabic
    expect(links.some((l) => l.href.includes('arabic'))).toBe(false);
  });

  it('dedupes by URL across contributing packages', () => {
    const links = fontPreloadLinks([TM_FONTS_LATIN, TM_FONTS_LATIN], ['en']);
    expect(new Set(links.map((l) => l.href)).size).toBe(links.length);
  });
});

describe('TM_FONT_SUBSETS multi-token merge (§7.1)', () => {
  it('injects the union of the core seed plus pack contributions', () => {
    TestBed.configureTestingModule({
      providers: [
        provideTellmaUi(),
        // A locale pack's contribution — same mechanism provideTellmaLocaleAr uses.
        { provide: TM_FONT_SUBSETS, useValue: [ARABIC_SUBSET], multi: true },
      ],
    });
    const merged = TestBed.inject(TM_FONT_SUBSETS);
    const flat = merged.flat();
    expect(flat).toContain(ARABIC_SUBSET);
    for (const subset of TM_FONTS_LATIN) {
      expect(flat).toContain(subset);
    }
  });
});

describe('self-hosted discipline (§7.1, DoD 12)', () => {
  it('no manifest URL points at a CDN', () => {
    for (const subset of TM_FONTS_LATIN) {
      expect(subset.url).not.toMatch(/^https?:/);
    }
  });
});
