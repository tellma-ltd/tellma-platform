// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import {
  type TmLauncherCandidate,
  tmLauncherIndex,
  tmLauncherMarks,
  tmLauncherMatch,
  tmLauncherRows,
  tmLauncherTokens,
  TM_LAUNCHER_CREATE_LIMIT,
  TM_LAUNCHER_ROW_LIMIT,
} from './tm-launcher-index';

// The launcher's page search as pure functions: the query's tokens, the two
// match classes with the Arabic article's variants and the English terms,
// the order of the matches, the create rows and the row cap, the marked runs
// of a label, and the time a 300-entry index takes to build and to query.

/** A candidate of a kind, label and terms, the label its first term unless the terms say otherwise. */
function candidate(
  key: string,
  label: string,
  patch: Partial<Omit<TmLauncherCandidate, 'key' | 'label'>> = {},
): TmLauncherCandidate {
  return {
    kind: 'entry',
    key,
    href: `/17/${key}`,
    label,
    icon: 'file-text',
    description: null,
    terms: [label],
    create: null,
    ...patch,
  };
}

/** A create page and its row's label. */
function create(key: string, label: string): TmLauncherCandidate['create'] {
  return { href: `/17/${key}/new`, label };
}

/** The labels of a query's rows, a create row's as its own label. */
function rows(candidates: readonly TmLauncherCandidate[], query: string): string[] {
  return tmLauncherRows(tmLauncherMatch(tmLauncherIndex(candidates), tmLauncherTokens(query))).map((row) =>
    row.kind === 'create' ? row.candidate.create!.label : row.candidate.label,
  );
}

/** The classes of a query's matches, as `label: class`. */
function classes(candidates: readonly TmLauncherCandidate[], query: string): string[] {
  return tmLauncherMatch(tmLauncherIndex(candidates), tmLauncherTokens(query)).map(({ candidate, match }) => `${candidate.label}: ${match}`);
}

/** A label's runs, a marked one in brackets. */
function marked(label: string, query: string): string {
  return tmLauncherMarks(label, tmLauncherTokens(query))
    .map((segment) => (segment.mark ? `[${segment.text}]` : segment.text))
    .join('');
}

/** The fastest of several runs after a warm-up, in milliseconds, so a busy machine's pause does not count. */
function fastest(run: () => unknown): number {
  for (let i = 0; i < 5; i++) {
    run();
  }
  let best = Infinity;
  for (let i = 0; i < 20; i++) {
    const started = performance.now();
    run();
    best = Math.min(best, performance.now() - started);
  }
  return best;
}

// The worked example: Farah works in Arabic, and has a pin no entry covers.
const SALES_INVOICES = candidate('sales-invoices', 'فواتير المبيعات', {
  terms: ['فواتير المبيعات', 'Sales invoices'],
  create: create('sales-invoices', 'فاتورة مبيعات · إنشاء'),
});
const CUSTOMERS = candidate('customers', 'العملاء', {
  terms: ['العملاء', 'زبائن', 'Customers', 'Clients'],
  create: create('customers', 'عميل · إنشاء'),
});
const TO_CHASE = candidate('pin:a1', 'Invoices to chase', { kind: 'pin', href: '/17/sales-invoices?f.Due=lt:today' });
const FARAH = [SALES_INVOICES, CUSTOMERS, TO_CHASE];

