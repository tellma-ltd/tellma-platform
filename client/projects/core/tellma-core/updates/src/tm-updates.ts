// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import {
  ApplicationRef,
  computed,
  createEnvironmentInjector,
  DestroyRef,
  DOCUMENT,
  effect,
  EnvironmentInjector,
  inject,
  Injectable,
  InjectionToken,
  isDevMode,
  signal,
  untracked,
  type Signal,
} from '@angular/core';
import { provideServiceWorker, SwUpdate, type VersionEvent } from '@angular/service-worker';
import { TmIconRegistry } from '@tellma/core-ui/icon';
import {
  TM_NOW,
  TmBanners,
  type TmBannerRef,
  TmLocalization,
  type TmStringTree,
  TmTelemetry,
  ɵTM_API_HOST,
  ɵTmClientMetadataStore,
} from '@tellma/core';
import { RefreshCw } from 'lucide';

import { TM_WORKER_SCRIPT, tmRegisterWorker, tmWorkerScriptUrl, type TmTrustedTypes, type TmWorkerContainer } from './tm-worker-registration';

// The service worker's updates (spec 0024 §5.4, §5.5; spec 0028 §9.5). The
// app loads this entry point after its first render: it registers the
// worker, follows the worker's version events, checks for a newer version
// once the worker is registered, when the tab comes back into view after an
// hour or more and every 6 hours while it is visible, and shows the update
// banner while a newer version waits. Angular's `checkForUpdate()` resolves `false` for three different
// outcomes, so a check reads the event that arrives while it runs. A worker
// that already holds a newer version answers a check with "no new version"
// naming that version, which reads as ready: the event that announced it may
// have reached this tab before it listened.

/** What an update check found. */
export type TmUpdateCheck = 'ready' | 'none' | 'failed' | 'unreachable' | 'disabled';

/** The update service's English strings, under `tm.updates`. */
export const TM_UPDATES_STRINGS_EN = {
  banner: 'A new version is available.',
} as const satisfies TmStringTree;

/** How long a tab must stay hidden before coming back into view checks, in milliseconds: an hour. */
export const TM_UPDATE_HIDDEN_MS = 60 * 60 * 1000;

/** How often a visible tab checks, in milliseconds: every 6 hours. */
export const TM_UPDATE_INTERVAL_MS = 6 * 60 * 60 * 1000;

/** The service worker container, as far as the update service uses it. */
export interface TmUpdatesContainer extends TmWorkerContainer {
  /** The worker that controls the page, or `null`. */
  readonly controller: { postMessage(message: unknown): void } | null;
  /**
   * Listens for a change of the controlling worker.
   *
   * @param type - `controllerchange`.
   * @param listener - The listener.
   */
  addEventListener(type: 'controllerchange', listener: () => void): void;
  /**
   * Stops listening for a change of the controlling worker.
   *
   * @param type - `controllerchange`.
   * @param listener - The listener.
   */
  removeEventListener(type: 'controllerchange', listener: () => void): void;
}

/** Angular's update service, as far as the update service uses it. */
export type TmSwUpdates = Pick<SwUpdate, 'versionUpdates' | 'unrecoverable' | 'checkForUpdate'>;

/** The browser behind the update service. Internal: a test replaces it. */
export interface TmUpdatesHost {
  /** The page's service worker container, or `null` where the browser offers none. */
  readonly container: TmUpdatesContainer | null;
  /** Angular's update service over the worker, or `null` when no worker runs: in development, or without a container. */
  readonly updates: TmSwUpdates | null;
  /** The page's origin. */
  readonly origin: string;
  /** The browser's Trusted Types policy factory, or `undefined` without Trusted Types. */
  readonly trustedTypes: TmTrustedTypes | undefined;
  /** Loads the page again. */
  reload(): void;
}

/**
 * The browser behind the update service: the page's worker container with
 * Angular's update service over it, which runs only outside development,
 * as `provideServiceWorker`'s `enabled: !isDevMode()` would. Internal.
 */
export const TM_UPDATES_HOST = new InjectionToken<TmUpdatesHost>('TM_UPDATES_HOST', {
  providedIn: 'root',
  factory: (): TmUpdatesHost => {
    const window = inject(DOCUMENT).defaultView;
    const container = (window?.navigator.serviceWorker as TmUpdatesContainer | undefined) ?? null;
    let updates: TmSwUpdates | null = null;
    if (container !== null && !isDevMode()) {
      // Angular's worker client, in an injector of its own: its registration
      // initializer never runs there, since the update service registers
      // the worker through the Trusted Types policy instead.
      const injector = createEnvironmentInjector([provideServiceWorker(TM_WORKER_SCRIPT)], inject(EnvironmentInjector));
      inject(DestroyRef).onDestroy(() => injector.destroy());
      updates = injector.get(SwUpdate);
    }
    return {
      container,
      updates,
      origin: window?.location.origin ?? '',
      trustedTypes: (window as { trustedTypes?: TmTrustedTypes } | null)?.trustedTypes,
      reload: () => window?.location.reload(),
    };
  },
});

