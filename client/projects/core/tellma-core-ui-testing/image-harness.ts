// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import { ComponentHarness } from '@angular/cdk/testing';

/** Harness for a `tm-image` box. */
export class TmImageHarness extends ComponentHarness {
  /** The selector for the `tm-image` host element. */
  static hostSelector = 'tm-image';

  /** Whether a loaded image (or a local edit preview) is showing. */
  async isShowingImage(): Promise<boolean> {
    return (
      (await this.locatorForOptional('.tm-image__img')()) !== null ||
      (await this.locatorForOptional('.tm-image__crop-view')()) !== null
    );
  }

  /** Whether the error glyph is showing. */
  async isShowingError(): Promise<boolean> {
    return (await this.locatorForOptional('.tm-image__glyph--error')()) !== null;
  }

  /** The rendered image's alt text, or null when no image is showing. */
  async getAltText(): Promise<string | null> {
    const img =
      (await this.locatorForOptional('.tm-image__img')()) ??
      (await this.locatorForOptional('.tm-image__crop-view img')());
    return img === null ? null : img.getAttribute('alt');
  }

  /** Clicks the edit-mode Remove affordance. */
  async clickRemove(): Promise<void> {
    return (await this.locatorFor('[data-tm-image-action="remove"]')()).click();
  }
}
