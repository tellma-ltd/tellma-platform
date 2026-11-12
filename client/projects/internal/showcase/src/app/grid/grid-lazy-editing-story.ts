// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import { Component, computed, signal } from '@angular/core';
import { form } from '@angular/forms/signals';

import { TmGrid, TmGridColumn, ɵTM_GRID_EDITING_LOADER } from '@tellma/core-ui/grid';

declare global {
  interface Window {
    /**
     * Lets the grid on the lazy-editing story receive its editing code. Until
     * it is called, the grid's request for that code stays open, so a browser
     * test can type into the window in which the code is on its way.
     */
    tmShowcaseReleaseGridEditing?: () => void;
  }
}

/** One line of the demo. */
interface Line {
  /** The line's identity. */
  readonly id: number;
  /** Free text. */
  readonly description: string | null;
  /** A number. */
  readonly quantity: number | null;
}

/**
 * An editable grid whose editing code is held back until the page calls
 * `window.tmShowcaseReleaseGridEditing()`: the window a slow network opens
 * between a grid becoming editable and its editing code arriving. Typing in
 * that window goes into the cell's draft, which commits on its own or
 * becomes the editor's text when the code arrives.
 */
@Component({
  imports: [TmGrid, TmGridColumn],
  providers: [
    {
      provide: ɵTM_GRID_EDITING_LOADER,
      useValue: () =>
        new Promise<void>((resolve) => (window.tmShowcaseReleaseGridEditing = resolve)).then(
          () => import('@tellma/core-ui/grid-editing'),
        ),
    },
  ],
  template: `
    <h2>Grid (editing code on its way)</h2>
    <p>The editing code arrives only when the test releases it.</p>

    <tm-grid
      class="demo-grid"
      gridId="grid-lazy-editing"
      [field]="f"
      [rowId]="rowId"
    >
      <tm-grid-column key="description" header="Description" [flex]="2" [minWidth]="160" />
      <tm-grid-column key="quantity" type="number" header="Qty" [width]="90" />
    </tm-grid>

    <button type="button" data-testid="outside-button">Outside</button>

    <h3>Model</h3>
    <!-- tabindex: the dump scrolls, so keyboard users must be able to reach it. -->
    <pre class="model-dump" data-testid="model-json" tabindex="0" aria-label="Model JSON">{{
      modelJson()
    }}</pre>
  `,
  styles: `
    .demo-grid {
      display: block;
      block-size: 240px;
    }
    .model-dump {
      max-block-size: 200px;
      overflow: auto;
    }
  `,
})
export class GridLazyEditingStory {
  /** The lines' model. */
  readonly model = signal<Line[]>([
    { id: 1, description: 'Consulting', quantity: 2 },
    { id: 2, description: 'Hardware', quantity: 5 },
    { id: 3, description: 'Support', quantity: 1 },
  ]);
  /** The field tree the grid edits. */
  readonly f = form(this.model);
  /** Reads a line's identity. */
  readonly rowId = (line: Line): number => line.id;
  /** The model as JSON, read by the browser tests. */
  readonly modelJson = computed(() => JSON.stringify(this.model(), null, 2));
}
