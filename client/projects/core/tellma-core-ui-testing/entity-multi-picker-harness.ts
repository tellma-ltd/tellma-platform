// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import { ComponentHarness } from '@angular/cdk/testing';

import { TmChipHarness } from './chip-harness';
import { TmEntityPickerHarness } from './entity-picker-harness';

/** Harness for `tm-entity-multi-picker`. */
export class TmEntityMultiPickerHarness extends ComponentHarness {
  /** The selector for the `tm-entity-multi-picker` host element. */
  static hostSelector = 'tm-entity-multi-picker';

  /** The search input: the composed single picker. */
  private readonly picker = this.locatorFor(TmEntityPickerHarness);

  /** The chosen entities' chips, in value order. */
  private readonly chips = this.locatorForAll(TmChipHarness);

  /** The visible count of the badge (its hidden width probe excluded). */
  private readonly countBadge = this.locatorForOptional('.tm-entity-multi-picker__count-value');

  /** The search input's harness: the dropdown's rows, its status, the magnifier. */
  async getPicker(): Promise<TmEntityPickerHarness> {
    return this.picker();
  }

  /** The chip labels, in value order. */
  async getChipLabels(): Promise<string[]> {
    const chips = await this.chips();
    return Promise.all(chips.map((chip) => chip.getLabel()));
  }

  /**
   * Removes the chip labelled `label` with its remove button. Fails loudly
   * when no chip has that label or the chip cannot be removed (the control
   * is disabled or read-only).
   */
  async removeChip(label: string): Promise<void> {
    for (const chip of await this.chips()) {
      if ((await chip.getLabel()) === label) {
        if (!(await chip.isRemovable())) {
          throw new Error(`TmEntityMultiPickerHarness.removeChip: "${label}" cannot be removed`);
        }
        return chip.remove();
      }
    }
    throw new Error(`TmEntityMultiPickerHarness.removeChip: no chip labelled "${label}"`);
  }

  /** Types `text` into the search input like a user, leaving it focused. */
  async search(text: string): Promise<void> {
    return (await this.picker()).typeQuery(text);
  }

  /**
   * Clicks the dropdown row labelled `label`, opening the browse list first
   * when the dropdown is closed. A chosen row comes off; any other goes on.
   */
  async pick(label: string): Promise<void> {
    const picker = await this.picker();
    if (!(await picker.isOpen())) {
      await picker.open();
    }
    await this.forceStabilize();
    return picker.selectOptionByLabel(label);
  }

  /**
   * The count badge's text ("3", "99+"), or `null` while nothing is chosen
   * and the badge is not shown.
   */
  async getCountBadge(): Promise<string | null> {
    const badge = await this.countBadge();
    if (badge === null) {
      return null;
    }
    const text = (await badge.text()).trim();
    return text === '' ? null : text;
  }
}
