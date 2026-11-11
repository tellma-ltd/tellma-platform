// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import { TM_BIDI_CONTROLS, tmStripBidiControls } from './tm-bidi';

/** The code points of the set: the marks, the embeddings and overrides, the isolates. */
const CONTROLS = [
  0x061c, // ARABIC LETTER MARK
  0x200e, // LEFT-TO-RIGHT MARK
  0x200f, // RIGHT-TO-LEFT MARK
  0x202a, // LEFT-TO-RIGHT EMBEDDING
  0x202b, // RIGHT-TO-LEFT EMBEDDING
  0x202c, // POP DIRECTIONAL FORMATTING
  0x202d, // LEFT-TO-RIGHT OVERRIDE
  0x202e, // RIGHT-TO-LEFT OVERRIDE
  0x2066, // LEFT-TO-RIGHT ISOLATE
  0x2067, // RIGHT-TO-LEFT ISOLATE
  0x2068, // FIRST STRONG ISOLATE
  0x2069, // POP DIRECTIONAL ISOLATE
];

describe('TM_BIDI_CONTROLS', () => {
  it('is a global pattern, so replace and matchAll take every occurrence', () => {
    expect(TM_BIDI_CONTROLS.global).toBe(true);
  });

  it('matches exactly the twelve bidirectional controls', () => {
    const matched: number[] = [];
    // Every code point of the Basic Multilingual Plane.
    for (let codePoint = 0; codePoint <= 0xffff; codePoint++) {
      if (String.fromCodePoint(codePoint).replace(TM_BIDI_CONTROLS, '') === '') {
        matched.push(codePoint);
      }
    }
    expect(matched).toEqual(CONTROLS);
  });
});

describe('tmStripBidiControls', () => {
  it('removes every control, wherever it sits', () => {
    const lrm = String.fromCodePoint(0x200e);
    const rlm = String.fromCodePoint(0x200f);
    expect(tmStripBidiControls(`${lrm}-12`)).toBe('-12');
    expect(tmStripBidiControls(`2${rlm}/11${rlm}/2026`)).toBe('2/11/2026');
    expect(tmStripBidiControls(CONTROLS.map((c) => String.fromCodePoint(c)).join('a'))).toBe(
      'a'.repeat(CONTROLS.length - 1),
    );
    expect(tmStripBidiControls(`${lrm}${rlm}`)).toBe('');
  });

  it('strips the same on every call (the pattern keeps no position)', () => {
    const marked = `${String.fromCodePoint(0x200f)}x`;
    expect(tmStripBidiControls(marked)).toBe('x');
    expect(tmStripBidiControls(marked)).toBe('x');
  });

  it('leaves every other character alone, invisible ones included', () => {
    const joiner = String.fromCodePoint(0x200d); // ZERO WIDTH JOINER: shapes letters
    const nonJoiner = String.fromCodePoint(0x200c); // ZERO WIDTH NON-JOINER: Persian spelling
    const noBreak = String.fromCodePoint(0x00a0);
    const text = `می${nonJoiner}خواهم ${joiner}${noBreak}1 234,5 Q4 الربع`;
    expect(tmStripBidiControls(text)).toBe(text);
    expect(tmStripBidiControls('')).toBe('');
  });
});
