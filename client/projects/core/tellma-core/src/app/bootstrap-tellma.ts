// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import {
  type ApplicationConfig,
  type ApplicationRef,
  isDevMode,
  provideBrowserGlobalErrorListeners,
  provideZonelessChangeDetection,
} from '@angular/core';
import { bootstrapApplication } from '@angular/platform-browser';

import type { TmBootResult } from '../boot/tm-boot-types';
import { TmApp } from './tm-app';

// The app's start (spec 0024 §2.2, §4.3). The boot script decides before
// Angular whether the app runs: it may leave for sign-in or show the boot
// page instead. Only on its `run` does the app start, zoneless, with the
// distribution's providers.

/**
 * Starts the Tellma app in the `tm-app` element: zoneless, with the
 * browser's global error listeners and the distribution's providers. The
 * returned promise never settles unless the boot script decided to run the
 * app; while it leaves for sign-in or shows the boot page, nothing starts.
 *
 * @param appConfig - The distribution's configuration, with `provideTellma` among its providers.
 * @returns The started application, once its first render is ready.
 *
 * @example
 * ```ts
 * // src/main.ts
 * bootstrapTellma(appConfig).catch((error: unknown) => console.error(error));
 * ```
 */
export function bootstrapTellma(appConfig: ApplicationConfig): Promise<ApplicationRef> {
  const boot = (globalThis as { readonly __tmBoot?: TmBootResult }).__tmBoot;
  if (boot?.state !== 'run') {
    if (boot === undefined && isDevMode()) {
      console.error('The Tellma app did not start: no boot script ran. index.html must load tm-boot.js before the app.');
    }
    return new Promise(() => undefined);
  }
  return bootstrapApplication(TmApp, {
    providers: [provideZonelessChangeDetection(), provideBrowserGlobalErrorListeners(), ...appConfig.providers],
  });
}
