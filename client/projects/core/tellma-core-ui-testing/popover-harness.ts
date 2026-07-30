// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import { ComponentHarness } from '@angular/cdk/testing';

/**
 * Harness for the `tm-popover` panel. The panel is portaled into the
 * top-layer overlay and exists in the DOM only while the popover is OPEN,
 * so this harness cannot be located through the fixture's own loader:
 * obtain it from a document root loader after opening —
 * `TestbedHarnessEnvironment.documentRootLoader(fixture).getHarness(TmPopoverHarness)`
 * — and expect the lookup to reject while the popover is closed.
 */
export class TmPopoverHarness extends ComponentHarness {
  /** The selector for the open popover panel. */
  static hostSelector = '.tm-popover__panel';

  /** The panel's text content, trimmed. */
  async getText(): Promise<string> {
    return (await (await this.host()).text()).trim();
  }

  /** The panel's accessible name. */
  async getAriaLabel(): Promise<string | null> {
    return (await this.host()).getAttribute('aria-label');
  }

  /** Whether focus currently sits inside the panel. */
  async isFocused(): Promise<boolean> {
    if (await (await this.host()).isFocused()) {
      return true;
    }
    return (await this.locatorForOptional(':focus')()) !== null;
  }
}
