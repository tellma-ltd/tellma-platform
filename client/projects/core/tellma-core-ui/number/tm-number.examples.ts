// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

/**
 * Canonical `tmNumber` usage — dependency-free template objects consumed by
 * the docs extractor and compiled against the live API by the examples spec.
 */

/** A numeric amount inside a form field. */
export const Amount = {
  template: `
    <tm-form-field label="Unit price" hint="In the document currency">
      <input tmNumber [minDecimals]="2" [maxDecimals]="2" placeholder="0.00" />
    </tm-form-field>
  `,
};

/** Percent mode: the model holds the fraction (0.75), the display shows 75%. */
export const Percent = {
  template: `
    <tm-form-field label="Discount">
      <input tmNumber [percent]="true" [maxDecimals]="1" />
    </tm-form-field>
  `,
};

/** The bare directive — what a grid number cell mounts, no chrome to strip. */
export const BareInGridCell = {
  template: `<input tmNumber placeholder="0" />`,
};
