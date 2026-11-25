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
  InjectionToken,
  Injector,
  provideAppInitializer,
  provideEnvironmentInitializer,
  type Type,
} from '@angular/core';
import { NavigationEnd, Router } from '@angular/router';

import { TmApi } from '../api/tm-api';
import { TM_UPDATES_LOAD } from '../api/tm-api-seams';
import { TmHttpApi } from '../api/tm-http-api';
import { TM_BOOT_RESULT } from '../boot/tm-boot-result';
import { TmPageContexts } from '../context/tm-page-contexts';
import { TmDeviceZone } from '../l10n/tm-device-zone';
import { TmLocalization } from '../l10n/tm-localization';
import { tmRoutedTenant } from '../launch/tm-routed-tenant';
import { TmClientMetadataStore } from '../metadata/tm-client-metadata';
import { TmPageVisits } from '../observe/tm-page-visits';
import { TmTelemetry, TmTelemetryFront } from '../observe/tm-telemetry';
import { TmAppRequestLog, TmRequestLog } from '../requests/tm-request-log';
import { TmPageFocus } from '../routes/tm-page-focus';
import { TmLaunchTimer } from '../routes/tm-route-host';
import { TmSession } from '../session/tm-session';
import { TmSessionPaths } from '../session/tm-session-paths';
import { TmPreferences } from '../state/prefs/tm-preferences';
import { TmClientStore } from '../state/store/tm-client-store';
import { TmSupport } from '../support/tm-support';
import { TmUrls } from '../urls/tm-urls';
import type { TmAppConfig } from './tm-app-config';

// The startup sequence's initializers (spec 0024 §4.3), in parallel: the
// Core chunk; the boot language's string pack and client tables, and
// English's; the session's cookie parse; and the client store's boot step,
// which the Core chunk's load never waits for. Once the store's launch
// documents are in, the language and calendar they resolve to load too, so
// the first frame shows them and never another first. None awaits the API,
// so a warm launch has no network wait. A chunk or boot-language load that
// fails shows the boot page `load-failed`, and the app never starts. Beside
// them, the router wiring: the routed tenant's `lastTenantId`, focus after
// navigation and the launch's timing; the session's cross-tab paths; after
// first render, the entry points of the recent pages, the leader election
// and the worker's updates; and at idle the Contracts chunk, so the first
// API call does not wait for it, the telemetry pipeline (spec 0026 §3),
// the load's paint metrics (§5.2), the device-zone check (spec 0024
// §7.8) and the support host (§12.1). The page visits are timed from the first navigation on (spec 0026
// §5.1), and Copy diagnostics' shortcut works from then (§8.3).

/** The recent pages' entry point, as far as the startup reads it. Internal. */
export interface TmRecentsModule {
  /** The service that counts visits once created. */
  readonly TmRecents: Type<unknown>;
}

/**
 * Loads the recent pages' entry point. Internal: the startup's seam, which a
 * test replaces. The specifier is cast so that the package build does not
 * report the import back as a cycle, as with the shell.
 */
export const TM_RECENTS_LOAD = new InjectionToken<() => Promise<TmRecentsModule>>('TM_RECENTS_LOAD', {
  providedIn: 'root',
  factory: () => () => import('@tellma/core/recents' as string) as Promise<TmRecentsModule>,
});

/** The leader election's entry point, as far as the startup reads it. Internal. */
export interface TmLeaderModule {
  /** The service that runs the election once created. */
  readonly ɵTmLeaderElection: Type<unknown>;
}

/**
 * Loads the leader election's entry point, or nothing, as in a test app,
 * which has no other tabs and no hub. Internal: the startup's seam, which
 * the state testing providers replace. The specifier is cast as the recent
 * pages' is.
 */
export const TM_LEADER_LOAD = new InjectionToken<() => Promise<TmLeaderModule | null>>('TM_LEADER_LOAD', {
  providedIn: 'root',
  factory: () => () => import('@tellma/core/leader' as string) as Promise<TmLeaderModule>,
});

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
      const telemetry = inject(TmTelemetry);
      // Created now, so the routed tenant is known before the first page's links,
      // and the preferences hear the other tabs' choices from the start.
      const urls = inject(TmUrls);
      inject(TmPreferences);
      const requests = inject(TmRequestLog);
      // Each telemetry call records what it sees where it is made, so a call
      // held until the pipeline loads keeps its tenant, page and requests.
      if (telemetry instanceof TmTelemetryFront) {
        const contexts = inject(TmPageContexts);
        telemetry.context = () => ({ tenantId: urls.tenantId(), page: contexts.current(), requests: requests.recent() });
      }
      const loadRecents = inject(TM_RECENTS_LOAD);
      const loadLeader = inject(TM_LEADER_LOAD);
      const loadUpdates = inject(TM_UPDATES_LOAD);
      // The other tabs' session messages are heard from the start.
      inject(TmSessionPaths).ɵstart();
      inject(TmPageFocus).ɵstart();
      inject(TmLaunchTimer).ɵstart();
      inject(TmPageVisits).ɵstart();
      // Copy diagnostics' shortcut, before the first page.
      if (requests instanceof TmAppRequestLog) {
        requests.ɵstart();
      }
      // After first render: the recent pages' visit count, the leader
      // election and the worker's updates; at idle, the Contracts chunk,
      // which the HTTP layer keeps, the telemetry pipeline, the paint metrics,
      // the device-zone check and the support host.
      // An app torn down before either moment starts nothing.
      let alive = true;
      inject(DestroyRef).onDestroy(() => (alive = false));
      afterNextRender(
        () => {
          // Recent pages count visits from the first page on (spec 0025 §13),
          // and the election revalidates a warm launch and connects the hub
          // (§7.2), neither of which the first frame waits for. Their entry
          // points load now, off the first frame. A failed load, or a service
          // that fails to start, goes without until the next launch: no
          // visits count, or the tab neither leads nor hears hub events, and
          // nothing else depends on either. The worker registers once the
          // app settles (spec 0024 §5.4), and the update banner shows from
          // then on; an update check loads the entry point itself.
          const start = <M>(load: () => Promise<M | null>, type: (loaded: M) => Type<unknown>, source: string): void => {
            load()
              .then((loaded) => {
                if (alive && loaded !== null) {
                  injector.get(type(loaded));
                }
              })
              .catch((error: unknown) => telemetry.error(error, { where: source }));
          };
          start(loadRecents, (recents) => recents.TmRecents, 'tm-recents');
          start(loadLeader, (leader) => leader.ɵTmLeaderElection, 'tm-leader');
          start(loadUpdates, (updates) => updates.TmUpdates, 'tm-updates');
          const load = (): void => {
            const api = alive ? injector.get(TmApi) : null;
            if (api instanceof TmHttpApi) {
              api.ɵprefetchContracts();
            }
            // The telemetry pipeline, which sends what the launch recorded.
            if (alive && telemetry instanceof TmTelemetryFront) {
              telemetry.start(injector);
            }
            // The load's paint metrics, observed with what the load buffered;
            // a chunk that fails to load sends no metrics.
            if (alive) {
              void import('../observe/tm-load-metrics').then(({ tmStartLoadMetrics }) => alive && tmStartLoadMetrics(injector), () => undefined);
            }
            // The device-zone check's chunk, which checks now and then on
            // each tenant entry and whenever the tab comes into view.
            if (alive) {
              injector.get(TmDeviceZone).ɵstart();
            }
            // The support host's chunk and the page snapshot, so the first
            // Report a problem opens its dialog in the press's frame.
            if (alive) {
              injector.get(TmSupport).ɵstart();
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
