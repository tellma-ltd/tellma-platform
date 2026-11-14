// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import { TestBed } from '@angular/core/testing';
import { TranslocoService } from '@jsverse/transloco';
import { firstValueFrom } from 'rxjs';

import { provideTellmaUi } from '../providers/provide-tellma-ui';
import type { TmUiTranslationTable, TmUiTranslationsLoader } from './tm-translations-loader';
import { TM_UI_I18N_SCOPE, TM_UI_TRANSLATE } from './tm-ui-translate';

/** What the app's loader serves per language: a server pack key, tmUi and the tm scope. */
const SERVED: Readonly<Record<string, TmUiTranslationTable>> = {
  en: {
    Customer_Name: 'Customer name',
    tmUi: { errors: { email: 'Enter an email address the server accepts' } },
    tm: { shell: { signOut: 'Sign out' } },
  },
  ar: {
    Customer_Name: 'اسم العميل',
    tmUi: { errors: { email: 'أدخل بريدا إلكترونيا صالحا' } },
    tm: { shell: { signOut: 'تسجيل الخروج' } },
  },
};

/** An Arabic locale pack's own tmUi strings, registered the way a pack does. */
const PACK_AR = { errors: { required: 'هذا الحقل مطلوب' } };

/** Sets up the library with a recording loader over {@link SERVED}. */
function setUp(): {
  transloco: TranslocoService;
  requests: Array<{ lang: string; scope: string | null }>;
} {
  const requests: Array<{ lang: string; scope: string | null }> = [];
  const loader: TmUiTranslationsLoader = async (request) => {
    requests.push(request);
    return SERVED[request.lang] ?? {};
  };
  TestBed.configureTestingModule({
    providers: [provideTellmaUi({ availableLangs: ['en', 'ar'], translations: loader })],
  });
  return { transloco: TestBed.inject(TranslocoService), requests };
}

/** Registers the Arabic pack exactly as a locale pack provider does. */
function registerPack(transloco: TranslocoService): void {
  transloco.setTranslation({ [TM_UI_I18N_SCOPE]: PACK_AR }, 'ar', { merge: true });
}

describe('the translations loader', () => {
  it('merges a loaded table over a pack registered before it', async () => {
    const { transloco } = setUp();
    registerPack(transloco);
    await firstValueFrom(transloco.load('ar'));

    expect(transloco.translate('tmUi.errors.required', {}, 'ar')).toBe('هذا الحقل مطلوب');
    expect(transloco.translate('tmUi.errors.email', {}, 'ar')).toBe('أدخل بريدا إلكترونيا صالحا');
    expect(transloco.translate('Customer_Name', {}, 'ar')).toBe('اسم العميل');
    expect(transloco.translate('tm.shell.signOut', {}, 'ar')).toBe('تسجيل الخروج');
  });

  it('keeps a pack registered after a load beside the loaded table', async () => {
    const { transloco } = setUp();
    await firstValueFrom(transloco.load('ar'));
    registerPack(transloco);

    expect(transloco.translate('tmUi.errors.required', {}, 'ar')).toBe('هذا الحقل مطلوب');
    expect(transloco.translate('tmUi.errors.email', {}, 'ar')).toBe('أدخل بريدا إلكترونيا صالحا');
    expect(transloco.translate('Customer_Name', {}, 'ar')).toBe('اسم العميل');
  });

  it("keeps the library's built-in English beside a loaded English table", async () => {
    const { transloco } = setUp();
    await firstValueFrom(transloco.load('en'));

    // The loaded key wins; every other built-in key survives the load.
    expect(transloco.translate('tmUi.errors.email', {}, 'en')).toBe(
      'Enter an email address the server accepts',
    );
    expect(transloco.translate('tmUi.errors.required', {}, 'en')).toBe('This field is required');
    expect(transloco.translate('Customer_Name', {}, 'en')).toBe('Customer name');
  });

  it('loads English through the same loader as the fallback of another language', async () => {
    const { transloco, requests } = setUp();
    await firstValueFrom(transloco.load('ar'));
    expect(requests).toEqual([
      { lang: 'ar', scope: null },
      { lang: 'en', scope: null },
    ]);
    // English is now loaded too, so a later switch to it requests nothing.
    await firstValueFrom(transloco.load('en'));
    expect(requests).toHaveLength(2);
  });

  it('passes a scoped load its language and scope apart', async () => {
    const { transloco, requests } = setUp();
    await firstValueFrom(transloco.load('reports/ar'));
    expect(requests).toEqual([
      { lang: 'ar', scope: 'reports' },
      { lang: 'en', scope: 'reports' },
    ]);
  });

  it('re-renders a resolved library string when its language loads', async () => {
    const { transloco } = setUp();
    const translate = TestBed.inject(TM_UI_TRANSLATE);
    transloco.setActiveLang('ar');
    const email = translate('errors.email');
    // Nothing for Arabic is loaded yet: the per-key English fallback shows.
    expect(email()).toBe('Enter a valid email address');

    // The app activates a language by loading it; the library never loads.
    await firstValueFrom(transloco.load('ar'));
    expect(email()).toBe('أدخل بريدا إلكترونيا صالحا');
  });

  it('never calls the loader on its own', () => {
    const { transloco, requests } = setUp();
    const translate = TestBed.inject(TM_UI_TRANSLATE);
    transloco.setActiveLang('ar');
    expect(translate('errors.required')()).toBe('This field is required');
    expect(requests).toEqual([]);
  });
});
