// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import { DestroyRef, DOCUMENT, inject, Injectable, InjectionToken, type Signal, signal, type Type, type ViewContainerRef } from '@angular/core';
import { type TmShellSlot, TmTelemetry } from '@tellma/core';

import type { TmShellMarker } from './tm-shell-identity';

// The tenant shell's overlays (spec 0027 §7.2, §7.4, §10, §11): the session
// menu, the marker popovers, the slot panels and the About and Shortcuts
// dialogs. Their code is a deferred chunk, so a tenant route's first frame
// carries only the app bar's buttons. The chunk loads when the browser is
// first idle after the shell renders, and on the first hover or focus of any
// of its buttons, so it is in memory by the time a click asks for it and the
// overlay opens in the click's frame; a click that beats it opens the overlay
// as soon as it lands. The buttons keep their ARIA live from the overlays'
// own state once the chunk is in.

/** A dialog the shell opens. */
export type TmShellDialog = 'about' | 'shortcuts';

/** The shell's overlays, as the deferred chunk implements them. Internal. */
export interface TmShellPanels {
  /**
   * Opens the session menu at its button, or closes it when it is open there.
   *
   * @param button - The session button.
   * @param initialFocus - The row that takes focus on open.
   */
  toggleSession(button: HTMLElement, initialFocus: 'first' | 'last'): void;
  /**
   * Opens a marker's popover at its button, or closes it when it is open there.
   *
   * @param marker - The marker.
   * @param button - Its button.
   */
  toggleMarker(marker: TmShellMarker, button: HTMLElement): void;
  /**
   * Opens a slot's panel at its button, or closes it when it is open there:
   * a popover from `lg` up, an end drawer below.
   *
   * @param slot - The slot.
   * @param button - Its button, or `null` when the session menu's row opens it as a drawer.
   */
  toggleSlot(slot: TmShellSlot, button: HTMLElement | null): void;
  /**
   * Starts loading a slot's panel.
   *
   * @param slot - The slot.
   */
  preloadSlot(slot: TmShellSlot): void;
  /**
   * Opens a dialog.
   *
   * @param dialog - The dialog.
   */
  openDialog(dialog: TmShellDialog): void;
  /**
   * Whether an overlay is open from a button; it reads signals, so a binding follows it.
   *
   * @param button - The button.
   */
  isOpenFor(button: HTMLElement): boolean;
  /**
   * The id of the menu open from a button, which the button controls; it reads signals.
   *
   * @param button - The button.
   */
  controls(button: HTMLElement): string | null;
  /** Closes every overlay the shell opened, as a tenant switch does. */
  closeAll(): void;
}

/** What the deferred chunk exports. */
export interface TmShellPanelsChunk {
  /** The component that hosts the overlays, created once beside the app bar's buttons. */
  readonly TmSessionPanels: Type<TmShellPanels>;
  /** The session button's avatar, which replaces the first frame's initials once it loads. */
  readonly TmSessionAvatar: Type<unknown>;
}

/** Loads the overlays' chunk. Internal: a test delays it or makes it fail. */
export const TM_SHELL_PANELS_LOAD = new InjectionToken<() => Promise<TmShellPanelsChunk>>('TM_SHELL_PANELS_LOAD', {
  providedIn: 'root',
  factory: () => () => import('../session/tm-session-panels'),
});

/**
 * Opens the tenant shell's overlays from the app bar's buttons, loading
 * their chunk at idle or on the first hover, focus or click. Provided by the
 * tenant shell.
 */
@Injectable()
export class TmShellOverlays {
  readonly #load = inject(TM_SHELL_PANELS_LOAD);
  readonly #telemetry = inject(TmTelemetry);
  readonly #window = inject(DOCUMENT).defaultView;
  readonly #panels = signal<TmShellPanels | null>(null);
  readonly #avatar = signal<Type<unknown> | null>(null);
  #host: ViewContainerRef | null = null;
  #loading: Promise<TmShellPanels | null> | null = null;
  #ended = false;

  /** The session button's loaded avatar component, or `null` until the chunk is in. */
  readonly avatar: Signal<Type<unknown> | null> = this.#avatar.asReadonly();

  /** Creates the service, whose overlays end with the shell. */
  constructor() {
    inject(DestroyRef).onDestroy(() => {
      this.#ended = true;
    });
  }

  /**
   * Gives the overlays their host, beside the app bar's buttons, and loads
   * their chunk once the browser is idle.
   *
   * @param host - Where the overlays' host component renders.
   */
  attach(host: ViewContainerRef): void {
    this.#host = host;
    const idle = (run: () => void): unknown => (this.#window?.requestIdleCallback ? this.#window.requestIdleCallback(run) : setTimeout(run));
    idle(() => void this.#panelsReady());
  }

  /** Starts loading the overlays' chunk, as a hover or focus on one of their buttons does. */
  preload(): void {
    void this.#panelsReady();
  }

  /**
   * Whether an overlay is open from a button.
   *
   * @param button - The button.
   * @returns `false` until the chunk is in.
   */
  isOpenFor(button: HTMLElement): boolean {
    return this.#panels()?.isOpenFor(button) ?? false;
  }

  /**
   * The id of the menu open from a button.
   *
   * @param button - The button.
   * @returns `null` while none is open there.
   */
  controls(button: HTMLElement): string | null {
    return this.#panels()?.controls(button) ?? null;
  }

  /**
   * Runs an action on the overlays: at once when their chunk is in, so the
   * overlay opens in the gesture's frame, else as soon as it lands.
   *
   * @param action - What to do with the overlays.
   */
  run(action: (panels: TmShellPanels) => void): void {
    const panels = this.#panels();
    if (panels !== null) {
      action(panels);
      return;
    }
    void this.#panelsReady().then((loaded) => loaded && action(loaded));
  }

  /** Closes every open overlay; nothing is open before the chunk is in. */
  closeAll(): void {
    this.#panels()?.closeAll();
  }

  /** Loads the chunk once and creates its host; `null` when it fails to load or the shell ended meanwhile. */
  #panelsReady(): Promise<TmShellPanels | null> {
    this.#loading ??= this.#load().then(
      (chunk) => {
        const host = this.#host;
        if (this.#ended || host === null) {
          return null;
        }
        const ref = host.createComponent(chunk.TmSessionPanels);
        // Renders the host at once, so its menu and popovers have their inputs
        // and views before the action that waited for the chunk runs.
        ref.changeDetectorRef.detectChanges();
        const panels = ref.instance;
        this.#avatar.set(chunk.TmSessionAvatar);
        this.#panels.set(panels);
        return panels;
      },
      (error: unknown) => {
        // Offline, or after a deploy removed the chunk: the next gesture tries again.
        this.#telemetry.error(error, { source: 'tm-shell-overlays' });
        this.#loading = null;
        return null;
      },
    );
    return this.#loading;
  }
}
