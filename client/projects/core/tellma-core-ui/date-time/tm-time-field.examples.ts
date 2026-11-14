// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

/**
 * Usage examples: the docs extractor reads each exported
 * const's `template` into components.json (→ llms.txt → the MCP `example`
 * tool), titled by the export name. Keep every template copy-pasteable.
 */

/** A time of day inside a form field; the value is a 24-hour `HH:mm` string. */
export const InField = {
  template: `
    <tm-form-field label="Opens at">
      <tm-time-field [step]="30" />
    </tm-form-field>
  `,
};

/** A bare time field named by `aria-label`, stepping by the hour. */
export const Bare = {
  template: `<tm-time-field aria-label="Reminder time" [step]="60" size="sm" />`,
};
