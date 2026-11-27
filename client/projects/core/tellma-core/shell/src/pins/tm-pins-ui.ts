// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import { LiveAnnouncer } from '@angular/cdk/a11y';
import {
  computed,
  DestroyRef,
  DOCUMENT,
  inject,
  Injectable,
  InjectionToken,
  type Signal,
  signal,
  type Type,
  untracked,
  type ViewContainerRef,
} from '@angular/core';
import { TM_TOAST_FEEDBACK_MS, TmLocalization, TmTelemetry } from '@tellma/core';
import { TmToasts } from '@tellma/core-ui/toast';

import { TmShellOverlays } from '../app-bar/tm-shell-overlays';
import type { TmLauncherCommand } from '../launcher/tm-launcher-commands';
import { TM_PIN_LIMITS } from './tm-pin-rules';
import { type TmPinFolderView, TmPins, tmPinsVia, type TmPinView } from './tm-pins';
import { tmRegisterPinsStrings } from './tm-pins-strings';

// The pins' controls in the tenant shell (spec 0027 §8.1, §8.5, §8.11,
// §9.3): the pin control's state and its disabled reason, what a press of
// it or Alt+Shift+P does, the launcher's Pinned-band commands, and the
// loader of the bar's overlays. The menus, the popover and the drag code
// are a deferred chunk that loads when the browser is first idle after the
// shell renders, and on the first hover or focus of the bar, so a press
// finds it in memory; a press that beats it acts as soon as it lands.
//
// Pinning the routed page appends the pin in the press's frame. When the
// bar shows, its next fit says whether the new chip shows: then the
// popover opens on the pin control, else (the chip went into More, or the
// bar is hidden) a toast says so once.

/** A menu the bar opens from one of its chips. Internal. */
export type TmPinMenu =
  | { readonly kind: 'more'; readonly items: Signal<readonly (TmPinView | TmPinFolderView)[]> }
  | { readonly kind: 'folder'; readonly id: string }
  | { readonly kind: 'revoked'; readonly id: string };

/** What the bar gives its overlays: its element, its pin control and its unpin path. Internal. */
export interface TmPinnedBarPort {
  /** The bar's `nav`. */
  readonly element: HTMLElement;
  /** The pin control. */
  control(): HTMLElement | null;
  /**
   * Unpins a pin; when focus was on its chip or on what the chip opened,
   * it goes to the next chip, else the previous one, else the pin control.
   *
   * @param id - The pin.
   * @param via - How the unpin was made.
   * @param refocus - Whether focus was on the chip or its menu.
   */
  unpin(id: string, via: 'shortcut' | 'menu', refocus: boolean): void;
}

/** The bar's overlays, as the deferred chunk implements them. Internal. */
export interface TmPinOverlays {
  /**
   * Opens a chip's menu, or closes it when it is open there.
   *
   * @param anchor - The chip.
   * @param menu - Which menu.
   */
  toggleMenu(anchor: HTMLElement, menu: TmPinMenu): void;
  /**
   * Opens the pin popover on an anchor: "Pinned" after a pin, "Edit pin" on a pinned page.
   *
   * @param anchor - The pin control, or the control focus went back to.
   * @param id - The pin.
   * @param mode - Which title it takes.
   */
  openPopover(anchor: HTMLElement, id: string, mode: 'pinned' | 'edit'): void;
  /**
   * Whether an overlay is open at an anchor; it reads signals.
   *
   * @param anchor - The chip or control.
   */
  isOpenFor(anchor: HTMLElement): boolean;
  /**
   * Gives the bar drag and drop: its chips become drag sources of kind `tm.pin`.
   *
   * @param bar - The bar.
   * @returns Removes the bar's listeners.
   */
  attachBar(bar: TmPinnedBarPort): () => void;
  /** Closes every open menu and the popover. */
  closeAll(): void;
}

/** What the overlays' deferred chunk exports. Internal. */
export interface TmPinOverlaysChunk {
  /** The component that hosts the bar's overlays, created once in the tenant shell. */
  readonly TmPinOverlayHost: Type<TmPinOverlays>;
}

/** Loads the overlays' chunk. Internal: a test delays it or makes it fail. */
export const TM_PIN_OVERLAYS_LOAD = new InjectionToken<() => Promise<TmPinOverlaysChunk>>('TM_PIN_OVERLAYS_LOAD', {
  providedIn: 'root',
  factory: () => () => import('./tm-pin-overlays'),
});

/** The pin control's state. */
export interface TmPinControlState {
  /** Whether the routed page is pinned: the control is "Edit pin", with a filled glyph. */
  readonly pinned: boolean;
  /** Why the control is disabled: the tenant's maintenance, the link's length or the cap; `null` when it is not. */
  readonly reason: string | null;
  /** Whether the control does nothing on a page with no pinnable route, which has no reason to give. */
  readonly inert: boolean;
}

