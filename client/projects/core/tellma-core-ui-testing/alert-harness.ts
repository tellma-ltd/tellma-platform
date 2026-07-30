// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import { ComponentHarness } from '@angular/cdk/testing';

/** Harness for `tm-alert`. */
export class TmAlertHarness extends ComponentHarness {
  /** The selector for the `tm-alert` host element. */
  static hostSelector = 'tm-alert';

  /** The alert's severity, read off the host classes. */
  async getKind(): Promise<'info' | 'success' | 'warning' | 'error'> {
    const host = await this.host();
    for (const kind of ['info', 'success', 'warning'] as const) {
      if (await host.hasClass(`tm-alert--${kind}`)) {
        return kind;
      }
    }
    return 'error';
  }

  /** The live-region role of the content region, or null when `live` is off. */
  async getRole(): Promise<string | null> {
    return (await this.locatorFor('.tm-alert__content')()).getAttribute('role');
  }

  /** The bold heading text, or null when no heading is set. */
  async getHeading(): Promise<string | null> {
    const heading = await this.locatorForOptional('.tm-alert__heading')();
    return heading === null ? null : (await heading.text()).trim();
  }

  /**
   * The alert's visible text (trimmed) — includes the visually-hidden
   * localized kind prefix, which screen readers announce.
   */
  async getText(): Promise<string> {
    return (await (await this.host()).text()).trim();
  }
}
