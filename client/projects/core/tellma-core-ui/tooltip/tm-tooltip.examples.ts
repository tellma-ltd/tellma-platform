// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

/**
 * Canonical `tmTooltip` usage — dependency-free template objects consumed
 * by the docs extractor and compiled against the live API by the examples
 * spec.
 */

/** Plain-text tooltip on a control; the text doubles as its description. */
export const OnButton = {
  template: `<button tmButton variant="secondary" tmTooltip="Refresh the list">Refresh</button>`,
};

/**
 * Disabled controls fire no pointer events, so the tooltip lives on a
 * focusable wrapper instead.
 */
export const DisabledWrapper = {
  template: `
    <span tmTooltip="Post the document first" tabindex="0">
      <button tmButton disabled>Void</button>
    </span>
  `,
};