/**
 * The pins' controls in the tenant shell: the pin control's state and
 * press, Alt+Shift+P, the launcher's commands and the overlays' loader.
 * Provided by the tenant shell.
 */
@Injectable()
export class TmPinsUi {
  readonly #pins = inject(TmPins);
  readonly #l10n = inject(TmLocalization);
  readonly #toasts = inject(TmToasts);
  readonly #announcer = inject(LiveAnnouncer);
  readonly #telemetry = inject(TmTelemetry);
  readonly #shellOverlays = inject(TmShellOverlays);
  readonly #load = inject(TM_PIN_OVERLAYS_LOAD);
  readonly #document = inject(DOCUMENT);
  readonly #overlays = signal<TmPinOverlays | null>(null);
  #host: ViewContainerRef | null = null;
  #loading: Promise<TmPinOverlays | null> | null = null;
  #bar: TmPinnedBarPort | null = null;
  #detachBar: (() => void) | null = null;
  #pending: { readonly anchor: HTMLElement; readonly menu: TmPinMenu } | null = null;
  #ended = false;

  /** The pin just pinned while the bar shows, until the bar's next fit places it. */
  readonly pending = signal<string | null>(null);

  /** The pin control's state. */
  readonly control: Signal<TmPinControlState> = computed(() => {
    const pinned = this.#pins.current() !== null;
    const refusal = pinned ? null : this.#pins.ɵrefusal();
    // The tenant's maintenance holds every pin control, whatever the page.
    const reason =
      this.#pins.ɵfrozen() ??
      (refusal === 'long' ? this.text('tooLong') : refusal === 'full' ? this.text('full', { count: TM_PIN_LIMITS.pins }) : null);
    return { pinned, reason, inert: reason === null && refusal === 'unpinnable' };
  });

  /**
   * The launcher's Pinned-band commands: "Pin this page" or "Edit pin"
   * while the tenant takes writes, then "Manage pins…".
   */
  readonly commands: Signal<readonly TmLauncherCommand[]> = computed(() => {
    const state = this.control();
    const commands: TmLauncherCommand[] = [];
    if (state.reason === null && !state.inert) {
      commands.push({
        id: state.pinned ? 'tm.pins.edit' : 'tm.pins.pin',
        label: this.text(state.pinned ? 'edit' : 'pin'),
        icon: 'pin',
        run: () => this.press('menu', null),
      });
    }
    commands.push({ id: 'tm.pins.manage', label: this.text('manageCommand'), icon: 'settings-2', run: () => this.manage() });
    return commands;
  });

  /** Creates the controls, which hand the launcher their commands for the shell's life. */
  constructor() {
    tmRegisterPinsStrings(this.#l10n);
    const detach = this.#pins.ɵattach(this.commands);
    inject(DestroyRef).onDestroy(() => {
      this.#ended = true;
      detach();
      this.#detachBar?.();
    });
  }

  /**
   * A string of the pins' scope.
   *
   * @param key - The key under `tm.pins`.
   * @param params - The message's parameters.
   * @returns The text.
   */
  text(key: string, params?: Readonly<Record<string, unknown>>): string {
    return this.#l10n.ɵtext(`tm.pins.${key}`, params)();
  }

  // --- The overlays' chunk ---

  /**
   * Gives the overlays their host and loads their chunk once the browser is idle.
   *
   * @param host - Where the overlays' host component renders.
   */
  attach(host: ViewContainerRef): void {
    this.#host = host;
    const view = this.#document.defaultView;
    const idle = (run: () => void): unknown => (view?.requestIdleCallback ? view.requestIdleCallback(run) : setTimeout(run));
    idle(() => void this.#ready());
  }

  /** Starts loading the overlays' chunk, as a hover or focus on the bar does. */
  preload(): void {
    void this.#ready();
  }

  /**
   * Runs an action on the overlays: at once when their chunk is in, else as soon as it lands.
   *
   * @param action - What to do with them.
   */
  run(action: (overlays: TmPinOverlays) => void): void {
    const overlays = this.#overlays();
    if (overlays !== null) {
      action(overlays);
      return;
    }
    void this.#ready().then((loaded) => loaded !== null && action(loaded));
  }

  /**
   * Opens a chip's menu, or closes it when it is open there. Presses made
   * before the chunk lands collapse to their net effect: a second press of
   * the same chip cancels the first, and a press of another chip replaces it.
   *
   * @param anchor - The chip.
   * @param menu - Which menu.
   */
  toggleMenu(anchor: HTMLElement, menu: TmPinMenu): void {
    const overlays = this.#overlays();
    if (overlays !== null) {
      overlays.toggleMenu(anchor, menu);
      return;
    }
    this.#pending = this.#pending?.anchor === anchor ? null : { anchor, menu };
    // Every press waits on the same load; the first to resume runs what is left and clears it.
    void this.#ready().then((loaded) => {
      const pending = this.#pending;
      this.#pending = null;
      if (loaded !== null && pending !== null) {
        loaded.toggleMenu(pending.anchor, pending.menu);
      }
    });
  }

