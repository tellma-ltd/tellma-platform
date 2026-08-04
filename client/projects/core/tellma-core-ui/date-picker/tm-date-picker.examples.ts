// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

/**
 * Canonical `tm-date-picker` usage — dependency-free template objects
 * consumed by the docs extractor and compiled against the live API by the
 * examples spec.
 */

/** A date field inside a form field; the value is an ISO YYYY-MM-DD string. */
export const InField = {
  template: `
    <tm-form-field label="Due date" hint="When payment is expected">
      <tm-date-picker />
    </tm-form-field>
  `,
};

/** ISO bounds clamp the popup and pair with tmMinDate/tmMaxDate validators. */
export const WithBounds = {
  template: `
    <tm-form-field label="Delivery date">
      <tm-date-picker minDate="2026-01-01" maxDate="2026-12-31" />
    </tm-form-field>
  `,
};

/** The long display style spells the month name in the active locale. */
export const LongStyle = {
  template: `
    <tm-form-field label="Posting date">
      <tm-date-picker dateStyle="long" />
    </tm-form-field>
  `,
};
