// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import { TM_REFUSING_STORAGE, TmFakeStorage } from '../state/tm-storage.testing.util';
import {
  TM_LOGIN_MARK_KEY,
  tmIsLocalPath,
  tmLoginUrl,
  tmReadLoginMark,
  tmRemoveLoginMark,
  tmWriteLoginMark,
  type TmLoginMark,
} from './tm-login-mark';

const MARK: TmLoginMark = {
  at: 1_700_000_000_000,
  expired: true,
  returnUrl: '/17/centers?x=1',
  auto: false,
  sub: '0b4f',
  hash: '#d=abc',
};

/** A storage holding one stored mark text. */
function holding(value: unknown): TmFakeStorage {
  return new TmFakeStorage({ [TM_LOGIN_MARK_KEY]: JSON.stringify(value) });
}

describe('tmIsLocalPath', () => {
  it('accepts a path on this origin', () => {
    for (const url of ['/', '/17', '/17/centers?x=1', '/a/b//c', '/signed-out', '/x?next=//evil']) {
      expect(tmIsLocalPath(url), url).toBe(true);
    }
  });

  it('refuses what a browser reads as another host, or not as a path', () => {
    const tab = String.fromCharCode(9);
    const newline = String.fromCharCode(10);
    for (const url of [
      '',
      '//evil.example',
      '/\\evil.example',
      `/${tab}/evil.example`,
      `/${newline}/evil.example`,
      `/17${newline}`,
      'https://evil.example',
      'javascript:alert(1)',
      '17',
      '\\\\evil.example',
    ]) {
      expect(tmIsLocalPath(url), JSON.stringify(url)).toBe(false);
    }
  });
});

describe('tmLoginUrl', () => {
  it('encodes the return URL and the language', () => {
    expect(tmLoginUrl('/17/centers?x=1&y=2', 'ar')).toBe(
      '/bff/login?returnUrl=%2F17%2Fcenters%3Fx%3D1%26y%3D2&ui_locales=ar',
    );
    expect(tmLoginUrl('/', 'zh-Hant')).toBe('/bff/login?returnUrl=%2F&ui_locales=zh-Hant');
  });
});

describe('the sign-in mark in session storage', () => {
  it('round-trips', () => {
    const storage = new TmFakeStorage();
    expect(tmWriteLoginMark(storage, MARK)).toBe(true);
    expect(tmReadLoginMark(storage)).toEqual(MARK);
    tmRemoveLoginMark(storage);
    expect(storage.getItem(TM_LOGIN_MARK_KEY)).toBeNull();
    expect(tmReadLoginMark(storage)).toBeNull();
  });

  it('reads a mark with no user and no fragment', () => {
    const mark = { ...MARK, sub: null, hash: '', returnUrl: '/', auto: true, expired: false };
    expect(tmReadLoginMark(holding(mark))).toEqual(mark);
  });

  it('refuses a stored value of any other shape', () => {
    const malformed: readonly unknown[] = [
      null,
      'text',
      [MARK],
      { ...MARK, at: '1700000000000' },
      { ...MARK, at: Number.NaN },
      { ...MARK, expired: 'true' },
      { ...MARK, returnUrl: '//evil.example' },
      { ...MARK, returnUrl: 'https://evil.example/' },
      { ...MARK, returnUrl: ['/17'] },
      { ...MARK, auto: 1 },
      { ...MARK, sub: '' },
      { ...MARK, sub: 7 },
      { ...MARK, hash: 'd=abc' },
      { ...MARK, hash: null },
      { at: MARK.at },
    ];
    for (const value of malformed) {
      expect(tmReadLoginMark(holding(value)), JSON.stringify(value)).toBeNull();
    }
    expect(tmReadLoginMark(new TmFakeStorage({ [TM_LOGIN_MARK_KEY]: '{"at":' }))).toBeNull();
  });

  it('treats a missing or refusing storage as holding no mark', () => {
    expect(tmReadLoginMark(null)).toBeNull();
    expect(tmReadLoginMark(TM_REFUSING_STORAGE)).toBeNull();
    expect(tmWriteLoginMark(null, MARK)).toBe(false);
    expect(tmWriteLoginMark(TM_REFUSING_STORAGE, MARK)).toBe(false);
    expect(() => tmRemoveLoginMark(null)).not.toThrow();
    expect(() => tmRemoveLoginMark(TM_REFUSING_STORAGE)).not.toThrow();
  });
});
