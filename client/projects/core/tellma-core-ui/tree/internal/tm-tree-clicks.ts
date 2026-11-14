// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import { Tree } from '@angular/aria/tree';
import { Directive, inject, input } from '@angular/core';

/** aria's model of one tree item. */
type ItemPattern = Tree<string>['_pattern']['items'] extends () => (infer I)[] ? I : never;

/**
 * Adapts the aria tree's pointer and focus handling to `tm-tree`.
 *
 * - **Clicks.** aria's own click selects and toggles expansion wherever
 *   the row is clicked. Here only the twisty expands and collapses, while
 *   a click on the row's content selects (`single`), toggles (`multiple`,
 *   Shift for a range) or only moves focus (`none`), so a double-click can
 *   expand a branch without its two clicks undoing it.
 * - **Focus.** A row that takes focus by other means than aria's keys (a
 *   press, or a script calling `focus()`) becomes aria's active row, so the
 *   keys that follow act on the row that has focus.
 * - **Drags.** A press focuses its row before a drag can start; when a drag
 *   does start, focus and the active row go back to where they were, so a
 *   drag never changes either.
 *
 * @internal Used by `TmTree` only.
 */
@Directive({
  selector: 'ul[tmTreeClicks]',
  host: {
    '(focusin)': 'onFocusIn($event)',
    '(pointerdown)': 'onPointerDown()',
    '(dragstart)': 'onDragStart()',
  },
})
export class ɵTmTreeClicks {
  /** The tree's selection mode, which decides what a click on a row's content does. */
  readonly selection = input.required<'none' | 'single' | 'multiple'>({ alias: 'tmTreeClicks' });

  /** aria's tree model. */
  private readonly pattern = inject<Tree<string>>(Tree, { self: true })._pattern;

  /** The focus and the active row before the latest press, restored if a drag starts. */
  private beforePress: {
    readonly focus: Element | null;
    readonly active: ItemPattern | undefined;
  } | null = null;

  constructor() {
    const pattern = this.pattern;
    // The aria host listener calls `_pattern.onClick` at each click, so an
    // own property here replaces the prototype's handler for this tree.
    pattern.onClick = (event: PointerEvent): void => {
      if (pattern.disabled()) {
        return;
      }
      pattern.hasBeenInteracted.set(true);
      const target = event.target instanceof Element ? event.target : null;
      const item = this.itemOf(target);
      if (target === null || item === undefined) {
        return;
      }
      const behavior = pattern.treeBehavior;
      if (target.closest('.tm-tree__twisty') !== null) {
        behavior.goto(item);
        behavior.toggleExpansion(item);
        return;
      }
      const mode = this.selection();
      behavior.goto(
        item,
        mode === 'single'
          ? { selectOne: true }
          : mode === 'multiple'
            ? event.shiftKey
              ? { selectRange: true }
              : { toggle: true }
            : undefined,
      );
    };
  }

  /** A row that takes focus becomes the active row. */
  protected onFocusIn(event: FocusEvent): void {
    const item = this.itemOf(event.target);
    if (item !== undefined && this.pattern.activeItem() !== item) {
      this.pattern.activeItem.set(item);
    }
  }

  /** Remembers the focus and the active row before a press moves them. */
  protected onPointerDown(): void {
    const element = this.pattern.element();
    this.beforePress = {
      focus: element.ownerDocument.activeElement,
      active: this.pattern.activeItem(),
    };
  }

  /** A drag that starts puts the focus and the active row back. */
  protected onDragStart(): void {
    const before = this.beforePress;
    this.beforePress = null;
    if (before === null) {
      return;
    }
    if (before.active !== undefined && this.pattern.activeItem() !== before.active) {
      this.pattern.activeItem.set(before.active);
    }
    const document = this.pattern.element().ownerDocument;
    const previous =
      before.focus instanceof HTMLElement &&
      before.focus !== document.body &&
      before.focus.isConnected
        ? before.focus
        : null;
    if (previous === null) {
      // Nothing had focus before the press: the press's focus goes too.
      (document.activeElement as HTMLElement | null)?.blur();
    } else if (previous !== document.activeElement) {
      previous.focus({ preventScroll: true });
    }
  }

  /** The aria item a target is in, if it is one of this tree's rows. */
  private itemOf(target: EventTarget | null): ItemPattern | undefined {
    const row = target instanceof Element ? target.closest('[role="treeitem"]') : null;
    return row === null
      ? undefined
      : this.pattern.items().find((candidate) => candidate.element() === row);
  }
}
