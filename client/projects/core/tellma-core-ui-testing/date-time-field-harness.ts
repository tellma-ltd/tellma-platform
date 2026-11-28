// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import { ComponentHarness } from '@angular/cdk/testing';

import { TmDatePickerHarness } from './date-picker-harness';
import { TmFormFieldHarness } from './form-field-harness';
import { TmTimeFieldHarness } from './time-field-harness';

/** Harness for `tm-date-time-field`. */
export class TmDateTimeFieldHarness extends ComponentHarness {
  /** The selector for the `tm-date-time-field` host element. */
  static hostSelector = 'tm-date-time-field';

  private readonly fields = this.locatorForAll(TmFormFieldHarness);
  private readonly datePicker = this.locatorFor(TmDatePickerHarness);
  private readonly timeField = this.locatorFor(TmTimeFieldHarness);
  private readonly zoneEl = this.locatorFor('.tm-date-time-field__zone');
  private readonly labelEl = this.locatorForOptional('.tm-date-time-field__label');

  /** The date part. */
  async getDate(): Promise<TmDatePickerHarness> {
    return this.datePicker();
  }

  /** The time part. */
  async getTime(): Promise<TmTimeFieldHarness> {
    return this.timeField();
  }

  /** The date part's field (its label and messages). */
  async getDateField(): Promise<TmFormFieldHarness> {
    return (await this.fields())[0];
  }

  /** The time part's field (its label and messages). */
  async getTimeField(): Promise<TmFormFieldHarness> {
    return (await this.fields())[1];
  }

  /** The zone label's text ("GMT+3"). */
  async getZoneLabel(): Promise<string> {
    return (await (await this.zoneEl()).text()).trim();
  }

  /** The group's visible label, or `null` without one. */
  async getLabel(): Promise<string | null> {
    const label = await this.labelEl();
    return label === null ? null : (await label.text()).trim();
  }
}
