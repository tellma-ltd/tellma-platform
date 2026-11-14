// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import { Directive, inject, input, TemplateRef } from '@angular/core';

/** A move of one item within a reorder list, by 0-based positions. */
export interface TmReorderMove<T> {
  /** The item that moved. */
  readonly item: T;
  /** Its position before the move. */
  readonly from: number;
  /** Its position after the move. */
  readonly to: number;
}

/** The template context of a reorder row (`ng-template[tmReorderItem]`). */
export interface TmReorderItemContext<T = unknown> {
  /** The item. */
  readonly $implicit: T;
  /** The item's 0-based position as shown, which follows a keyboard move in progress. */
  readonly index: number;
}

/**
 * A row's content after the grab handle: `<ng-template tmReorderItem
 * let-item let-index="index">`. Without one, a row shows the list's
 * `itemLabel`.
 *
 * Bind `tmReorderItemOf` to the list's `items` for a typed `item`; it is
 * read only by the template type checker.
 *
 * @tmGroup collection
 */
@Directive({ selector: 'ng-template[tmReorderItem]' })
export class TmReorderItem<T = unknown> {
  /** The row template. */
  readonly template = inject<TemplateRef<TmReorderItemContext<T>>>(TemplateRef);

  /** The list's items, so the template checker types `item`; unused at runtime. */
  readonly tmReorderItemOf = input<readonly T[] | undefined>(undefined);

  /** Narrows the template context for type-checked bindings. */
  static ngTemplateContextGuard<T>(
    _dir: TmReorderItem<T>,
    // eslint-disable-next-line @typescript-eslint/no-unused-vars -- type-position-only parameter
    _ctx: unknown,
  ): _ctx is TmReorderItemContext<T> {
    return true;
  }
}

/**
 * What an empty reorder list shows: `<ng-template tmReorderEmpty>`. The
 * empty list stays the drop target, so an accepted drag can land on it.
 *
 * @tmGroup collection
 */
@Directive({ selector: 'ng-template[tmReorderEmpty]' })
export class TmReorderEmpty {
  /** The empty-state template. */
  readonly template = inject(TemplateRef);
}
