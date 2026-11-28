// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

/**
 * Usage examples: the docs extractor reads each exported
 * const's `template` into components.json (→ llms.txt → the MCP `example`
 * tool), titled by the export name. Keep every template copy-pasteable.
 */

/** A labelled field around the group, which then names it; a radio's hint describes it. */
export const InField = {
  template: `
    <tm-form-field label="Payment terms">
      <tm-radio-group>
        <tm-radio value="cash" label="Cash" />
        <tm-radio value="net30" label="Net 30" hint="Due 30 days after the invoice" />
        <tm-radio value="net60" label="Net 60" disabled />
      </tm-radio-group>
    </tm-form-field>
  `,
};

/** A short group laid out in one row, named by `ariaLabel`. */
export const Horizontal = {
  template: `
    <tm-radio-group ariaLabel="Document state" orientation="horizontal">
      <tm-radio value="draft" label="Draft" />
      <tm-radio value="posted" label="Posted" />
      <tm-radio value="void" label="Void" />
    </tm-radio-group>
  `,
};
