// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import {
  ComponentHarness,
  HarnessPredicate,
  TestKey,
  type TestElement,
} from '@angular/cdk/testing';

/** Harness for one option row of an open `tm-entity-picker` dropdown. */
export class TmEntityPickerOptionHarness extends ComponentHarness {
  /** Matches entity result rows AND the command footer rows. */
  static hostSelector = '.tm-entity-picker__option';

  /** The row's visible text. */
  async getText(): Promise<string> {
    return (await this.host()).text();
  }

  /** Whether the row is the highlighted (active-descendant) option. */
  async isActive(): Promise<boolean> {
    return (await (await this.host()).getAttribute('data-active')) === 'true';
  }

  /** Whether the row mirrors the committed value (aria-selected). */
  async isSelected(): Promise<boolean> {
    return (await (await this.host()).getAttribute('aria-selected')) === 'true';
  }

  /** Whether the row is a command footer row (Advanced search…/Create…/Edit…). */
  async isAction(): Promise<boolean> {
    return (await this.host()).hasClass('tm-entity-picker__action');
  }

  /** Clicks the row (commits an entity row; opens a footer row's modal). */
  async click(): Promise<void> {
    return (await this.host()).click();
  }
}

/** Harness for `tm-entity-picker`. */
export class TmEntityPickerHarness extends ComponentHarness {
  /** The selector for the `tm-entity-picker` host element. */
  static hostSelector = 'tm-entity-picker';

  private readonly input = this.locatorFor('.tm-entity-picker__input');
  private readonly magnifier = this.locatorForOptional('.tm-entity-picker__magnifier');

  /**
   * The dropdown renders in the top layer, so its locators anchor at the
   * document root — but they must still find THIS picker's dropdown. The
   * combobox points at its own popup through `aria-controls`, so that id
   * scopes every panel query to the right one even when a second picker
   * (one inside a modal this picker launched, say) is also on the page.
   * Returns `null` when this picker's dropdown is closed.
   */
  private async panelSelector(): Promise<string | null> {
    const id = await (await this.input()).getAttribute('aria-controls');
    return id === null || id === '' ? null : `#${id}`;
  }

  /** This picker's open listbox element, or `null` when the dropdown is closed. */
  private async panelElement(): Promise<TestElement | null> {
    const selector = await this.panelSelector();
    return selector === null
      ? null
      : this.documentRootLocatorFactory().locatorForOptional(selector)();
  }

  /** The input's current text (the query, or the committed display text). */
  async getQueryText(): Promise<string> {
    return (await this.input()).getProperty<string>('value');
  }

  /** Types like a user: focus, clear, send keys — leaving the input focused. */
  async typeQuery(text: string): Promise<void> {
    const input = await this.input();
    await input.clear();
    if (text !== '') {
      await input.sendKeys(text);
    }
  }

  /** Focuses the input. */
  async focus(): Promise<void> {
    return (await this.input()).focus();
  }

  /** Blurs the input (triggers the picker's departure handling). */
  async blur(): Promise<void> {
    return (await this.input()).blur();
  }

  /** Whether the dropdown is open. */
  async isOpen(): Promise<boolean> {
    return (await this.panelElement()) !== null;
  }

  /** Opens the dropdown by clicking the input (the pristine browse path). */
  async open(): Promise<void> {
    const input = await this.input();
    await input.focus();
    await input.click();
  }

  /** Closes the dropdown with Escape (the text is kept). */
  async close(): Promise<void> {
    return (await this.input()).sendKeys(TestKey.ESCAPE);
  }

  /** The option rows of the open dropdown (entity rows and footer rows). */
  async getOptions(
    filter?: HarnessPredicate<TmEntityPickerOptionHarness>,
  ): Promise<TmEntityPickerOptionHarness[]> {
    const selector = await this.panelSelector();
    if (selector === null) {
      return [];
    }
    const scoped = new HarnessPredicate(TmEntityPickerOptionHarness, { ancestor: selector });
    const rows = await this.documentRootLocatorFactory().locatorForAll(scoped)();
    if (filter === undefined) {
      return rows;
    }
    // The caller's predicate narrows WITHIN this picker's panel — it never
    // widens the search back out to the document.
    const matched: TmEntityPickerOptionHarness[] = [];
    for (const row of rows) {
      if (await filter.evaluate(row)) {
        matched.push(row);
      }
    }
    return matched;
  }

  /** The visible entity-result labels, in order (footer rows excluded). */
  async getOptionLabels(): Promise<string[]> {
    const options = await this.getOptions();
    const labels: string[] = [];
    for (const option of options) {
      if (!(await option.isAction())) {
        labels.push(await option.getText());
      }
    }
    return labels;
  }

  /** The highlighted option's text, or `null` when nothing is highlighted. */
  async getActiveOptionLabel(): Promise<string | null> {
    for (const option of await this.getOptions()) {
      if (await option.isActive()) {
        return option.getText();
      }
    }
    return null;
  }

  /**
   * Clicks the entity row whose text matches `label`. Fails loudly when the
   * dropdown is closed or no row matches — a silent no-op would otherwise
   * masquerade as a pick.
   */
  async selectOptionByLabel(label: string): Promise<void> {
    if ((await this.panelElement()) === null) {
      throw new Error('TmEntityPickerHarness.selectOptionByLabel: the dropdown is not open');
    }
    for (const option of await this.getOptions()) {
      if (!(await option.isAction()) && (await option.getText()) === label) {
        return option.click();
      }
    }
    throw new Error(`TmEntityPickerHarness.selectOptionByLabel: no option labeled "${label}"`);
  }

  /** Whether the open dropdown currently shows the loading spinner. */
  async isSpinnerShown(): Promise<boolean> {
    const selector = await this.panelSelector();
    if (selector === null) {
      return false;
    }
    const spinner = await this.documentRootLocatorFactory().locatorForOptional(
      `${selector} .tm-entity-picker__status tm-spinner`,
    )();
    return spinner !== null;
  }

  /** The status row's text ("No results" / "Search failed"), or `null`. */
  async getStatusText(): Promise<string | null> {
    const selector = await this.panelSelector();
    if (selector === null) {
      return null;
    }
    const status = await this.documentRootLocatorFactory().locatorForOptional(
      `${selector} .tm-entity-picker__status`,
    )();
    if (status === null) {
      return null;
    }
    const text = await status.text();
    return text === '' ? null : text;
  }

  /** Whether the magnifier button renders (advanced search configured). */
  async hasMagnifier(): Promise<boolean> {
    return (await this.magnifier()) !== null;
  }

  /** Clicks the magnifier (opens the advanced-search page). Fails when absent. */
  async clickMagnifier(): Promise<void> {
    const magnifier = await this.magnifier();
    if (magnifier === null) {
      throw new Error(
        'TmEntityPickerHarness.clickMagnifier: no magnifier — [advancedSearch] is not configured',
      );
    }
    if (await magnifier.getProperty<boolean>('disabled')) {
      throw new Error('TmEntityPickerHarness.clickMagnifier: the magnifier is disabled');
    }
    return magnifier.click();
  }

  /** Whether the input currently announces itself as invalid (aria-invalid). */
  async isInvalid(): Promise<boolean> {
    return (await (await this.input()).getAttribute('aria-invalid')) === 'true';
  }

  /**
   * Whether the control reports busy (`aria-busy`) — that is, whether it is
   * mapping the typed text to an entity. A dropdown search in progress is
   * NOT busy: the panel's own spinner reports that (`isSpinnerShown`).
   */
  async isBusy(): Promise<boolean> {
    return (await (await this.input()).getAttribute('aria-busy')) === 'true';
  }
}
