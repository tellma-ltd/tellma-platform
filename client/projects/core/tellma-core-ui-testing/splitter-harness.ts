// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import { ComponentHarness, TestKey } from '@angular/cdk/testing';

/** The keys a splitter's bar answers to. */
export type TmSplitterKey = 'ArrowLeft' | 'ArrowRight' | 'ArrowUp' | 'ArrowDown' | 'Home' | 'End';

/** The CDK test keys for each splitter key. */
const TEST_KEYS: Record<TmSplitterKey, TestKey> = {
  ArrowLeft: TestKey.LEFT_ARROW,
  ArrowRight: TestKey.RIGHT_ARROW,
  ArrowUp: TestKey.UP_ARROW,
  ArrowDown: TestKey.DOWN_ARROW,
  Home: TestKey.HOME,
  End: TestKey.END,
};

/** Harness for `tm-splitter`. */
export class TmSplitterHarness extends ComponentHarness {
  /** The selector for the `tm-splitter` host element. */
  static hostSelector = 'tm-splitter';

  /** The bar. */
  private readonly separator = this.locatorFor('.tm-splitter__separator');

  /** `horizontal` (panes side by side) or `vertical`. */
  async getOrientation(): Promise<'horizontal' | 'vertical'> {
    return (await (await this.host()).getAttribute('data-orientation')) === 'vertical'
      ? 'vertical'
      : 'horizontal';
  }

  /** The bar's accessible name. */
  async getLabel(): Promise<string | null> {
    return (await this.separator()).getAttribute('aria-label');
  }

  /** The primary pane's share of the container, 0 to 100, as the bar reports it. */
  async getValue(): Promise<number | null> {
    return this.numberAttribute('aria-valuenow');
  }

  /** The smallest share the clamp allows the primary pane, 0 to 100. */
  async getMinValue(): Promise<number | null> {
    return this.numberAttribute('aria-valuemin');
  }

  /** The largest share the clamp allows the primary pane, 0 to 100. */
  async getMaxValue(): Promise<number | null> {
    return this.numberAttribute('aria-valuemax');
  }

  /** The id of the pane the bar controls (the primary pane). */
  async getControlledPaneId(): Promise<string | null> {
    return (await this.separator()).getAttribute('aria-controls');
  }

  /** A pane's rendered size along the split, in px. */
  async getPaneSize(side: 'start' | 'end'): Promise<number> {
    const [pane, orientation] = await Promise.all([
      this.locatorFor(`[tmSplitterPane="${side}"]`)(),
      this.getOrientation(),
    ]);
    const { width, height } = await pane.getDimensions();
    return orientation === 'horizontal' ? width : height;
  }

  /** Focuses the bar. */
  async focus(): Promise<void> {
    return (await this.separator()).focus();
  }

  /** Whether the bar has focus. */
  async isFocused(): Promise<boolean> {
    return (await this.separator()).isFocused();
  }

  /** Presses a key on the bar, with Shift when `shift` is set. */
  async press(key: TmSplitterKey, shift = false): Promise<void> {
    return (await this.separator()).sendKeys({ shift }, TEST_KEYS[key]);
  }

  /** Reads a numeric attribute of the bar. */
  private async numberAttribute(name: string): Promise<number | null> {
    const value = await (await this.separator()).getAttribute(name);
    return value === null ? null : Number(value);
  }
}
