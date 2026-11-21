// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import { ComponentHarness } from '@angular/cdk/testing';

/** Harness for `tm-empty-state`. */
export class TmEmptyStateHarness extends ComponentHarness {
  /** The selector for the `tm-empty-state` host element. */
  static hostSelector = 'tm-empty-state';

  /** The heading. */
  async getHeading(): Promise<string> {
    return (await (await this.locatorFor('.tm-empty-state__heading')()).text()).trim();
  }

  /** The detail text (the projected content outside the actions). */
  async getDetail(): Promise<string> {
    return (await (await this.locatorFor('.tm-empty-state__detail')()).text()).trim();
  }

  /** Whether an icon shows. */
  async hasIcon(): Promise<boolean> {
    return (await this.locatorForOptional('.tm-empty-state__icon')()) !== null;
  }

  /** The tone, read off the host's `data-tone`. */
  async getTone(): Promise<'neutral' | 'error'> {
    return (await (await this.host()).getAttribute('data-tone')) === 'error' ? 'error' : 'neutral';
  }

  /** The size, read off the host's `data-size`. */
  async getSize(): Promise<'page' | 'panel'> {
    return (await (await this.host()).getAttribute('data-size')) === 'panel' ? 'panel' : 'page';
  }

  /** Whether the state is announced: a polite live region. */
  async isAnnounced(): Promise<boolean> {
    const body = await this.locatorFor('.tm-empty-state__body')();
    return (await body.getAttribute('role')) === 'status';
  }
}
