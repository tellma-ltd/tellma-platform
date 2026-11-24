// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import { ComponentHarness, HarnessPredicate, TestKey } from '@angular/cdk/testing';
import {
  TmButtonHarness,
  TmCheckboxHarness,
  TmDatePickerHarness,
  TmDateRangeHarness,
  TmEntityMultiPickerHarness,
  TmInputHarness,
  TmMultiSelectHarness,
  TmNumberHarness,
  TmRadioGroupHarness,
  TmSelectHarness,
} from '@tellma/core-ui-testing';

import { TmEntityMultiFieldHarness } from './tm-entity-field-harness';

// Harnesses for @tellma/core/entity's column filters. The control draws
// the component library's own controls, one set per column kind, so the
// harness hands each out, and drives the operator, Apply, Clear and Enter.

/** Harness for `tm-column-filter`, the control that edits one column's filter. */
export class TmColumnFilterHarness extends ComponentHarness {
  /** The selector for the `tm-column-filter` host element. */
  static hostSelector = 'tm-column-filter';

  /** The operator select, absent for a kind with one operator. */
  private readonly operator = this.locatorForOptional(new HarnessPredicate(TmSelectHarness, { selector: '.tm-column-filter__operator' }));

  /** The Clear and Apply buttons. */
  private readonly buttons = this.locatorForAll(new HarnessPredicate(TmButtonHarness, { ancestor: '.tm-column-filter__actions' }));

  /** The operator select, or `null` when the column's kind has one operator. */
  async getOperator(): Promise<TmSelectHarness | null> {
    return this.operator();
  }

  /** The chosen operator's name, or `null` when the column's kind has one operator. */
  async getOperatorText(): Promise<string | null> {
    const select = await this.operator();
    return select === null ? null : select.getTriggerText();
  }

  /**
   * Chooses the operator named `label`. Fails loudly when the control has
   * no operator select or no operator by that name.
   *
   * @param label - The operator's name, such as "Starts with".
   */
  async selectOperator(label: string): Promise<void> {
    const select = await this.operator();
    if (select === null) {
      throw new Error('This column filter has one operator, and no operator select.');
    }
    return select.selectOption(label);
  }

  /** A text filter's input, or `null`. */
  async getTextInput(): Promise<TmInputHarness | null> {
    return this.locatorForOptional(new HarnessPredicate(TmInputHarness, { selector: '.tm-column-filter__text' }))();
  }

  /** A number filter's inputs: one, or the From and To of a range. */
  async getNumberInputs(): Promise<TmNumberHarness[]> {
    return this.locatorForAll(TmNumberHarness)();
  }

  /** A day filter's date picker, or `null` for a range or another kind. */
  async getDatePicker(): Promise<TmDatePickerHarness | null> {
    return this.locatorForOptional(new HarnessPredicate(TmDatePickerHarness, { selector: '.tm-column-filter__form > tm-date-picker' }))();
  }

  /** A day filter's range, or `null`. */
  async getDateRange(): Promise<TmDateRangeHarness | null> {
    return this.locatorForOptional(TmDateRangeHarness)();
  }

  /** A small enum's checkboxes, in the enum's order; none for any other control. */
  async getCheckboxes(): Promise<TmCheckboxHarness[]> {
    return this.locatorForAll(TmCheckboxHarness)();
  }

  /** A mid-sized enum's multi-select, or `null`. */
  async getMultiSelect(): Promise<TmMultiSelectHarness | null> {
    return this.locatorForOptional(TmMultiSelectHarness)();
  }

  /** A large enum's searchable multi-picker, or `null`. */
  async getEnumPicker(): Promise<TmEntityMultiPickerHarness | null> {
    return this.locatorForOptional(new HarnessPredicate(TmEntityMultiPickerHarness, { selector: '.tm-column-filter__form > tm-entity-multi-picker' }))();
  }

  /** A navigation's entity field, or `null`. */
  async getEntityField(): Promise<TmEntityMultiFieldHarness | null> {
    return this.locatorForOptional(TmEntityMultiFieldHarness)();
  }

  /** A boolean filter's radio group, or `null`. */
  async getRadioGroup(): Promise<TmRadioGroupHarness | null> {
    return this.locatorForOptional(TmRadioGroupHarness)();
  }

  /** The accessible name of the group a small enum's checkboxes or a number range sit in, or `null`. */
  async getGroupLabel(): Promise<string | null> {
    const group = await this.locatorForOptional('[role="group"].tm-column-filter__list, [role="group"].tm-column-filter__pair')();
    return group === null ? null : group.getAttribute('aria-label');
  }

  /** Whether Apply is enabled: the controls hold a value the column takes. */
  async isApplyEnabled(): Promise<boolean> {
    return !(await (await this.button('apply')).isDisabled());
  }

  /** Clicks Apply. */
  async apply(): Promise<void> {
    return (await this.button('apply')).click();
  }

  /** Clicks Clear. */
  async clear(): Promise<void> {
    return (await this.button('clear')).click();
  }

  /** Presses Enter in the control's first text or number input. Fails loudly when it has none. */
  async pressEnter(): Promise<void> {
    const input = await this.locatorForOptional('input.tm-column-filter__text, input.tm-number')();
    if (input === null) {
      throw new Error('This column filter has no text or number input to press Enter in.');
    }
    return input.sendKeys(TestKey.ENTER);
  }

  /**
   * Clear or Apply, by position: Clear comes first.
   *
   * @param which - The button.
   * @returns Its harness.
   */
  private async button(which: 'clear' | 'apply'): Promise<TmButtonHarness> {
    const [clear, apply] = await this.buttons();
    const button = which === 'clear' ? clear : apply;
    if (button === undefined) {
      throw new Error('The column filter has no Clear and Apply buttons.');
    }
    return button;
  }
}
