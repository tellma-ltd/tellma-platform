// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import { ComponentHarness } from '@angular/cdk/testing';
import { TmEntityMultiPickerHarness, TmEntityPickerHarness } from '@tellma/core-ui-testing';

// Harnesses for @tellma/core/entity's entity fields. Each field draws one
// picker from @tellma/core-ui and forwards its state to it, so a harness
// reads and drives the picker inside, and hands it out for the dropdown's
// rows, descriptions and status.

/** Harness for `tm-entity-field`, the field that chooses one entity of a type. */
export class TmEntityFieldHarness extends ComponentHarness {
  /** The selector for the `tm-entity-field` host element. */
  static hostSelector = 'tm-entity-field';

  /** The field's picker. */
  private readonly picker = this.locatorFor(TmEntityPickerHarness);

  /** The field's picker: its dropdown's rows, its status and its magnifier. */
  async getPicker(): Promise<TmEntityPickerHarness> {
    return this.picker();
  }

  /** The input's text: the chosen row's display name, or the text being typed. */
  async getText(): Promise<string> {
    return (await this.picker()).getQueryText();
  }

  /**
   * Types `text` like a user, leaving the input focused while the field
   * searches.
   *
   * @param text - The text.
   */
  async search(text: string): Promise<void> {
    return (await this.picker()).typeQuery(text);
  }

  /**
   * Chooses the dropdown row labelled `label`, opening the dropdown's first
   * rows when it is closed. Fails loudly when no row has that label.
   *
   * @param label - The row's label.
   */
  async pick(label: string): Promise<void> {
    const picker = await this.picker();
    if (!(await picker.isOpen())) {
      await picker.open();
    }
    await this.forceStabilize();
    return picker.selectOptionByLabel(label);
  }

  /** Whether the field is read-only: it shows its row and searches nothing. */
  async isReadonly(): Promise<boolean> {
    return (await this.picker()).isReadonly();
  }

  /** Whether the field announces itself as invalid. */
  async isInvalid(): Promise<boolean> {
    return (await this.picker()).isInvalid();
  }
}

/** Harness for `tm-entity-multi-field`, the field that chooses several entities of a type. */
export class TmEntityMultiFieldHarness extends ComponentHarness {
  /** The selector for the `tm-entity-multi-field` host element. */
  static hostSelector = 'tm-entity-multi-field';

  /** The field's multi-picker. */
  private readonly picker = this.locatorFor(TmEntityMultiPickerHarness);

  /** The field's multi-picker: its chips, its count badge and its search input. */
  async getPicker(): Promise<TmEntityMultiPickerHarness> {
    return this.picker();
  }

  /** The chosen rows' display names, in pick order. */
  async getChipLabels(): Promise<string[]> {
    return (await this.picker()).getChipLabels();
  }

  /**
   * Types `text` into the search input like a user, leaving it focused.
   *
   * @param text - The text.
   */
  async search(text: string): Promise<void> {
    return (await this.picker()).search(text);
  }

  /**
   * Clicks the dropdown row labelled `label`: a chosen row comes off, any
   * other goes on.
   *
   * @param label - The row's label.
   */
  async pick(label: string): Promise<void> {
    return (await this.picker()).pick(label);
  }

  /**
   * Removes the chip labelled `label`. Fails loudly when no chip has that
   * label or the field cannot remove it.
   *
   * @param label - The chip's label.
   */
  async removeChip(label: string): Promise<void> {
    return (await this.picker()).removeChip(label);
  }

  /** Whether the field is read-only: it shows its rows and searches nothing. */
  async isReadonly(): Promise<boolean> {
    return (await (await this.picker()).getPicker()).isReadonly();
  }

  /** Whether the field announces itself as invalid. */
  async isInvalid(): Promise<boolean> {
    return (await (await this.picker()).getPicker()).isInvalid();
  }
}
