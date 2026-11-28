// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import { DestroyRef, Directive, ElementRef, inject, input, output } from '@angular/core';

import { ɵTM_DRAG_TYPE, TmDragDrop, type TmDragPayload } from './tm-drag-drop';

/**
 * The `dragstart` events a drag source has already started a drag for, so
 * a source nested inside another source starts its drag once: the
 * innermost source handles the event, and the outer ones see it marked as
 * it bubbles.
 */
const startedDrags = new WeakSet<Event>();

/**
 * Makes an element a native drag source of a typed payload.
 *
 * With a payload, the element is `draggable`. On `dragstart` it allows
 * copy and move, writes the payload's `text` as `text/plain` (so a text
 * field inserts it at the drop caret) and records the payload in
 * {@link TmDragDrop}, from where `tmDropTarget`s read it while the drag
 * hovers. `null` makes the element an ordinary one again.
 *
 * Put the directive on the element the pointer grabs, never on an ancestor
 * of other sources: an outer source never starts a drag that an inner one
 * already started, but it would make every empty spot draggable.
 *
 * @tmGroup collection
 * @tmA11yNotes Dragging is pointer-only. Every drag source needs a path
 *   to the same result that does not drag (WCAG 2.5.7), such as a
 *   reorder handle's keys or an "Add to…" command.
 */
@Directive({
  selector: '[tmDragSource]',
  host: {
    '[attr.draggable]': 'tmDragSource() === null ? null : "true"',
    '(dragstart)': 'onDragStart($event)',
    '(dragend)': 'onDragEnd($event)',
  },
})
export class TmDragSource {
  /** The source element. */
  private readonly host = inject<ElementRef<Element>>(ElementRef).nativeElement;

  /** The drag in flight. */
  private readonly dragDrop = inject(TmDragDrop);

  /** What a drag from this element carries; `null`, not draggable. */
  readonly tmDragSource = input<TmDragPayload | null>(null);

  /**
   * Emits when a drag from this element ends: `dropped` is true when a
   * `tmDropTarget` accepted it or the browser dropped its text (into a
   * text field or another application), false when the drag was
   * cancelled.
   */
  readonly dragEnded = output<{ readonly dropped: boolean }>();

  /** The id of the drag this element started and that has not ended, or `null`. */
  private dragId: string | null = null;

  constructor() {
    // A source removed mid-drag (its row collapsed or re-rendered) never
    // receives its dragend: the service then ends the drag on its own.
    inject(DestroyRef).onDestroy(() => {
      if (this.dragId !== null) {
        this.dragDrop.ɵorphan(this.dragId);
        this.dragId = null;
      }
    });
  }

  /** Starts the drag: the data, the allowed effects and the recorded payload. */
  protected onDragStart(event: DragEvent): void {
    const payload = this.tmDragSource();
    if (payload === null || event.dataTransfer === null || startedDrags.has(event)) {
      return;
    }
    startedDrags.add(event);
    const id = this.dragDrop.ɵbegin(payload, this.host);
    this.dragId = id;
    event.dataTransfer.effectAllowed = 'copyMove';
    event.dataTransfer.setData('text/plain', payload.text);
    event.dataTransfer.setData(ɵTM_DRAG_TYPE, id);
  }

  /** Ends the drag and reports whether it was dropped. */
  protected onDragEnd(event: DragEvent): void {
    const id = this.dragId;
    if (id === null) {
      return;
    }
    this.dragId = null;
    const accepted = this.dragDrop.ɵend(id);
    // A drop outside any tmDropTarget (a text field, another application)
    // reports its effect; a cancelled drag reports 'none'.
    const effect = event.dataTransfer?.dropEffect ?? 'none';
    this.dragEnded.emit({ dropped: accepted || effect !== 'none' });
  }
}
