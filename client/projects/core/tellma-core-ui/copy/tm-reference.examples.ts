// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

/**
 * Usage examples: the docs extractor reads each exported
 * const's `template` into components.json (→ llms.txt → the MCP `example`
 * tool), titled by the export name. Keep every template copy-pasteable.
 */

/** A trace id to quote to support; the button copies all of it. */
export const TraceId = {
  template: `<tm-reference value="4bf92f3577b34da6a3ce929d0e0e4736" />`,
};

/** A version, under its own label. */
export const Version = {
  template: `<tm-reference label="Version" value="2026.11.3+a41c9e2" />`,
};
