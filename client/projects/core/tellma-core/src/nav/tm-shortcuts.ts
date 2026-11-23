// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import { DestroyRef, DOCUMENT, inject, Injectable, InjectionToken, signal, type Signal } from '@angular/core';
import { tmIsApplePlatform, tmKeysMatch } from '@tellma/core-ui/kbd';
import { ɵtmIsBusy, ɵtmOpenModals } from '@tellma/core-ui/overlay';

import { TM_DEV_MODE } from '../app/tm-dev-mode';
import { TmTelemetry } from '../observe/tm-telemetry';
import { TmSession } from '../session/tm-session';
import {
  TM_PRESENTATION_EXIT_ID,
  TM_RESERVED_SHORTCUTS,
  tmKeyParts,
  tmRunsInFields,
  TmShortcutConflictError,
  tmShortcutRefusal,
  tmSameKeys,
  type TmKeyParts,
  type TmShortcut,
  type TmShortcutRefusal,
} from './tm-shortcut';

// The shortcut registry (spec 0027 §4.2, §4.3). One bubble-phase keydown
// listener on the document serves every app-level shortcut, so a focused
// grid, editor, menu or dialog sees each key first and wins it by preventing
// the event. Registrations are checked once, against the key families, the
// grid's and the browser's keys and the live shortcuts; the dispatch then
// only skips, matches and runs.

declare const ngDevMode: unknown;

/**
 * Whether the platform is Apple's, where `Mod` is ⌘ and Option types
 * characters. Internal: a spec provides another answer.
 */
export const TM_APPLE_PLATFORM = new InjectionToken<boolean>('TM_APPLE_PLATFORM', {
  providedIn: 'root',
  factory: tmIsApplePlatform,
});

/** One live registration: the shortcut, its parsed keys and the dialog it belongs to, if any. */
interface TmRegistration {
  /** The shortcut as registered. */
  readonly shortcut: TmShortcut;
  /** Its parsed keys. */
  readonly parts: TmKeyParts;
  /** Whether it runs in an editable field. */
  readonly inFields: boolean;
  /** An element of the dialog whose page shortcut it is, which lets it run while that dialog is the top modal. */
  readonly dialog: Element | null;
}

// The editable targets: where typing goes. A text input (not a checkbox, a
// button or another control that types nothing), a text area, a select, an
// editable region, and an open cell editor of a grid, inline or in its pane.
const EDITABLE = 'input, textarea, select, .tm-grid__cell--editing, tm-grid-editor-pane';
const TYPES_NOTHING = /^(checkbox|radio|button|submit|reset|image|file|range|color|hidden)$/;

/** Whether a keydown's target is an editable field. */
function inEditable(target: EventTarget | null): boolean {
  if (!(target instanceof HTMLElement)) {
    return false;
  }
  if (target.isContentEditable) {
    return true;
  }
  const field = target.closest(EDITABLE);
  return field !== null && !(field instanceof HTMLInputElement && TYPES_NOTHING.test(field.type));
}

/**
 * The one keyboard listener for app-level shortcuts: it owns the reserved
 * keys, refuses conflicting registrations and lists every live shortcut for
 * the keyboard shortcuts dialog. A root service.
 *
 * A shortcut runs from a `keydown` the document receives in the bubble
 * phase, so the focused component handles its own keys first. The registry
 * ignores a key that something already prevented, one that is part of a
 * composition or an auto-repeat, and every key while the busy overlay runs
 * or while the session is not active. While a modal dialog is open, only
 * shortcuts registered `overModals` run, besides the page shortcuts of that
 * dialog's own page context. In an editable field, only `inFields`
 * shortcuts run; on an Apple platform, a key that Option turns into another
 * character in a field (`∏` from Option+Shift+P) is typing, never a
 * shortcut. Matching goes through `tmKeysMatch`, so a shortcut works on
 * every keyboard layout (`Mod+K` while an Arabic layout types `ن`).
 *
 * A registration whose keys lie outside its family, are a key a focused grid
 * handles with a modifier, a key the browser keeps, or the keys of a live
 * shortcut, is refused: development throws {@link TmShortcutConflictError},
 * production drops it and reports `tm.shortcut.conflict`.
 *
 * @example
 * ```ts
 * inject(TmShortcuts).register({
 *   id: 'acme.credit-review.approve-next', keys: 'Mod+Shift+M',
 *   labelKey: 'Acme_CreditReview_ApproveNext', scope: 'page', run: () => this.approveNext(),
 * });
 * ```
 */
