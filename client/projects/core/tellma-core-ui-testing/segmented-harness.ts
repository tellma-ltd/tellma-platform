// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import { ComponentHarness, type TestElement, type TestKey } from '@angular/cdk/testing';

/** Harness for `tm-segmented`. */
export class TmSegmentedHarness extends ComponentHarness {
  /** The selector for the `tm-segmented` host element. */
  static hostSelector = 'tm-segmented';

  private readonly track = this.locatorFor('[role="radiogroup"]');
  private readonly natives = this.locatorForAll('input[type="radio"]');
  private readonly labelEls = this.locatorForAll('.tm-segmented__label');

  /** The segments' labels (each one's accessible name), in order. */
  async getLabels(): Promise<string[]> {
    const labels = await this.labelEls();
    return Promise.all(labels.map(async (label) => (await label.text()).trim()));
  }

  /** The checked segment's label, or `null` while none is checked. */
  async getCheckedLabel(): Promise<string | null> {
    const labels = await this.getLabels();
    const natives = await this.natives();
    for (let index = 0; index < natives.length; index++) {
      if (await natives[index].getProperty<boolean>('checked')) {
        return labels[index];
      }
    }
    return null;
  }

  /** Checks the segment with this label by clicking it, as the user would. */
  async check(label: string): Promise<void> {
    return (await this.nativeFor(label)).click();
  }

  /** Whether the segment with this label is disabled. */
  async isDisabled(label: string): Promise<boolean> {
    return (await this.nativeFor(label)).getProperty<boolean>('disabled');
  }

  /** Whether the segment with this label holds the control's one tab stop. */
  async isTabStop(label: string): Promise<boolean> {
    return (await (await this.nativeFor(label)).getProperty<number>('tabIndex')) === 0;
  }

  /** The accessible name a segment's radio carries itself (an icon segment's label), or `null`. */
  async getSegmentAriaLabel(label: string): Promise<string | null> {
    return (await this.nativeFor(label)).getAttribute('aria-label');
  }

  /** Focuses the segment with this label. */
  async focus(label: string): Promise<void> {
    return (await this.nativeFor(label)).focus();
  }

  /** The label of the focused segment, or `null` when none has focus. */
  async getFocusedLabel(): Promise<string | null> {
    const labels = await this.getLabels();
    const natives = await this.natives();
    for (let index = 0; index < natives.length; index++) {
      if (await natives[index].isFocused()) {
        return labels[index];
      }
    }
    return null;
  }

  /** Presses a key on the segment with this label (the control handles the arrows). */
  async press(label: string, key: TestKey): Promise<void> {
    return (await this.nativeFor(label)).sendKeys(key);
  }

  /** The control's accessible name. */
  async getAriaLabel(): Promise<string | null> {
    return (await this.track()).getAttribute('aria-label');
  }

  /** The native radio of the segment with this label. */
  private async nativeFor(label: string): Promise<TestElement> {
    const index = (await this.getLabels()).indexOf(label);
    if (index === -1) {
      throw new Error(`No segment labelled "${label}".`);
    }
    return (await this.natives())[index];
  }
}
