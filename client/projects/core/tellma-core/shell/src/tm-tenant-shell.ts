// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import { NgComponentOutlet } from '@angular/common';
import {
  afterNextRender,
  ChangeDetectionStrategy,
  Component,
  computed,
  effect,
  inject,
  signal,
  untracked,
  ViewContainerRef,
  viewChild,
} from '@angular/core';
import { RouterOutlet } from '@angular/router';
import { TmApiError, type TmApiErrorKind, TmLaunchCache, TmLocalization, TmShellMain, TmShortcuts, TmTelemetry, TmUrls } from '@tellma/core';
import { TmArrivals } from '@tellma/core/attention';
import { TmButton } from '@tellma/core-ui/button';

import { TmAppBar } from './app-bar/tm-app-bar';
import { TmInstalledMode } from './app-bar/tm-installed-mode';
import { TmShellIdentity } from './app-bar/tm-shell-identity';
import { TmShellOverlays } from './app-bar/tm-shell-overlays';
import { TmShellSlots } from './app-bar/tm-shell-slots';
import { TmShellFrame } from './frame/tm-shell-frame';
import { TmLauncher } from './launcher/tm-launcher';
import { TmPinnedBar } from './pins/tm-pinned-bar';
import { TmPins } from './pins/tm-pins';
import { TmPinsUi } from './pins/tm-pins-ui';
import { TM_SHELL_ERROR_VIEWS } from './tm-shell-errors';
import { tmRegisterTenantShellStrings } from './tm-tenant-shell-strings';

// The tenant shell (spec 0027 §7, §7.5): the `:t` route's component, the
// shell frame around the tenant's pages, with the app bar and the banner row
// in its header and, in its `main`, one of three things:
//
// - the routed page, through the route host, which draws its own skeleton
//   while a cold launch's sync runs (spec 0024 §4.4);
// - the error state of a cold sync that failed, from the lazy error views,
//   with Retry, which runs the sync again (spec 0025 §3.2): the route host
//   keeps waiting for the documents meanwhile, so the page renders once a
//   retry or any later sync brings them. A `client-outdated` failure gives
//   the error views' own Reload or Try again;
// - what TmShellMain shows instead of the route host, such as spec 0024
//   §11's tenant-state page, until the next path change restores it.
//
// The app bar and the pinned bar render from the launch documents, so a
// warm launch's first frame is complete with no request; the pinned bar's
// row is open unless the user hid the bar (spec 0027 §8.11), so a cold
// launch's skeleton already has the page's geometry, with three chip
// skeletons in the row while the sync is pending and nothing in it once the
// sync failed (spec 0027 §7.5). A tenant switch closes
// the shell's overlays, which end with the shell, and clears the launcher's
// text. The shell registers the launcher's, the Shortcuts dialog's and the
// pin control's keys and starts the arrival engine (spec 0028 §8.2).

/**
 * The failures another part of the app answers: the tab's own cancel, the
 * session's end (a sign-in), and a tenant that is gone or unavailable (the
 * error presenter's picker notice or tenant-state page).
 */
const ELSEWHERE: ReadonlySet<TmApiErrorKind> = new Set(['cancelled', 'unauthenticated', 'tenant-not-found', 'tenant-unavailable']);

