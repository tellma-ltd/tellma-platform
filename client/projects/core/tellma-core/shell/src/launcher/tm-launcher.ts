// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import {
  type ComponentRef,
  computed,
  DestroyRef,
  DOCUMENT,
  EnvironmentInjector,
  inject,
  Injectable,
  InjectionToken,
  type Signal,
  signal,
  untracked,
} from '@angular/core';
import { TmTelemetry } from '@tellma/core';

// The launcher (spec 0027 §9): the Menu button, the app bar's search field,
// the search buttons and Mod+K open it, and it keeps its text across close
// and navigation in this tab. This service is the part the first frame
// carries: the state, the kept text and the calls. The surface itself, the
// overlay panel, its index and its keyboard, is a deferred chunk that loads
// when the browser is first idle after the shell renders, and on the first
// hover or focus of a control that opens it, so it is in memory by the time
// a click asks for it and opens in the click's frame. An open that beats the
// chunk shows the app bar's skeleton until the chunk lands. The text belongs
// to the tenant it was typed in: a tenant switch, through /tenants or a deep
// link, clears it.

/** What opened the launcher, as `tm.launcher.opened` reports it: `link` is an in-page link to it. */
export type TmLauncherVia = 'menu' | 'field' | 'shortcut' | 'icon' | 'link';

/** Why the launcher closed. */
export type TmLauncherCloseReason = 'escape' | 'outside' | 'tab' | 'chosen' | 'route';

/** The launcher's surface, as its deferred chunk implements it. Internal. */
export interface TmLauncherSurface {
  /** The id of its listbox, which the input controls. */
  readonly listboxId: string;
  /** The id of its hidden instructions, which describe the input. */
  readonly hintId: string;
  /** The id of the active option, or `null`; it reads signals, so a binding follows it. */
  readonly activeId: Signal<string | null>;
  /** Whether the app bar's search field is the surface's input, as it is from `lg` up. */
  readonly usesField: Signal<boolean>;
  /** Whether an open waits for a piece of the surface still loading, so the app bar's skeleton shows. */
  readonly waiting: Signal<boolean>;
  /**
   * Shows the surface, or, when it is open, answers another open: it focuses
   * the input and selects the text.
   *
   * @param via - What opened it.
   * @param reopened - Whether it was already open.
   * @param returnTo - Where focus goes back on Escape or an outside click.
   */
  open(via: TmLauncherVia, reopened: boolean, returnTo: HTMLElement | null): void;
  /**
   * Hides the surface.
   *
   * @param reason - Why it closed.
   */
  close(reason: TmLauncherCloseReason): void;
}

/** What the launcher's deferred chunks give it. Internal. */
export interface TmLauncherChunk {
  /**
   * Creates the surface, closed, at the end of the body, with the icons it
   * draws registered.
   *
   * @param injector - The app's injector.
   * @returns The surface's component.
   */
  create(injector: EnvironmentInjector): ComponentRef<TmLauncherSurface>;
}

/**
 * Loads the launcher's chunks: the panel, and beside it the icons it draws,
 * which are icon data rather than the panel's code, and the recent pages'
 * entry point, which the app has loaded by then. Internal: a test delays the
 * load or makes it fail.
 */
export const TM_LAUNCHER_LOAD = new InjectionToken<() => Promise<TmLauncherChunk>>('TM_LAUNCHER_LOAD', {
  providedIn: 'root',
  factory: () => () =>
    Promise.all([import('./tm-launcher-panel'), import('./tm-launcher-icons'), import('@tellma/core/recents')]).then(
      ([panel, icons, recents]) => ({
        create: (injector) => panel.tmCreateLauncherSurface(injector, icons.tmRegisterLauncherIcons, recents.TmRecents),
      }),
    ),
});

/**
 * The launcher: its state, its kept text, and the calls that open and close
 * it. A root service; the app bar's controls and `Mod+K` open it, and a page
 * may open it from an in-page link.
 *
 * @example
 * ```ts
 * const launcher = inject(TmLauncher);
 * launcher.setQuery('invoices');
 * launcher.show('link');
 * ```
 */
@Injectable({ providedIn: 'root' })
export class TmLauncher {
  readonly #load = inject(TM_LAUNCHER_LOAD);
  readonly #injector = inject(EnvironmentInjector);
  readonly #telemetry = inject(TmTelemetry);
  readonly #document = inject(DOCUMENT);
  readonly #query = signal('');
  readonly #open = signal(false);
  readonly #surface = signal<TmLauncherSurface | null>(null);
  #chunk: Promise<TmLauncherChunk | null> | null = null;
  #ref: ComponentRef<TmLauncherSurface> | null = null;
  #ended = false;
  // While the surface gives focus back to a control, a focus there opens nothing.
  #restoring = false;
  // The open that waits for the chunk: what asked for it, and where focus goes back.
  #pending: { readonly via: TmLauncherVia; readonly returnTo: HTMLElement | null } | null = null;
  // The tenant the text was typed in, or `null` before any.
  #tenantId: number | null = null;

