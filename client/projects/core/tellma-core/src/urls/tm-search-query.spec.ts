// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import {
  tmIsColumnFilter,
  tmParseListPayload,
  tmParseSearchQuery,
  tmPrintListPayload,
  tmPrintSearchQuery,
  type TmSearchQuery,
} from './tm-search-query';

/** Parses a query string and a raw hash, collecting what was dropped. */
function parse(search: string, hash = ''): { query: TmSearchQuery; problems: string[] } {
  const problems: string[] = [];
  const query = tmParseSearchQuery(new URLSearchParams(search), hash, (problem) => problems.push(problem));
  return { query, problems };
}

/** Prints a query as `search` and `hash` joined, as a URL's tail reads. */
function print(query: TmSearchQuery, defaults = {}): string {
  const printed = tmPrintSearchQuery(query, defaults);
  return printed.search + printed.hash;
}

describe('the search query codec', () => {
  describe('parsing', () => {
    it.each([
      ['q=jed&f.Name=contains:a,b&order=Code', { q: 'jed', columnFilters: { Name: 'contains:a,b' }, order: 'Code' }],
      [
        'tab=mine&f.CreatedAt=between:2026-01-01,2026-03-31&view=tiles&list=1',
        { tab: 'mine', columnFilters: { CreatedAt: 'between:2026-01-01,2026-03-31' }, view: 'tiles', list: true },
      ],
      ['page=0&view=cards', {}],
    ])('parses %s', (search, expected) => {
      expect(parse(search).query).toEqual(expected);
    });

    it('reads every field of the parameter table', () => {
      const { query, problems } = parse(
        'tab=mine&q=jed&f.CenterType=in:Service,Sale&inactive=1&x.overdue=1&order=Name:desc&cols=Code,Name,Parent&page=2&size=100&view=tiles&list=1',
        '#d=eyJmaWx0ZXIiOiJDcmVkaXRMaW1pdCA-IDEwMDAwMCJ9',
      );
      expect(problems).toEqual([]);
      expect(query).toEqual({
        tab: 'mine',
        q: 'jed',
        columnFilters: { CenterType: 'in:Service,Sale' },
        filter: 'CreditLimit > 100000',
        inactive: true,
        custom: { overdue: '1' },
        order: 'Name:desc',
        cols: ['Code', 'Name', 'Parent'],
        page: 2,
        size: 100,
        view: 'tiles',
        list: true,
      });
    });

    it('drops each invalid entry, reports it, and keeps the rest', () => {
      const { query, problems } = parse(
        'f.1bad=eq:1&f.Name=fuzzy:a&f.Code=eq:&f.Ids=in:1,,2&f.When=between:2026-01-01&f.Esc=in:a\\q&order=Name:up&cols=Code,9x&page=-1&size=abc&inactive=2&q=ok',
      );
      expect(query).toEqual({ q: 'ok', cols: ['Code'] });
      expect(problems).toHaveLength(11);
    });

    it('keeps the escapes of an in-list and accepts + for a space', () => {
      expect(parse('f.Name=in:a\\,b,c\\\\d&q=jed+mall').query).toEqual({ q: 'jed mall', columnFilters: { Name: 'in:a\\,b,c\\\\d' } });
      expect(tmIsColumnFilter('between:1,2,3')).toBe(false);
      expect(tmIsColumnFilter('contains:a,b,c')).toBe(true);
    });

    it('drops a malformed d and reports it', () => {
      for (const hash of ['#d=***', '#d=e30', '#d=WzFd', '#d=eyJmaWx0ZXIiOjF9', '#d=_w']) {
        const { query, problems } = parse('q=x', hash);
        expect(query).toEqual({ q: 'x' });
        expect(problems).toHaveLength(1);
      }
    });

    it('strips bidi controls from q and keeps a zero-width non-joiner', () => {
      const q = 'می‌خواهم ‫jed‬⁦x⁩‏؜';
      expect(parse(`q=${encodeURIComponent(q)}`).query.q).toBe('می‌خواهم jedx');
      expect(print({ q })).toBe(`q=${encodeURIComponent('می‌خواهم jedx')}`);
      expect(parse(`q=${encodeURIComponent('‎')}`).query).toEqual({});
    });
  });

  describe('printing', () => {
    it('prints the canonical order and leaves out every default', () => {
      expect(
        print(
          {
            list: true,
            view: 'grid',
            size: 50,
            page: 1,
            cols: ['Code', 'Name'],
            order: 'Code',
            custom: { z: '1', a: '2' },
            inactive: false,
            columnFilters: { Name: 'contains:a b', CenterType: 'in:Service,Sale' },
            q: '',
            tab: 'all',
          },
          { tab: 'all', cols: ['Code', 'Name'], view: 'grid' },
        ),
      ).toBe('f.CenterType=in:Service,Sale&f.Name=contains:a%20b&x.a=2&x.z=1&order=Code&list=1');
    });

    it('writes the filter as base64url JSON in the fragment, never in the query', () => {
      expect(tmPrintSearchQuery({ filter: 'CreditLimit > 100000' })).toEqual({
        search: '',
        hash: '#d=eyJmaWx0ZXIiOiJDcmVkaXRMaW1pdCA-IDEwMDAwMCJ9',
      });
    });

    it('restores commas, colons and at signs and encodes the rest', () => {
      expect(print({ q: 'a@b, c:d&e=f/g?' })).toBe('q=a@b,%20c:d%26e%3Df%2Fg%3F');
    });

    it.each([
      'q=jed',
      'tab=mine&q=jed%20mall&f.CenterType=in:Service,Sale&f.Name=contains:a%20b&inactive=1&x.overdue=1&order=Name:desc&cols=Code,Name&page=2&size=100&view=tiles&list=1#d=eyJmaWx0ZXIiOiJDcmVkaXRMaW1pdCA-IDEwMDAwMCJ9',
      'f.Name=in:a%5C,b,c&order=Code',
      'q=%D8%AC%D8%AF%D8%A9#d=eyJmaWx0ZXIiOiJOYW1lIGNvbnRhaW5zICfYrNiv2KknIn0',
    ])('prints the query it parsed from the canonical %s', (url) => {
      const [search, hash = ''] = url.split('#');
      expect(print(parse(search!, hash ? `#${hash}` : '').query)).toBe(url);
    });
  });

  describe('the list payload', () => {
    it('reads back what it wrote, cols included', () => {
      for (const payload of [{ filter: "Customer.Name = 'جدة & co'" }, { filter: null }, { filter: 'x', cols: ['Code', 'Customer.Name'] }]) {
        const hash = tmPrintListPayload(payload);
        expect(hash).toMatch(/^#d=[A-Za-z0-9_-]+$/);
        expect(tmParseListPayload(hash)).toEqual(payload);
        expect(tmPrintListPayload(tmParseListPayload(hash)!)).toBe(hash);
      }
    });

    it('parses a malformed d, or no d, to a null payload', () => {
      for (const hash of ['', '#x', '#d=***', '#d=eyJmaWx0ZXIiOjF9', '#d=eyJmaWx0ZXIiOiJ4IiwiY29scyI6WyIxIl19']) {
        expect(tmParseListPayload(hash)).toBeNull();
      }
    });
  });
});
