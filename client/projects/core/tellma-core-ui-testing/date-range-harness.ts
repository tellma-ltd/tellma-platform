// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import { ComponentHarness } from '@angular/cdk/testing';

import { TmDatePickerHarness } from './date-picker-harness';
import { TmFormFieldHarness } from './form-field-harness';

/** Harness for `tm-date-range`. */
export class TmDateRangeHarness extends ComponentHarness {
  /** The selector for the `tm-date-range` host element. */
  static hostSelector = 'tm-date-range';

  private readonly fields = this.locatorForAll(TmFormFieldHarness);
  private readonly pickers = this.locatorForAll(TmDatePickerHarness);
  private readonly labelEl = this.locatorForOptional('.tm-date-range__label');

  /** The "from" date picker. */
  async getFrom(): Promise<TmDatePickerHarness> {
    return (await this.pickers())[0];
  }

  /** The "to" date picker. */
  async getTo(): Promise<TmDatePickerHarness> {
    return (await this.pickers())[1];
  }

  /** The "from" part's field (its label and messages). */
  async getFromField(): Promise<TmFormFieldHarness> {
    return (await this.fields())[0];
  }

  /** The "to" part's field (its label and messages; the order error shows here). */
  async getToField(): Promise<TmFormFieldHarness> {
    return (await this.fields())[1];
  }

  /** The group's visible label, or `null` without one. */
  async getLabel(): Promise<string | null> {
    const label = await this.labelEl();
    return label === null ? null : (await label.text()).trim();
  }
}
