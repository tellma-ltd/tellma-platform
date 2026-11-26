// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import {
  type ComponentRef,
  computed,
  DestroyRef,
  DOCUMENT,
  effect,
  EnvironmentInjector,
  inject,
  Injectable,
  InjectionToken,
  type Signal,
  signal,
  untracked,
} from '@angular/core';
import { NavigationStart, Router } from '@angular/router';
import { TmTelemetry } from '@tellma/core';
import { tmPushEscapeDismissal } from '@tellma/core-ui/overlay';

// The launcher (spec 0027 §9): the Menu button, the app bar's search field,
// the search buttons and Mod+K open it, and it keeps its text across close
// and navigation in this tab. This service is the part the first frame
// carries: the state, the kept text and the calls. The surface itself, the
// overlay panel, its index and its keyboard, is a deferred chunk that loads
// when the browser is first idle after the shell renders, and on the first
// hover or focus of a control that opens it, so it is in memory by the time
// a click asks for it and opens in the click's frame. The text belongs to
// the tenant it was typed in: a tenant switch, through /tenants or a deep
// link, clears it.
//
// An open that beats the chunk, or below md the Menu modal's own chunk,
// shows the app bar's skeleton until it lands, and this service answers it
// meanwhile as the open launcher would (§9.2): Escape, a press outside the
// controls that open it, Tab, a path change and a focus that moves elsewhere
// close it. Its keys are the launcher's, never the page's: from lg up the
// app bar's field, its input, takes focus at once; below lg the skeleton
// does, and the keys typed there become the launcher's text.

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
   * @param typed - Whether the text changed while the open waited, so the input keeps it unselected.
   */
  open(via: TmLauncherVia, reopened: boolean, returnTo: HTMLElement | null, typed: boolean): void;
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

