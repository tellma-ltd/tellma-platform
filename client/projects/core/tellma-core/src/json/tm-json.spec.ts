// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import { TmDecimals } from './tm-decimals';
import type { TmDecimal, TmJsonObject } from './tm-json';

// The wire table of plain JSON: bodies parse with JSON.parse and serialize
// with JSON.stringify, with no reviver or replacer, and lose nothing.
describe('plain JSON bodies', () => {
  const wire =
    '{"id":5,"ratio":0.25,"flag":true,"balance":"1234.5000","big":"9007199254740993",' +
    '"modifiedAt":"2026-10-20T10:42:00.0000000+00:00","day":"2026-10-20","guid":"6f9619ff-8b86-d011-b42d-00cf4fc964ff"}';

  it('keeps a long beyond 2^53 exact, as its string', () => {
    const body = JSON.parse(wire) as TmJsonObject;
    expect(body['big']).toBe('9007199254740993');
    expect(TmDecimals.isDecimal(body['big'])).toBe(true);
    const big = body['big'] as TmDecimal;
    expect(TmDecimals.add(big, '1' as TmDecimal)).toBe('9007199254740994');
    expect(TmDecimals.compare(big, '9007199254740992' as TmDecimal)).toBe(1);
  });

  it('keeps a decimal at its full scale', () => {
    const body = JSON.parse(wire) as TmJsonObject;
    expect(body['balance']).toBe('1234.5000');
  });

  it('leaves ints, doubles and booleans as JSON numbers and booleans', () => {
    const body = JSON.parse(wire) as TmJsonObject;
    expect(body['id']).toBe(5);
    expect(body['ratio']).toBe(0.25);
    expect(body['flag']).toBe(true);
  });

  it('leaves dates, instants and GUIDs as their raw strings', () => {
    const body = JSON.parse(wire) as TmJsonObject;
    expect(body['modifiedAt']).toBe('2026-10-20T10:42:00.0000000+00:00');
    expect(body['day']).toBe('2026-10-20');
    expect(body['guid']).toBe('6f9619ff-8b86-d011-b42d-00cf4fc964ff');
  });

  it('serializes back to the same text', () => {
    expect(JSON.stringify(JSON.parse(wire))).toBe(wire);
  });

  it('serializes a TmDecimal as its string', () => {
    const amount = TmDecimals.parse('1e3');
    expect(JSON.stringify({ amount })).toBe('{"amount":"1000"}');
  });

  it('carries decimals through structured clone unchanged', () => {
    const body = JSON.parse(wire) as TmJsonObject;
    expect(structuredClone(body)).toEqual(body);
  });
});
