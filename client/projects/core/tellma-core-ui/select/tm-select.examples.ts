// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

/**
 * Usage examples: the docs extractor reads each exported
 * const's `template` into components.json (→ llms.txt → the MCP `example`
 * tool), titled by the export name. Keep every template copy-pasteable.
 */

export const Basic = {
  template: `
    <tm-select placeholder="Pick a country" style="max-width: 260px; display: block;">
      <tm-option [value]="1" label="Saudi Arabia">Saudi Arabia</tm-option>
      <tm-option [value]="2" label="United Arab Emirates">United Arab Emirates</tm-option>
      <tm-option [value]="3" label="Ethiopia">Ethiopia</tm-option>
      <tm-option [value]="4" label="Jordan">Jordan</tm-option>
    </tm-select>
  `,
};

export const Disabled = {
  template: `
    <tm-select disabled placeholder="Cannot open" style="max-width: 260px; display: block;">
      <tm-option [value]="1" label="One">One</tm-option>
    </tm-select>
  `,
};

export const RichOptions = {
  template: `
    <tm-select placeholder="Assign to" style="max-width: 260px; display: block;">
      <tm-option [value]="'aa'" label="Ahmad Akra">
        <strong>Ahmad Akra</strong>&nbsp;— Finance
      </tm-option>
      <tm-option [value]="'mb'" label="Mariam B">
        <strong>Mariam B</strong>&nbsp;— Operations
      </tm-option>
    </tm-select>
  `,
};
