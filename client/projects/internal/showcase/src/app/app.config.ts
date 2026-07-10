// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import { ApplicationConfig, provideBrowserGlobalErrorListeners } from '@angular/core';
import { provideRouter } from '@angular/router';

import { provideTellmaUi } from '@tellma/core-ui';
import { provideTellmaLocaleAr } from '@tellma/locale-ar';

import { routes } from './app.routes';

export const appConfig: ApplicationConfig = {
  providers: [
    provideBrowserGlobalErrorListeners(),
    provideRouter(routes),
    // The zero-config default path + the reference Arabic pack (its font
    // stylesheet rides the styles array in angular.json).
    provideTellmaUi(),
    provideTellmaLocaleAr(),
  ],
};
