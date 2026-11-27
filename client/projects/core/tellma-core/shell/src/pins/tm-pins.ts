// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import { computed, DOCUMENT, inject, Injectable, signal, type Signal, untracked } from '@angular/core';
import { NavigationEnd, Router } from '@angular/router';
import {
  TmLaunchCache,
  TmLocalization,
  TmNavigation,
  TmPageContexts,
  TmPreferences,
  TmTelemetry,
  TmUrls,
  tmPinnedBarVisibleKey,
  tmPinsKey,
  type TmPin,
  type TmPinsDoc,
} from '@tellma/core';

import {
  TM_PIN_LIMITS,
  tmCurrentPin,
  tmNewPinId,
  tmPinHref,
  tmPinLabel,
  tmPinsAddFolder,
  tmPinsAppend,
  tmPinsMove,
  tmPinsOf,
  tmPinsRemove,
  tmPinsRemoveFolder,
  tmPinsUpdate,
  tmPinUrl,
  tmPinsView,
} from './tm-pin-rules';

// The user's pins in the routed tenant (spec 0027 §8.2 to §8.5): the bar's
// items as views that carry each pin's label, state and current flag as
// signals, the routed page's pin, whether that page can be pinned, the
// bar's switch, and the edits. Every edit writes the next document through
// TmPreferences, so the bar changes in the same frame, the curated value
// goes out at once and the user's other tabs follow. Each edit reports
// `tm.shell.pins-changed` with how it was made, which the shell's own
// controls set around their calls; an app's own call counts as the pin
// control's.

/** A pin as the bar shows it. */
export interface TmPinView {
  /** The pin's id. */
  readonly id: string;
  /** The URL the pin opens in the routed tenant, such as `/17/customers?…`. */
  readonly href: string;
  /** The folder the pin sits in, or `null` at the top level. */
  readonly folderId: string | null;
  /** The chip's label: the user's own, else a plain URL's entry label in the active language, else the stored title. */
  readonly label: Signal<string>;
  /** Whether the pin opens: `ok`, `no-access` when its permission check fails, `unavailable` when no visible route matches. */
  readonly state: Signal<'ok' | 'no-access' | 'unavailable'>;
  /** Whether the pin's chip marks the routed page: the page's own pin, else a pin of its path with no query or fragment. */
  readonly current: Signal<boolean>;
}

/** A folder as the bar shows it. */
export interface TmPinFolderView {
  /** The folder's id. */
  readonly id: string;
  /** The folder's name. */
  readonly folder: string;
  /** Its pins, in order. */
  readonly items: readonly TmPinView[];
}

/** How a pins change was made, as `tm.shell.pins-changed` reports it. Internal. */
export type TmPinsVia = 'control' | 'shortcut' | 'popover' | 'drag' | 'menu' | 'dialog';

/**
 * Why the routed page cannot be pinned: the pins not loaded yet, the tenant
 * under maintenance, no pinnable route, a link past the cap, or the pins at
 * the cap. Internal.
 */
export type TmPinRefusal = 'cold' | 'frozen' | 'unpinnable' | 'long' | 'full';

/** The tenant shell's pin controls, which the launcher's Pinned-band commands run. Internal. */
export interface TmPinControls {
  /** The pin control's state: whether the page is pinned, why the control is disabled, and whether it is inert. */
  readonly control: Signal<{ readonly pinned: boolean; readonly reason: string | null; readonly inert: boolean }>;
  /**
   * Presses the pin control as the launcher's command does.
   *
   * @param via - The launcher's command.
   * @param anchor - None: the popover opens on the pin control or the focused control.
   */
  press(via: 'menu', anchor: null): void;
  /** Opens the Manage pins dialog. */
  manage(): void;
}

/** How the change being made was made; the pin control's outside the shell's own calls. */
let currentVia: TmPinsVia = 'control';

/**
 * Runs an edit of the pins as made one way, so its telemetry says so. The
 * shell's controls call TmPins through it. Internal.
 *
 * @param via - How the change is made.
 * @param edit - The edit, which calls TmPins.
 * @returns What the edit returns.
 */
export function tmPinsVia<T>(via: TmPinsVia, edit: () => T): T {
  const before = currentVia;
  currentVia = via;
  try {
    return edit();
  } finally {
    currentVia = before;
  }
}

