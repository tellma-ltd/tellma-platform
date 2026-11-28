// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

/**
 * Usage examples: the docs extractor reads each exported
 * const's `template` into components.json (→ llms.txt → the MCP `example`
 * tool), titled by the export name. Keep every template copy-pasteable.
 */

/** The bare directive on a field's own input, for a custom layout. */
export const BareDirective = {
  template: `
    <tm-form-field label="Filter">
      <input tmInput tmSearch [debounce]="200" />
    </tm-form-field>
  `,
};
