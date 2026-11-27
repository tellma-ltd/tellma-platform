// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import { inject, Injectable, InjectionToken, type Signal, signal, type Type, type ViewContainerRef } from '@angular/core';
import type { TmShellSlot } from '@tellma/core';

import { TmDeferredHost } from '../tm-deferred-host';
import type { TmShellMarker } from './tm-shell-identity';

// The tenant shell's overlays (spec 0027 §7.2, §7.4, §8.10, §10, §11): the
// session menu, the marker popovers, the slot panels and the About,
// Shortcuts and Manage pins dialogs. Their code is a deferred chunk, so a
// tenant route's first frame carries only the app bar's buttons. It loads
// through the shell's deferred host: at idle after the shell renders, and on
// the first hover or focus of any of its buttons, with the presses that beat
// it collapsed to their net effect. The buttons keep their ARIA live from
// the overlays' own state once the chunk is in.

/** A dialog the shell opens. */
export type TmShellDialog = 'about' | 'shortcuts' | 'pins';

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
  readonly #avatar = signal<Type<unknown> | null>(null);
  readonly #chunk = new TmDeferredHost('tm-shell-overlays', inject(TM_SHELL_PANELS_LOAD), (chunk: TmShellPanelsChunk, host) => {
    const ref = host.createComponent(chunk.TmSessionPanels);
    // Renders the host at once, so its menu and popovers have their inputs
    // and views before the action that waited for the chunk runs.
    ref.changeDetectorRef.detectChanges();
    this.#avatar.set(chunk.TmSessionAvatar);
    return ref.instance;
  });

  /** The session button's loaded avatar component, or `null` until the chunk is in. */
  readonly avatar: Signal<Type<unknown> | null> = this.#avatar.asReadonly();

  /**
   * Gives the overlays their host, beside the app bar's buttons, and loads
   * their chunk once the browser is idle.
   *
   * @param host - Where the overlays' host component renders.
   */
  attach(host: ViewContainerRef): void {
    this.#chunk.attach(host);
  }

  /** Starts loading the overlays' chunk, as a hover or focus on one of their buttons does. */
  preload(): void {
    this.#chunk.preload();
  }

  /**
   * Whether an overlay is open from a button.
   *
   * @param button - The button.
   * @returns `false` until the chunk is in.
   */
  isOpenFor(button: HTMLElement): boolean {
    return this.#chunk.value()?.isOpenFor(button) ?? false;
  }

  /**
   * The id of the menu open from a button.
   *
   * @param button - The button.
   * @returns `null` while none is open there.
   */
  controls(button: HTMLElement): string | null {
    return this.#chunk.value()?.controls(button) ?? null;
  }

  /**
   * Runs an action on the overlays: at once when their chunk is in, so the
   * overlay opens in the gesture's frame, else as soon as it lands.
   *
   * @param action - What to do with the overlays.
   */
  run(action: (panels: TmShellPanels) => void): void {
    this.#chunk.run(action);
  }

  /**
   * Runs a button's press on the overlays: at once when their chunk is in,
   * else once it lands, as the net effect of every press made meanwhile.
   * Before the chunk nothing shows, so a second toggle of the same button
   * cancels the first; an opening press, or a press of another button,
   * replaces what waits.
   *
   * @param button - The pressed button.
   * @param action - What the press does with the overlays.
   * @param opens - Whether the press only opens (the session button's arrows), so a pending press of that button does not cancel it.
   */
  press(button: HTMLElement, action: (panels: TmShellPanels) => void, opens = false): void {
    this.#chunk.press(button, action, opens);
  }

  /** Closes every open overlay, and drops a press that waits for the chunk. */
  closeAll(): void {
    this.#chunk.drop();
    this.#chunk.value()?.closeAll();
  }
}
