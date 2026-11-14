// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

/**
 * Usage examples: the docs extractor reads each exported
 * const's `template` into components.json (→ llms.txt → the MCP `example`
 * tool), titled by the export name. Keep every template copy-pasteable.
 */

/** An inline count beside a label; it reserves two digits, so 9 → 10 moves nothing. */
export const Inline = {
  template: `<span>Lines <tm-counter [value]="12" /></span>`,
};

/**
 * A count on an icon button's corner. The overlay is hidden from assistive
 * technology, so the button's name carries the count.
 */
export const OnAButton = {
  template: `
    <button tmButton variant="ghost" aria-label="Notifications, 120 new">
      <tm-icon name="plus" />
      <tm-counter [value]="120" placement="overlay" tone="attention" />
    </button>
  `,
};

/** A zero that should still show, and a custom ceiling. */
export const ZeroAndMax = {
  template: `
    <tm-counter [value]="0" showZero />
    <tm-counter [value]="1200" [max]="999" tone="accent" />
  `,
};
