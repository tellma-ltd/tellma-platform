// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import { type BaseHarnessFilters, ComponentHarness, HarnessPredicate } from '@angular/cdk/testing';
import { TmFormFieldHarness } from '@tellma/core-ui-testing';

// The harness of one cell of @tellma/core/details's `tm-field`: a field is
// one cell, or one per language of a multilingual group, each marked with
// the member it shows. In view mode a cell is a description list's term
// and value box; in edit and create mode a tm-form-field around the
// control, or the value box with its lock when the field is read-only;
// while the record loads with nothing held, a skeleton in the value box.

/** What a `tm-field` cell is showing. */
export type TmFieldMode = 'loading' | 'view' | 'edit';

/** The filters of a `tm-field` cell. */
export interface TmFieldHarnessFilters extends BaseHarnessFilters {
  /** The member the cell shows, PascalCase, such as `Name2` or `Currency`. */
  readonly path?: string;
  /** The cell's label. */
  readonly label?: string | RegExp;
}

/** The label row's and value box's measures within a cell, in pixels. */
export interface TmFieldGeometry {
  /** The label row's height. */
  readonly labelHeight: number;
  /** The value box's offset from the cell's top. */
  readonly boxTop: number;
  /** The value box's height. */
  readonly boxHeight: number;
  /** The value box's width. */
  readonly boxWidth: number;
  /** The cell's height. */
  readonly cellHeight: number;
}

/** Harness for one cell of a `tm-field`: its label, value, mode, lock, hint and errors. */
export class TmFieldHarness extends ComponentHarness {
  /** The selector for a `tm-field` cell. */
  static hostSelector = '.tm-field__cell';

  /** The cell's form field, in edit and create mode. */
  private readonly formField = this.locatorForOptional(TmFormFieldHarness);

  /**
   * A predicate for the cells that match the filters.
   *
   * @param options - The member and the label.
   * @returns The predicate.
   */
  static with(options: TmFieldHarnessFilters = {}): HarnessPredicate<TmFieldHarness> {
    return new HarnessPredicate(TmFieldHarness, options)
      .addOption('path', options.path, async (harness, path) => (await harness.getPath()) === path)
      .addOption('label', options.label, (harness, label) => HarnessPredicate.stringMatches(harness.getLabel(), label));
  }

  /** The member the cell shows, PascalCase. */
  async getPath(): Promise<string> {
    return (await (await this.host()).getAttribute('data-tm-field')) ?? '';
  }

  /** What the cell shows: a skeleton while the record loads, the value, or the control. */
  async getMode(): Promise<TmFieldMode> {
    if ((await this.formField()) !== null) {
      return 'edit';
    }
    return (await this.locatorForOptional('.tm-field__box tm-skeleton')()) === null ? 'view' : 'loading';
  }

  /** The label's text. */
  async getLabel(): Promise<string> {
    const field = await this.formField();
    if (field !== null) {
      return (await field.getLabelText()) ?? '';
    }
    return (await (await this.locatorFor('.tm-field__label')()).text()).trim();
  }

  /** The value box's text in view mode, and of a read-only field in edit mode; `null` while a control shows. */
  async getValueText(): Promise<string | null> {
    const value = await this.locatorForOptional('.tm-field__box .tm-field__value')();
    return value === null ? null : (await value.text()).trim();
  }

  /** Whether the value box shows the read-only lock, as a read-only field does in edit and create mode. */
  async isLocked(): Promise<boolean> {
    return (await this.locatorForOptional('.tm-field__lock')()) !== null;
  }

  /** The hint's text, or `null` without one. */
  async getHint(): Promise<string | null> {
    const field = await this.formField();
    if (field !== null) {
      return field.getHintText();
    }
    const hint = await this.locatorForOptional('.tm-field__hint')();
    return hint === null ? null : (await hint.text()).trim();
  }

  /** The validation messages the control shows; none outside edit and create mode. */
  async getErrors(): Promise<string[]> {
    const field = await this.formField();
    return field === null ? [] : field.getErrorTexts();
  }

  /** The cell's form field, in edit and create mode; `null` otherwise. */
  async getFormField(): Promise<TmFormFieldHarness | null> {
    return this.formField();
  }

  /** Whether the cell takes every column of its card's grid. */
  async isWide(): Promise<boolean> {
    return (await this.host()).hasClass('tm-field__cell--wide');
  }

  /** The label row's and value box's measures, which match across modes. */
  async getGeometry(): Promise<TmFieldGeometry> {
    const cell = await (await this.host()).getDimensions();
    // The cell's own label row and box: a control inside the box may hold form fields of its own.
    const label = await (await this.locatorFor('.tm-field__label, tm-field-editor > tm-form-field > .tm-form-field__label')()).getDimensions();
    // A select or a picker draws its own chrome: the field's box takes no space, and the control's own box is the box.
    const chromeless = 'tm-field-editor > tm-form-field > .tm-form-field__box--chromeless';
    const box = await (
      await this.locatorFor(
        '.tm-field__box',
        'tm-field-editor > tm-form-field > .tm-form-field__box:not(.tm-form-field__box--chromeless)',
        `${chromeless} .tm-select__trigger, ${chromeless} .tm-entity-picker, ${chromeless} .tm-entity-multi-picker`,
      )()
    ).getDimensions();
    return { labelHeight: label.height, boxTop: box.top - cell.top, boxHeight: box.height, boxWidth: box.width, cellHeight: cell.height };
  }

  /** Blurs the control, which marks the field touched, so its error shows. */
  async blur(): Promise<void> {
    const control = await this.locatorForOptional('input, textarea, [role="combobox"], [tabindex="0"]')();
    if (control !== null) {
      await control.focus();
      await control.blur();
    }
  }
}
