// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

/**
 * Usage examples: the docs extractor reads each exported
 * const's `template` into components.json (→ llms.txt → the MCP `example`
 * tool), titled by the export name. Keep every template copy-pasteable.
 */

/** A search page's pager: "1–20 / 55", with a page-size select. */
export const Range = {
  template: `
    <tm-pager [(page)]="page" [(pageSize)]="pageSize" [pageSizes]="[20, 50, 100]" [total]="55" />
  `,
};

/** A capped count: "1–20 / 1,000+", Next enabled until page 50. */
export const LowerBound = {
  template: `
    <tm-pager [(page)]="page" [pageSize]="20" [total]="1000" totalIsLowerBound [maxPage]="50" />
  `,
};

/** A details page stepping through its list's items: "3 / 293". */
export const Item = {
  template: `
    <tm-pager mode="item" [index]="index()" [total]="293" (previous)="stepBack()" (next)="stepOn()" />
  `,
};
