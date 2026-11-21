// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

/**
 * Usage examples: the docs extractor reads each exported
 * const's `template` into components.json (→ llms.txt → the MCP `example`
 * tool), titled by the export name. Keep every template copy-pasteable.
 */

/** Two coupled pickers stacked, as in a filter popover; pair with `tmDateRangeOrder`. */
export const Stacked = {
  template: `<tm-date-range label="Posting period" />`,
};

/** Side by side, within the fiscal year's bounds. */
export const Inline = {
  template: `
    <tm-date-range
      label="Period"
      layout="inline"
      minDate="2026-01-01"
      maxDate="2026-12-31"
      fromLabel="Start"
      toLabel="End"
    />
  `,
};
