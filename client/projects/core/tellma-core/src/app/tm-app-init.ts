// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import { DOCUMENT, type EnvironmentProviders, inject, provideAppInitializer, provideEnvironmentInitializer } from '@angular/core';
import { NavigationEnd, Router } from '@angular/router';

import { TM_BOOT_RESULT } from '../boot/tm-boot-result';
import { tmRoutedTenant } from '../launch/tm-routed-tenant';
import { TmClientMetadataStore } from '../metadata/tm-client-metadata';
import { TmTelemetry } from '../observe/tm-telemetry';
import { TmPageFocus } from '../routes/tm-page-focus';
import { TmSession } from '../session/tm-session';
import { TmClientStore } from '../state/store/tm-client-store';
import { TmUrls } from '../urls/tm-urls';
import type { TmAppConfig } from './tm-app-config';
import { TmPlatformText, type TmLoadedStrings } from './tm-platform-text';

// The startup sequence's initializers (spec 0024 §4.3), in parallel: the
// Core chunk; the active language's string pack and client tables, and
// English's; the session's cookie parse; and the client store's boot step,
// which the Core chunk's load never waits for. None awaits the API, so a warm
// launch has no network wait. A chunk or string load that fails shows the
// boot page `load-failed`, and the app never starts. Beside them, the router
// wiring: the routed tenant's `lastTenantId` and focus after navigation.

/** A promise that never settles: the app does not start under the boot page. */
const NEVER = new Promise<never>(() => undefined);

/**
 * The app's startup initializers and router wiring.
 *
 * @param config - The distribution's app configuration.
 * @returns The providers.
 */
export function tmStartup(config: TmAppConfig): EnvironmentProviders[] {
  return [
    provideAppInitializer(() => {
      const document = inject(DOCUMENT);
      const boot = inject(TM_BOOT_RESULT);
      const telemetry = inject(TmTelemetry);
      const metadata = inject(TmClientMetadataStore);
      const text = inject(TmPlatformText);
      const failed =
        (chunk: string) =>
        (error: unknown): Promise<never> => {
          telemetry.event('tm.boot.load-failed', { chunk });
          if (boot === null) {
            throw error;
          }
          boot.showPage('load-failed', () => document.location.reload());
          return NEVER;
        };

      // --- The canonical form: no trailing slash, before the first navigation reads the address ---
      const { pathname, search, hash } = document.location;
      if (pathname.length > 1 && pathname.endsWith('/')) {
        document.defaultView?.history.replaceState(document.defaultView.history.state, '', pathname.replace(/\/+$/, '') + search + hash);
      }

      // --- The Core chunk and the string packs of the active language and English ---
      const core = config.metadata.core().then((chunk) => metadata.set(chunk), failed('core'));
      const language = document.documentElement.lang || 'en';
      const strings = Promise.all(
        [...new Set([language, 'en'])].map(
          async (code): Promise<TmLoadedStrings> => ({
            pack: (await config.metadata.strings[code]?.()) ?? {},
            tables: await config.languages.find((offered) => offered.code === code)?.strings(),
          }),
        ),
      ).then((loaded) => text.ɵset(loaded), failed('strings'));

      // --- The session, and the store's boot step, which never rejects ---
      inject(TmSession);
      const started = inject(TmClientStore).ɵstart(tmRoutedTenant(document.location.pathname));
      return Promise.all([core, strings, started]).then(() => undefined);
    }),
    provideEnvironmentInitializer(() => {
      const router = inject(Router);
      const store = inject(TmClientStore);
      // Created now, so the routed tenant is known before the first page's links.
      inject(TmUrls);
      inject(TmPageFocus).ɵstart();
      // Entering a tenant route records it as the last tenant, for the next warm launch at `/`.
      router.events.subscribe((event) => {
        const tenantId = event instanceof NavigationEnd ? tmRoutedTenant(event.urlAfterRedirects) : null;
        if (tenantId !== null && store.boot.read().lastTenantId !== tenantId) {
          store.boot.patch({ lastTenantId: tenantId });
        }
      });
    }),
  ];
}
