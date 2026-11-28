// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import { ComponentHarness, TestKey, type ModifierKeys } from '@angular/cdk/testing';

// A harness for @tellma/core/expression-editor's editor. It types into the
// textarea, reads the status line, the problems list (portaled into the
// overlay, so read from the document root) and the parameter chips, and
// drives Retry, F8 and Mod+Enter.

/** One parameter chip, as {@link TmExpressionEditorHarness.getChips} reads it. */
export interface TmExpressionEditorChip {
  /** The chip's text, such as `@FromDate · Date`. */
  readonly text: string;
  /** Whether the chip warns: the parameter has no type, or conflicting ones. */
  readonly warning: boolean;
}

/** Harness for `tm-expression-editor`, the editor of one Queryex clause. */
export class TmExpressionEditorHarness extends ComponentHarness {
  /** The selector for the `tm-expression-editor` host element. */
  static hostSelector = 'tm-expression-editor';

  /** The textarea the code is typed into. */
  private readonly textarea = this.locatorFor('textarea.tm-expression-editor__input');

  /** The status line's text. */
  private readonly statusText = this.locatorFor('.tm-expression-editor__status-text');

  /** The editor's text. */
  async getValue(): Promise<string> {
    return (await this.textarea()).getProperty<string>('value');
  }

  /**
   * Replaces the editor's text, as one input event, the caret at its end.
   *
   * @param text - The new text.
   */
  async setValue(text: string): Promise<void> {
    const textarea = await this.textarea();
    await textarea.focus();
    await textarea.setInputValue(text);
    await textarea.dispatchEvent('input');
  }

  /**
   * Types text at the caret, key by key.
   *
   * @param text - The text.
   */
  async type(text: string): Promise<void> {
    return (await this.textarea()).sendKeys(text);
  }

  /** The status line's text: "No problems", "Checking…", the first problem, or why nothing was checked. */
  async getStatusText(): Promise<string> {
    return (await (await this.statusText()).text()).trim();
  }

  /** The text of the status line's list button, such as "+2 more", or `null` when there is none. */
  async getMoreText(): Promise<string | null> {
    const more = await this.locatorForOptional('.tm-expression-editor__more')();
    return more === null ? null : (await more.text()).trim();
  }

  /** Whether the status line offers Retry: the last check could not run. */
  async canRetry(): Promise<boolean> {
    return (await this.locatorForOptional('.tm-expression-editor__retry')()) !== null;
  }

  /** Clicks Retry. Fails loudly when the status line offers none. */
  async retry(): Promise<void> {
    const retry = await this.locatorForOptional('.tm-expression-editor__retry')();
    if (retry === null) {
      throw new Error('The expression editor offers no Retry: its last check ran.');
    }
    return retry.click();
  }

  /** Opens the problems list and reads each problem's line. Fails loudly when the status line has no list. */
  async getProblems(): Promise<string[]> {
    await this.openProblems();
    const items = await this.documentRootLocatorFactory().locatorForAll('.tm-expression-editor__problem-item')();
    return Promise.all(items.map(async (item) => (await item.text()).trim()));
  }

  /**
   * Opens the problems list and picks a problem, which selects its span.
   *
   * @param index - The problem's position in the list.
   */
  async pickProblem(index: number): Promise<void> {
    await this.openProblems();
    const item = (await this.documentRootLocatorFactory().locatorForAll('.tm-expression-editor__problem-item')())[index];
    if (item === undefined) {
      throw new Error(`The expression editor's problems list has no problem at ${index}.`);
    }
    return item.click();
  }

  /** The parameter chips, in order; none without `showParameters`. */
  async getChips(): Promise<TmExpressionEditorChip[]> {
    const chips = await this.locatorForAll('.tm-expression-editor__chip')();
    return Promise.all(
      chips.map(async (chip) => ({
        text: (await chip.text()).replace(/\s+/gu, ' ').trim(),
        warning: await chip.hasClass('tm-expression-editor__chip--warning'),
      })),
    );
  }

  /** Whether the textarea is `aria-invalid`: the text has a problem, or the field shows an error. */
  async isInvalid(): Promise<boolean> {
    return (await (await this.textarea()).getAttribute('aria-invalid')) === 'true';
  }

  /** The textarea's selection, as `[start, end]`. */
  async getSelection(): Promise<[number, number]> {
    const textarea = await this.textarea();
    return [await textarea.getProperty<number>('selectionStart'), await textarea.getProperty<number>('selectionEnd')];
  }

  /**
   * Presses F8, or Shift+F8, in the textarea: the next or previous problem is selected.
   *
   * @param backward - Whether to press Shift+F8.
   */
  async pressF8(backward = false): Promise<void> {
    return (await this.textarea()).sendKeys({ shift: backward }, TestKey.F8);
  }

  /**
   * Presses a key in the textarea, such as Enter with Control for Mod+Enter.
   *
   * @param key - The key.
   * @param modifiers - The modifiers held.
   */
  async press(key: TestKey, modifiers: ModifierKeys = {}): Promise<void> {
    return (await this.textarea()).sendKeys(modifiers, key);
  }

  /** The accessible name of the textarea, or `null`. */
  async getAriaLabel(): Promise<string | null> {
    return (await this.textarea()).getAttribute('aria-label');
  }

  /** The ids the textarea is described by: the status line's first. */
  async getDescribedByIds(): Promise<string[]> {
    const ids = await (await this.textarea()).getAttribute('aria-describedby');
    return ids === null || ids === '' ? [] : ids.split(' ');
  }

  /** What the polite region last announced. */
  async getAnnouncement(): Promise<string> {
    return (await (await this.locatorFor('.tm-expression-editor__announcer')()).text()).trim();
  }

  /** Whether the textarea has focus. */
  async isFocused(): Promise<boolean> {
    return (await this.textarea()).isFocused();
  }

  /** Opens the problems list, when it is closed. */
  private async openProblems(): Promise<void> {
    const open = await this.documentRootLocatorFactory().locatorForOptional('.tm-expression-editor__problems')();
    if (open !== null) {
      return;
    }
    const more = await this.locatorForOptional('.tm-expression-editor__more')();
    if (more === null) {
      throw new Error('The expression editor lists no problems: its status line has no list.');
    }
    await more.click();
  }
}
