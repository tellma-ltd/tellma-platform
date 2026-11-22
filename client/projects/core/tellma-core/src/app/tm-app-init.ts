// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import {
  afterNextRender,
  DestroyRef,
  DOCUMENT,
  type EnvironmentProviders,
  inject,
  Injector,
  provideAppInitializer,
  provideEnvironmentInitializer,
} from '@angular/core';
import { NavigationEnd, Router } from '@angular/router';

import { TmApi } from '../api/tm-api';
import { TmHttpApi } from '../api/tm-http-api';
import { TM_BOOT_RESULT } from '../boot/tm-boot-result';
import { TmLocalization } from '../l10n/tm-localization';
import { tmRoutedTenant } from '../launch/tm-routed-tenant';
import { TmClientMetadataStore } from '../metadata/tm-client-metadata';
import { TmTelemetry } from '../observe/tm-telemetry';
import { TmPageFocus } from '../routes/tm-page-focus';
import { TmLaunchTimer } from '../routes/tm-route-host';
import { TmSession } from '../session/tm-session';
import { TmClientStore } from '../state/store/tm-client-store';
import { TmUrls } from '../urls/tm-urls';
import type { TmAppConfig } from './tm-app-config';

// The startup sequence's initializers (spec 0024 §4.3), in parallel: the
// Core chunk; the boot language's string pack and client tables, and
// English's; the session's cookie parse; and the client store's boot step,
// which the Core chunk's load never waits for. Once the store's launch
// documents are in, the language and calendar they resolve to load too, so
// the first frame shows them and never another first. None awaits the API,
// so a warm launch has no network wait. A chunk or boot-language load that
// fails shows the boot page `load-failed`, and the app never starts. Beside them, the router
// wiring: the routed tenant's `lastTenantId`, focus after navigation and the
// launch's timing; and
// after first render, at idle, the Contracts chunk, so the first API call
// does not wait for it.

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
      const l10n = inject(TmLocalization);
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
      // The boot language is the one the boot script set on <html>, or the default when it is not offered.
      const lang = document.documentElement.lang;
      const language = config.languages.some((offered) => offered.code === lang) ? lang : (config.languages[0]?.code ?? 'en');
      const strings = Promise.all([...new Set([language, 'en'])].map((code) => l10n.ɵloadLanguage(code))).then(
        () => undefined,
        failed('strings'),
      );

      // --- The session, and the store's boot step, which never rejects ---
      inject(TmSession);
      const started = inject(TmClientStore).ɵstart(tmRoutedTenant(document.location.pathname));
      // --- Then what the launch documents resolve to: the user's language and calendar ---
      return Promise.all([core, strings, started]).then(() => l10n.ɵready());
    }),
    provideEnvironmentInitializer(() => {
      const router = inject(Router);
      const store = inject(TmClientStore);
      const injector = inject(Injector);
      const window = inject(DOCUMENT).defaultView;
      // Created now, so the routed tenant is known before the first page's links.
      inject(TmUrls);
      inject(TmPageFocus).ɵstart();
      inject(TmLaunchTimer).ɵstart();
      // After first render, at idle: the Contracts chunk, which the HTTP layer keeps.
      // An app torn down before its idle moment loads nothing.
      let alive = true;
      inject(DestroyRef).onDestroy(() => (alive = false));
      afterNextRender(
        () => {
          const load = (): void => {
            const api = alive ? injector.get(TmApi) : null;
            if (api instanceof TmHttpApi) {
              api.ɵprefetchContracts();
            }
          };
          if (window?.requestIdleCallback) {
            window.requestIdleCallback(load);
          } else {
            setTimeout(load);
          }
        },
        { injector },
      );
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
