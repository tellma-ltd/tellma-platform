// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import {
  ComponentHarness,
  HarnessPredicate,
  type BaseHarnessFilters,
  type TestKey,
} from '@angular/cdk/testing';

/** Filters for `TmRadioHarness`. */
export interface TmRadioHarnessFilters extends BaseHarnessFilters {
  /** Only the radio whose label matches. */
  readonly label?: string | RegExp;
}

/** Harness for one radio (one row) of a `tm-radio-group`. */
export class TmRadioHarness extends ComponentHarness {
  /** The selector for a radio row inside `tm-radio-group`. */
  static hostSelector = '.tm-radio-group__row';

  private readonly native = this.locatorFor('input[type="radio"]');
  private readonly labelEl = this.locatorFor('.tm-radio-group__label');
  private readonly hintEl = this.locatorForOptional('.tm-radio-group__hint');

  /** A predicate that finds a radio by its label. */
  static with(options: TmRadioHarnessFilters = {}): HarnessPredicate<TmRadioHarness> {
    return new HarnessPredicate(TmRadioHarness, options).addOption(
      'label',
      options.label,
      (harness, label) => HarnessPredicate.stringMatches(harness.getLabel(), label),
    );
  }

  /** The radio's visible label. */
  async getLabel(): Promise<string> {
    return (await (await this.labelEl()).text()).trim();
  }

  /** The radio's hint line, or `null` without one. */
  async getHint(): Promise<string | null> {
    const hint = await this.hintEl();
    return hint === null ? null : (await hint.text()).trim();
  }

  /** Whether the radio is checked. */
  async isChecked(): Promise<boolean> {
    return (await this.native()).getProperty<boolean>('checked');
  }

  /** Whether the radio is disabled. */
  async isDisabled(): Promise<boolean> {
    return (await this.native()).getProperty<boolean>('disabled');
  }

  /** Whether the radio holds the group's one tab stop. */
  async isTabStop(): Promise<boolean> {
    return (await (await this.native()).getProperty<number>('tabIndex')) === 0;
  }

  /** Whether the radio has focus. */
  async isFocused(): Promise<boolean> {
    return (await this.native()).isFocused();
  }

  /** Checks the radio by clicking it, as the user would. */
  async check(): Promise<void> {
    return (await this.native()).click();
  }

  /** Focuses the radio. */
  async focus(): Promise<void> {
    return (await this.native()).focus();
  }

  /** Presses a key on the radio (the group handles the arrows). */
  async press(key: TestKey): Promise<void> {
    return (await this.native()).sendKeys(key);
  }
}

/** Harness for `tm-radio-group`. */
export class TmRadioGroupHarness extends ComponentHarness {
  /** The selector for the `tm-radio-group` host element. */
  static hostSelector = 'tm-radio-group';

  private readonly group = this.locatorFor('[role="radiogroup"]');

  /** The group's radios, in order (optionally filtered). */
  async getRadios(filters: TmRadioHarnessFilters = {}): Promise<TmRadioHarness[]> {
    return this.locatorForAll(TmRadioHarness.with(filters))();
  }

  /** The label of the checked radio, or `null` while none is checked. */
  async getCheckedLabel(): Promise<string | null> {
    for (const radio of await this.getRadios()) {
      if (await radio.isChecked()) {
        return radio.getLabel();
      }
    }
    return null;
  }

  /** Checks the radio with this label. */
  async check(label: string | RegExp): Promise<void> {
    const [radio] = await this.getRadios({ label });
    if (radio === undefined) {
      throw new Error(`No radio labelled ${String(label)}.`);
    }
    return radio.check();
  }

  /** The group's `aria-label`, or `null` when an enclosing field's label names it. */
  async getAriaLabel(): Promise<string | null> {
    return (await this.group()).getAttribute('aria-label');
  }

  /** The id list of the group's `aria-labelledby`, or `null`. */
  async getAriaLabelledby(): Promise<string | null> {
    return (await this.group()).getAttribute('aria-labelledby');
  }

  /** Whether the group announces itself as invalid. */
  async isInvalid(): Promise<boolean> {
    return (await (await this.group()).getAttribute('aria-invalid')) === 'true';
  }
}
