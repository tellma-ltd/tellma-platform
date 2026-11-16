// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import { Directive, inject, input, TemplateRef } from '@angular/core';

import type { TmTileContentContext } from './tm-tiles-types';

/**
 * Replaces a tile's body — the image and the four corners — at the same
 * size: `<ng-template tmTileContent [tmTileContentOf]="rows()" let-row
 * let-rowId="rowId">`. The checkbox and the tree chrome stay the tiles'
 * own, and so do activation, selection and the keyboard.
 *
 * Bind `tmTileContentOf` to the tiles' `data` for a typed `row`; it is read
 * only by the template type checker.
 *
 * @tmGroup collection
 */
@Directive({ selector: 'ng-template[tmTileContent]' })
export class TmTileContent<T = unknown> {
  /** The tile body template. */
  readonly template = inject<TemplateRef<TmTileContentContext<T>>>(TemplateRef);

  /** The tiles' data, so the template checker types `row`; unused at runtime. */
  readonly tmTileContentOf = input<readonly T[] | undefined>(undefined);

  /** Narrows the template context for type-checked bindings. */
  static ngTemplateContextGuard<T>(
    _dir: TmTileContent<T>,
    // eslint-disable-next-line @typescript-eslint/no-unused-vars -- type-position-only parameter
    _ctx: unknown,
  ): _ctx is TmTileContentContext<T> {
    return true;
  }
}

/**
 * Replaces the tiles' built-in "No items to display" (`*tmTilesEmpty`),
 * shown while the tiles are bound, loaded and empty.
 *
 * @tmGroup collection
 */
@Directive({ selector: '[tmTilesEmpty]' })
export class TmTilesEmpty {
  /** The template to render while the tiles are loaded and empty. */
  readonly template = inject<TemplateRef<void>>(TemplateRef);
}

/**
 * Replaces the skeleton tiles shown while the first data loads
 * (`*tmTilesLoading`).
 *
 * @tmGroup collection
 */
@Directive({ selector: '[tmTilesLoading]' })
export class TmTilesLoading {
  /** The template to render while loading with no data yet. */
  readonly template = inject<TemplateRef<void>>(TemplateRef);
}
