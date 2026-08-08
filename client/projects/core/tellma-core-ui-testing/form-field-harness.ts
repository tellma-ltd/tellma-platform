// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import { ComponentHarness } from '@angular/cdk/testing';

/**
 * Harness for `tm-form-field` — label/required-marker/hint/error scaffold
 * assertions.
 */
export class TmFormFieldHarness extends ComponentHarness {
  /** The selector for the `tm-form-field` host element. */
  static hostSelector = 'tm-form-field';

  private readonly labelEl = this.locatorForOptional('.tm-form-field__label');
  private readonly hintEl = this.locatorForOptional('.tm-form-field__hint');
  /**
   * Required, not optional: the message region is rendered whether or not
   * it holds text, because it is a live region the control's
   * `aria-describedby` points at. A field without one is a bug, not an
   * empty state.
   */
  private readonly errorEl = this.locatorFor('.tm-form-field__error');
  private readonly errorLineEls = this.locatorForAll('.tm-form-field__error-line');
  private readonly requiredEl = this.locatorForOptional('.tm-form-field__required');

  /** Gets the label text, or null when the field renders no label. */
  async getLabelText(): Promise<string | null> {
    const label = await this.labelEl();
    return label === null ? null : (await label.text()).trim();
  }

  /** Whether the visual required marker is shown next to the label. */
  async hasRequiredMarker(): Promise<boolean> {
    return (await this.requiredEl()) !== null;
  }

  /**
   * Gets the hint text, or null when the field carries no hint. A displayed
   * error no longer hides it — the hint is the field's standing
   * instruction, and it is most useful when the value is wrong.
   */
  async getHintText(): Promise<string | null> {
    const hint = await this.hintEl();
    if (hint === null || (await hint.getProperty<boolean>('hidden'))) {
      return null;
    }
    return (await hint.text()).trim();
  }

  /** Every currently displayed validation message, in order; empty when valid. */
  async getErrorTexts(): Promise<string[]> {
    const lines = await this.errorLineEls();
    return Promise.all(lines.map(async (line) => (await line.text()).trim()));
  }

  /** The first displayed error text, or null when none is shown. */
  async getErrorText(): Promise<string | null> {
    return (await this.getErrorTexts())[0] ?? null;
  }

  /**
   * Whether THIS field's visible validation bubble is on screen. It is
   * decoration — shown only while the control holds focus — so assert
   * message CONTENT through {@link getErrorTexts}, which does not depend on
   * focus. The bubble renders in the top layer outside the field host, so
   * it is matched by the field's error-region id stamped onto it
   * (`data-tm-error-for`): a page can hold OTHER fields' bubbles, or the
   * grid's cell bubble (the same element, and not focus-gated), and those
   * must not count as this field's.
   */
  async isErrorPopoverOpen(): Promise<boolean> {
    const id = await (await this.errorEl()).getAttribute('id');
    if (id === null) {
      return false;
    }
    const popover = await this.documentRootLocatorFactory().locatorForOptional(
      `tm-error-popover[data-tm-error-for="${id}"]`,
    )();
    return popover !== null;
  }

  /** Clicks the field's label; throws when the field renders none. */
  async labelClick(): Promise<void> {
    const label = await this.labelEl();
    if (label === null) {
      throw new Error('tm-form-field has no label to click');
    }
    await label.click();
  }
}
