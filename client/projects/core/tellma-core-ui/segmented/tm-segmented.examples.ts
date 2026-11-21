// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

/**
 * Usage examples: the docs extractor reads each exported
 * const's `template` into components.json (→ llms.txt → the MCP `example`
 * tool), titled by the export name. Keep every template copy-pasteable.
 */

/** A text-only toggle between a few views. */
export const ViewToggle = {
  template: `
    <tm-segmented ariaLabel="Period">
      <tm-segment value="day" label="Day" />
      <tm-segment value="week" label="Week" />
      <tm-segment value="month" label="Month" />
    </tm-segmented>
  `,
};

/** Icon segments that drop their labels below the `md` container width; the label stays as the tooltip and the name. */
export const IconsWhenNarrow = {
  template: `
    <tm-segmented ariaLabel="Layout" labels="md">
      <tm-segment value="table" label="Table" icon="table-2" />
      <tm-segment value="cards" label="Cards" icon="layout-grid" />
    </tm-segmented>
  `,
};