/** The tenant shell: the app bar, the banner row, and the routed tenant page, its error state or its replacement in the main landmark. */
@Component({
  selector: 'tm-tenant-shell',
  imports: [NgComponentOutlet, RouterOutlet, TmAppBar, TmButton, TmPinnedBar, TmShellFrame],
  providers: [TmInstalledMode, TmShellIdentity, TmShellOverlays, TmShellSlots, TmPinsUi],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <tm-shell-frame [pinnedBar]="pinnedBar()">
      <!-- The app bar, in the frame's first row, and the pinned bar in its third. -->
      <tm-app-bar tmShellBar />
      @if (pinnedBar()) {
        <tm-pinned-bar tmShellPinnedBar [failed]="failure() !== null" />
      }
      @if (failure() !== null) {
        <!-- A cold sync failed: the error state, with Retry; a line of its own while the error views cannot load. -->
        <div class="tm-tenant-shell__error">
          <ng-container #errorSlot />
          @if (errorViewsMissing() || failure()?.error === null) {
            <p class="tm-tenant-shell__failed">
              {{ failedLabel() }}
              <button tmButton type="button" size="sm" (click)="retry()">{{ retryLabel() }}</button>
            </p>
          }
        </div>
      } @else if (override(); as override) {
        <!-- What main shows instead of the route host. -->
        <ng-container [ngComponentOutlet]="override.component" [ngComponentOutletInputs]="override.inputs ?? {}" />
      } @else {
        <!-- The routed page. -->
        <router-outlet />
      }
    </tm-shell-frame>
    <!-- Where the pinned bar's deferred overlays render, outside the landmarks. -->
    <ng-container #pinOverlays />
  `,
  styles: `
    .tm-tenant-shell__failed {
      display: flex;
      align-items: center;
      justify-content: center;
      gap: var(--space-2);
      margin: var(--space-6) var(--space-4);
      color: var(--text-muted);
      font-size: var(--text-sm);
    }
  `,
})
export class TmTenantShell {
  readonly #cache = inject(TmLaunchCache);
  readonly #telemetry = inject(TmTelemetry);
  readonly #l10n = inject(TmLocalization);
  readonly #urls = inject(TmUrls);
  /** Where the error state renders. */
  private readonly errorSlot = viewChild('errorSlot', { read: ViewContainerRef });
  /** Where the pinned bar's overlays render. */
  private readonly pinOverlays = viewChild.required('pinOverlays', { read: ViewContainerRef });
  // The last cold entry's failure, `{ error: null }` for one that is not an
  // API error; it shows only while the documents are missing.
  readonly #failure = signal<{ readonly error: TmApiError | null } | null>(null);
  readonly #errorViewsMissing = signal(false);
  // Counts entries, so only the latest one's failure shows.
  #entries = 0;

  /** The failure of the cold sync the shell is waiting on, or `null`. */
  protected readonly failure = computed(() => (this.#cache.warm() ? null : this.#failure()));

  /** Whether the error views failed to load, so the shell shows its own line. */
  protected readonly errorViewsMissing = this.#errorViewsMissing.asReadonly();

  /** Whether the pinned bar shows, as the user chose. */
  protected readonly pinnedBar = inject(TmPins).visible;

  /** What main shows instead of the route host, or `null`. */
  protected readonly override = inject(TmShellMain).override;

  /** The line a failed cold sync shows while the error views cannot load. */
  protected readonly failedLabel = this.#l10n.ɵtext('tm.tenantShell.failed');

  /** Try again's label. */
  protected readonly retryLabel = this.#l10n.ɵtext('tm.errors.retry');

  /** Creates the shell, which follows the routed tenant's cold entry and starts the arrival engine. */
  constructor() {
    tmRegisterTenantShellStrings(this.#l10n);
    inject(TmArrivals);
    const overlays = inject(TmShellOverlays);
    const launcher = inject(TmLauncher);
    const pins = inject(TmPinsUi);
    afterNextRender(() => pins.attach(this.pinOverlays()));

    // --- Entering a tenant: the cold entry's failure, and a switch's clean slate ---
    // No route survives a change of tenant, so a switch creates a new shell:
    // the old one's overlays end with it, and the launcher, which outlives
    // both, clears the text typed in the old tenant.
    effect(() => {
      const tenantId = this.#urls.tenantId();
      untracked(() => {
        if (tenantId !== null) {
          launcher.ɵenterTenant(tenantId);
        }
        this.#enter(tenantId);
      });
    });

    // --- The launcher's, the Shortcuts dialog's and the pin control's keys ---
    // Registered for this shell's life: outside a tenant, the tenant picker
    // takes Mod+K for its own search.
    const shortcuts = inject(TmShortcuts);
    shortcuts.register({
      id: 'tm.launcher.open',
      keys: 'Mod+K',
      labelKey: 'tm.tenantShell.bar.search',
      scope: 'global',
      run: () => launcher.show('shortcut'),
    });
    shortcuts.register({
      id: 'tm.shortcuts.open',
      keys: 'Mod+/',
      labelKey: 'tm.shellOverlays.dialogs.shortcuts',
      scope: 'global',
      run: () => overlays.run((panels) => panels.openDialog('shortcuts')),
    });
    shortcuts.register({
      id: 'tm.pins.toggle',
      keys: 'Alt+Shift+P',
      labelKey: 'tm.pins.toggle',
      scope: 'global',
      run: () => pins.press('shortcut', null),
    });

    // --- The error state, from the lazily loaded error views ---
    const errorViews = inject(TM_SHELL_ERROR_VIEWS);
    effect((onCleanup) => {
      const slot = this.errorSlot();
      const error = this.failure()?.error ?? null;
      if (slot === undefined || error === null) {
        return;
      }
      let live = true;
      onCleanup(() => {
        live = false;
        slot.clear();
        this.#errorViewsMissing.set(false);
      });
      errorViews().then(
        ({ TmErrorPresenter, TmErrorState }) => {
          if (live) {
            const view = slot.injector.get(TmErrorPresenter).describe(error, { kind: 'read' });
            const state = slot.createComponent(TmErrorState);
            state.setInput('view', view);
            state.instance.retry.subscribe(() => this.retry());
          }
        },
        (error: unknown) => {
          // Offline, or after a deploy removed the chunk: say so, and offer Try again.
          this.#telemetry.error(error, { source: 'tm-tenant-shell' });
          if (live) {
            this.#errorViewsMissing.set(true);
          }
        },
      );
    });
  }

  /** Runs the routed tenant's sync again, as Retry does. */
  protected retry(): void {
    this.#enter(this.#urls.tenantId());
  }

  /**
   * Joins the routed tenant's entry, or starts one: a stored tenant's entry
   * runs no request. A failed cold entry's error shows until the documents
   * arrive; a newer entry's outcome replaces it.
   */
  #enter(tenantId: number | null): void {
    const entry = ++this.#entries;
    this.#failure.set(null);
    if (tenantId === null) {
      return;
    }
    this.#cache.ɵenter(tenantId).catch((error: unknown) => {
      if (entry !== this.#entries) {
        return;
      }
      if (!(error instanceof TmApiError)) {
        // The sync reported it already (or the store, for a refused write).
        this.#failure.set({ error: null });
      } else if (!ELSEWHERE.has(error.kind)) {
        this.#failure.set({ error });
      }
    });
  }
}
