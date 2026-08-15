// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

/**
 * Canonical `tmButton` usage — dependency-free template objects consumed by
 * the docs extractor and compiled against the live API by the examples spec.
 */

/** The four variants at the default (md) size. */
export const Variants = {
  template: `
    <button tmButton variant="primary">Save</button>
    <button tmButton>Cancel</button>
    <button tmButton variant="ghost">More</button>
    <button tmButton variant="danger">Delete</button>
  `,
};

/** Sizes align with the form-field height scale for toolbar rows. */
export const Sizes = {
  template: `
    <button tmButton size="sm">Small</button>
    <button tmButton>Medium</button>
    <button tmButton size="lg">Large</button>
  `,
};

/** A pending async action keeps its size and focus while activation is swallowed. */
export const PendingSave = {
  template: `<button tmButton variant="primary" [pending]="true">Save</button>`,
};

/** An icon-only button must carry an accessible name. */
export const IconOnly = {
  template: `
    <button tmButton aria-label="Search">
      <svg
        viewBox="0 0 24 24"
        fill="none"
        stroke="currentColor"
        stroke-width="1.75"
        stroke-linecap="round"
        stroke-linejoin="round"
        aria-hidden="true"
      >
        <circle cx="11" cy="11" r="8" />
        <path d="m21 21-4.3-4.3" />
      </svg>
    </button>
  `,
};
