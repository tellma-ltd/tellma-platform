// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import { TestBed } from '@angular/core/testing';
import { TranslocoService } from '@jsverse/transloco';
import { tmEnglish, tmEntity, tmFeature, TmLocalization, TmNavigation, type TmLanguageDefinition, type TmNavItem } from '@tellma/core';

import { tmAppRig, tmLoadTestPage, tmNavigate, tmSettle, TM_T17_DOCS } from '../../../src/routes/tm-app.testing.util';
import { TmNavTermsReader } from './tm-nav-terms';

// The launcher index's terms in a whole test app whose user prefers Arabic:
// an entity, a feature entry with English synonyms and the platform's My
// profile entry, with Arabic synonyms split on the Arabic comma; the reader
// brings the platform entries' English synonyms when it is created.

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

/** Starts the app in Arabic, enters tenant 17, loads English too and returns the registry; the test creates the reader. */
async function rig(): Promise<{ readonly navigation: TmNavigation }> {
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
        entries: [{ id: 'acme.overdue', labelKey: 'Acme_Overdue', url: (t) => `/${t}/customers?f.Overdue=eq:true`, requires: ['acme.Customer', 'Read'] }],
      }),
    ],
    languages: [tmEnglish(), ARABIC],
    strings: {
      en: { acme_Customer_Plural: 'Customers', Acme_Overdue: 'Overdue customers', Acme_Overdue_Synonyms: 'Late, , Arrears ,' },
      ar: { acme_Customer_Plural: 'العملاء', Acme_Overdue: 'العملاء المتأخرون' },
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
});
