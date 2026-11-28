// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

/// <reference types="@tellma/core-tools/lucide-icons" />

import { Component, input } from '@angular/core';
import ChevronDown from 'lucide/dist/esm/icons/chevron-down.mjs';
import ChevronRight from 'lucide/dist/esm/icons/chevron-right.mjs';

import { TmIcon } from '@tellma/core-ui/icon';
import { TmSpinner } from '@tellma/core-ui/spinner';
import type { ɵTmTilesTreePlace } from '@tellma/core-ui/tiles';

/**
 * A tree tile's chrome, which the tiles render at inline-start of each tree
 * tile: the twisty (a pointer target; the keys and `aria-expanded` carry its
 * state) and, while children load lazily, the reserved spinner slot. Its
 * host takes no box, so the two lie in the tile's own row of content.
 * Internal: `TmTilesTree` hands it to the tiles, which bind its `place`.
 */
@Component({
  selector: 'tm-tiles-tree-affordance',
  imports: [TmIcon, TmSpinner],
  template: `
    <!-- On a leaf the twisty's slot stays, empty, so titles align across levels. -->
    <span
      class="tm-tiles__twisty"
      data-tm-twisty
      aria-hidden="true"
      [class.tm-tiles__twisty--open]="place().expanded"
    >
      @if (place().expanded !== null) {
        <tm-icon [svg]="place().expanded ? expandedGlyph : collapsedGlyph" />
      }
    </span>
    <!-- Reserved whenever children load lazily, so a spinner that comes and
         goes moves nothing. -->
    @if (place().loading !== null) {
      <span class="tm-tiles__spinner-slot">
        @if (place().loading) {
          <tm-spinner />
        }
      </span>
    }
  `,
  styleUrl: './tree-affordance.css',
})
export class ɵTmTilesTreeAffordance {
  /** The tile's place in the tree: its expansion and whether its children load. */
  readonly place = input.required<ɵTmTilesTreePlace>();

  /** A collapsed row's twisty, mirrored in RTL. */
  protected readonly collapsedGlyph = { node: ChevronRight, mirrorInRtl: true };
  /** An expanded row's twisty. */
  protected readonly expandedGlyph = ChevronDown;
}
