// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

/**
 * Usage examples: the docs extractor reads each exported
 * const's `template` into components.json (→ llms.txt → the MCP `example`
 * tool), titled by the export name. Keep every template copy-pasteable.
 */

/** Icon-only beside a value: the tooltip says "Copy", then "Copied". */
export const IconOnly = {
  template: `<span>INV-0042</span> <tm-copy-button text="INV-0042" label="Copy invoice number" />`,
};

/** With its label: the button keeps the width of "Copied". */
export const WithLabel = {
  template: `<tm-copy-button text="https://example.com/invoices/42" label="Copy link" showLabel />`,
};
