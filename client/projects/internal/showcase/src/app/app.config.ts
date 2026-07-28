// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import {
  ApplicationConfig,
  computed,
  DestroyRef,
  inject,
  provideBrowserGlobalErrorListeners,
  signal,
} from '@angular/core';
import { provideHttpClient } from '@angular/common/http';
import { provideRouter } from '@angular/router';
import { TranslocoService } from '@jsverse/transloco';

import { provideTellmaUi, TM_ACTIVE_LOCALE } from '@tellma/core-ui';
import { provideTellmaLocaleAr } from '@tellma/locale-ar';

import { routes } from './app.routes';

export const appConfig: ApplicationConfig = {
  providers: [
    provideBrowserGlobalErrorListeners(),
    provideRouter(routes),
    // Backs tm-image's default TM_BLOB_FETCHER.
    provideHttpClient(),
    // The zero-config default path + the reference Arabic pack (its font
    // stylesheet rides the styles array in angular.json).
    provideTellmaUi(),
    provideTellmaLocaleAr(),
    // The formatting-locale seam, exercised the way a distribution would:
    // UI language tags are bare (en/ar), but formatting nominates REGIONAL
    // locales — bare 'ar' resolves to Latin digits in ICU, ar-SA to
    // Arabic-Indic ones, which is also what the locale-switch e2e asserts.
    {
      provide: TM_ACTIVE_LOCALE,
      useFactory: () => {
        const transloco = inject(TranslocoService);
        const lang = signal(transloco.getActiveLang());
        const langSub = transloco.langChanges$.subscribe((next) => lang.set(next));
        inject(DestroyRef).onDestroy(() => langSub.unsubscribe());
        return computed(() => (lang() === 'ar' ? 'ar-SA' : 'en-US'));
      },
    },
  ],
};