/** A count's telemetry bucket. */
function bucket(count: number): '0' | '1-5' | '6-15' | '16+' {
  return count === 0 ? '0' : count <= 5 ? '1-5' : count <= 15 ? '6-15' : '16+';
}

/** The ids a document holds, its folders' and their pins' included. */
function idsOf(doc: TmPinsDoc): ReadonlySet<string> {
  return new Set(doc.items.flatMap((item) => ('folder' in item ? [item.id, ...item.items.map((pin) => pin.id)] : [item.id])));
}

/** A text cut to a number of characters (code points). */
function cut(text: string, max: number): string {
  const characters = [...text];
  return characters.length <= max ? text : characters.slice(0, max).join('');
}

/**
 * The user's pins in the routed tenant: the bar's items, the routed page's
 * pin and the edits. Every edit writes the pins preference, so the bar, the
 * launcher and the user's other tabs follow it at once.
 *
 * @example
 * ```ts
 * const pins = inject(TmPins);
 * if (pins.canPinCurrent()) {
 *   const pin = pins.pinCurrent();
 *   pins.update(pin.id, { label: 'Month-end invoices' });
 * }
 * ```
 */
@Injectable({ providedIn: 'root' })
export class TmPins {
  readonly #prefs = inject(TmPreferences);
  readonly #urls = inject(TmUrls);
  readonly #navigation = inject(TmNavigation);
  readonly #contexts = inject(TmPageContexts);
  readonly #telemetry = inject(TmTelemetry);
  readonly #cache = inject(TmLaunchCache);
  readonly #l10n = inject(TmLocalization);
  readonly #document = inject(DOCUMENT);
  readonly #router = inject(Router);
  readonly #stored = this.#prefs.get(tmPinsKey);
  readonly #shown = this.#prefs.get(tmPinnedBarVisibleKey);
  readonly #routed = signal(this.#router.url);
  // Each pin's view by id, kept while the pin's members, its folder and the
  // tenant stay, so its signals do too: a write decodes every stored pin
  // afresh. Each reading of the items keeps only the ids it met.
  #views = new Map<string, { readonly key: string; readonly view: TmPinView }>();

  /** The stored document's valid items, which every edit starts from. */
  readonly #view = computed(() => tmPinsView(this.#stored()));

  /** The id of the pin whose chip marks the routed page, or `null`. */
  readonly #currentId = computed(() => tmCurrentPin(this.#view().items, tmPinUrl(this.#routed()))?.id ?? null);

  /** The id of the routed page's own pin, its query and `#d=` matching too, or `null`. */
  readonly #ownId = computed(() => {
    const url = tmPinUrl(this.#routed());
    return tmPinsOf(this.#view().items).find((pin) => pin.url === url)?.id ?? null;
  });

