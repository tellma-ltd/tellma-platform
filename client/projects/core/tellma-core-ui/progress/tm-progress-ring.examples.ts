// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

/**
 * Usage examples: the docs extractor reads each exported
 * const's `template` into components.json (→ llms.txt → the MCP `example`
 * tool), titled by the export name. Keep every template copy-pasteable.
 */

/** A decorative spinning ring; the button it surrounds names the state. */
export const AroundAButton = {
  template: `<tm-progress-ring [value]="null" [diameter]="30" />`,
};

/** A ring that is its own progress bar, named by aria-label. */
export const Named = {
  template: `<tm-progress-ring [value]="60" [decorative]="false" aria-label="Export progress" />`,
};
