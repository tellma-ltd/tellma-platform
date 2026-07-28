// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

/**
 * Canonical `tmFilePicker` usage — dependency-free template objects
 * consumed by the docs extractor and compiled against the live API by the
 * examples spec.
 */

/** A plain toolbar "Attach" button over the selection engine. */
export const AttachButton = {
  template: `
    <button
      tmButton
      variant="secondary"
      tmFilePicker
      accept=".pdf,image/*"
      multiple
      [maxFiles]="10"
      (filesSelected)="onFiles($event)"
    >
      Attach
    </button>
  `,
};
