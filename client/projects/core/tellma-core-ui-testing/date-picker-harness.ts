// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import { ComponentHarness } from '@angular/cdk/testing';

/** Harness for `tm-date-picker`. */
export class TmDatePickerHarness extends ComponentHarness {
  /** The selector for the `tm-date-picker` host element. */
  static hostSelector = 'tm-date-picker';

  private readonly input = this.locatorFor('.tm-date-picker__input');
  private readonly toggle = this.locatorFor('.tm-date-picker__toggle');
  /** The popup renders in the top layer — anchor its locators at the document root. */
  private readonly popup = this.documentRootLocatorFactory().locatorForOptional('.tm-date-popup');

  /** Gets the displayed text (the locale-formatted date or the raw entry). */
  async getText(): Promise<string> {
    return (await this.input()).getProperty<string>('value');
  }

  /** Types like a user: focus, clear, send keys, leaving the input focused. */
  async setText(text: string): Promise<void> {
    const input = await this.input();
    await input.clear();
    if (text !== '') {
      await input.sendKeys(text);
    }
  }

  /** Blurs the input (commits typed text). */
  async blur(): Promise<void> {
    return (await this.input()).blur();
  }

  /** Focuses the input. */
  async focus(): Promise<void> {
    return (await this.input()).focus();
  }

  /** Whether the input currently announces itself as invalid (aria-invalid). */
  async isInvalid(): Promise<boolean> {
    return (await (await this.input()).getAttribute('aria-invalid')) === 'true';
  }

  /** Opens the calendar popup via the (pointer-only) calendar button. */
  async openPopup(): Promise<void> {
    return (await this.toggle()).click();
  }

  /** Whether the calendar popup is open. */
  async isPopupOpen(): Promise<boolean> {
    return (await this.popup()) !== null;
  }

  /** Activates a day cell of the open popup by its in-calendar day number. */
  async selectDay(day: number): Promise<void> {
    const popup = await this.popup();
    if (popup === null) {
      throw new Error('TmDatePickerHarness.selectDay: the popup is not open');
    }
    const cell = await this.documentRootLocatorFactory().locatorFor(
      `.tm-date-popup [data-tm-day="${day}"]`,
    )();
    return cell.click();
  }

  /** Activates the localized Today action of the open popup. */
  async selectToday(): Promise<void> {
    const action = await this.documentRootLocatorFactory().locatorFor(
      '.tm-date-popup__footer .tm-date-popup__action:first-child',
    )();
    return action.click();
  }

  /** Activates the localized Clear action of the open popup. */
  async clear(): Promise<void> {
    const action = await this.documentRootLocatorFactory().locatorFor(
      '.tm-date-popup__footer .tm-date-popup__action:last-child',
    )();
    return action.click();
  }
}
