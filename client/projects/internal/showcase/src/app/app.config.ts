// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import {
  ApplicationConfig,
  DOCUMENT,
  inject,
  provideAppInitializer,
  provideBrowserGlobalErrorListeners,
} from '@angular/core';
import { provideRouter } from '@angular/router';

import { fontPreloadLinks, provideTellmaUi, TM_FONT_SUBSETS } from '@tellma/core-ui';
import { provideTellmaLocaleAr } from '@tellma/locale-ar';

import { routes } from './app.routes';

export const appConfig: ApplicationConfig = {
  providers: [
    provideBrowserGlobalErrorListeners(),
    provideRouter(routes),
    // The zero-config default path (§5) + the reference Arabic pack (§7).
    provideTellmaUi(),
    provideTellmaLocaleAr(),
    // The distribution-shell job §7.1 describes: resolve the tenant's
    // preloads from the merged manifest and inject them. The showcase's
    // default locale is English, so only the Latin subsets preload; Arabic
    // fetches on demand via unicode-range when its glyphs first render.
    provideAppInitializer(() => {
      const doc = inject(DOCUMENT);
      for (const link of fontPreloadLinks(inject(TM_FONT_SUBSETS), ['en'])) {
        const el = doc.createElement('link');
        el.rel = link.rel;
        el.href = link.href;
        el.as = link.as;
        el.type = link.type;
        el.crossOrigin = link.crossorigin;
        doc.head.appendChild(el);
      }
    }),
  ],
};
