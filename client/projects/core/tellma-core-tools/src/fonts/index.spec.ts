// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import { mkdirSync, mkdtempSync, readFileSync, rmSync, writeFileSync } from 'node:fs';
import { tmpdir } from 'node:os';
import { join } from 'node:path';

import { afterEach, describe, expect, it } from 'vitest';

import { tmBuiltFontUrls, tmFontFragments, tmInjectFontPreloads, tmIsFontForFragments } from './index.js';

describe('font preloads', () => {
  let dir: string | undefined;
  afterEach(() => {
    if (dir !== undefined) {
      rmSync(dir, { recursive: true, force: true });
    }
  });

  it("name Latin and each offered language's script", () => {
    expect(tmFontFragments(['en'])).toEqual(['latin']);
    expect(tmFontFragments(['en', 'ar', 'am', 'ti', 'fr', 'ru'])).toEqual(['latin', 'arabic', 'ethiopic', 'cyrillic']);
    expect(tmFontFragments(['zh-Hant', 'not a tag'])).toEqual(['latin', 'chinese-traditional']);
  });

  it("match a font file by its subset's name, never its -ext subset", () => {
    expect(tmIsFontForFragments('media/noto-sans-latin-wght-normal-EBRFDBOD.woff2', ['latin'])).toBe(true);
    expect(tmIsFontForFragments('media/noto-sans-mono-latin-400-normal-VWJXAL7K.woff2', ['latin'])).toBe(true);
    expect(tmIsFontForFragments('media/noto-sans-latin-ext-wght-normal-CW6O4BDT.woff2', ['latin'])).toBe(false);
    expect(tmIsFontForFragments('media/noto-sans-arabic-arabic-wght-normal-EZSVSFPT.woff2', ['latin'])).toBe(false);
    expect(tmIsFontForFragments('media/noto-sans-arabic-arabic-wght-normal-EZSVSFPT.woff2', ['latin', 'arabic'])).toBe(true);
  });

  it("inject a preload per matching font the built stylesheets reference, once", () => {
    dir = mkdtempSync(join(tmpdir(), 'tm-fonts-'));
    mkdirSync(join(dir, 'media'));
    writeFileSync(
      join(dir, 'styles-A.css'),
      '@font-face{src:url(./media/noto-sans-latin-wght-normal-AAAAAAAA.woff2)}@font-face{src:url("media/noto-sans-latin-ext-wght-normal-BBBBBBBB.woff2")}' +
        "@font-face{src:url('media/noto-sans-arabic-arabic-wght-normal-CCCCCCCC.woff2')}",
    );
    writeFileSync(join(dir, 'index.html'), '<html><head><title>A</title></head><body></body></html>');
    expect(tmBuiltFontUrls(dir)).toEqual([
      'media/noto-sans-arabic-arabic-wght-normal-CCCCCCCC.woff2',
      'media/noto-sans-latin-ext-wght-normal-BBBBBBBB.woff2',
      'media/noto-sans-latin-wght-normal-AAAAAAAA.woff2',
    ]);
    expect(tmInjectFontPreloads(dir, ['latin'])).toEqual(['media/noto-sans-latin-wght-normal-AAAAAAAA.woff2']);
    tmInjectFontPreloads(dir, ['latin']);
    expect(readFileSync(join(dir, 'index.html'), 'utf8')).toBe(
      '<html><head><title>A</title><link rel="preload" href="media/noto-sans-latin-wght-normal-AAAAAAAA.woff2" as="font" type="font/woff2" crossorigin></head><body></body></html>',
    );
    expect(() => tmInjectFontPreloads(dir!, ['ethiopic'])).toThrow(/references a font for: ethiopic/);
  });
});
