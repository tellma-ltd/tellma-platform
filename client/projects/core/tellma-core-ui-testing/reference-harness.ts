// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import { ComponentHarness } from '@angular/cdk/testing';

import { TmCopyButtonHarness } from './copy-button-harness';

/** Harness for `tm-reference`. */
export class TmReferenceHarness extends ComponentHarness {
  /** The selector for the `tm-reference` host element. */
  static hostSelector = 'tm-reference';

  /** The label before the chip ("Reference"). */
  async getLabel(): Promise<string> {
    return (await (await this.locatorFor('.tm-reference__label')()).text()).trim();
  }

  /** The full value in the chip, however much of it is on screen. */
  async getValue(): Promise<string> {
    return (await (await this.locatorFor('.tm-reference__value')()).text()).trim();
  }

  /** The copy button. */
  async getCopyButton(): Promise<TmCopyButtonHarness> {
    return this.locatorFor(TmCopyButtonHarness)();
  }

  /** Clicks the copy button. */
  async copy(): Promise<void> {
    return (await this.getCopyButton()).click();
  }
}
