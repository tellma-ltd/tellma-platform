// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import { TestBed } from '@angular/core/testing';
import { TranslocoService } from '@jsverse/transloco';
import {
  TmEntities,
  tmEnglish,
  tmEntity,
  tmFeature,
  TmLocalization,
  TmNavigation,
  type TmLanguageDefinition,
  type TmNavEntry,
  type TmNavItem,
} from '@tellma/core';

import { tmAppRig, tmLoadTestPage, tmNavigate, tmSettle, TM_T17_DOCS } from '../../../src/routes/tm-app.testing.util';
import { tmLauncherCandidates, tmLauncherIndex } from './tm-launcher-index';
import { TmNavTermsReader } from './tm-nav-terms';

// The launcher index's terms in a whole test app whose user prefers Arabic:
// an entity, a feature entry with English synonyms and the platform's My
// profile entry, with Arabic synonyms split on the Arabic comma; the reader
// brings the platform entries' English synonyms when it is created. Then the
// work the first open's index takes over 300 such entries: each entry's
// terms read once in each language, and one collator. The wall-clock budget
// is the nightly tier's, in the launcher's perf spec.

/** Arabic, with the platform strings the tests read. */
const ARABIC: TmLanguageDefinition = {
  code: 'ar',
  dir: 'rtl',
  strings: () =>
    Promise.resolve({
      tm: { nav: { me: { profile: 'ملفي الشخصي' } }, launcher: { synonyms: { meProfile: 'الصورة، التوقيع، بيانات الاتصال' } } },
      tmUi: {},
    }),
};

/** The extra entries a test adds, with their strings in each language. */
interface TmExtraEntries {
  /** The entries. */
  readonly entries: readonly TmNavEntry[];
  /** Their English strings. */
  readonly en: Readonly<Record<string, string>>;
  /** Their Arabic strings. */
  readonly ar: Readonly<Record<string, string>>;
}

/** Starts the app in Arabic, enters tenant 17, loads English too and returns the registry; the test creates the reader. */
async function rig(extra: TmExtraEntries = { entries: [], en: {}, ar: {} }): Promise<{ readonly navigation: TmNavigation }> {
  const { me, settings } = TM_T17_DOCS;
  const grants = ['acme.Customer', 'acme.Currency'].map((resource) => ({ resource, action: 'Read', hasFilter: false }));
  await tmAppRig({
    docs: {
      ...TM_T17_DOCS,
      me: { ...me, user: { ...me.user, preferredLanguage: 'ar' }, access: { ...me.access, securables: grants } },
      settings: { ...settings, languages: [...settings.languages, { code: 'ar', nativeName: 'العربية', symbol: 'ع', isRightToLeft: true }] },
    },
    features: [
      tmFeature({
        key: 'acme',
        entities: [tmEntity('acme.Customer', { details: tmLoadTestPage }), tmEntity('acme.Currency', {})],
        entries: [{ id: 'acme.overdue', labelKey: 'Acme_Overdue', url: (t) => `/${t}/customers?f.Overdue=eq:true`, requires: ['acme.Customer', 'Read'] }, ...extra.entries],
      }),
    ],
    languages: [tmEnglish(), ARABIC],
    strings: {
      en: { acme_Customer_Plural: 'Customers', Acme_Overdue: 'Overdue customers', Acme_Overdue_Synonyms: 'Late, , Arrears ,', ...extra.en },
      ar: { acme_Customer_Plural: 'العملاء', Acme_Overdue: 'العملاء المتأخرون', ...extra.ar },
    },
  });
  await tmNavigate('/17/customers');
  await tmSettle();
  await TestBed.inject(TmLocalization).ɵloadLanguage('en');
  return { navigation: TestBed.inject(TmNavigation) };
}

/** The visible entry of an id. */
function item(navigation: TmNavigation, id: string): TmNavItem {
  const found = navigation.items().find((candidate) => candidate.id === id);
  if (found === undefined) {
    throw new Error(`No visible entry '${id}'.`);
  }
  return found;
}