describe('the launcher page search', () => {
  describe('tokens', () => {
    it('folds the query and splits it into at most five words', () => {
      expect(tmLauncherTokens('  Inv — SAL ')).toEqual(['inv', 'sal']);
      expect(tmLauncherTokens('a b c d e f g')).toEqual(['a', 'b', 'c', 'd', 'e']);
      expect(tmLauncherTokens('الفاتورة ١٢')).toEqual(['الفاتوره', '12']);
    });

    it('has none for a query with no letter or digit', () => {
      expect(tmLauncherTokens(' — … ')).toEqual([]);
      expect(rows(FARAH, ' — ')).toEqual([]);
    });
  });

  describe('match classes', () => {
    it('matches the prefix class when every token starts a word of one term, in any order', () => {
      const lines = candidate('lines', 'Sales invoice lines');
      expect(classes([lines], 'inv sal')).toEqual(['Sales invoice lines: prefix']);
      expect(classes([lines], 'lin')).toEqual(['Sales invoice lines: prefix']);
      // Two tokens that start words of two different terms are no prefix match.
      expect(classes([candidate('x', 'Centers', { terms: ['Centers', 'Cost units'] })], 'cen uni')).toEqual([]);
    });

    it('matches the contains class when every token of two or more characters occurs in one term', () => {
      expect(classes([candidate('i', 'Sales invoices')], 'voice')).toEqual(['Sales invoices: contains']);
      expect(classes([candidate('i', 'Sales invoices')], 'les voi')).toEqual(['Sales invoices: contains']);
      // A one-character token takes no part in the contains class.
      expect(classes([candidate('i', 'Sales invoices')], 'x voice')).toEqual(['Sales invoices: contains']);
      expect(classes([candidate('i', 'Sales invoices')], 'v')).toEqual([]);
    });

    it("decides a candidate's class by its best term", () => {
      const both = candidate('c', 'Accounts', { terms: ['Accounts', 'Chart of accounts'] });
      expect(classes([both], 'chart')).toEqual(['Accounts: prefix']);
      expect(classes([both], 'count')).toEqual(['Accounts: contains']);
    });

    it('folds case, diacritics, Arabic letter forms and digits on both sides', () => {
      expect(classes([candidate('c', 'Café 12')], 'CAFE ١٢')).toEqual(['Café 12: prefix']);
      expect(classes([candidate('f', 'الفاتورة')], 'فاتوره')).toEqual(['الفاتورة: prefix']);
      expect(classes([candidate('a', 'أصول')], 'اصو')).toEqual(['أصول: prefix']);
    });

    it('finds an Arabic word of four or more letters without its article', () => {
      expect(classes([CUSTOMERS], 'عملاء')).toEqual(['العملاء: prefix']);
      // A word of three letters keeps its article: "الف" is no variant of "ف".
      expect(classes([candidate('a', 'الف')], 'ف')).toEqual([]);
    });

    it('matches the English terms beside the active language', () => {
      expect(classes([SALES_INVOICES], 'sales')).toEqual(['فواتير المبيعات: prefix']);
      expect(classes([CUSTOMERS], 'clie')).toEqual(['العملاء: prefix']);
    });
  });

  describe('rows', () => {
    // Spec 0027 §9.4's example, in which every row follows the rules: the
    // pin also contains "voice", so it follows the entry's contains match.
    for (const [query, expected] of [
      ['inv', ['فواتير المبيعات', 'فاتورة مبيعات · إنشاء', 'Invoices to chase']],
      ['فوات', ['فواتير المبيعات', 'فاتورة مبيعات · إنشاء']],
      ['voice', ['فواتير المبيعات', 'Invoices to chase']],
      ['زبائن', ['العملاء', 'عميل · إنشاء']],
    ] as const) {
      it(`lists the rows of "${query}" in order`, () => {
        expect(rows(FARAH, query)).toEqual(expected);
      });
    }

    it('lists the prefix matches before the contains matches, each class in the index order', () => {
      const candidates = [
        candidate('a', 'Sales invoices'),
        candidate('b', 'Voice notes'),
        candidate('c', 'Invoice lines', { kind: 'pin' }),
        candidate('d', 'Voicemail', { kind: 'recent' }),
      ];
      expect(classes(candidates, 'voice')).toEqual([
        'Voice notes: prefix',
        'Voicemail: prefix',
        'Sales invoices: contains',
        'Invoice lines: contains',
      ]);
    });

    it('follows a prefix-matched entity that may be created by its create row, three at most', () => {
      const entities = ['Cash', 'Cars', 'Cards', 'Carts', 'Cats'].map((label) => candidate(label, label, { create: create(label, `${label} · Create`) }));
      expect(rows(entities, 'ca')).toEqual(['Cash', 'Cash · Create', 'Cars', 'Cars · Create', 'Cards', 'Cards · Create', 'Carts', 'Cats']);
      expect(rows(entities, 'ca').filter((label) => label.endsWith('Create'))).toHaveLength(TM_LAUNCHER_CREATE_LIMIT);
      // A contains match has no create row.
      expect(rows(entities, 'sh')).toEqual(['Cash']);
    });

    it('shows fifty rows at most, create rows included', () => {
      const many = Array.from({ length: 60 }, (_, i) => candidate(`e${i}`, `Entry ${String(i).padStart(2, '0')}`, { create: create(`e${i}`, `Entry ${i} · Create`) }));
      const shown = rows(many, 'ent');
      expect(shown).toHaveLength(TM_LAUNCHER_ROW_LIMIT);
      expect(shown.slice(0, 7)).toEqual(['Entry 00', 'Entry 0 · Create', 'Entry 01', 'Entry 1 · Create', 'Entry 02', 'Entry 2 · Create', 'Entry 03']);
      expect(shown.at(-1)).toBe('Entry 46');
      // An entity in the fiftieth row shows without its create row.
      const plain = many.slice(0, 49).map((entry) => ({ ...entry, create: null }));
      const last = candidate('last', 'Entry last', { create: create('last', 'Last · Create') });
      expect(rows([...plain, last], 'ent')).toHaveLength(TM_LAUNCHER_ROW_LIMIT);
      expect(rows([...plain, last], 'ent').at(-1)).toBe('Entry last');
    });
  });

  describe('marks', () => {
    it("marks the part of each word that a token starts, and joins back into the label", () => {
      expect(marked('Sales invoices', 'inv')).toBe('Sales [inv]oices');
      expect(marked('Sales invoice lines', 'lin sal')).toBe('[Sal]es invoice [lin]es');
      expect(tmLauncherMarks('Sales invoices', ['inv']).map((segment) => segment.text).join('')).toBe('Sales invoices');
    });

    it('marks the longest token that starts a word', () => {
      expect(marked('Sales', 's sal')).toBe('[Sal]es');
    });

    it('maps the folded run back to the displayed characters', () => {
      expect(marked('Café bar', 'cafe')).toBe('[Café] bar');
      expect(marked('الفاتورة', 'فاتوره')).toBe('ال[فاتورة]');
      expect(marked('أصول ثابتة', 'اصو')).toBe('[أصو]ل ثابتة');
      expect(marked('Cost-centers', 'cen')).toBe('Cost-[cen]ters');
    });

    it("marks a word's start after its article for a token that starts its variant", () => {
      expect(marked('العملاء', 'عمل')).toBe('ال[عمل]اء');
      expect(marked('العملاء', 'العم')).toBe('[العم]لاء');
    });

    it('marks nothing for a match on another term or a contains match', () => {
      expect(tmLauncherMarks('فواتير المبيعات', ['inv'])).toEqual([{ text: 'فواتير المبيعات', mark: false }]);
      expect(marked('Sales invoices', 'voice')).toBe('Sales invoices');
    });
  });

  describe('speed', () => {
    // 300 entries, each with a label, two synonyms and the English label, as
    // a large distribution's launcher holds. The limits are the spec's: an
    // index built in 5 ms, a query answered in 2 ms, each the fastest of
    // twenty runs after a warm-up.
    const words = ['Sales', 'Purchase', 'Invoices', 'Receipts', 'Centers', 'Accounts', 'Journal', 'Assets', 'Stock', 'Payroll', 'Budgets', 'Taxes'];
    const arabic = ['المبيعات', 'المشتريات', 'الفواتير', 'الإيصالات', 'المراكز', 'الحسابات', 'اليومية', 'الأصول', 'المخزون', 'الرواتب', 'الموازنات', 'الضرائب'];
    const entries = Array.from({ length: 300 }, (_, i) => {
      const [a, b, c] = [i % 12, (i * 5 + 3) % 12, (i * 7 + 1) % 12];
      return candidate(`e${i}`, `${arabic[a]} ${arabic[b]} ${i}`, {
        terms: [`${arabic[a]} ${arabic[b]} ${i}`, `${arabic[c]} ${i}`, `${words[c]} register`, `${words[a]} ${words[b]} ${i}`],
        create: i % 3 === 0 ? create(`e${i}`, `${words[a]} ${i} · Create`) : null,
      });
    });

    it('builds the index of 300 entries in 5 ms or less', () => {
      expect(fastest(() => tmLauncherIndex(entries))).toBeLessThanOrEqual(5);
    });

    it('answers a query over 300 entries in 2 ms or less', () => {
      const index = tmLauncherIndex(entries);
      for (const query of ['inv', 'sales inv', 'voice', 'الفوات', 'مراكز']) {
        const tokens = tmLauncherTokens(query);
        expect(fastest(() => tmLauncherRows(tmLauncherMatch(index, tokens))), query).toBeLessThanOrEqual(2);
      }
    });
  });
});
