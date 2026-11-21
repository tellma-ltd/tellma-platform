// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import {
  TM_BOOT_RECORD_KEY,
  tmIsTenantId,
  tmParseBootRecord,
  tmReadBootRecord,
  tmWriteBootRecord,
  type TmBootRecord,
} from './tm-boot-record';
import { TM_REFUSING_STORAGE, TmFakeStorage } from './tm-storage.testing.util';

const RECORD: TmBootRecord = { v: 1, theme: 'dark', lang: 'ar', dir: 'rtl', sub: '0b4f', lastTenantId: 17 };

describe('tmIsTenantId', () => {
  it('accepts a positive safe integer only', () => {
    expect(tmIsTenantId(1)).toBe(true);
    expect(tmIsTenantId(Number.MAX_SAFE_INTEGER)).toBe(true);
    for (const value of [0, -1, 1.5, Number.MAX_SAFE_INTEGER + 1, Number.NaN, Infinity, '17', null, undefined]) {
      expect(tmIsTenantId(value), String(value)).toBe(false);
    }
  });
});

describe('tmParseBootRecord', () => {
  it('parses a version 1 record', () => {
    expect(tmParseBootRecord(JSON.stringify(RECORD))).toEqual(RECORD);
    const signedOut = { ...RECORD, theme: 'system', sub: null, lastTenantId: null };
    expect(tmParseBootRecord(JSON.stringify(signedOut))).toEqual(signedOut);
  });

  it('treats absent text, text that is not JSON, and another version as no record', () => {
    expect(tmParseBootRecord(null)).toBeNull();
    expect(tmParseBootRecord('')).toBeNull();
    expect(tmParseBootRecord('{"v":1')).toBeNull();
    expect(tmParseBootRecord('null')).toBeNull();
    expect(tmParseBootRecord(JSON.stringify({ ...RECORD, v: 2 }))).toBeNull();
  });

  it('treats a member of the wrong shape as no record', () => {
    const malformed: readonly unknown[] = [
      { ...RECORD, theme: 'blue' },
      { ...RECORD, lang: '' },
      { ...RECORD, lang: 7 },
      { ...RECORD, dir: 'auto' },
      { ...RECORD, sub: '' },
      { ...RECORD, sub: 1 },
      { ...RECORD, lastTenantId: 0 },
      { ...RECORD, lastTenantId: '17' },
      { ...RECORD, lastTenantId: 1.5 },
      [RECORD],
    ];
    for (const value of malformed) {
      expect(tmParseBootRecord(JSON.stringify(value)), JSON.stringify(value)).toBeNull();
    }
  });
});

describe('the boot record in local storage', () => {
  it('round-trips', () => {
    const storage = new TmFakeStorage();
    tmWriteBootRecord(storage, RECORD);
    expect(storage.json(TM_BOOT_RECORD_KEY)).toEqual(RECORD);
    expect(tmReadBootRecord(storage)).toEqual(RECORD);
  });

  it('treats a missing or refusing storage as holding no record', () => {
    expect(tmReadBootRecord(null)).toBeNull();
    expect(tmReadBootRecord(TM_REFUSING_STORAGE)).toBeNull();
    expect(() => tmWriteBootRecord(null, RECORD)).not.toThrow();
    expect(() => tmWriteBootRecord(TM_REFUSING_STORAGE, RECORD)).not.toThrow();
  });
});