describe('TmNavTermsReader', () => {
  it("gives an entry's label and synonyms in the active language and in English", async () => {
    const { navigation } = await rig();
    // The platform entries' English synonyms arrive with the reader, not with the app.
    const english = (): unknown => (TestBed.inject(TranslocoService).getTranslation('en') as Readonly<Record<string, unknown>>)['tm.launcher.synonyms.meProfile'];
    expect(english()).toBeUndefined();
    const reader = TestBed.inject(TmNavTermsReader);
    expect(english()).toBe('Photo, Signature, Contact');
    const profile = item(navigation, 'core.me.profile');
    expect(reader.read(profile, 'ar')).toEqual({ label: 'ملفي الشخصي', synonyms: ['الصورة', 'التوقيع', 'بيانات الاتصال'] });
    expect(reader.read(profile, 'en')).toEqual({ label: 'My profile', synonyms: ['Photo', 'Signature', 'Contact'] });
  });

  it('trims the synonyms and drops the empty ones', async () => {
    const { navigation } = await rig();
    const reader = TestBed.inject(TmNavTermsReader);
    const overdue = item(navigation, 'acme.overdue');
    expect(reader.read(overdue, 'ar')).toEqual({ label: 'العملاء المتأخرون', synonyms: [] });
    expect(reader.read(overdue, 'en')).toEqual({ label: 'Overdue customers', synonyms: ['Late', 'Arrears'] });
  });

  it("falls back to the entity's own name in a language without its label", async () => {
    const { navigation } = await rig();
    const reader = TestBed.inject(TmNavTermsReader);
    expect(reader.read(item(navigation, 'acme.Currency'), 'en')).toEqual({ label: 'acme.Currency', synonyms: [] });
  });

  it("builds the first open's index over 300 entries with one read of each entry's terms in each language, and one collator", async () => {
    // 300 entries, each with a label and a synonym in Arabic and in English.
    const words = ['Sales', 'Purchase', 'Invoices', 'Receipts', 'Centers', 'Accounts', 'Journal', 'Assets', 'Stock', 'Payroll', 'Budgets', 'Taxes'];
    const arabic = ['المبيعات', 'المشتريات', 'الفواتير', 'الإيصالات', 'المراكز', 'الحسابات', 'اليومية', 'الأصول', 'المخزون', 'الرواتب', 'الموازنات', 'الضرائب'];
    const en: Record<string, string> = {};
    const ar: Record<string, string> = {};
    const entries = Array.from({ length: 300 }, (_, i): TmNavEntry => {
      const [a, b, c] = [i % 12, (i * 5 + 3) % 12, (i * 7 + 1) % 12];
      en[`Acme_E${i}`] = `${words[a]} ${words[b]} ${i}`;
      en[`Acme_E${i}_Synonyms`] = `${words[c]} register`;
      ar[`Acme_E${i}`] = `${arabic[a]} ${arabic[b]} ${i}`;
      ar[`Acme_E${i}_Synonyms`] = `${arabic[c]} ${i}`;
      return { id: `acme.e${i}`, labelKey: `Acme_E${i}`, synonymsKey: `Acme_E${i}_Synonyms`, url: (t) => `/${t}/customers?f.E=eq:${i}` };
    });
    const { navigation } = await rig({ entries, en, ar });
    const reader = TestBed.inject(TmNavTermsReader);
    const entities = TestBed.inject(TmEntities);
    const items = navigation.items();
    expect(items.length).toBeGreaterThanOrEqual(300);
    const read = vi.spyOn(reader, 'read');
    const collator = vi.spyOn(Intl, 'Collator');
    try {
      // The panel's own build: the candidates from the registry and the reader, then the index.
      const index = tmLauncherIndex(
        tmLauncherCandidates({
          language: 'ar',
          entries: items,
          sections: new Map(navigation.sections().map((section) => [section.id, section.label])),
          pins: [],
          recents: [],
          pinned: 'Pinned',
          recent: 'Recently visited',
          terms: (entry, of) => reader.read(entry, of),
          createLabel: (entity) => `${entities.label(entity)()} · Create`,
          entry: (href) => navigation.byHref(href),
        }),
      );
      expect(index.entries).toHaveLength(items.length);
      // Each entry's terms, read once in Arabic and once in English.
      const reads = new Map<string, number>();
      for (const [entry, language] of read.mock.calls) {
        const key = `${entry.id} ${language}`;
        reads.set(key, (reads.get(key) ?? 0) + 1);
      }
      expect(read).toHaveBeenCalledTimes(2 * items.length);
      expect(items.flatMap((entry) => ['ar', 'en'].map((language) => reads.get(`${entry.id} ${language}`)))).toEqual(items.flatMap(() => [1, 1]));
      expect(collator).toHaveBeenCalledTimes(1);
    } finally {
      read.mockRestore();
      collator.mockRestore();
    }
  });
});
