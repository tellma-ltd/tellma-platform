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

/**
 * An icon-only button must carry an accessible name. The glyph is a
 * `tm-icon`, sized by the button; `searchIcon` is the `Search` data from
 * `lucide` (or use `name` with a registered icon).
 */
export const IconOnly = {
  template: `
    <button tmButton aria-label="Search">
      <tm-icon [svg]="searchIcon" />
    </button>
  `,
};

/** A `tm-icon` child takes the button's icon size and hides while pending. */
export const WithIcon = {
  template: `<button tmButton variant="primary"><tm-icon name="plus" /> Add a line</button>`,
};

/** A link styled as a button stays a link: Mod+click opens a new tab. */
export const Link = {
  template: `<a tmButton variant="primary" href="/customers/new">New customer</a>`,
};

/**
 * An action the user may not take says why: focusable, activation
 * swallowed, the reason shown on hover, focus and tap.
 */
export const DisabledWithReason = {
  template: `
    <button tmButton disabledReason="Needs the Customers: Create and edit permission.">
      Edit
    </button>
  `,
};
