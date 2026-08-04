// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

/**
 * Canonical `tm-alert` usage — dependency-free template objects consumed by
 * the docs extractor and compiled against the live API by the examples spec.
 */

/** The four kinds; the severity is also announced via a hidden prefix. */
export const Kinds = {
  template: `
    <tm-alert kind="info">Posting runs nightly at 02:00.</tm-alert>
    <tm-alert kind="success">The invoice was posted.</tm-alert>
    <tm-alert kind="warning">Two lines have no cost center.</tm-alert>
    <tm-alert kind="error">The document could not be saved.</tm-alert>
  `,
};

/** A heading renders as a bold first line before the body. */
export const WithHeading = {
  template: `
    <tm-alert kind="warning" heading="Unposted lines">
      Three lines are still drafts and will not appear in reports.
    </tm-alert>
  `,
};

/** A dynamically inserted alert announces via role="alert". */
export const DynamicError = {
  template: `<tm-alert kind="error" live="assertive">Saving failed — try again.</tm-alert>`,
};
