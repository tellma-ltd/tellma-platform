// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

/**
 * Canonical `tm-dropzone` usage — dependency-free template objects
 * consumed by the docs extractor and compiled against the live API by the
 * examples spec.
 */

/** A visual drop target sharing the picker's guardrails and output. */
export const Dropzone = {
  template: `
    <tm-dropzone
      accept=".pdf,.docx,image/*"
      multiple
      [maxFileSize]="25 * 1024 * 1024"
      (filesSelected)="onFiles($event)"
    />
  `,
};
