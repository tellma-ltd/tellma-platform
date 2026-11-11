// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import { tmDecodeProfile, tmParseProfileCookie } from './tm-profile';
import { tmBase64Url as base64Url, tmProfileCookieValue as cookieOf } from './tm-profile.testing.util';

const FARAH = {
  v: 2,
  sub: '00000000-0000-0000-0000-000000000001',
  sh: 'Y4kQ1x-_',
  name: 'Farah Nasser',
  email: 'farah@acme.example',
  locale: 'ar',
};

describe('tmDecodeProfile', () => {
  it('decodes a version 2 profile', () => {
    expect(tmDecodeProfile(cookieOf(FARAH))).toEqual(FARAH);
  });

  it('decodes UTF-8 names', () => {
    const profile = tmDecodeProfile(cookieOf({ ...FARAH, name: 'فرح ناصر', email: 'ፋራ@acme.example' }));
    expect(profile?.name).toBe('فرح ناصر');
    expect(profile?.email).toBe('ፋራ@acme.example');
  });

  it('accepts padding that completes the last group, and refuses padding that does not', () => {
    // 26 JSON bytes encode to 35 characters: one short of a group.
    const bare = cookieOf({ v: 2, sub: 'a', sh: 'b' });
    const padding = '='.repeat((4 - (bare.length % 4)) % 4);
    expect(padding).not.toBe('');
    expect(tmDecodeProfile(bare + padding)?.sub).toBe('a');
    expect(tmDecodeProfile(`${bare}${padding}=`)).toBeNull();
    expect(tmDecodeProfile(`${bare}=x`)).toBeNull();
  });

  it('refuses text that is not base64url', () => {
    const plain = btoa(JSON.stringify({ ...FARAH, name: '???>>>' }));
    // Standard base64's `+` and `/` are not base64url.
    expect(/[+/]/.test(plain)).toBe(true);
    expect(tmDecodeProfile(plain)).toBeNull();
    expect(tmDecodeProfile('eyJ2Ijoy.fQ')).toBeNull();
    expect(tmDecodeProfile('a')).toBeNull();
    expect(tmDecodeProfile('')).toBeNull();
  });

  it('refuses bytes that are not UTF-8, and text that is not a JSON object', () => {
    expect(tmDecodeProfile(btoa(`${String.fromCharCode(0xff, 0xfe)}{}`).replace(/=+$/, ''))).toBeNull();
    expect(tmDecodeProfile(base64Url('{"v":2'))).toBeNull();
    expect(tmDecodeProfile(cookieOf(null))).toBeNull();
    expect(tmDecodeProfile(cookieOf([2, 'sub']))).toBeNull();
    expect(tmDecodeProfile(cookieOf('text'))).toBeNull();
  });

  it('refuses another version, and an empty or missing subject or session handle', () => {
    expect(tmDecodeProfile(cookieOf({ ...FARAH, v: 1 }))).toBeNull();
    expect(tmDecodeProfile(cookieOf({ ...FARAH, v: '2' }))).toBeNull();
    expect(tmDecodeProfile(cookieOf({ ...FARAH, sub: '' }))).toBeNull();
    expect(tmDecodeProfile(cookieOf({ ...FARAH, sub: 42 }))).toBeNull();
    expect(tmDecodeProfile(cookieOf({ ...FARAH, sh: '' }))).toBeNull();
    const { sh, ...withoutHandle } = FARAH;
    void sh;
    expect(tmDecodeProfile(cookieOf(withoutHandle))).toBeNull();
  });

  it('reads a missing name or email as empty and a missing or empty locale as null', () => {
    expect(tmDecodeProfile(cookieOf({ v: 2, sub: 'a', sh: 'b' }))).toEqual({
      v: 2,
      sub: 'a',
      sh: 'b',
      name: '',
      email: '',
      locale: null,
    });
    expect(tmDecodeProfile(cookieOf({ ...FARAH, name: 7, locale: '' }))).toMatchObject({ name: '', locale: null });
  });

  it('keeps only the known members', () => {
    const profile = tmDecodeProfile(cookieOf({ ...FARAH, token: 'secret', admin: true }));
    expect(Object.keys(profile ?? {}).sort()).toEqual(['email', 'locale', 'name', 'sh', 'sub', 'v']);
  });
});

describe('tmParseProfileCookie', () => {
  const value = cookieOf(FARAH);

  it('finds the profile cookie among others', () => {
    expect(tmParseProfileCookie(`theme=dark; tellma.profile=${value}; other=1`)?.sub).toBe(FARAH.sub);
    expect(tmParseProfileCookie(`tellma.profile=${value}`)?.sub).toBe(FARAH.sub);
  });

  it('is absent when the cookie is missing, empty or invalid', () => {
    expect(tmParseProfileCookie('')).toBeNull();
    expect(tmParseProfileCookie('theme=dark')).toBeNull();
    expect(tmParseProfileCookie('tellma.profile=')).toBeNull();
    expect(tmParseProfileCookie('tellma.profile=%%%')).toBeNull();
  });

  it('matches the name exactly', () => {
    expect(tmParseProfileCookie(`xtellma.profile=${value}`)).toBeNull();
    expect(tmParseProfileCookie(`tellma.profile2=${value}`)).toBeNull();
  });

  it('reads the first cookie of that name', () => {
    const other = cookieOf({ ...FARAH, sub: 'second' });
    expect(tmParseProfileCookie(`tellma.profile=${value}; tellma.profile=${other}`)?.sub).toBe(FARAH.sub);
  });
});
