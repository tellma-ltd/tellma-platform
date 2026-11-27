// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import { type BaseHarnessFilters, ComponentHarness, HarnessPredicate } from '@angular/cdk/testing';
import { TmGridHarness } from '@tellma/core-ui-testing';

// The harness of @tellma/core/details's `tm-child-grid`: a frame of a fixed
// height that holds the collection's grid once its deferred chunk renders,
// a placeholder of the same geometry before that, or the failed load's
// message; and the totals under it. The grid itself is a tm-grid, driven
// through the component library's grid harness.

/** The filters of a `tm-child-grid`. */
export interface TmChildGridHarnessFilters extends BaseHarnessFilters {
  /** The collection's name, such as `Lines`. */
  readonly collection?: string;
}

/** What a `tm-child-grid` shows in its frame. */
export type TmChildGridState = 'placeholder' | 'grid' | 'failed';

/** Harness for a `tm-child-grid`: its collection, its frame, its grid and its totals. */
export class TmChildGridHarness extends ComponentHarness {
  /** The selector for a `tm-child-grid`. */
  static hostSelector = 'tm-child-grid';

  /**
   * A predicate for the child grids that match the filters.
   *
   * @param options - The collection.
   * @returns The predicate.
   */
  static with(options: TmChildGridHarnessFilters = {}): HarnessPredicate<TmChildGridHarness> {
    return new HarnessPredicate(TmChildGridHarness, options).addOption(
      'collection',
      options.collection,
      async (harness, collection) => (await harness.getCollection()) === collection,
    );
  }

  /** The collection's name, as the record's entity names it. */
  async getCollection(): Promise<string> {
    return (await (await this.host()).getAttribute('data-tm-collection')) ?? '';
  }

  /** What the frame shows: the placeholder, the grid, or the failed load's message. */
  async getState(): Promise<TmChildGridState> {
    if ((await this.locatorForOptional('tm-grid')()) !== null) {
      return 'grid';
    }
    return (await this.locatorForOptional('.tm-child-grid__failed')()) === null ? 'placeholder' : 'failed';
  }

  /** Whether the grid edits: the record is in edit or create mode, and the grid is not read-only. */
  async isEditable(): Promise<boolean> {
    return (await this.host()).hasClass('tm-child-grid--editable');
  }

  /** The grid, once its deferred chunk has rendered it. */
  async getGrid(): Promise<TmGridHarness> {
    return this.locatorFor(TmGridHarness)();
  }

  /** The frame's height in pixels: the header, the rows shown, the border and an editable grid's status band. */
  async getHeight(): Promise<number> {
    return (await (await this.locatorFor('.tm-child-grid__frame')()).getDimensions()).height;
  }

  /** The totals under the grid, as shown: each one's label and its sum, in display order. */
  async getTotals(): Promise<Readonly<Record<string, string>>> {
    const terms = await this.locatorForAll('.tm-child-grid__total dt')();
    const values = await this.locatorForAll('.tm-child-grid__total dd')();
    const texts = await Promise.all([...terms, ...values].map(async (element) => (await element.text()).trim()));
    return Object.fromEntries(texts.slice(0, terms.length).map((term, index) => [term, texts[terms.length + index]!]));
  }

  /** The failed load's message; `null` while none shows. */
  async getFailure(): Promise<string | null> {
    const failure = await this.locatorForOptional('.tm-child-grid__failed')();
    return failure === null ? null : (await failure.text()).trim();
  }
}
