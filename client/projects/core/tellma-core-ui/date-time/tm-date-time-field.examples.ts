// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

/**
 * Usage examples: the docs extractor reads each exported
 * const's `template` into components.json (→ llms.txt → the MCP `example`
 * tool), titled by the export name. Keep every template copy-pasteable.
 */

/** An instant edited as a date and a time on the zone's clock, with the zone's offset shown. */
export const DateTime = {
  template: `<tm-date-time-field label="Meeting starts" />`,
};

/** An instant shown on a named zone's clock rather than the app's. */
export const InZone = {
  template: `<tm-date-time-field label="Departure" timeZone="Asia/Dubai" />`,
};
