// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import { tmAvatarTone, tmInitials } from './tm-initials';

describe('tmInitials', () => {
  it('takes the first and last words of a cased name, upper-cased', () => {
    expect(tmInitials('Farah Nasser', 'en-US')).toBe('FN');
    expect(tmInitials('farah ali nasser', 'en-US')).toBe('FN');
    expect(tmInitials('Иван Петров', 'ru-RU')).toBe('ИП');
    expect(tmInitials('Ωmega Ψ', 'el-GR')).toBe('ΩΨ');
    expect(tmInitials('Farah', 'en-US')).toBe('F');
  });

  it('upper-cases in the locale', () => {
    expect(tmInitials('ilker aydın', 'tr-TR')).toBe('İA');
    expect(tmInitials('ilker aydın', 'en-US')).toBe('IA');
  });

  it('keeps a letter and its combining marks together', () => {
    expect(tmInitials('E\u0301mile Zola', 'fr-FR')).toBe('E\u0301Z');
  });

  it('gives one grapheme for an uncased script', () => {
    expect(tmInitials('فرح ناصر', 'ar-SA')).toBe('ف');
    expect(tmInitials('ተስፋዬ ገብረ', 'am-ET')).toBe('ተ');
    expect(tmInitials('王小明', 'zh-CN')).toBe('王');
    expect(tmInitials('김 민준', 'ko-KR')).toBe('김');
  });

  it('never pairs letters from two scripts', () => {
    expect(tmInitials('Farah ناصر', 'en-US')).toBe('F');
    expect(tmInitials('فرح Nasser', 'ar-SA')).toBe('ف');
  });

  it('skips punctuation and emoji, and counts digits', () => {
    expect(tmInitials('(Farah) Nasser', 'en-US')).toBe('FN');
    expect(tmInitials('Farah 🙂', 'en-US')).toBe('F');
    expect(tmInitials('3M Group', 'en-US')).toBe('3G');
  });

  it('gives an empty string for a name with no letter or digit', () => {
    expect(tmInitials('', 'en-US')).toBe('');
    expect(tmInitials('   ', 'en-US')).toBe('');
    expect(tmInitials('— 🙂', 'en-US')).toBe('');
  });

  it('degrades a malformed locale to the default case rules', () => {
    expect(tmInitials('farah nasser', 'not a locale!')).toBe('FN');
  });
});

describe('tmAvatarTone', () => {
  it('matches the fixed FNV-1a vectors', () => {
    expect(tmAvatarTone('core.User:17')).toBe(4);
    expect(tmAvatarTone('core.User:18')).toBe(3);
    expect(tmAvatarTone('core.User:1')).toBe(7);
    expect(tmAvatarTone('acme.Customer:42')).toBe(2);
    expect(tmAvatarTone('a')).toBe(5);
    expect(tmAvatarTone('')).toBe(6);
  });

  it('always picks one of the eight slots', () => {
    for (let id = 0; id < 500; id++) {
      const tone = tmAvatarTone(`core.User:${id}`);
      expect(tone).toBeGreaterThanOrEqual(1);
      expect(tone).toBeLessThanOrEqual(8);
    }
  });
});
