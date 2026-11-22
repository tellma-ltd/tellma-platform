// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import type { TmLanguageMetadata } from '../metadata/tm-client-metadata';
import type { TmDigitStyle } from '../state/launch/tm-launch-docs';
import {
  tmBrowserResolves,
  tmContentSlot,
  tmFormattingLocale,
  tmLocaleString,
  tmResolveCalendarCode,
  tmResolveLanguage,
  tmResolveRegion,
  tmSwitchLanguages,
} from './tm-locale-resolution';

// The resolution rules over Acme's languages: English (default region GB)
// and Arabic (default region SA, native digits arab).

const ENGLISH: TmLanguageMetadata = {
  code: 'en',
  nativeName: 'English',
  dir: 'ltr',
  region: 'GB',
  regions: ['GB', 'AE', 'US', 'ZZ'],
  nativeDigits: null,
};

const ARABIC: TmLanguageMetadata = {
  code: 'ar',
  nativeName: 'العربية',
  dir: 'rtl',
  region: 'SA',
  regions: ['SA', 'AE', 'EG'],
  nativeDigits: 'arab',
};

const OFFERED = ['en', 'ar'];

describe('the locale resolution', () => {
  describe('the language', () => {
    it("takes the user's preference in a tenant, then the boot record's language, then the tenant's primary", () => {
      expect(tmResolveLanguage({ offered: OFFERED, bootLang: 'en', tenant: { preferred: 'ar', contentLanguages: ['en'] } })).toBe('ar');
      expect(tmResolveLanguage({ offered: OFFERED, bootLang: 'ar', tenant: { preferred: null, contentLanguages: ['en'] } })).toBe('ar');
      expect(tmResolveLanguage({ offered: OFFERED, bootLang: 'fr', tenant: { contentLanguages: ['ar-SA', 'en'] } })).toBe('ar');
    });

    it("takes the boot record's language outside a tenant, and the default when nothing is offered", () => {
      expect(tmResolveLanguage({ offered: OFFERED, bootLang: 'ar' })).toBe('ar');
      expect(tmResolveLanguage({ offered: OFFERED, bootLang: 'fr' })).toBe('en');
      expect(tmResolveLanguage({ offered: OFFERED, bootLang: 'fr', tenant: { preferred: 'de', contentLanguages: ['fr'] } })).toBe('en');
    });

    it("offers every language outside a tenant, and the tenant's offered content languages plus the active one in it", () => {
      expect(tmSwitchLanguages(OFFERED, 'en', undefined)).toEqual(['en', 'ar']);
      expect(tmSwitchLanguages(OFFERED, 'en', ['ar', 'fr'])).toEqual(['ar', 'en']);
      expect(tmSwitchLanguages(OFFERED, 'ar', ['ar', 'en'])).toEqual(['ar', 'en']);
    });

    it("reads the content slot of the active language's position among the content languages, else slot 1", () => {
      expect(tmContentSlot('ar', ['en', 'ar'])).toBe(2);
      expect(tmContentSlot('ar', ['en', 'fr', 'ar-SA'])).toBe(3);
      expect(tmContentSlot('en', ['ar', 'en'])).toBe(2);
      expect(tmContentSlot('fr', ['ar', 'en'])).toBe(1);
      expect(tmContentSlot('ar', undefined)).toBe(1);
    });
  });

  describe('the calendar', () => {
    it("takes the user's calendar when the tenant has it, else the tenant's primary, and gc outside a tenant", () => {
      expect(tmResolveCalendarCode(['gc', 'uq'], 'uq')).toBe('uq');
      expect(tmResolveCalendarCode(['uq', 'gc'], null)).toBe('uq');
      expect(tmResolveCalendarCode(['gc', 'uq'], 'et')).toBe('gc');
      expect(tmResolveCalendarCode(undefined, 'uq')).toBe('gc');
    });
  });

  describe('the regional formats', () => {
    // Spec 0024 §7.7's example table, row by row.
    const rows: readonly (readonly [TmLanguageMetadata, string | null, string | null, TmDigitStyle | null, string])[] = [
      [ARABIC, null, null, null, 'ar-SA'],
      [ARABIC, null, 'AE', null, 'ar-AE'],
      [ARABIC, null, null, 'Latin', 'ar-SA-u-nu-latn'],
      [ARABIC, 'AE', 'SA', 'Native', 'ar-AE-u-nu-arab'],
      [ENGLISH, 'SA', null, 'Latin', 'en-GB'],
      [ENGLISH, 'AE', null, null, 'en-AE'],
    ];

    for (const [language, user, tenant, digits, locale] of rows) {
      it(`resolves ${language.code} with user region ${user ?? '—'}, tenant region ${tenant ?? '—'} and digits ${digits ?? '—'} to ${locale}`, () => {
        const region = tmResolveRegion(language, [user, tenant])!;

        expect(tmLocaleString(`${language.code}-${region}`, digits, language.nativeDigits)).toBe(locale);
        expect(tmFormattingLocale(language, region, digits, ENGLISH)).toEqual({ locale, fellBack: false });
      });
    }

    it('takes the default region outside a tenant, and none before the client metadata loads', () => {
      expect(tmResolveRegion(ARABIC, [])).toBe('SA');
      expect(tmResolveRegion(undefined, ['AE'])).toBeUndefined();
    });

    it("adds no digits extension that the region's default already has", () => {
      expect(tmLocaleString('ar-SA', 'Native', 'arab')).toBe('ar-SA');
      expect(tmLocaleString('en-GB', 'Native', null)).toBe('en-GB');
      expect(tmLocaleString('en-GB', 'Latin', null)).toBe('en-GB');
    });
  });

  describe('the browser check', () => {
    it("falls back to the language's default region when the browser lacks the region", () => {
      expect(tmBrowserResolves('en-ZZ')).toBe(false);
      expect(tmFormattingLocale(ENGLISH, 'ZZ', null, ENGLISH)).toEqual({ locale: 'en-GB', fellBack: true });
    });

    it("falls back to the default language's locale when the browser lacks the language, with that locale's digits", () => {
      const unknown: TmLanguageMetadata = { ...ARABIC, code: 'qaa', region: 'SA', regions: ['SA'] };

      expect(tmFormattingLocale(unknown, 'SA', 'Native', ENGLISH)).toEqual({ locale: 'en-GB', fellBack: true });
    });

    it('keeps the digits choice on the fallback tag', () => {
      const arabic: TmLanguageMetadata = { ...ARABIC, regions: ['SA', 'ZZ'] };

      expect(tmFormattingLocale(arabic, 'ZZ', 'Latin', ENGLISH)).toEqual({ locale: 'ar-SA-u-nu-latn', fellBack: true });
    });
  });
});
