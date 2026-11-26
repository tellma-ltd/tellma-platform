// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import {
  ApplicationRef,
  type ComponentRef,
  computed,
  createComponent,
  DestroyRef,
  DOCUMENT,
  EnvironmentInjector,
  inject,
  Injectable,
  InjectionToken,
  Injector,
  type Signal,
  signal,
  type Type,
  untracked,
} from '@angular/core';
import { TmTelemetry } from '@tellma/core';
import type { TmRecents } from '@tellma/core/recents';
import { TmIconRegistry } from '@tellma/core-ui/icon';

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

/** What the launcher's deferred chunks export. Internal. */
export interface TmLauncherChunk {
  /** The surface, created once at the end of the body. */
  readonly TmLauncherPanel: Type<TmLauncherSurface>;
  /** Registers the icon names the surface draws that the app has not registered. */
  readonly tmRegisterLauncherIcons: (registry: TmIconRegistry) => void;
  /** The recent pages' service, whose entry point the app loads after its first render. */
  readonly TmRecents: Type<TmRecents>;
}

/** The recent pages, as the surface reads them. Internal: the launcher provides it to the surface it creates. */
export const TM_LAUNCHER_RECENTS = new InjectionToken<Pick<TmRecents, 'pages'>>('TM_LAUNCHER_RECENTS');

/**
 * Loads the launcher's chunks: the panel, and beside it the icons it draws,
 * which are icon data rather than the panel's code, and the
 * recent pages' entry point, which the app has loaded by then. Internal: a
 * test delays the load or makes it fail.
 */
export const TM_LAUNCHER_LOAD = new InjectionToken<() => Promise<TmLauncherChunk>>('TM_LAUNCHER_LOAD', {
  providedIn: 'root',
  factory: () => () =>
    Promise.all([import('./tm-launcher-panel'), import('./tm-launcher-icons'), import('@tellma/core/recents')]).then(
      ([panel, icons, recents]) => ({
        TmLauncherPanel: panel.TmLauncherPanel,
        tmRegisterLauncherIcons: icons.tmRegisterLauncherIcons,
        TmRecents: recents.TmRecents,
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

  /** Whether the launcher is open before its chunk is in, so the app bar shows its skeleton. Internal. */
  readonly ɵpending: Signal<boolean> = computed(() => this.#open() && this.#surface() === null);

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
   * outlives it, remembers the tenant.
   *
   * @param tenantId - The tenant the shell entered.
   */
  enterTenant(tenantId: number): void {
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
   * Loads the chunk and creates the surface at the end of the body, outside
   * the shell's landmarks, then opens what waits. A failed load closes the
   * waiting open, if any, so its skeleton goes.
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
    chunk.tmRegisterLauncherIcons(this.#injector.get(TmIconRegistry));
    // The recent pages reach the surface through its own injector: their entry point is lazy to the shell's.
    const elementInjector = Injector.create({
      providers: [{ provide: TM_LAUNCHER_RECENTS, useValue: this.#injector.get(chunk.TmRecents) }],
      parent: this.#injector,
    });
    const ref = createComponent(chunk.TmLauncherPanel, { environmentInjector: this.#injector, elementInjector });
    this.#document.body.appendChild(ref.location.nativeElement as HTMLElement);
    this.#injector.get(ApplicationRef).attachView(ref.hostView);
    // Renders the surface at once, so the open that waited finds its views.
    ref.changeDetectorRef.detectChanges();
    this.#ref = ref;
    this.#surface.set(ref.instance);
    const pending = this.#pending;
    this.#pending = null;
    if (pending !== null && untracked(this.#open)) {
      ref.instance.open(pending.via, false, pending.returnTo);
    }
  }
}
