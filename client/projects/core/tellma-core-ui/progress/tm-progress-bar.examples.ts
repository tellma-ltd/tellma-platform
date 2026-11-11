// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

/**
 * Usage examples: the docs extractor reads each exported
 * const's `template` into components.json (→ llms.txt → the MCP `example`
 * tool), titled by the export name. Keep every template copy-pasteable.
 */

/** A task of known length: the share done, 0 to 100. */
export const Determinate = {
  template: `<tm-progress-bar [value]="40" label="Uploading the statement" />`,
};

/** A task of unknown length sweeps. Bind `active` to the loading state. */
export const Indeterminate = {
  template: `<tm-progress-bar [value]="null" [active]="true" label="Loading invoices" />`,
};

/** A tone for the outcome a bar is heading to. */
export const Tone = {
  template: `<tm-progress-bar [value]="90" tone="warning" label="Storage used" />`,
};
