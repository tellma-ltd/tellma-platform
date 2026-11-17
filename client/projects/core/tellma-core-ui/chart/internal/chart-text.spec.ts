// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import { ɵtmCanvasText, ɵtmRunDirection } from './chart-text';

/** LEFT-TO-RIGHT MARK. */
const LRM = '\u200E';
/** RIGHT-TO-LEFT MARK. */
const RLM = '\u200F';
/** Every bidi control but the two marks: embeddings, overrides, isolates and the Arabic letter mark. */
const OTHER_CONTROLS = /[\u061C\u202A-\u202E\u2066-\u2069]/;

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
    expect(ɵtmCanvasText(['المبيعات', { base: ': ' }], 'rtl')).toBe('المبيعات: ');
  });

  it('wraps a run against the base as base mark, run mark, text, run mark, base mark', () => {
    expect(ɵtmCanvasText(['Cash'], 'rtl')).toBe(`${RLM}${LRM}Cash${LRM}${RLM}`);
    expect(ɵtmCanvasText(['النقدية'], 'ltr')).toBe(`${LRM}${RLM}النقدية${RLM}${LRM}`);
    // A number is a left-to-right run.
    expect(ɵtmCanvasText(['-1,200'], 'rtl')).toBe(`${RLM}${LRM}-1,200${LRM}${RLM}`);
  });

  it('wraps each part of a display name on its own', () => {
    const text = ɵtmCanvasText(['1100', ' - ', 'النقدية'], 'ltr');
    expect(text).toBe(`1100 - ${LRM}${RLM}النقدية${RLM}${LRM}`);
    const rtl = ɵtmCanvasText(['1100', ' - ', 'النقدية'], 'rtl');
    expect(rtl).toBe(`${RLM}${LRM}1100${LRM}${RLM}${RLM}${LRM} - ${LRM}${RLM}النقدية`);
  });

  it('strips every incoming bidi control before marking', () => {
    const text = ɵtmCanvasText(['\u2067Cash\u2069', { base: '\u200F: ' }, '\u202B1,200\u202C'], 'rtl');
    expect(text).toBe(`${RLM}${LRM}Cash${LRM}${RLM}: ${RLM}${LRM}1,200${LRM}${RLM}`);
  });

  it('uses only LRM and RLM', () => {
    const samples = [
      ɵtmCanvasText(['Cash', { base: ' — ' }, 'النقدية', '٪٥٠-'], 'rtl'),
      ɵtmCanvasText(['\u2066x\u2069', '\u061Cy', '\u202Ez\u202C'], 'ltr'),
    ];
    for (const sample of samples) {
      expect(OTHER_CONTROLS.test(sample)).toBe(false);
    }
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