  /** Whether the launcher is closed, browsing, or listing matches for typed text. */
  readonly state: Signal<'closed' | 'browse' | 'typed'> = computed(() =>
    !this.#open() ? 'closed' : this.#query().trim() === '' ? 'browse' : 'typed',
  );

  /** The launcher's text, kept across close and navigation in this tab. */
  readonly query: Signal<string> = this.#query.asReadonly();

  /** The surface once its chunk is in, else `null`. Internal: the app bar's field reads its ids. */
  readonly ɵsurface: Signal<TmLauncherSurface | null> = this.#surface.asReadonly();

  /** Whether the launcher is open before its surface can show, so the app bar shows its skeleton. Internal. */
  readonly ɵpending: Signal<boolean> = computed(() => this.#open() && (this.#surface()?.waiting() ?? true));

  /** The app bar's search field, the input from `lg` up; `null` before the bar renders. Internal. */
  ɵfield: HTMLInputElement | null = null;

  /** Creates the service, whose surface ends with the app. */
  constructor() {
    inject(DestroyRef).onDestroy(() => {
      this.#ended = true;
      this.#ref?.destroy();
    });
  }

  /**
   * Opens the launcher with its kept text, or, when it is open, focuses its
   * input and selects the text.
   *
   * @param via - What opened it.
   */
  show(via: TmLauncherVia): void {
    if (this.#restoring) {
      return;
    }
    const reopened = untracked(this.#open);
    const active = this.#document.activeElement;
    const returnTo = active instanceof HTMLElement && active !== this.#document.body ? active : null;
    if (!reopened) {
      this.#open.set(true);
      this.#telemetry.event('tm.launcher.opened', { via, state: untracked(this.state) }, { sampled: true });
    }
    const surface = untracked(this.#surface);
    if (surface !== null) {
      surface.open(via, reopened, returnTo);
      return;
    }
    // Before the chunk: the skeleton shows, and the surface opens once it lands.
    this.#pending = { via, returnTo: this.#pending?.returnTo ?? returnTo };
    void this.#ready();
  }

  /**
   * Closes the launcher, keeping its text.
   *
   * @param reason - Why it closed.
   */
  close(reason: TmLauncherCloseReason): void {
    this.#pending = null;
    if (!untracked(this.#open)) {
      return;
    }
    this.#open.set(false);
    untracked(this.#surface)?.close(reason);
  }

  /**
   * Replaces the launcher's text.
   *
   * @param text - The new text; `''` clears it.
   */
  setQuery(text: string): void {
    this.#query.set(text);
  }

  /**
   * Keeps the text for the tenant it was typed in: entering another tenant
   * closes the launcher and clears it, as the tenant shell asks on each
   * entry. A tenant shell lives for one tenant, so the launcher, which
   * outlives it, remembers the tenant. Internal.
   *
   * @param tenantId - The tenant the shell entered.
   */
  ɵenterTenant(tenantId: number): void {
    if (this.#tenantId !== null && this.#tenantId !== tenantId) {
      this.close('route');
      this.#query.set('');
    }
    this.#tenantId = tenantId;
  }

  /**
   * Starts loading the surface's chunk and creates the surface, closed, once
   * it lands, as the browser's first idle time and a hover or focus on a
   * control that opens it do; the next open then shows in its own frame.
   * Internal.
   */
  ɵprefetch(): void {
    void this.#ready();
  }

  /**
   * Gives focus back to a control as the launcher closes; when the control
   * is the app bar's field, that focus does not open the launcher again.
   * Internal: the surface calls it.
   *
   * @param control - The control.
   */
  ɵrestore(control: HTMLElement): void {
    this.#restoring = true;
    try {
      control.focus();
    } finally {
      this.#restoring = false;
    }
  }

  /** Loads the chunk once; `null` when it fails, so the next gesture tries again. */
  #loadChunk(): Promise<TmLauncherChunk | null> {
    this.#chunk ??= this.#load().then(
      (chunk) => chunk,
      (error: unknown) => {
        // Offline, or after a deploy removed the chunk.
        this.#telemetry.error(error, { source: 'tm-launcher' });
        this.#chunk = null;
        return null;
      },
    );
    return this.#chunk;
  }

  /**
   * Loads the chunk and has it create the surface at the end of the body,
   * outside the shell's landmarks, then opens what waits. A failed load
   * closes the waiting open, if any, so its skeleton goes.
   */
  async #ready(): Promise<void> {
    const chunk = await this.#loadChunk();
    if (this.#ended || untracked(this.#surface) !== null) {
      return;
    }
    if (chunk === null) {
      this.close('route');
      return;
    }
    const ref = chunk.create(this.#injector);
    this.#ref = ref;
    this.#surface.set(ref.instance);
    const pending = this.#pending;
    this.#pending = null;
    if (pending !== null && untracked(this.#open)) {
      ref.instance.open(pending.via, false, pending.returnTo);
    }
  }
}