@Injectable({ providedIn: 'root' })
export class TmShortcuts {
  readonly #telemetry = inject(TmTelemetry);
  readonly #session = inject(TmSession);
  readonly #devMode = inject(TM_DEV_MODE);
  readonly #apple = inject(TM_APPLE_PLATFORM);
  // The live registrations, which the dispatch reads, and the shortcuts they
  // hold, which the active list shows; both change together.
  #live: readonly TmRegistration[] = [];
  readonly #active = signal<readonly TmShortcut[]>([]);

  /** The live shortcuts, in registration order. */
  readonly active: Signal<readonly TmShortcut[]> = this.#active.asReadonly();

  /** Starts listening for shortcuts on the document, until the app ends. */
  constructor() {
    const document = inject(DOCUMENT);
    const listener = (event: KeyboardEvent): void => this.#dispatch(event);
    document.addEventListener('keydown', listener);
    inject(DestroyRef).onDestroy(() => document.removeEventListener('keydown', listener));
  }

  /**
   * Registers a shortcut until `destroyRef` ends.
   *
   * @param shortcut - The shortcut.
   * @param destroyRef - What ends the registration; by default, the caller's injection context's.
   * @throws TmShortcutConflictError in development, when the registration is refused.
   */
  register(shortcut: TmShortcut, destroyRef: DestroyRef = inject(DestroyRef)): void {
    this.#register(shortcut, destroyRef, null);
  }

  /**
   * Registers a page shortcut of a dialog's own page context until
   * `destroyRef` ends. While modal dialogs are open, it runs when its dialog
   * is the topmost one, as a shortcut over modals would. Not for app code:
   * the page actions register their dialogs' shortcuts through it.
   *
   * @param dialog - An element inside the dialog, such as its content's host.
   * @param shortcut - The shortcut.
   * @param destroyRef - What ends the registration; by default, the caller's injection context's.
   * @throws TmShortcutConflictError in development, when the registration is refused.
   */
  ɵregisterInDialog(dialog: Element, shortcut: TmShortcut, destroyRef: DestroyRef = inject(DestroyRef)): void {
    this.#register(shortcut, destroyRef, dialog);
  }

  /** Checks one registration and, unless it is refused, keeps it until `destroyRef` ends. */
  #register(shortcut: TmShortcut, destroyRef: DestroyRef, dialog: Element | null): void {
    // Section: the checks, once per registration.
    let parts: TmKeyParts;
    try {
      parts = tmKeyParts(shortcut.keys);
    } catch (error) {
      this.#refuse(shortcut, 'invalid', error);
      return;
    }
    const live = this.#live;
    const refusal = tmShortcutRefusal(
      shortcut,
      parts,
      live.map((registration) => registration.parts),
    );
    if (refusal !== null) {
      this.#refuse(shortcut, refusal, live.find((entry) => tmSameKeys(parts, entry.parts))?.shortcut.id);
      return;
    }

    // Section: keep it until its owner ends.
    const registration: TmRegistration = { shortcut, parts, inFields: tmRunsInFields(shortcut, parts), dialog };
    this.#set([...live, registration]);
    destroyRef.onDestroy(() => this.#set(this.#live.filter((entry) => entry !== registration)));
  }

