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
    <tm-multi-select placeholder="Regions" style="max-width: 260px; display: block;">
      <tm-option value="riyadh" label="Riyadh">Riyadh</tm-option>
      <tm-option value="jeddah" label="Jeddah">Jeddah</tm-option>
      <tm-option value="dammam" label="Dammam">Dammam</tm-option>
    </tm-multi-select>
  `,
};

export const Grouped = {
  template: `
    <tm-multi-select placeholder="Center types" style="max-width: 260px; display: block;">
      <tm-option-group label="Operating">
        <tm-option value="Service" label="Service">Service</tm-option>
        <tm-option value="Operation" label="Operation">Operation</tm-option>
      </tm-option-group>
      <tm-option value="Sale" label="Sale">Sale</tm-option>
    </tm-multi-select>
  `,
};
