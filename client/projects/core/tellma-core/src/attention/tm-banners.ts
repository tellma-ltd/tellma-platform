// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import { DestroyRef, inject, Injectable, signal, type Signal } from '@angular/core';

import { TM_API_HOST } from '../api/tm-api-host';
import { TM_DEV_MODE } from '../app/tm-dev-mode';
import { TM_NOW } from '../app/tm-now';
import { TmTelemetry } from '../observe/tm-telemetry';
import { TmInputIdle } from './tm-input-idle';

// The banner service (spec 0028 §9.1-§9.3). The shell's banner row shows one
// entry at a time: the head by precedence (level, then the order `show` first
// ran), or the entry the user paged to. Every change recomputes two views:
// the head, which the banner host announces at once, and what the row
// renders, which follows the head except that opening and closing the row
// waits for the input-idle gate, so the page never moves under a keystroke
// or a click. A message swap in an open row, the user's own dismiss, and any
// change while the page is hidden apply at once.

declare const ngDevMode: unknown;

/**
 * A banner's level, which orders the row: 1 connection lost, 2 a critical
 * announcement (reserved), 3 an update, 4 a warning, 5 information. Levels
 * 1-3 are the platform's.
 */
export type TmBannerLevel = 1 | 2 | 3 | 4 | 5;

/** One banner, as {@link TmBanners.show} takes it. */
export interface TmBannerOptions {
  /** The banner's id, such as `tm.offline` or `acme.period-closing`; showing a live id again updates it. */
  readonly id: string;
  /** Its level; levels 1-3 are the platform's, and a distribution's banner there throws in development. */
  readonly level: TmBannerLevel;
  /** Its tone. */
  readonly tone: 'info' | 'warning' | 'error';
  /** The localized message: at most 45 characters, so one 32 px line fits a 390 px screen. */
  readonly message: Signal<string>;
  /** An icon by registry name; unset, the tone's own. */
  readonly icon?: string;
  /** The action button, such as "Reload" or "Learn more". */
  readonly action?: {
    /** The button's localized label. */
    readonly label: Signal<string>;
    /** What the button does. */
    readonly run: () => void;
  };
  /** Whether the user may dismiss it; never for levels 1 and 2. */
  readonly dismissible: boolean;
}

/** A handle on one shown banner. */
export interface TmBannerRef {
  /**
   * Changes the banner in place. Does nothing once it is closed.
   *
   * @param patch - The members to change.
   */
  update(patch: Partial<Omit<TmBannerOptions, 'id' | 'level'>>): void;
  /** Removes the banner. Does nothing once it is closed. */
  close(): void;
}

/** What the banner row shows: one banner, and where it sits among the banners the user can page through. */
export interface TmBannerView {
  /** The banner. */
  readonly options: TmBannerOptions;
  /** Its position among the shown banners, counting from 1. */
  readonly index: number;
  /** How many banners the user can page through. */
  readonly count: number;
}

/** One live banner. */
interface TmBannerEntry {
  /** Its options, as last shown or updated. */
  options: TmBannerOptions;
  /** What its dismissal is scoped to, such as the update banner's version. */
  scope: string | undefined;
  /** The order `show` first ran in. */
  readonly order: number;
  /** When `show` first ran, for the time to show. */
  readonly queuedAt: number;
  /** Whether the row has shown it, so its time to show is reported once. */
  shown: boolean;
  /** Its handle. */
  readonly ref: TmBannerRef;
}

/** Whether two views show the same thing. */
function sameView(a: TmBannerView | null, b: TmBannerView | null): boolean {
  return a === b || (a !== null && b !== null && a.options === b.options && a.index === b.index && a.count === b.count);
}

/**
 * The app's banners: conditions the shell's banner row shows one at a time,
 * by a fixed precedence, with a pager for the rest. The row opens and closes
 * only at an idle moment (2 s without input), so the page never moves under
 * the user's hands. A root service.
 *
 * @example
 * ```ts
 * const ref = inject(TmBanners).show({ id: 'acme.period-closing', level: 4, tone: 'warning', dismissible: true,
 *   message: computed(() => t('Acme_Banner_PeriodClosing')) });
 * // When the condition ends:
 * ref.close();
 * ```
 */
