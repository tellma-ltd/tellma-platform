// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

/**
 * Usage examples: the docs extractor reads each exported
 * const's `template` into components.json (→ llms.txt → the MCP `example`
 * tool), titled by the export name. Keep every template copy-pasteable.
 */

/** An age in a sentence; hovering shows the date and time. */
export const InASentence = {
  template: `<span>Saved <tm-relative-time value="2026-10-20T10:40:00Z" /></span>`,
};

/** The long wording, for space to spare. */
export const Long = {
  template: `<tm-relative-time value="2026-10-20T10:40:00Z" textStyle="long" />`,
};
