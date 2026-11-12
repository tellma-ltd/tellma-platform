// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import { ComponentHarness } from '@angular/cdk/testing';

/** Harness for `tm-avatar`. */
export class TmAvatarHarness extends ComponentHarness {
  /** The selector for the `tm-avatar` host element. */
  static hostSelector = 'tm-avatar';

  /**
   * The initials on show, or `''` when the face shows the person glyph or
   * a loaded photo covers it.
   */
  async getInitials(): Promise<string> {
    const face = await this.locatorForOptional('.tm-avatar__face')();
    return face === null ? '' : (await face.text()).trim();
  }

  /** Whether the person glyph stands in for an empty name. */
  async isShowingGlyph(): Promise<boolean> {
    return (await this.locatorForOptional('.tm-avatar__face tm-icon')()) !== null;
  }

  /** Whether the avatar has a photo source (loaded or not). */
  async hasPhoto(): Promise<boolean> {
    return (await this.locatorForOptional('tm-image')()) !== null;
  }

  /** The accessible name, or `null` when the avatar is hidden from assistive technology. */
  async getAccessibleName(): Promise<string | null> {
    const host = await this.host();
    return (await host.getAttribute('role')) === 'img' ? host.getAttribute('aria-label') : null;
  }

  /** Whether the avatar is hidden from assistive technology (a decorative one is). */
  async isDecorative(): Promise<boolean> {
    return (await (await this.host()).getAttribute('aria-hidden')) === 'true';
  }

  /** The size step: `'xs'`, `'sm'`, `'md'`, `'lg'` or `'xl'`. */
  async getSize(): Promise<string> {
    return (await (await this.host()).getAttribute('data-size')) ?? '';
  }

  /** The categorical tone (1–8) the key picked, or `null` for the accent tone. */
  async getTone(): Promise<number | null> {
    const tone = await (await this.host()).getAttribute('data-tone');
    return tone === null ? null : Number(tone);
  }
}