@Injectable({ providedIn: 'root' })
export class TmBanners {
  readonly #idle = inject(TmInputIdle);
  readonly #host = inject(TM_API_HOST);
  readonly #now = inject(TM_NOW);
  readonly #telemetry = inject(TmTelemetry);
  readonly #devMode = inject(TM_DEV_MODE);

  // The live banners in the order `show` first ran, and the next order.
  readonly #entries: TmBannerEntry[] = [];
  #order = 0;
  // The ids the user dismissed, with the scope each dismissal holds for.
  readonly #dismissed = new Map<string, string | undefined>();
  // The banner the user paged to, until it closes or is dismissed.
  #paged: TmBannerEntry | null = null;
  // Whether a settle waits for the idle gate.
  #waiting = false;

  readonly #open = signal(false);
  readonly #current = signal<TmBannerView | null>(null, { equal: sameView });
  readonly #head = signal<TmBannerView | null>(null, { equal: sameView });

  /** What the row renders now, after the idle gate; `null` while closed, or open and emptied. */
  readonly current: Signal<TmBannerView | null> = this.#current.asReadonly();

  /** Whether the row is open; the shell sets the banner row's height from it. */
  readonly visible: Signal<boolean> = this.#open.asReadonly();

  /**
   * What the row is to show, before the idle gate: the banner host announces
   * it the moment it changes. Internal.
   */
  readonly ɵhead: Signal<TmBannerView | null> = this.#head.asReadonly();

  /** Creates the service; a page that turns hidden takes any waiting change at once. */
  constructor() {
    const stop = this.#host.listen('document', 'visibilitychange', () => this.#settle(false));
    inject(DestroyRef).onDestroy(stop);
  }

  /**
   * Shows a banner, or updates the live banner of the same id. A banner of a
   * higher level (a lower number) than the one the row shows takes the row at
   * once; one of a lower or equal level adds to the pager's count.
   *
   * @param options - The banner.
   * @returns Its handle.
   */
  show(options: TmBannerOptions): TmBannerRef {
    return this.ɵshow(options);
  }

  /**
   * Shows a banner whose dismissal holds only for a scope: dismissed, it
   * stays hidden while it is shown again with the same scope, and returns
   * with another. The update banner scopes its dismissal to the version.
   * Internal.
   *
   * @param options - The banner.
   * @param scope - What its dismissal is scoped to.
   * @returns Its handle.
   */
  ɵshow(options: TmBannerOptions, scope?: string): TmBannerRef {
    const checked = this.#check(options);
    let entry = this.#entries.find((live) => live.options.id === options.id);
    if (entry === undefined) {
      const created: TmBannerEntry = {
        options: checked,
        scope,
        order: this.#order++,
        queuedAt: this.#now(),
        shown: false,
        ref: {
          update: (patch) => {
            if (this.#entries.includes(created)) {
              created.options = this.#check({ ...created.options, ...patch, id: created.options.id, level: created.options.level });
              this.#settle(false);
            }
          },
          close: () => this.#remove(created),
        },
      };
      entry = created;
      this.#entries.push(entry);
    } else {
      entry.options = checked;
      entry.scope = scope;
    }
    // A banner that outranks the shown one takes the row from the user's page.
    const shown = this.#target();
    if (shown !== null && checked.level < shown.options.level) {
      this.#paged = null;
    }
    this.#settle(false);
    return entry.ref;
  }

  /**
   * Dismisses the banner the row shows, for the rest of this tab's life (or
   * of its scope). The row closes at once when it was the last. Internal:
   * the banner host's dismiss button.
   *
   * @param id - The banner's id.
   */
  ɵdismiss(id: string): void {
    const entry = this.#entries.find((live) => live.options.id === id);
    if (entry?.options.dismissible === true) {
      this.#dismissed.set(id, entry.scope);
      if (this.#paged === entry) {
        this.#paged = null;
      }
      this.#settle(true);
    }
  }

