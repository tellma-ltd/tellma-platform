// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

/**
 * Usage examples: the docs extractor reads each exported
 * const's `template` into components.json (→ llms.txt → the MCP `example`
 * tool), titled by the export name. Keep every template copy-pasteable.
 */

/** The composed field: icon, input and clear button; `search` emits the debounced query. */
export const Basic = {
  template: `<tm-search-field ariaLabel="Search customers" />`,
};

/** A compact field with its own placeholder and a slower debounce. */
export const Compact = {
  template: `
    <tm-search-field ariaLabel="Search documents" placeholder="Search by number" size="sm" [debounce]="200" />
  `,
};
