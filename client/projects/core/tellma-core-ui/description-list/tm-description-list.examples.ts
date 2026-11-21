// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

/**
 * Usage examples: the docs extractor reads each exported
 * const's `template` into components.json (→ llms.txt → the MCP `example`
 * tool), titled by the export name. Keep every template copy-pasteable.
 */

/** Facts in two columns, label above value. */
export const TwoColumns = {
  template: `
    <dl tmDescriptionList [columns]="2">
      <div><dt>Code</dt><dd>JED-01</dd></div>
      <div><dt>Manager</dt><dd>Sara Ali</dd></div>
      <div><dt>Opened</dt><dd>2024-03-01</dd></div>
      <div><dt>Currency</dt><dd>SAR</dd></div>
    </dl>
  `,
};

/** Label and value on one line, two columns while the list is wide enough. */
export const InlineAuto = {
  template: `
    <dl tmDescriptionList columns="auto" layout="inline">
      <div><dt>Status</dt><dd>500</dd></div>
      <div><dt>UI version</dt><dd>2.14.0</dd></div>
    </dl>
  `,
};