  /**
   * Pages the row to the next or previous banner, wrapping around. The
   * choice sticks until that banner closes or is dismissed. Internal: the
   * banner host's pager.
   *
   * @param step - `1` for the next banner, `-1` for the previous.
   */
  ɵpage(step: 1 | -1): void {
    const list = this.#showable();
    const shown = this.#target();
    if (shown !== null && list.length > 1) {
      this.#paged = list[(shown.index - 1 + step + list.length) % list.length];
      this.#settle(false);
    }
  }

  /**
   * Checks a banner's options: a distribution's banner at a platform level,
   * or a dismissible banner at level 1 or 2, throws in development. In
   * production the banner shows, never dismissible at levels 1 and 2.
   */
  #check(options: TmBannerOptions): TmBannerOptions {
    // The messages exist only in development builds, which fold them away with the throw.
    if (typeof ngDevMode !== 'undefined' && ngDevMode && this.#devMode()) {
      if (options.level <= 3 && !options.id.startsWith('tm.')) {
        throw new Error(`The banner '${options.id}' uses level ${options.level}: levels 1-3 are the platform's; use 4 or 5.`);
      }
      if (options.level <= 2 && options.dismissible) {
        throw new Error(`The banner '${options.id}' is dismissible at level ${options.level}: levels 1 and 2 never are.`);
      }
    }
    return options.level <= 2 && options.dismissible ? { ...options, dismissible: false } : options;
  }

  /** Removes a banner; the row's next banner shows at once, and an emptied row closes at the gate. */
  #remove(entry: TmBannerEntry): void {
    const at = this.#entries.indexOf(entry);
    if (at >= 0) {
      this.#entries.splice(at, 1);
      if (this.#paged === entry) {
        this.#paged = null;
      }
      this.#settle(false);
    }
  }

  /** The banners the user can see and page through, in precedence order. */
  #showable(): TmBannerEntry[] {
    return this.#entries
      .filter((entry) => !this.#dismissed.has(entry.options.id) || this.#dismissed.get(entry.options.id) !== entry.scope)
      .sort((a, b) => a.options.level - b.options.level || a.order - b.order);
  }

  /** What the row is to show: the banner the user paged to while it shows, else the head. */
  #target(): TmBannerView | null {
    const list = this.#showable();
    const paged = this.#paged === null ? -1 : list.indexOf(this.#paged);
    const index = paged >= 0 ? paged : 0;
    return list.length === 0 ? null : { options: list[index].options, index: index + 1, count: list.length };
  }

  /**
   * Brings the views up to date. The head follows at once; the row's height
   * changes only at once (the user's own dismiss), at an idle moment or
   * while the page is hidden, and otherwise waits for the gate. An open row
   * whose last banner went shows nothing until it closes.
   *
   * @param now - Whether the change is the user's own, which applies at once.
   */
  #settle(now: boolean): void {
    const target = this.#target();
    this.#head.set(target);
    const wanted = target !== null;
    if (wanted !== this.#open() && (now || this.#idle.idle() || !this.#host.visible())) {
      this.#open.set(wanted);
    }
    this.#current.set(this.#open() ? target : null);
    if (wanted !== this.#open() && !this.#waiting) {
      this.#waiting = true;
      void this.#idle.whenIdle().then(() => {
        this.#waiting = false;
        this.#settle(false);
      });
    }
    // The time from the first show to the row showing it, once per banner.
    const entry = target === null || !this.#open() ? undefined : this.#entries.find((live) => live.options === target.options);
    if (entry !== undefined && !entry.shown) {
      entry.shown = true;
      this.#telemetry.timing('tm.attention.banner', this.#now() - entry.queuedAt, {
        id: entry.options.id,
        level: entry.options.level,
      });
    }
  }
}
