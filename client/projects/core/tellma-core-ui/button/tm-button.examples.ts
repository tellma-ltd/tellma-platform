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
      <svg viewBox="0 0 16 16" fill="none" aria-hidden="true">
        <circle cx="7" cy="7" r="4.5" stroke="currentColor" />
        <path d="m10.5 10.5 3 3" stroke="currentColor" stroke-linecap="round" />
      </svg>
    </button>
  `,
};
