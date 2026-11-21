// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import { ɵtmCanvasText, ɵtmRunDirection } from './chart-text';

/** LEFT-TO-RIGHT MARK. */
const LRM = '\u200E';
/** RIGHT-TO-LEFT MARK. */
const RLM = '\u200F';
/** ARABIC LETTER MARK. */
const ALM = '\u061C';
/** Every bidi control but the three marks: embeddings, overrides and isolates. */
const OTHER_CONTROLS = /[\u202A-\u202E\u2066-\u2069]/;

describe('ɵtmRunDirection', () => {
  it('takes the direction of the first letter', () => {
    expect(ɵtmRunDirection('Sales')).toBe('ltr');
    expect(ɵtmRunDirection('المبيعات')).toBe('rtl');
    expect(ɵtmRunDirection('שלום')).toBe('rtl');
    expect(ɵtmRunDirection('1100 النقدية')).toBe('rtl');
    expect(ɵtmRunDirection('(Cash) النقدية')).toBe('ltr');
  });

  it('reads a run without letters, a number, as left-to-right', () => {
    expect(ɵtmRunDirection('-1,234.5')).toBe('ltr');
    expect(ɵtmRunDirection('٫٥-١٢٣')).toBe('ltr');
    expect(ɵtmRunDirection('')).toBe('ltr');
  });
});

