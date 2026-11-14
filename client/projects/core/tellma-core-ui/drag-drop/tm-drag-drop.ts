// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import { DOCUMENT } from '@angular/common';
import { computed, inject, Injectable, signal, type Signal } from '@angular/core';

/**
 * What a drag carries: the kind drop targets match against, the data the
 * drop hands over, the text a text field inserts, and the name
 * announcements use.
 */
export interface TmDragPayload {
  /** What drop targets match against their `accepts`, such as `'tm.path'`. */
  readonly kind: string;
  /**
   * The data a drop hands to its target. It stays in memory, so it reaches
   * only drop targets in the same document.
   */
  readonly data: unknown;
  /**
   * The drag's `text/plain`: a text field the drag is dropped on inserts it
   * at the drop caret, and so does any other application.
   */
  readonly text: string;
  /** The payload's name in announcements ("Code"). */
  readonly label: string;
}

/** One accepted drop: the payload and the native drop event. */
export interface TmDrop {
  /** The payload of the drag that was dropped. */
  readonly payload: TmDragPayload;
  /** The native `drop` event, for its position. */
  readonly event: DragEvent;
}

/**
 * The `DataTransfer` type a drag source writes its drag id under, beside
 * the payload's `text/plain`. A drop target accepts a drop only when the id
 * it reads names the drag in flight.
 * @internal
 */
export const ɵTM_DRAG_TYPE = 'application/x-tellma-drag';

/** The drag in flight, as the service records it. */
interface TmActiveDrag {
  /** The id written into the drag's data. */
  readonly id: string;
  /** What the drag carries. */
  readonly payload: TmDragPayload;
  /** The element the drag started on. */
  readonly source: Element;
  /** Whether a drop target accepted the drop. */
  dropped: boolean;
}

/** Makes the drag ids unique within the page's lifetime. */
let nextDragId = 0;

/**
 * The drag in flight. Native drag and drop cannot read a drag's data while
 * it hovers (only on drop), so drag sources record their payload here and
 * drop targets read it to decide, at every `dragover`, whether they accept
 * the drag.
 *
 * `current` is `null` between drags and while something other than a
 * `tmDragSource` drags (a file, a text selection, a drag from another
 * window), so a target can show a hint only while one of its kinds is in
 * flight.
 *
 * @tmGroup collection
 * @tmA11yNotes Every drag needs a path to the same result that does not
 *   drag (WCAG 2.5.7): a handle's keys, an activation, a checkbox or an
 *   "Add to…" command. The service itself has no UI.
 */
@Injectable({ providedIn: 'root' })
export class TmDragDrop {
  /** The document the drags run in. */
  private readonly document = inject(DOCUMENT);

  /** The drag in flight, or `null`. */
  private readonly active = signal<TmActiveDrag | null>(null);

  /** The payload of the drag in flight, or `null` while no `tmDragSource` drags. */
  readonly current: Signal<TmDragPayload | null> = computed(() => this.active()?.payload ?? null);

  /**
   * The id of the drag in flight, or `null`.
   * @internal
   */
  readonly ɵcurrentId: Signal<string | null> = computed(() => this.active()?.id ?? null);

  /**
   * Records a drag that starts on `source` and returns the id the source
   * writes into the drag's data. A drag already recorded is replaced: only
   * one drag runs at a time.
   * @internal
   */
  ɵbegin(payload: TmDragPayload, source: Element): string {
    nextDragId += 1;
    const id = `tm-drag-${nextDragId}-${Math.random().toString(36).slice(2, 10)}`;
    this.active.set({ id, payload, source, dropped: false });
    return id;
  }

  /**
   * Ends the drag `id` and answers whether a drop target accepted it. A drag
   * that is no longer the one in flight answers `false` and changes
   * nothing.
   * @internal
   */
  ɵend(id: string): boolean {
    const active = this.active();
    if (active === null || active.id !== id) {
      return false;
    }
    this.active.set(null);
    return active.dropped;
  }

  /**
   * The element the drag in flight started on, or `null`.
   * @internal
   */
  ɵsource(): Element | null {
    return this.active()?.source ?? null;
  }

  /**
   * The payload of the drag `id` when it is the drag in flight, else
   * `null`: a drop from another window, a file or a stale drag carries an
   * id (or none) that does not match.
   * @internal
   */
  ɵpayloadOf(id: string): TmDragPayload | null {
    const active = this.active();
    return active !== null && active.id === id ? active.payload : null;
  }

  /**
   * Records that a drop target accepted the drag `id`.
   * @internal
   */
  ɵmarkDropped(id: string): void {
    const active = this.active();
    if (active !== null && active.id === id) {
      active.dropped = true;
    }
  }

  /**
   * Called by a drag source destroyed while its drag is in flight. The
   * source's `dragend` can no longer arrive, so the drag ends at the first
   * sign that no drag runs any more: a pointer that moves or presses (the
   * engines send no pointer events during a drag), or a drop or drag end
   * that reaches the window.
   * @internal
   */
  ɵorphan(id: string): void {
    const window = this.document.defaultView;
    if (window === null) {
      this.ɵend(id);
      return;
    }
    const capture = ['pointermove', 'pointerdown'] as const;
    const bubble = ['drop', 'dragend'] as const;
    const finish = (): void => {
      for (const type of capture) {
        window.removeEventListener(type, finish, true);
      }
      for (const type of bubble) {
        window.removeEventListener(type, finish);
      }
      this.ɵend(id);
    };
    for (const type of capture) {
      window.addEventListener(type, finish, true);
    }
    // In the bubble phase, so a drop target handles a drop before the drag
    // it accepts is forgotten.
    for (const type of bubble) {
      window.addEventListener(type, finish);
    }
  }
}
