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
    return this.activate('selectDay', `[data-tm-day="${day}"]`);
  }

  /**
   * Activates the header's view switch, cycling the open popup's view
   * day → month → year → day.
   */
  async switchView(): Promise<void> {
    if ((await this.popup()) === null) {
      throw new Error('TmDatePickerHarness.switchView: the popup is not open');
    }
    const button = await this.documentRootLocatorFactory().locatorFor(
      '.tm-date-popup__view-switch',
    )();
    return button.click();
  }

  /**
   * Activates a month cell of the open popup's month view by its 1-based
   * in-calendar month number (13 in thirteen-month calendars). The month
   * view drills down: this navigates to that month's day view.
   */
  async selectMonth(month: number): Promise<void> {
    return this.activate('selectMonth', `[data-tm-month="${month}"]`);
  }

  /**
   * Activates a year cell of the open popup's year view by its
   * display-calendar year number. The year view drills down: this
   * navigates to that year's month view.
   */
  async selectYear(year: number): Promise<void> {
    return this.activate('selectYear', `[data-tm-year="${year}"]`);
  }

  /** Activates the localized Today action of the open popup. */
  async selectToday(): Promise<void> {
    // Today is disabled whenever today itself falls outside min/max.
    return this.activate(
      'selectToday',
      '.tm-date-popup__footer .tm-date-popup__action:first-child',
    );
  }

  /**
   * Clicks a control of the open popup. Fails loudly when the popup is
   * closed, the control is not on the displayed page, or it is disabled —
   * a disabled button swallows the click, so the caller would otherwise
   * see a silent no-op instead of a failure.
   */
  private async activate(method: string, selector: string): Promise<void> {
    if ((await this.popup()) === null) {
      throw new Error(`TmDatePickerHarness.${method}: the popup is not open`);
    }
    const control = await this.documentRootLocatorFactory().locatorForOptional(
      `.tm-date-popup ${selector}`,
    )();
    if (control === null) {
      throw new Error(`TmDatePickerHarness.${method}: nothing matching ${selector} is displayed`);
    }
    if (await control.getProperty<boolean>('disabled')) {
      throw new Error(`TmDatePickerHarness.${method}: ${selector} is disabled`);
    }
    return control.click();
  }

  /** Activates the localized Clear action of the open popup. */
  async clear(): Promise<void> {
    const action = await this.documentRootLocatorFactory().locatorFor(
      '.tm-date-popup__footer .tm-date-popup__action:last-child',
    )();
    return action.click();
  }
}