describe('ɵtmCanvasText', () => {
  it('writes runs in the base direction and base pieces as they are', () => {
    expect(ɵtmCanvasText(['Sales', { base: ': ' }, '1,200'], 'ltr')).toBe('Sales: 1,200');
    expect(ɵtmCanvasText(['المبيعات', { base: ': ' }], 'rtl')).toBe(`المبيعات${ALM}: `);
  });

  it('wraps a run against the base as base mark, run mark, text, run mark, base mark', () => {
    expect(ɵtmCanvasText(['Cash'], 'rtl')).toBe(`${RLM}${LRM}Cash${LRM}${RLM}`);
    expect(ɵtmCanvasText(['النقدية'], 'ltr')).toBe(`${LRM}${RLM}النقدية${ALM}${RLM}${LRM}`);
    // A number is a left-to-right run.
    expect(ɵtmCanvasText(['-1,200'], 'rtl')).toBe(`${RLM}${LRM}-1,200${LRM}${RLM}`);
  });

  it('wraps each part of a display name on its own', () => {
    const text = ɵtmCanvasText(['1100', ' - ', 'النقدية'], 'ltr');
    expect(text).toBe(`1100 - ${LRM}${RLM}النقدية${ALM}${RLM}${LRM}`);
    const rtl = ɵtmCanvasText(['1100', ' - ', 'النقدية'], 'rtl');
    expect(rtl).toBe(`${RLM}${LRM}1100${LRM}${RLM}${RLM}${LRM} - ${LRM}${RLM}النقدية${ALM}`);
  });

  it('strips every incoming bidi control before marking', () => {
    const text = ɵtmCanvasText(
      ['\u2067Cash\u2069', { base: '\u200F: ' }, '\u202B1,200\u202C'],
      'rtl',
    );
    expect(text).toBe(`${RLM}${LRM}Cash${LRM}${RLM}: ${RLM}${LRM}1,200${LRM}${RLM}`);
  });

  it('uses only LRM, RLM and ALM', () => {
    const samples = [
      ɵtmCanvasText(['Cash', { base: ' — ' }, 'النقدية', '٪٥٠-'], 'rtl'),
      ɵtmCanvasText(['\u2066x\u2069', '\u061Cy', '\u202Ez\u202C'], 'ltr'),
    ];
    for (const sample of samples) {
      expect(OTHER_CONTROLS.test(sample)).toBe(false);
    }
    // An incoming Arabic letter mark is stripped like any other control.
    expect(samples[1]).not.toContain(ALM);
  });

  it('closes every Arabic word with an ALM, after its marks and joiners', () => {
    // WebKit draws a right-to-left run whose leftmost glyph is offset
    // vertically off the baseline, and moves what it draws after it alike;
    // the closing mark becomes that glyph.
    expect(ɵtmCanvasText(['شركات أفراد'], 'rtl')).toBe(`شركات${ALM} أفراد${ALM}`);
    // Harakat, a tatweel and a joiner are part of the word; a non-joiner
    // ends it.
    expect(ɵtmCanvasText(['مُحَمَّدٌ'], 'rtl')).toBe(`مُحَمَّدٌ${ALM}`);
    expect(ɵtmCanvasText(['عـ'], 'rtl')).toBe(`عـ${ALM}`);
    expect(ɵtmCanvasText(['ع\u200D'], 'rtl')).toBe(`ع\u200D${ALM}`);
    expect(ɵtmCanvasText(['می\u200Cخواهم'], 'rtl')).toBe(`می${ALM}\u200Cخواهم${ALM}`);
    // In a base piece, and in a run wrapped against the base.
    expect(ɵtmCanvasText(['B2C', { base: ' من ' }, 'أفراد'], 'ltr')).toBe(
      `B2C من${ALM} ${LRM}${RLM}أفراد${ALM}${RLM}${LRM}`,
    );
  });

  it('closes an Arabic word with a mark of its own class, so a number after it reads as before', () => {
    // An ALM is of the class AL, like the letter before it: the European
    // digits after it still read as Arabic numbers, and "35%" keeps its
    // order. An RLM there would make them European numbers and move the sign.
    expect(ɵtmCanvasText(['ألف 35%'], 'rtl')).toBe(`ألف${ALM} 35%`);
    // A Hebrew word, of the class R, is left as it is.
    expect(ɵtmCanvasText(['שלום'], 'ltr')).toBe(`${LRM}${RLM}שלום${RLM}${LRM}`);
    // Arabic-Indic digits are not letters, and close nothing.
    expect(ɵtmCanvasText(['١٠٠ ألف'], 'ltr')).toBe(`${LRM}${RLM}١٠٠ ألف${ALM}${RLM}${LRM}`);
  });

  it('keeps a closing mark last in its word inside a number’s run', () => {
    // A left-to-right run with Arabic-Indic digits takes an LRM on each side
    // of its neutrals; the closing mark comes after them and takes none.
    expect(ɵtmCanvasText(['B2C ٣٥ ألف'], 'rtl')).toBe(
      `${RLM}${LRM}B2C${LRM} ${LRM}٣٥${LRM} ${LRM}ألف${ALM}${LRM}${RLM}`,
    );
  });

  it('keeps the signs of an Arabic-Indic number on the run’s sides', () => {
    // Arabic-Indic digits are of the bidi class AN, so a minus or a percent
    // sign beside them inside an RTL paragraph would leave the run; an LRM
    // on each side keeps it where the number drawn alone puts it.
    expect(ɵtmCanvasText(['-١٢٣٫٥'], 'rtl')).toBe(`${RLM}${LRM}${LRM}-${LRM}١٢٣٫٥${LRM}${RLM}`);
    expect(ɵtmCanvasText(['٪٥٠'], 'rtl')).toBe(`${RLM}${LRM}${LRM}٪${LRM}٥٠${LRM}${RLM}`);
    // European digits need no help.
    expect(ɵtmCanvasText(['-50%'], 'rtl')).toBe(`${RLM}${LRM}-50%${LRM}${RLM}`);
  });

  it('keeps a rich-text token from reading as markup', () => {
    // The tooltip draws with rich styles, where `{name|text}` would style text.
    const text = ɵtmCanvasText(['{bold|Cash}', { base: '{a|' }], 'ltr');
    expect(text).toBe(`{${LRM}bold|Cash}{${LRM}a|`);
    expect(/\{[A-Za-z0-9_]+\|/.test(text)).toBe(false);
    expect(ɵtmCanvasText(['{x|y}'], 'rtl')).toBe(`${RLM}${LRM}{${LRM}x|y}${LRM}${RLM}`);
  });
});
