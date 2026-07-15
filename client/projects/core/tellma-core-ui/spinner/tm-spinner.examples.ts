// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

/**
 * Usage examples: the docs extractor reads each exported
 * const's `template` into components.json (→ llms.txt → the MCP `example`
 * tool), titled by the export name. Keep every template copy-pasteable.
 */

export const Standalone = {
  template: `<tm-spinner style="color: var(--color-primary); font-size: 16px"></tm-spinner>`,
};

/** The composition the form controls use: glyph only, aria-busy on the busy control. */
export const NextToBusyContent = {
  template: `
    <span aria-busy="true">
      Saving…
      <tm-spinner></tm-spinner>
    </span>
  `,
};
