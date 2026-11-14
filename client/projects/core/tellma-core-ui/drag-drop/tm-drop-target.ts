// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import {
  computed,
  Directive,
  ElementRef,
  inject,
  input,
  output,
  signal,
  type Signal,
} from '@angular/core';

import { ɵTM_DRAG_TYPE, TmDragDrop, type TmDragPayload, type TmDrop } from './tm-drag-drop';

/**
 * The drag events a drop target has already answered. A target nested
 * inside another target answers first (the events bubble from it), so the
 * outer target leaves a claimed event alone: one drop is accepted once, by
 * the innermost target that accepts its kind.
 */
const claimedEvents = new WeakSet<Event>();

/**
 * Makes an element a native drop target for the payload kinds it
 * `accepts`.
 *
 * While a `tmDragSource` drag of an accepted kind hovers, the element
 * allows the drop, `over` is true and the host carries the class
 * `tm-drop-target--over` (the global stylesheet
 * `styles/tm-drop-target.css` tints it). A drop emits `dropped` with the
 * payload. Drags of other kinds, files and drags from another window or
 * application pass through untouched: the browser's own handling applies,
 * and a drop whose drag id does not name the drag in flight is ignored.
 * A drop that arrives without its drag id (some engines deliver only the
 * standard types) is the drag in flight only when it carries that drag's
 * text.
 *
 * A drag that starts inside the target (a row moved within its own list)
 * shows the move cursor; one from outside shows the copy cursor.
 *
 * @tmGroup collection
 * @tmA11yNotes Dropping is pointer-only. Whatever a drop does needs a
 *   path that does not drag (WCAG 2.5.7). The over state is a tint plus
 *   an outline, so it survives forced colors.
 */
@Directive({
  selector: '[tmDropTarget]',
  host: {
    '[class.tm-drop-target--over]': 'over()',
    '(dragenter)': 'onDragEnter($event)',
    '(dragover)': 'onDragOver($event)',
    '(dragleave)': 'onDragLeave()',
    '(drop)': 'onDrop($event)',
  },
})
export class TmDropTarget {
  /** The target element. */
  private readonly host = inject<ElementRef<Element>>(ElementRef).nativeElement;

  /** The drag in flight. */
  private readonly dragDrop = inject(TmDragDrop);

  /** The payload kinds this target accepts, such as `['tm.path']`. Default none. */
  readonly accepts = input<readonly string[]>([]);

  /** Emits each accepted drop. */
  readonly dropped = output<TmDrop>();

  /** The drag whose latest hover this target accepted, or `null`. */
  private readonly acceptedDrag = signal<string | null>(null);

  /** Whether a drag of an accepted kind hovers the target. */
  readonly over: Signal<boolean> = computed(() => {
    const id = this.acceptedDrag();
    return id !== null && id === this.dragDrop.ɵcurrentId();
  });

  /**
   * How many `dragenter`s minus `dragleave`s the target has seen during
   * `depthDrag`: the events fire for every child crossed, so the drag has
   * left the target only when the count is back at zero.
   */
  private depth = 0;

  /** The drag `depth` counts for; a new drag starts the count afresh. */
  private depthDrag: string | null = null;

  /** A drag enters the target or one of its children. */
  protected onDragEnter(event: DragEvent): void {
    const id = this.dragDrop.ɵcurrentId();
    if (id !== this.depthDrag) {
      this.depthDrag = id;
      this.depth = 0;
    }
    this.depth += 1;
    this.answer(event);
  }

  /** A drag moves over the target: it decides again, as the pointer may now be over an inner target. */
  protected onDragOver(event: DragEvent): void {
    this.answer(event);
  }

  /** A drag leaves the target or one of its children. */
  protected onDragLeave(): void {
    this.depth = Math.max(0, this.depth - 1);
    if (this.depth === 0) {
      this.acceptedDrag.set(null);
    }
  }

  /** A drop on the target: accepted when its id names the drag in flight and its kind is accepted. */
  protected onDrop(event: DragEvent): void {
    this.depth = 0;
    this.acceptedDrag.set(null);
    if (claimedEvents.has(event)) {
      return;
    }
    const drag = this.droppedDrag(event.dataTransfer);
    if (drag === null || !this.accepts().includes(drag.payload.kind)) {
      return;
    }
    claimedEvents.add(event);
    event.preventDefault();
    this.dragDrop.ɵmarkDropped(drag.id);
    this.dropped.emit({ payload: drag.payload, event });
  }

  /**
   * The drag a drop delivers, when it is the drag in flight: named by the
   * drag id it carries, or, from an engine that delivers only the standard
   * types (WebKit can drop the id on the way), carrying the in-flight
   * drag's text. A file, a drag from another window or a stale drag is
   * `null`.
   */
  private droppedDrag(data: DataTransfer | null): { id: string; payload: TmDragPayload } | null {
    if (data === null) {
      return null;
    }
    const id = data.getData(ɵTM_DRAG_TYPE);
    if (id !== '') {
      const payload = this.dragDrop.ɵpayloadOf(id);
      return payload === null ? null : { id, payload };
    }
    // No id: only the drag in flight with the very text this drop carries.
    const current = this.dragDrop.current();
    const currentId = this.dragDrop.ɵcurrentId();
    return current !== null && currentId !== null && data.getData('text/plain') === current.text
      ? { id: currentId, payload: current }
      : null;
  }

  /**
   * Allows the drop when the drag in flight is of an accepted kind and no
   * inner target answered the event first.
   */
  private answer(event: DragEvent): void {
    const payload = this.acceptable();
    if (payload === null || claimedEvents.has(event)) {
      this.acceptedDrag.set(null);
      return;
    }
    claimedEvents.add(event);
    event.preventDefault();
    if (event.dataTransfer !== null) {
      const source = this.dragDrop.ɵsource();
      event.dataTransfer.dropEffect =
        source !== null && this.host.contains(source) ? 'move' : 'copy';
    }
    this.acceptedDrag.set(this.dragDrop.ɵcurrentId());
  }

  /** The payload in flight when this target accepts its kind, else `null`. */
  private acceptable(): TmDragPayload | null {
    const payload = this.dragDrop.current();
    return payload !== null && this.accepts().includes(payload.kind) ? payload : null;
  }
}