  /** The top-level pins and folders, in the bar's order; none outside a tenant. */
  readonly items: Signal<readonly (TmPinView | TmPinFolderView)[]> = computed(() => {
    const tenantId = this.#urls.tenantId();
    if (tenantId === null) {
      return [];
    }
    const kept = this.#views;
    this.#views = new Map();
    return this.#view().items.map((item) =>
      'folder' in item
        ? { id: item.id, folder: item.folder, items: item.items.map((pin) => this.#pinView(pin, item.id, tenantId, kept)) }
        : this.#pinView(item, null, tenantId, kept),
    );
  });

  /**
   * The routed page's own pin: the one whose link is the routed page's,
   * its query and `#d=` included, or `null`. A filtered view of a pinned
   * page has none, so it pins as a pin of its own; its chip still marks the
   * page.
   */
  readonly current: Signal<TmPinView | null> = computed(() => {
    const id = this.#ownId();
    return (
      this.items()
        .flatMap((item) => ('folder' in item ? item.items : [item]))
        .find((pin) => pin.id === id) ?? null
    );
  });

  /**
   * Why the routed page cannot be pinned, or `null` when it can.
   *
   * @internal
   */
  readonly ɵrefusal: Signal<TmPinRefusal | null> = computed(() => {
    // Before the launch documents land, the stored pins are unknown: a write would replace them.
    if (!this.#cache.warm()) {
      return 'cold';
    }
    if (this.ɵfrozen() !== null) {
      return 'frozen';
    }
    const url = this.#routed();
    const capture = tmPinUrl(url);
    if (capture === null || this.#urls.describe(url)?.pinnable !== true) {
      return 'unpinnable';
    }
    if (capture.length > TM_PIN_LIMITS.url) {
      return 'long';
    }
    return tmPinsOf(this.#view().items).length >= TM_PIN_LIMITS.pins ? 'full' : null;
  });

  /**
   * Why the pins cannot change while the tenant is under maintenance, which
   * holds every edit and the controls that make them; `null` while the
   * tenant takes writes.
   *
   * @internal
   */
  readonly ɵfrozen: Signal<string | null> = computed(() => {
    if (this.#cache.docs.tenant()?.state !== 'Frozen') {
      return null;
    }
    const names = this.#cache.docs.settings()?.names;
    return this.#l10n.ɵtext('tm.entity.tenantFrozen', { tenant: names?.[this.#l10n.contentSlot() - 1] ?? names?.[0] ?? '' })();
  });

  /** Whether the routed page can be pinned: the pins loaded, the tenant taking writes, a pinnable route, a link within the cap, and fewer than 30 pins. */
  readonly canPinCurrent: Signal<boolean> = computed(() => this.ɵrefusal() === null);

  /** Whether the pinned bar shows. */
  readonly visible: Signal<boolean> = this.#shown;

  // The tenant shell's pin controls while one is rendered.
  readonly #controls = signal<TmPinControls | null>(null);

  /**
   * The tenant shell's pin controls while it renders, else `null`; the
   * launcher's Pinned-band commands run them.
   *
   * @internal
   */
  readonly ɵcontrols: Signal<TmPinControls | null> = this.#controls.asReadonly();

  /** Creates the service, which follows the routed URL. */
  constructor() {
    this.#router.events.subscribe((event) => {
      if (event instanceof NavigationEnd) {
        this.#routed.set(this.#router.url);
      }
    });
  }

