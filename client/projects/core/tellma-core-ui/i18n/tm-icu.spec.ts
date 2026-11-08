// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import { TM_UI_STRINGS_EN } from './strings-en';
import { ɵtmFormatMessage as format } from './tm-icu';

/** Every leaf message of a nested string table. */
function messages(table: object, prefix = ''): Array<[string, string]> {
  return Object.entries(table).flatMap(([key, value]) =>
    typeof value === 'string'
      ? [[prefix + key, value] as [string, string]]
      : messages(value as object, `${prefix}${key}.`),
  );
}

describe('ICU message interpreter', () => {
  afterEach(() => vi.restoreAllMocks());

  describe('text and arguments', () => {
    it('passes plain text through, including a lone apostrophe', () => {
      expect(format('Plain text', {}, 'en-US')).toBe('Plain text');
      expect(format("Don't stop", {}, 'en-US')).toBe("Don't stop");
    });

    it('substitutes simple arguments and shows a missing one as its placeholder', () => {
      expect(format('{label} selected', { label: 'Acme' }, 'en-US')).toBe('Acme selected');
      expect(format('{label} selected', {}, 'en-US')).toBe('{label} selected');
      expect(format('{ label } and {n}', { label: 'A', n: 0 }, 'en-US')).toBe('A and 0');
    });

    it('never re-reads a parameter value as message syntax', () => {
      const value = '{count, plural, other {# injected}}';
      expect(format('Hello {name}', { name: value }, 'en-US')).toBe(`Hello ${value}`);
    });

    it('formats numbers in the given locale, with the integer, percent and currency styles', () => {
      expect(format('{n, number}', { n: 1234.5 }, 'en-US')).toBe('1,234.5');
      expect(format('{n, number, integer}', { n: 1234.5 }, 'en-US')).toBe('1,235');
      expect(format('{n, number, percent}', { n: 0.5 }, 'en-US')).toBe('50%');
      expect(format('{n, number, currency:EUR}', { n: 12 }, 'en-US')).toBe('€12.00');
      expect(format('{n, number}', { n: 1234.5 }, 'ar-SA')).toBe('١٬٢٣٤٫٥');
      expect(format('{n, number}', { n: 1234.5 }, 'ar-SA-u-nu-latn')).toBe('1,234.5');
    });
  });

  describe('plural', () => {
    const files = '{n, plural, =0 {No files} one {# file} other {# files}}';

    it('prefers an exact case, then the CLDR category, then other', () => {
      expect(format(files, { n: 0 }, 'en-US')).toBe('No files');
      expect(format(files, { n: 1 }, 'en-US')).toBe('1 file');
      expect(format(files, { n: 1000 }, 'en-US')).toBe('1,000 files');
    });

    it('picks every Arabic category and formats # in the locale digits', () => {
      const ar =
        '{n, plural, zero {صفر} one {واحد} two {اثنان} few {# قليل} many {# كثير} other {# آخر}}';
      expect(format(ar, { n: 0 }, 'ar-SA')).toBe('صفر');
      expect(format(ar, { n: 1 }, 'ar-SA')).toBe('واحد');
      expect(format(ar, { n: 2 }, 'ar-SA')).toBe('اثنان');
      expect(format(ar, { n: 5 }, 'ar-SA')).toBe('٥ قليل');
      expect(format(ar, { n: 11 }, 'ar-SA')).toBe('١١ كثير');
      expect(format(ar, { n: 100 }, 'ar-SA')).toBe('١٠٠ آخر');
      expect(format(ar, { n: 100 }, 'ar')).toBe('100 آخر');
    });

    it('applies offset to the category and to #, but not to exact cases', () => {
      const message =
        '{n, plural, offset:1 =0 {nobody} =1 {only {who}} one {{who} and # other} other {{who} and # others}}';
      expect(format(message, { n: 0, who: 'Sam' }, 'en-US')).toBe('nobody');
      expect(format(message, { n: 1, who: 'Sam' }, 'en-US')).toBe('only Sam');
      expect(format(message, { n: 2, who: 'Sam' }, 'en-US')).toBe('Sam and 1 other');
      expect(format(message, { n: 5, who: 'Sam' }, 'en-US')).toBe('Sam and 4 others');
    });

    it('selects ordinals', () => {
      const message = '{n, selectordinal, one {#st} two {#nd} few {#rd} other {#th}}';
      const rendered = [1, 2, 3, 4, 11, 21, 22, 103].map((n) => format(message, { n }, 'en-US'));
      expect(rendered).toEqual(['1st', '2nd', '3rd', '4th', '11th', '21st', '22nd', '103rd']);
    });

    it('binds # to the innermost plural, through a nested select', () => {
      const message =
        '{n, plural, other {{g, select, female {# for her} other {# for them}} ' +
        '{m, plural, one {and # more} other {and # more}}}}';
      expect(format(message, { n: 3, g: 'female', m: 7 }, 'en-US')).toBe('3 for her and 7 more');
    });

    it('treats # outside a plural as text', () => {
      expect(format('Item #{n}', { n: 4 }, 'en-US')).toBe('Item #4');
    });

    it('reads a missing or non-numeric count as the other case', () => {
      expect(format('{n, plural, one {one} other {other}}', {}, 'en-US')).toBe('other');
      expect(format('{n, plural, one {one} other {other}}', { n: 'abc' }, 'en-US')).toBe('other');
    });
  });

  describe('select', () => {
    const message = '{gender, select, female {She} male {He} other {They}} replied';

    it('matches a case by its string value, else other', () => {
      expect(format(message, { gender: 'female' }, 'en-US')).toBe('She replied');
      expect(format(message, { gender: 'male' }, 'en-US')).toBe('He replied');
      expect(format(message, { gender: 'unknown' }, 'en-US')).toBe('They replied');
      expect(format(message, {}, 'en-US')).toBe('They replied');
    });

    it('nests a plural inside a select case', () => {
      const nested =
        '{g, select, female {{n, plural, one {she has # item} other {she has # items}}} ' +
        'other {{n, plural, one {they have # item} other {they have # items}}}}';
      expect(format(nested, { g: 'female', n: 1 }, 'en-US')).toBe('she has 1 item');
      expect(format(nested, { g: 'x', n: 2 }, 'en-US')).toBe('they have 2 items');
    });
  });

  describe('apostrophe quoting', () => {
    it("turns '' into one apostrophe and quotes braces", () => {
      expect(format("It''s '{literal}' text", {}, 'en-US')).toBe("It's {literal} text");
      expect(format("'{'{name}'}'", { name: 'x' }, 'en-US')).toBe('{x}');
    });

    it('quotes # inside a plural, and keeps the quotes around # outside one', () => {
      expect(format("{n, plural, other {'#' is #}}", { n: 2 }, 'en-US')).toBe('# is 2');
      expect(format("Use '#' here", {}, 'en-US')).toBe("Use '#' here");
    });

    it('leaves an unclosed quote as a literal apostrophe', () => {
      expect(format("Quote: '{x}", { x: 'X' }, 'en-US')).toBe("Quote: 'X");
    });
  });

  describe('malformed messages', () => {
    const malformed = [
      '{unclosed',
      '{n, plural, one {x}}', // no other case
      '{n, select, =1 {x} other {y}}', // exact case in a select
      '{n, select, offset:1 other {y}}', // offset in a select
      '{n, plural other {x}}', // missing comma
      '{, number}', // missing name
      '{n, number, {m}}', // an argument inside a style
    ];

    for (const message of malformed) {
      it(`renders ${JSON.stringify(message)} as its raw text, with one warning`, () => {
        const warn = vi.spyOn(console, 'warn').mockImplementation(() => {});
        expect(format(message, { n: 1, m: 2 }, 'en-US')).toBe(message);
        expect(format(message, { n: 2, m: 3 }, 'en-US')).toBe(message);
        expect(warn).toHaveBeenCalledTimes(1);
        expect(String(warn.mock.calls[0][0])).toContain('Malformed ICU message');
      });
    }

    it('keeps a stray closing brace at the root as text', () => {
      expect(format('a } b {n}', { n: 1 }, 'en-US')).toBe('a } b 1');
    });
  });

  describe('unsupported formats', () => {
    it('formats an unknown number style as a plain number, with a warning', () => {
      const warn = vi.spyOn(console, 'warn').mockImplementation(() => {});
      expect(format('{n, number, ::compact-short}', { n: 1500 }, 'en-US')).toBe('1,500');
      expect(warn).toHaveBeenCalled();
    });

    it('shows an unsupported argument type as its raw value, with a warning', () => {
      const warn = vi.spyOn(console, 'warn').mockImplementation(() => {});
      expect(format('On {d, date, short}', { d: '2026-11-08' }, 'en-US')).toBe('On 2026-11-08');
      expect(warn).toHaveBeenCalled();
    });

    it('falls back to the runtime default locale for an invalid tag instead of throwing', () => {
      expect(() =>
        format('{n, plural, one {#} other {#}}', { n: 2 }, 'not a locale'),
      ).not.toThrow();
      expect(() => format('{n, number}', { n: 2 }, 'not a locale')).not.toThrow();
    });
  });

  it('parses every built-in English library string without a warning', () => {
    const warn = vi.spyOn(console, 'warn').mockImplementation(() => {});
    for (const [key, message] of messages(TM_UI_STRINGS_EN)) {
      const rendered = format(message, { gender: 'other' }, 'en-US');
      expect(rendered, key).not.toBe('');
    }
    expect(warn.mock.calls.filter(([text]) => String(text).includes('Malformed'))).toEqual([]);
  });
});
