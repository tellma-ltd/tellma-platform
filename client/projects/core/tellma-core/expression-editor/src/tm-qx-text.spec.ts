// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

// The engine's corpus is checked in once, beside the lexer it was written for; a fixture, not a
// dependency on that entry point's code.
// eslint-disable-next-line tm/core-import-boundary -- the one corpus both suites are held to
import corpus from '../../entity/src/queryex-lexer-corpus.json';
import { tmQxInsert, tmQxParameters, ɵtmQxFold, ɵtmQxPlanInsert } from './tm-qx-text';

// The parameters and the insertion the editor reads and writes: the
// parameters against the engine's corpus (the authority, as for the
// lexer), and the insertion's boundaries rule by rule.

describe('tmQxParameters', () => {
  it.each(corpus.cases.map((entry) => [JSON.stringify(entry.text), entry] as const))('groups the parameters of %s as the engine does', (_, entry) => {
    expect(tmQxParameters(entry.text)).toEqual(entry.parameters);
  });

  it('groups parameters without regard to case, under the first spelling', () => {
    expect(tmQxParameters('@From = @from or @to > @FROM')).toEqual([
      { name: 'From', sites: [{ start: 0, length: 5 }, { start: 8, length: 5 }, { start: 23, length: 5 }] },
      { name: 'to', sites: [{ start: 17, length: 3 }] },
    ]);
  });

  it('never reads a parameter inside a string', () => {
    expect(tmQxParameters("'a@b'")).toEqual([]);
  });
});

describe('ɵtmQxFold', () => {
  it('folds case per code unit, never a non-ASCII letter to an ASCII one', () => {
    expect(ɵtmQxFold('From')).toBe(ɵtmQxFold('fROM'));
    expect(ɵtmQxFold(String.fromCharCode(0x131))).not.toBe(ɵtmQxFold('i'));
  });
});

describe('tmQxInsert', () => {
  it('inserts at a token boundary as it stands, with the range it occupies', () => {
    expect(tmQxInsert('a = ', 4, 'b')).toEqual({ text: 'a = b', start: 4, end: 5 });
    expect(tmQxInsert('f()', 2, 'x')).toEqual({ text: 'f(x)', start: 2, end: 3 });
  });

  it('moves an insertion inside a token to the token end', () => {
    expect(tmQxInsert('Amount', 3, ' + 1')).toEqual({ text: 'Amount + 1', start: 6, end: 10 });
    expect(tmQxInsert("Name = 'abc' or", 9, ' x')).toEqual({ text: "Name = 'abc' x or", start: 12, end: 14 });
    expect(tmQxInsert('a = 123', 5, '')).toEqual({ text: 'a = 123', start: 7, end: 7 });
  });

  it('separates the insertion from a name, a digit or a closing parenthesis it would touch', () => {
    expect(tmQxInsert('a and', 5, 'b')).toEqual({ text: 'a and b', start: 6, end: 7 });
    expect(tmQxInsert('f(x)', 4, 'Code')).toEqual({ text: 'f(x) Code', start: 5, end: 9 });
    expect(tmQxInsert('12', 2, 'x')).toEqual({ text: '12 x', start: 3, end: 4 });
    expect(tmQxInsert('a', 0, 'Parent.Code')).toEqual({ text: 'Parent.Code a', start: 0, end: 11 });
    expect(tmQxInsert('x = ', 4, '@From')).toEqual({ text: 'x = @From', start: 4, end: 9 });
  });

  it('adds no space beside an operator, a separator or white space', () => {
    expect(tmQxInsert('a', 1, '+')).toEqual({ text: 'a+', start: 1, end: 2 });
    expect(tmQxInsert('f(a,)', 4, 'b')).toEqual({ text: 'f(a,b)', start: 4, end: 5 });
    expect(tmQxInsert('a ', 2, 'b')).toEqual({ text: 'a b', start: 2, end: 3 });
  });

  it('never lets the insertion run into the token beside it', () => {
    expect(tmQxInsert("'a'", 3, "'b'")).toEqual({ text: "'a' 'b'", start: 4, end: 7 });
    expect(tmQxInsert('a <', 3, '= 1')).toEqual({ text: 'a < = 1', start: 4, end: 7 });
    expect(tmQxInsert('a.', 2, '5')).toEqual({ text: 'a. 5', start: 3, end: 4 });
    expect(tmQxInsert('x', 0, "'b'")).toEqual({ text: "'b' x", start: 0, end: 3 });
  });

  it('clamps the caret to the text', () => {
    expect(tmQxInsert('a', 9, ' b')).toEqual({ text: 'a b', start: 1, end: 3 });
    expect(tmQxInsert('a', -4, 'b ')).toEqual({ text: 'b a', start: 0, end: 2 });
  });

  it('never splits an unterminated string or a malformed number', () => {
    expect(tmQxInsert("a = 'ab", 6, 'x')).toEqual({ text: "a = 'ab x", start: 8, end: 9 });
    expect(tmQxInsert('a = 1e3', 5, 'x').text).toBe('a = 1e3 x');
  });
});

describe('ɵtmQxPlanInsert', () => {
  it('names where the added text goes in the old text, spaces included', () => {
    expect(ɵtmQxPlanInsert('Amount>', 3, 'x')).toEqual({ text: 'Amount x>', at: 6, added: ' x', start: 7, end: 8 });
    expect(ɵtmQxPlanInsert('f(x)', 4, 'Code')).toEqual({ text: 'f(x) Code', at: 4, added: ' Code', start: 5, end: 9 });
  });
});