/**
 * The app's updates through its service worker: whether a worker controls
 * the page, the version it runs, a newer version ready to apply, and the
 * checks and reloads that apply it. The app starts it after its first
 * render; running tabs keep their version, and a reload or a new tab gets
 * the new one.
 *
 * @example
 * ```ts
 * const updates = inject(TmUpdates);
 * if ((await updates.check()) === 'ready') {
 *   updates.reload();
 * }
 * ```
 */
@Injectable({ providedIn: 'root' })
export class TmUpdates {
  readonly #host = inject(TM_UPDATES_HOST);
  readonly #telemetry = inject(TmTelemetry);
  readonly #now = inject(TM_NOW);
  /** The UI version this page runs, which the worker's version names. */
  readonly #ours = inject(ɵTmClientMetadataStore).peek()?.distribution.version ?? null;
  /** Whether a worker controls the page. */
  readonly #controlled = signal(this.#host.updates !== null && (this.#host.container?.controller ?? null) !== null);
  /** A newer version, ready to apply. */
  readonly #available = signal<string | null>(null);
  /** How many checks and updates are running. */
  readonly #running = signal(0);
  /** The check in progress, which another check joins. */
  #check: Promise<TmUpdateCheck> | null = null;

  /** Whether a service worker controls this page. */
  get enabled(): boolean {
    return untracked(this.#controlled);
  }

  /** The version the controlling worker serves this page, or `null` without one. */
  readonly version: Signal<string | null> = computed(() => (this.#controlled() ? this.#ours : null));

  /** A newer version, downloaded and ready to apply at the next reload, or `null`. */
  readonly available: Signal<string | null> = this.#available.asReadonly();

  /** Whether a check or an update is running. */
  readonly checking: Signal<boolean> = computed(() => this.#running() > 0);

  /** Starts following the worker: its registration, its events, the periodic checks and the banner. */
  constructor() {
    const host = this.#host;
    const updates = host.updates;
    const container = host.container;
    this.#banner();
    if (updates === null || container === null) {
      return;
    }
    const destroyRef = inject(DestroyRef);

    // --- Registration, through the Trusted Types policy, once the app settles ---
    // Then one check: a version the worker announced before this entry point
    // loaded reached no listener, and the worker's answer names it again.
    void tmRegisterWorker(
      container,
      inject(ApplicationRef).whenStable(),
      () => tmWorkerScriptUrl(host.origin, host.trustedTypes),
      () => this.#telemetry.event('tm.sw.register-failed'),
    ).then(() => this.check());
    // A worker that takes control of the page starts initializing at once,
    // as Angular's own registration has it do.
    const controllerChanged = (): void => {
      this.#controlled.set(container.controller !== null);
      container.controller?.postMessage({ action: 'INITIALIZE' });
    };
    container.addEventListener('controllerchange', controllerChanged);
    destroyRef.onDestroy(() => container.removeEventListener('controllerchange', controllerChanged));

    // --- The worker's events: a version ready, a failed download, a broken cache ---
    const events = updates.versionUpdates.subscribe((event) => {
      const ready = this.#readyVersion(event);
      if (ready !== null) {
        this.#ready(ready);
      } else if (event.type === 'VERSION_INSTALLATION_FAILED') {
        // The worker serves new loads from the network until the next check retries.
        this.#telemetry.event('tm.sw.install-failed');
      }
    });
    const unrecoverable = updates.unrecoverable.subscribe(() => {
      // The cache is broken: a reload at once, which drafts in session storage survive.
      this.#telemetry.event('tm.sw.unrecoverable');
      this.#telemetry.flush();
      this.reload();
    });
    destroyRef.onDestroy(() => {
      events.unsubscribe();
      unrecoverable.unsubscribe();
    });
    this.#schedule(destroyRef);
  }

  /**
   * Asks the worker for a newer version, and settles when its check does: `ready`
   * when a newer version is downloaded, `none` when there is none, `failed`
   * when it did not download, `unreachable` when the worker could not
   * reach the server, and `disabled` when no worker controls the page.
   *
   * @returns What the check found.
   */
  check(): Promise<TmUpdateCheck> {
    const updates = this.#host.updates;
    if (updates === null || !this.enabled) {
      return Promise.resolve('disabled');
    }
    if (untracked(this.#available) !== null) {
      return Promise.resolve('ready');
    }
    return (this.#check ??= this.#checkNow(updates).finally(() => (this.#check = null)));
  }

  /**
   * Checks, then reloads into the new version when one is ready, or when
   * no worker is in the way of a plain reload.
   *
   * @returns What the check found; the page is reloading when it is `ready` or `disabled`.
   */
  async update(): Promise<TmUpdateCheck> {
    this.#running.update((count) => count + 1);
    try {
      const result = await this.check();
      if (result === 'ready' || result === 'disabled') {
        this.reload();
      }
      return result;
    } finally {
      this.#running.update((count) => count - 1);
    }
  }

  /** Loads the page again; unsaved drafts survive in session storage. */
  reload(): void {
    this.#host.reload();
  }

  /** One check: the worker's answer, read from the event that arrives while it runs. */
  async #checkNow(updates: TmSwUpdates): Promise<TmUpdateCheck> {
    this.#running.update((count) => count + 1);
    let seen: TmUpdateCheck | null = null;
    const subscription = updates.versionUpdates.subscribe((event) => {
      if (this.#readyVersion(event) !== null) {
        seen = 'ready';
      } else if (event.type === 'NO_NEW_VERSION_DETECTED') {
        seen ??= 'none';
      } else if (event.type === 'VERSION_INSTALLATION_FAILED') {
        seen = 'failed';
      }
    });
    try {
      const found = await updates.checkForUpdate();
      // `false` with no event: the worker could not fetch the manifest.
      return found || untracked(this.#available) !== null ? 'ready' : (seen ?? 'unreachable');
    } catch {
      // The worker could not be asked.
      return seen ?? 'unreachable';
    } finally {
      subscription.unsubscribe();
      this.#running.update((count) => count - 1);
    }
  }

  /**
   * The newer version an event reports ready, or `null`: a version ready,
   * or "no new version" that names a version other than this page's, which
   * the worker downloaded before this tab listened.
   */
  #readyVersion(event: VersionEvent): string | null {
    if (event.type === 'VERSION_READY') {
      return versionOf(event.latestVersion);
    }
    if (event.type === 'NO_NEW_VERSION_DETECTED') {
      const name = appVersionOf(event.version);
      return this.#ours !== null && name !== null && name !== this.#ours ? name : null;
    }
    return null;
  }

  /** Records a newer version, once, and reports it. */
  #ready(version: string): void {
    if (untracked(this.#available) !== version) {
      this.#available.set(version);
      this.#telemetry.event('tm.sw.update-ready', { fromVersion: this.#ours ?? '' });
    }
  }

  /**
   * The checks the update service runs itself: when the tab comes back
   * into view after an hour or more, and every 6 hours while it is visible.
   */
  #schedule(destroyRef: DestroyRef): void {
    const host = inject(ɵTM_API_HOST);
    let hiddenAt: number | null = host.visible() ? null : this.#now();
    let timer: ReturnType<typeof setTimeout> | undefined;
    // The 6-hour timer runs while the tab is visible, from its last check.
    const arm = (): void => {
      clearTimeout(timer);
      timer = host.visible()
        ? setTimeout(() => {
            void this.check();
            arm();
          }, TM_UPDATE_INTERVAL_MS)
        : undefined;
    };
    const stop = host.listen('document', 'visibilitychange', () => {
      if (!host.visible()) {
        hiddenAt = this.#now();
        clearTimeout(timer);
        return;
      }
      if (hiddenAt !== null && this.#now() - hiddenAt >= TM_UPDATE_HIDDEN_MS) {
        void this.check();
      }
      hiddenAt = null;
      arm();
    });
    arm();
    destroyRef.onDestroy(() => {
      stop();
      clearTimeout(timer);
    });
  }

  /**
   * The update banner: it shows while a newer version
   * waits, dismissible for that version alone, with Reload.
   */
  #banner(): void {
    const l10n = inject(TmLocalization);
    const banners = inject(TmBanners);
    l10n.ɵregisterStrings('updates', TM_UPDATES_STRINGS_EN);
    // The glyph joins the platform's icons here, unless a feature drew its own.
    const icons = inject(TmIconRegistry);
    if (icons.get('refresh-cw') === undefined) {
      icons.register({ 'refresh-cw': RefreshCw });
    }
    let ref: TmBannerRef | null = null;
    effect(() => {
      const version = this.#available();
      untracked(() => {
        if (version === null) {
          ref?.close();
          ref = null;
          return;
        }
        ref = banners.ɵshow(
          {
            id: 'tm.update',
            level: 3,
            tone: 'info',
            icon: 'refresh-cw',
            message: l10n.ɵtext('tm.updates.banner'),
            action: { label: l10n.ɵtext('tm.errors.reload'), run: () => this.reload() },
            dismissible: true,
          },
          version,
        );
      });
    });
  }
}

/** A worker version's `appData.version`, the UI version its build wrote, or `null` without one. */
function appVersionOf(version: { readonly appData?: object }): string | null {
  const name = (version.appData as { readonly version?: unknown } | undefined)?.version;
  return typeof name === 'string' && name !== '' ? name : null;
}

/** A worker version's name: its UI version, else its manifest hash. */
function versionOf(version: { readonly hash: string; readonly appData?: object }): string {
  return appVersionOf(version) ?? version.hash;
}