  /** Replaces the live registrations, and the active list with their shortcuts. */
  #set(live: readonly TmRegistration[]): void {
    this.#live = live;
    this.#active.set(live.map((entry) => entry.shortcut));
  }

  /**
   * Refuses a registration: in development by throwing, in production by
   * dropping it and reporting the conflict. The messages exist only in
   * development builds, which fold them away with the throw.
   */
  #refuse(shortcut: TmShortcut, reason: TmShortcutRefusal, detail: unknown): void {
    if (typeof ngDevMode !== 'undefined' && ngDevMode && this.#devMode()) {
      throw refusalError(shortcut, reason, detail);
    }
    this.#telemetry.event('tm.shortcut.conflict', { id: shortcut.id, reason });
  }

  /** Runs the first live shortcut a keydown matches, newest first, under the dispatch rules. */
  #dispatch(event: KeyboardEvent): void {
    // Section: the keys the registry never handles.
    if (
      event.defaultPrevented ||
      event.isComposing ||
      event.key === 'Process' ||
      event.repeat ||
      ɵtmIsBusy() ||
      this.#session.state() !== 'active'
    ) {
      return;
    }
    const live = this.#live;
    if (live.length === 0) {
      return;
    }

    // Section: where the key went, read once.
    const editable = inEditable(event.target);
    const modals = ɵtmOpenModals();
    const topModal = modals.length === 0 ? null : modals[modals.length - 1];
    // On an Apple platform, Option without ⌘ or Ctrl types a character.
    const optionTyped =
      this.#apple &&
      event.altKey &&
      !event.metaKey &&
      !event.ctrlKey &&
      (event.key === 'Dead' || [...event.key].length === 1);

    // Section: match and run.
    for (let index = live.length - 1; index >= 0; index--) {
      const { shortcut, parts, inFields, dialog } = live[index];
      if (!tmKeysMatch(event, shortcut.keys, this.#apple)) {
        continue;
      }
      if (editable && (!inFields || (optionTyped && event.key.toLowerCase() !== parts.key))) {
        continue;
      }
      if (topModal !== null && !shortcut.overModals && !(dialog !== null && topModal.contains(dialog))) {
        continue;
      }
      if (shortcut.run(event) === false) {
        continue;
      }
      event.preventDefault();
      event.stopPropagation();
      this.#telemetry.event('tm.shortcut.used', { id: shortcut.id }, { sampled: true });
      return;
    }
  }
}

/**
 * The development error for a refused registration, saying what to change.
 *
 * @param shortcut - The refused shortcut.
 * @param reason - Why it was refused.
 * @param detail - The grammar's error for `invalid`, the id of the live shortcut that holds the keys for `live`.
 */
function refusalError(shortcut: TmShortcut, reason: TmShortcutRefusal, detail: unknown): TmShortcutConflictError {
  const { id, keys } = shortcut;
  const head = `TmShortcuts refused "${id}" with ${keys}: `;
  let message: string;
  switch (reason) {
    case 'invalid':
      return new TmShortcutConflictError(id, reason, head + String((detail as Error).message), { cause: detail });
    case 'family': {
      const reserved = TM_RESERVED_SHORTCUTS.find((entry) => entry.id === id);
      message =
        reserved !== undefined
          ? `the platform reserves ${reserved.keys} for it.`
          : id.startsWith('tm.')
            ? "'tm.' ids belong to the platform's reserved shortcuts, and this is not one of them."
            : 'a page shortcut takes Mod+<key> or Mod+Shift+<key> (except Mod+K and Mod+/), ' +
              `F2, Alt+PageUp or Alt+PageDown, and only "${TM_PRESENTATION_EXIT_ID}" takes Escape.`;
      break;
    }
    case 'grid':
      message = 'a focused grid handles these keys itself.';
      break;
    case 'browser':
      message = 'the browser keeps these keys for itself.';
      break;
    case 'live':
      message = `the live shortcut "${String(detail)}" already takes these keys.`;
      break;
  }
  return new TmShortcutConflictError(id, reason, head + message);
}
