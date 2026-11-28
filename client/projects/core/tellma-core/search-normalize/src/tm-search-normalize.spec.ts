// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import { tmSearchNormalize } from './tm-search-normalize';

describe('tmSearchNormalize', () => {
  it('folds the documented example', () => {
    expect(tmSearchNormalize('Café — الفاتورة ١٢')).toBe('cafe الفاتوره 12');
  });

  it('removes every bidi control a paste can carry and keeps the zero-width non-joiner and joiner', () => {
    const controls = '؜‎‏‪‫‬‭‮⁦⁧⁨⁩';
    expect(tmSearchNormalize(`${controls}INV${controls}-2026`)).toBe('inv 2026');
    // The non-joiner is spelling in Persian (می‌خواهم); the joiner shapes emoji and scripts.
    expect(tmSearchNormalize('می‌خواهم')).toBe('مي‌خواهم');
    expect(tmSearchNormalize('a‍b')).toBe('a‍b');
  });

  it('removes Latin diacritics, whether precomposed or combining, and lowercases', () => {
    expect(tmSearchNormalize('Ünïcödé ÇAFÉ Ñandú Øre')).toBe('unicode cafe nandu øre');
    expect(tmSearchNormalize('Café')).toBe('cafe');
    expect(tmSearchNormalize('İstanbul')).toBe('istanbul');
  });

  it('decomposes compatibility characters: full-width letters, ligatures and presentation forms', () => {
    expect(tmSearchNormalize('ＩＮＶ ﬁle')).toBe('inv file');
    // Lam-alef and isolated presentation forms fold to their base letters.
    expect(tmSearchNormalize('ﻻﺍ')).toBe('لاا');
  });

  it('folds the interchangeable Arabic letters', () => {
    expect(tmSearchNormalize('أإآٱ')).toBe('اااا');
    expect(tmSearchNormalize('ىیئ')).toBe('ييي');
    expect(tmSearchNormalize('ة')).toBe('ه');
    expect(tmSearchNormalize('ؤ')).toBe('و');
    expect(tmSearchNormalize('ک')).toBe('ك');
    expect(tmSearchNormalize('مستشفى الأمل')).toBe(tmSearchNormalize('مستشفي الامل'));
  });

  it('removes tashkeel, the superscript alef and the Quranic annotation signs', () => {
    expect(tmSearchNormalize('فَاتُورَةٌ')).toBe('فاتوره');
    expect(tmSearchNormalize('مُحَمَّدٍ')).toBe('محمد');
    expect(tmSearchNormalize('هٰذا')).toBe('هذا');
    expect(tmSearchNormalize('الرحيمۖ')).toBe('الرحيم');
  });

  it('removes tatweel', () => {
    expect(tmSearchNormalize('فـــاتـورة')).toBe('فاتوره');
  });

  it('folds Arabic-Indic and Extended Arabic-Indic digits to Latin digits', () => {
    expect(tmSearchNormalize('٠١٢٣٤٥٦٧٨٩')).toBe('0123456789');
    expect(tmSearchNormalize('۰۱۲۳۴۵۶۷۸۹')).toBe('0123456789');
    expect(tmSearchNormalize('INV-٢٠٢٦-٠٤٣١')).toBe('inv 2026 0431');
  });

  it('turns punctuation and symbols into single spaces, with none at either end', () => {
    expect(tmSearchNormalize('  Sales, invoices… (draft)!  ')).toBe('sales invoices draft');
    expect(tmSearchNormalize('العملاء، الموردون؟')).toBe('العملاء الموردون');
    expect(tmSearchNormalize('a+b=c $5 © 50%')).toBe('a b c 5 50');
    expect(tmSearchNormalize('tab\tand\nnew line')).toBe('tab and new line');
    expect(tmSearchNormalize('—…')).toBe('');
  });

  it('is idempotent, so a folded term folds to itself', () => {
    for (const text of ['Café — الفاتورة ١٢', 'أحمد', 'Ünïcödé', 'می‌خواهم', '  x  ']) {
      const once = tmSearchNormalize(text);
      expect(tmSearchNormalize(once)).toBe(once);
    }
  });
});
