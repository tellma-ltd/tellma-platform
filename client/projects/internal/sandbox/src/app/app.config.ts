import { ApplicationConfig, provideBrowserGlobalErrorListeners } from '@angular/core';
import { provideRouter } from '@angular/router';

import { provideTellmaUi } from '@tellma/core-ui';
import { provideTellmaLocaleAr } from '@tellma/locale-ar';

import { routes } from './app.routes';

export const appConfig: ApplicationConfig = {
  providers: [
    provideBrowserGlobalErrorListeners(),
    provideRouter(routes),
    // The zero-config default path (§5) + the reference Arabic pack (§7).
    provideTellmaUi(),
    provideTellmaLocaleAr(),
  ],
};