  /**
   * Pins the routed page at the end of the top level: its path, its query
   * without `page` and its `#d=` fragment, with the page's title.
   *
   * @returns The new pin's view.
   * @throws Error when {@link TmPins.canPinCurrent} is false.
   */
  pinCurrent(): TmPinView {
    const refusal = untracked(this.ɵrefusal);
    if (refusal !== null) {
      throw new Error(`Cannot pin: ${refusal}.`);
    }
    const view = untracked(this.#view);
    const pin: TmPin = { id: tmNewPinId(idsOf(view)), url: tmPinUrl(untracked(this.#routed))!, title: this.#title() };
    this.#write(tmPinsAppend(view, pin), 'add');
    return untracked(this.items).find((item) => item.id === pin.id) as TmPinView;
  }

  /**
   * Renames a pin or files it: a label of 1 to 40 characters is the user's
   * own name for it, and a blank or `null` one restores the derived label; a
   * folder takes the pin at its end, and `null` at the end of the top level.
   *
   * @param id - The pin's id; an id no pin has changes nothing.
   * @param patch - The label and the folder, each kept when left out.
   * @throws RangeError for a label past 40 characters or a folder that does not exist.
   */
  update(id: string, patch: { label?: string | null; folderId?: string | null }): void {
    const view = untracked(this.#view);
    const renamed = patch.label === undefined ? view : tmPinsUpdate(view, id, { label: patch.label });
    const filed = patch.folderId === undefined ? renamed : tmPinsUpdate(renamed, id, { folderId: patch.folderId });
    if (filed === view || this.#held()) {
      return;
    }
    this.#prefs.set(tmPinsKey, filed);
    if (renamed !== view) {
      this.#report('rename', filed);
    }
    if (filed !== renamed) {
      this.#report('move', filed);
    }
  }

  /**
   * Moves a pin or a folder; a folder stays at the top level.
   *
   * @param id - The pin's or folder's id; an id no item has changes nothing.
   * @param to - The target folder, `null` for the top level, and the item's index there once moved.
   * @throws RangeError when a folder would go into a folder, or the target folder does not exist.
   */
  move(id: string, to: { folderId: string | null; index: number }): void {
    this.#write(tmPinsMove(untracked(this.#view), id, to), 'move');
  }

  /**
   * Unpins a page.
   *
   * @param id - The pin's id; an id no pin has changes nothing.
   */
  remove(id: string): void {
    this.#write(tmPinsRemove(untracked(this.#view), id), 'remove');
  }

  /**
   * Shows or hides the pinned bar.
   *
   * @param visible - Whether the bar shows.
   */
  setVisible(visible: boolean): void {
    if (untracked(this.#shown) === visible || this.#held()) {
      return;
    }
    this.#prefs.set(tmPinnedBarVisibleKey, visible);
    this.#telemetry.event('tm.shell.pinned-bar-toggled', { visible });
  }

  /**
   * Adds a folder at the end of the top level.
   *
   * @param name - The folder's name: trimmed, 1 to 40 characters, unique among the folders ignoring case.
   * @returns The new folder's id.
   * @throws RangeError for a name that breaks those rules, or an eleventh folder.
   */
  addFolder(name: string): string {
    const view = untracked(this.#view);
    const id = tmNewPinId(idsOf(view));
    this.#write(tmPinsAddFolder(view, { id, name }), 'folder');
    return id;
  }

  /**
   * Removes a folder; its pins take its place at the top level.
   *
   * @param id - The folder's id; an id no folder has changes nothing.
   */
  removeFolder(id: string): void {
    this.#write(tmPinsRemoveFolder(untracked(this.#view), id), 'folder');
  }

  /**
   * Replaces every pin and folder, as the Manage pins dialog's Save does,
   * overwriting any change made meanwhile. Items that break the model's
   * rules are dropped.
   *
   * @param doc - The new document.
   */
  replace(doc: TmPinsDoc): void {
    if (this.#held()) {
      return;
    }
    const next = tmPinsView(doc);
    this.#prefs.set(tmPinsKey, next);
    this.#report('replace', next);
  }

  /**
   * Hands the launcher the tenant shell's pin controls, for as long as that shell renders.
   *
   * @param controls - The controls.
   * @returns Takes them back.
   * @internal
   */
  ɵattach(controls: TmPinControls): () => void {
    this.#controls.set(controls);
    return () => {
      if (untracked(this.#controls) === controls) {
        this.#controls.set(null);
      }
    };
  }

  /** Writes a document that differs from the stored view, and reports the change. */
  #write(next: TmPinsDoc, op: 'add' | 'remove' | 'move' | 'folder'): void {
    if (next === untracked(this.#view) || this.#held()) {
      return;
    }
    this.#prefs.set(tmPinsKey, next);
    this.#report(op, next);
  }

  /** Whether the edits are held: before the pins load, and while the tenant is under maintenance. */
  #held(): boolean {
    const refusal = untracked(this.ɵrefusal);
    return refusal === 'cold' || refusal === 'frozen';
  }

  /** Reports a change, with how it was made and the pins it leaves. */
  #report(op: 'add' | 'remove' | 'move' | 'rename' | 'folder' | 'replace', next: TmPinsDoc): void {
    this.#telemetry.event('tm.shell.pins-changed', { op, via: currentVia, count: bucket(tmPinsOf(next.items).length) });
  }

  /** The routed page's title as displayed, else its entry's label, else the document's; at most 80 characters. */
  #title(): string {
    const href = untracked(this.#routed).split(/[?#]/, 1)[0];
    const title = untracked(() => this.#contexts.current()?.title() || this.#navigation.byHref(href)?.label) || this.#document.title;
    return cut(title.trim(), TM_PIN_LIMITS.title);
  }

  /** A stored pin's view in a tenant: the one kept for its id while its members, its folder and the tenant stay, else a new one. */
  #pinView(pin: TmPin, folderId: string | null, tenantId: number, kept: ReadonlyMap<string, { readonly key: string; readonly view: TmPinView }>): TmPinView {
    const key = JSON.stringify([tenantId, folderId, pin.url, pin.title, pin.label ?? null]);
    const old = kept.get(pin.id);
    if (old?.key === key) {
      this.#views.set(pin.id, old);
      return old.view;
    }
    const href = tmPinHref(tenantId, pin.url);
    const view: TmPinView = {
      id: pin.id,
      href,
      folderId,
      label: computed(() => tmPinLabel(pin, (entry) => this.#navigation.byHref(entry)?.label, tenantId)),
      state: computed(() => this.#navigation.resolve(href)),
      current: computed(() => this.#currentId() === pin.id),
    };
    this.#views.set(pin.id, { key, view });
    return view;
  }
}
