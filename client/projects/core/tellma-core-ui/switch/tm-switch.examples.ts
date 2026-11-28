// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

/**
 * Usage examples: the docs extractor reads each exported
 * const's `template` into components.json (→ llms.txt → the MCP `example`
 * tool), titled by the export name. Keep every template copy-pasteable.
 */

/** An on/off setting; the projected content is the label. */
export const Basic = {
  template: `<tm-switch>Notify me by email</tm-switch>`,
};

/** The label on the track's start side, as in a settings list. */
export const LabelStart = {
  template: `<tm-switch labelPosition="start" [checked]="true">Show archived documents</tm-switch>`,
};

/** Disabled, off and on. */
export const Disabled = {
  template: `
    <tm-switch disabled>Locked setting</tm-switch>
    <tm-switch disabled [checked]="true">Locked and on</tm-switch>
  `,
};