/** An open that waits for the panel's chunk. */
interface TmPendingOpen {
  /** What asked for it last. */
  readonly via: TmLauncherVia;
  /** Where focus goes back. */
  readonly returnTo: HTMLElement | null;
  /** The text when the wait began, so the open knows whether it changed meanwhile. */
  readonly query: string;
}

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
  readonly #router = inject(Router, { optional: true });
  readonly #query = signal('');
  readonly #open = signal(false);
  readonly #surface = signal<TmLauncherSurface | null>(null);
  #chunk: Promise<TmLauncherChunk | null> | null = null;
  #ref: ComponentRef<TmLauncherSurface> | null = null;
  #ended = false;
  // While the launcher gives focus to a control, a focus there opens nothing.
  #restoring = false;
  // The open that waits for the chunk.
  #pending: TmPendingOpen | null = null;
  // Where focus goes back when an open that waits closes.
  #returnTo: HTMLElement | null = null;
  // The listeners that answer an open that waits, released as the wait ends.
  #guard: (() => void) | null = null;
  // Whether a key held while waiting has replaced the kept text, as a key typed over its selection would.
  #held = false;
  // The app bar's skeleton, while it shows.
  #skeleton: HTMLElement | null = null;
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

  /** The app bar's Menu button, where focus goes when no control had it; `null` before the bar renders. Internal. */
  ɵmenu: HTMLElement | null = null;

  /** Creates the service, whose surface ends with the app. */
  constructor() {
    // The wait ends as the surface shows: its own handlers answer from then on.
    effect(() => {
      if (!this.ɵpending()) {
        untracked(() => this.#disarm());
      }
    });
    inject(DestroyRef).onDestroy(() => {
      this.#ended = true;
      this.#disarm();
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
    this.#show(via, null);
  }

  /**
   * Opens the launcher from a control of the app bar's, as {@link show}
   * does; focus goes back to the control when none had it, as after a press
   * that does not focus a button (Safari, iOS). Internal.
   *
   * @param via - What opened it.
   * @param trigger - The control.
   */
  ɵshowFrom(via: TmLauncherVia, trigger: HTMLElement): void {
    this.#show(via, trigger);
  }

  /**
   * Closes the launcher, keeping its text.
   *
   * @param reason - Why it closed.
   */
  close(reason: TmLauncherCloseReason): void {
    const waited = this.#guard !== null;
    this.#pending = null;
    this.#disarm();
    if (!untracked(this.#open)) {
      return;
    }
    this.#open.set(false);
    untracked(this.#surface)?.close(reason);
    if (waited) {
      this.#giveBack(reason);
    }
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

  /**
   * Hears that the app bar's skeleton rendered, or went: below `lg`, where
   * no input exists yet, it takes focus, so the keys typed while the open
   * waits are the launcher's. Internal: the app bar calls it.
   *
   * @param skeleton - The skeleton, or `null` once it is gone.
   */
  ɵskeleton(skeleton: HTMLElement | null): void {
    this.#skeleton = skeleton;
    if (skeleton !== null && this.#guard !== null && !this.#fieldShows()) {
      this.ɵrestore(skeleton);
    }
  }

  // --- Opening ---

  /** Opens the launcher, or answers another open; focus goes back to what had it, else to the trigger. */
  #show(via: TmLauncherVia, trigger: HTMLElement | null): void {
    if (this.#restoring) {
      return;
    }
    const reopened = untracked(this.#open);
    const active = this.#document.activeElement;
    const returnTo = active instanceof HTMLElement && active !== this.#document.body ? active : trigger;
    if (!reopened) {
      this.#open.set(true);
      this.#returnTo = returnTo;
      this.#telemetry.event('tm.launcher.opened', { via, state: untracked(this.state) }, { sampled: true });
    }
    const surface = untracked(this.#surface);
    if (surface !== null) {
      surface.open(via, reopened, returnTo, false);
      // Below md, the open may wait for the Menu modal's chunk.
      this.#arm();
      if (this.#guard !== null) {
        this.#focus(via);
      }
      return;
    }
    // --- Before the chunk: the skeleton shows, and the surface opens once it lands ---
    const pending = this.#pending;
    this.#pending = { via, returnTo: pending?.returnTo ?? returnTo, query: pending?.query ?? untracked(this.#query) };
    this.#arm();
    this.#focus(via);
    void this.#ready();
  }

  /**
   * The input of an open that waits: from lg up the app bar's field takes
   * focus at once and, but for a press in it, which placed the caret, has
   * its text selected; below lg the skeleton takes it as it renders, and a
   * key typed there first replaces the text, as over a selection.
   */
  #focus(via: TmLauncherVia): void {
    const field = this.ɵfield;
    if (field !== null && this.#fieldShows()) {
      if (via !== 'field') {
        this.ɵrestore(field);
        field.select();
      }
    } else {
      this.#held = false;
      if (this.#skeleton !== null) {
        this.ɵrestore(this.#skeleton);
      }
    }
  }

  /** Whether the app bar's field shows, so it is the launcher's input: from lg up. */
  #fieldShows(): boolean {
    return this.ɵfield !== null && this.ɵfield.getClientRects().length > 0;
  }

  // --- The open that waits ---

  /** Answers an open that waits, as the open launcher would, until the wait ends. */
  #arm(): void {
    if (this.#guard !== null || !untracked(this.ɵpending)) {
      return;
    }
    const listening = new AbortController();
    for (const type of ['pointerdown', 'keydown', 'focusin']) {
      this.#document.addEventListener(type, this.#waited, { capture: true, signal: listening.signal });
    }
    const releaseEscape = tmPushEscapeDismissal(() => this.close('escape'));
    const navigations = this.#router?.events.subscribe((event) => {
      if (event instanceof NavigationStart && pathOf(event.url) !== pathOf(this.#router!.url)) {
        this.close('route');
      }
    });
    this.#guard = () => {
      listening.abort();
      releaseEscape();
      navigations?.unsubscribe();
    };
  }

  /** Stops answering an open that waits. */
  #disarm(): void {
    const guard = this.#guard;
    this.#guard = null;
    guard?.();
  }

  /**
   * An event while an open waits:
   *
   * - a press outside the launcher's controls (the app bar's and the
   *   skeleton) closes it;
   * - a focus that moves elsewhere, a dialog's for one, closes it;
   * - Tab closes it;
   * - below lg, where the skeleton holds focus, every other key is the
   *   launcher's, never the page's, and a printable one or Backspace edits
   *   its text. A shortcut's chord goes on to the shortcut registry, Escape
   *   to the dismissal stack, and a composition's keys to the composition.
   */
  readonly #waited = (event: Event): void => {
    const target = event.target;
    const own =
      target instanceof Node &&
      (this.#skeleton?.contains(target) === true || (target instanceof Element && target.closest('[data-tm-launcher-trigger]') !== null));
    if (!untracked(this.ɵpending)) {
      return;
    }
    if (!(event instanceof KeyboardEvent)) {
      if (!own && !this.#restoring) {
        this.close(event.type === 'pointerdown' ? 'outside' : 'route');
      }
      return;
    }
    const key = event.key;
    if (key === 'Tab') {
      this.close('tab');
      return;
    }
    if (key === 'Escape' || event.metaKey || (event.ctrlKey && !event.altKey) || event.isComposing || event.keyCode === 229 || this.#fieldShows()) {
      return;
    }
    event.preventDefault();
    event.stopPropagation();
    const printable = [...key].length === 1;
    if (printable || key === 'Backspace') {
      const text = this.#held ? untracked(this.#query) : '';
      this.#held = true;
      this.#query.set(printable ? text + key : [...text].slice(0, -1).join(''));
    }
  };

  /**
   * Gives focus back as an open that waited closes, as the open surface
   * would (spec 0027 §5.3): on Escape, and on Tab below lg, to the control
   * that had it, from which Tab moves on; after a press outside, to that
   * control when the press left focus nowhere.
   */
  #giveBack(reason: TmLauncherCloseReason): void {
    const returnTo = this.#returnTo;
    if (returnTo === null) {
      return;
    }
    if (reason === 'escape' || (reason === 'tab' && !this.#fieldShows())) {
      this.ɵrestore(returnTo);
    } else if (reason === 'outside') {
      setTimeout(() => {
        const active = this.#document.activeElement;
        if (active === null || active === this.#document.body || active === this.#skeleton) {
          this.ɵrestore(returnTo);
        }
      });
    }
  }

  // --- The chunk ---

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
   * outside the shell's landmarks, then opens what waits, unless it closed
   * meanwhile. A failed load closes the waiting open, if any, so its
   * skeleton goes.
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
      ref.instance.open(pending.via, false, pending.returnTo, untracked(this.#query) !== pending.query);
    }
    // Below md, the open may go on to wait for the Menu modal's chunk.
    if (!untracked(this.ɵpending)) {
      this.#disarm();
    }
  }
}

/** A URL's path, without its query or fragment. */
function pathOf(url: string): string {
  return url.split(/[?#]/, 1)[0]!;
}