  /**
   * Whether an overlay is open at an anchor.
   *
   * @param anchor - The chip or control.
   * @returns `false` until the chunk is in.
   */
  isOpenFor(anchor: HTMLElement): boolean {
    return this.#overlays()?.isOpenFor(anchor) ?? false;
  }

  /**
   * Hands the bar to the controls and, once the chunk is in, to its drag code.
   *
   * @param bar - The bar.
   * @returns Takes the bar back, as it ends.
   */
  attachBar(bar: TmPinnedBarPort): () => void {
    this.#bar = bar;
    this.#detachBar = null;
    this.run((overlays) => {
      if (this.#bar === bar) {
        this.#detachBar = overlays.attachBar(bar);
      }
    });
    return () => {
      if (this.#bar === bar) {
        this.#detachBar?.();
        this.#detachBar = null;
        this.#bar = null;
        this.pending.set(null);
      }
    };
  }

  // --- Pinning and unpinning ---

  /**
   * A press of the pin control, Alt+Shift+P or the launcher's command. On
   * an unpinned page it pins the page; on a pinned one, Alt+Shift+P unpins
   * it and the others open the Edit pin popover. A press while the control
   * is disabled does nothing.
   *
   * @param via - What was pressed: the control, the shortcut or the launcher's command.
   * @param anchor - What the popover opens on when the bar is hidden: the control that has focus.
   */
  press(via: 'control' | 'shortcut' | 'menu', anchor: HTMLElement | null): void {
    const state = untracked(this.control);
    if (state.reason !== null || state.inert) {
      return;
    }
    const current = untracked(this.#pins.current);
    if (current === null) {
      const pin = tmPinsVia(via, () => this.#pins.pinCurrent());
      if (this.#bar !== null) {
        // The bar's next fit says whether the chip shows.
        this.pending.set(pin.id);
      } else {
        this.#toast(pin);
      }
      return;
    }
    if (via === 'shortcut') {
      const label = untracked(current.label);
      const focused = this.#document.activeElement;
      const onChip = focused instanceof HTMLElement && focused.dataset['tmPin'] === current.id;
      if (this.#bar !== null) {
        this.#bar.unpin(current.id, 'shortcut', onChip);
      } else {
        tmPinsVia('shortcut', () => this.#pins.remove(current.id));
      }
      void this.#announcer.announce(this.text('unpinned', { label }), 'polite');
      return;
    }
    const target = this.#bar?.control() ?? anchor ?? (this.#document.activeElement as HTMLElement | null);
    if (target !== null) {
      this.run((overlays) => overlays.openPopover(target, current.id, 'edit'));
    }
  }

  /**
   * The bar's fit placed the pending pin: its chip shows, so the popover
   * opens on the control, or it does not (it went into More), so a toast
   * says it was pinned.
   *
   * @param id - The pin.
   * @param shows - Whether its chip shows.
   */
  reveal(id: string, shows: boolean): void {
    this.pending.set(null);
    const pin = untracked(this.#pins.items).find((item) => item.id === id) as TmPinView | undefined;
    const control = this.#bar?.control() ?? null;
    if (pin === undefined) {
      return;
    }
    if (!shows || control === null) {
      this.#toast(pin);
      return;
    }
    void this.#announcer.announce(this.text('pinned', { label: untracked(pin.label) }), 'polite');
    this.run((overlays) => overlays.openPopover(control, id, 'pinned'));
  }

  /** Opens the Manage pins dialog. */
  manage(): void {
    this.#shellOverlays.run((panels) => panels.openDialog('pins'));
  }

  /** Says once that a pin whose chip does not show was pinned. */
  #toast(pin: TmPinView): void {
    this.#toasts.show({ tone: 'success', title: this.text('pinnedToast', { label: untracked(pin.label) }), duration: TM_TOAST_FEEDBACK_MS });
  }

  /** Loads the chunk once and creates its host; `null` when it fails to load or the shell ended meanwhile. */
  #ready(): Promise<TmPinOverlays | null> {
    this.#loading ??= this.#load().then(
      (chunk) => {
        const host = this.#host;
        if (this.#ended || host === null) {
          this.#loading = null;
          return null;
        }
        const ref = host.createComponent(chunk.TmPinOverlayHost);
        // Renders the host at once, so its menus and popover exist before the action that waited runs.
        ref.changeDetectorRef.detectChanges();
        this.#overlays.set(ref.instance);
        return ref.instance;
      },
      (error: unknown) => {
        // Offline, or after a deploy removed the chunk: the next gesture tries again.
        this.#telemetry.error(error, { source: 'tm-pins-ui' });
        this.#loading = null;
        return null;
      },
    );
    return this.#loading;
  }
}
