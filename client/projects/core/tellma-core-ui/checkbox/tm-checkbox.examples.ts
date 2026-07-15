// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

/**
 * Usage examples: the docs extractor reads each exported
 * const's `template` into components.json (→ llms.txt → the MCP `example`
 * tool), titled by the export name. Keep every template copy-pasteable.
 */

export const Unchecked = {
  template: `<tm-checkbox>Email me updates</tm-checkbox>`,
};

export const Checked = {
  template: `<tm-checkbox [checked]="true">Email me updates</tm-checkbox>`,
};

export const Indeterminate = {
  template: `<tm-checkbox [indeterminate]="true">Select all rows</tm-checkbox>`,
};

export const Disabled = {
  template: `
    <tm-checkbox disabled>Locked option</tm-checkbox>
    <tm-checkbox disabled [checked]="true">Locked and checked</tm-checkbox>
  `,
};
