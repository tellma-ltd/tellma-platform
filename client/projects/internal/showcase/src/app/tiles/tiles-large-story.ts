// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import { Component } from '@angular/core';

import { TmTiles } from '@tellma/core-ui/tiles';

import { CUSTOMER_TILE, storyCustomers } from './tiles-story';

/**
 * tm-tiles over 10,000 rows, the scroll probe's subject: every rendered
 * tile carries an image or its placeholder, four corners and a badge.
 */
@Component({
  imports: [TmTiles],
  template: `
    <h2>Tiles (10,000 rows)</h2>
    <tm-tiles
      class="tiles"
      data-testid="tiles-large"
      ariaLabel="Customers"
      [data]="rows"
      [rowId]="idOf"
      [layout]="layout"
      selectable
    />
  `,
  styles: `
    .tiles {
      block-size: 600px;
    }
  `,
})
export class TilesLargeStory {
  /** The rows. */
  protected readonly rows = storyCustomers(10_000);

  /** A row's id. */
  protected readonly idOf = (row: { readonly id: number }): number => row.id;

  /** The tile layout. */
  protected readonly layout = CUSTOMER_TILE;
}
