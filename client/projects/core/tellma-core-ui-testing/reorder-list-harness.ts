// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import {
  ComponentHarness,
  TestKey,
  type ModifierKeys,
  type TestElement,
} from '@angular/cdk/testing';

import { TmMenuHarness } from './menu-harness';

/** The keys a reorder handle answers to. */
export type TmReorderKey =
  'Space' | 'Enter' | 'Escape' | 'Tab' | 'ArrowUp' | 'ArrowDown' | 'Home' | 'End';

/** The CDK test keys for each reorder key. */
const TEST_KEYS: Record<TmReorderKey, TestKey | string> = {
  Space: ' ',
  Enter: TestKey.ENTER,
  Escape: TestKey.ESCAPE,
  Tab: TestKey.TAB,
  ArrowUp: TestKey.UP_ARROW,
  ArrowDown: TestKey.DOWN_ARROW,
  Home: TestKey.HOME,
  End: TestKey.END,
};

/**
 * Harness for `tm-reorder-list`: its rows, handles, the grab model's keys
 * and the move menu a handle's click opens.
 */
export class TmReorderListHarness extends ComponentHarness {
  /** The selector for the `tm-reorder-list` host element. */
  static hostSelector = 'tm-reorder-list';

  /** The list element. */
  private readonly list = this.locatorFor('[role="list"]');

  /** The rows, in the order shown. */
  private readonly rows = this.locatorForAll('.tm-reorder-list__row');

  /** The handles, in the order shown. */
  private readonly handles = this.locatorForAll('.tm-reorder-list__handle');

  /** The list's accessible name. */
  async getLabel(): Promise<string | null> {
    return (await this.list()).getAttribute('aria-label');
  }

  /** The rows' content texts, in the order shown. */
  async getItemTexts(): Promise<string[]> {
    const contents = await this.locatorForAll('.tm-reorder-list__content')();
    return Promise.all(contents.map(async (content) => (await content.text()).trim()));
  }

  /** The handles' accessible names, in the order shown. */
  async getHandleLabels(): Promise<(string | null)[]> {
    const handles = await this.handles();
    return Promise.all(handles.map((handle) => handle.getAttribute('aria-label')));
  }

  /** The handles' description: the keyboard instructions. */
  async getInstructions(): Promise<string> {
    const [handle] = await this.handles();
    const id = handle === undefined ? null : await handle.getAttribute('aria-describedby');
    if (id === null) {
      return '';
    }
    const description = await this.locatorFor(`[id="${id}"]`)();
    return (await description.text()).trim();
  }

  /** The 0-based position of the grabbed row, or -1 when none is grabbed. */
  async getGrabbedIndex(): Promise<number> {
    const rows = await this.rows();
    for (let index = 0; index < rows.length; index += 1) {
      if (await rows[index].hasClass('tm-reorder-list__row--grabbed')) {
        return index;
      }
    }
    return -1;
  }

  /** The 0-based position of the row whose handle has focus, or -1. */
  async getFocusedHandleIndex(): Promise<number> {
    const handles = await this.handles();
    for (let index = 0; index < handles.length; index += 1) {
      if (await handles[index].isFocused()) {
        return index;
      }
    }
    return -1;
  }

  /** Focuses the handle of the row at a 0-based position. */
  async focusHandle(index: number): Promise<void> {
    return (await this.handleAt(index)).focus();
  }

  /** Presses a key on the handle of the row at a 0-based position, with modifiers. */
  async press(index: number, key: TmReorderKey, modifiers: ModifierKeys = {}): Promise<void> {
    const handle = await this.handleAt(index);
    await handle.focus();
    return handle.sendKeys(modifiers, TEST_KEYS[key]);
  }

  /** Presses a key on whichever handle has focus. */
  async pressFocused(key: TmReorderKey, modifiers: ModifierKeys = {}): Promise<void> {
    const index = await this.getFocusedHandleIndex();
    if (index === -1) {
      throw new Error('No reorder handle has focus.');
    }
    return (await this.handleAt(index)).sendKeys(modifiers, TEST_KEYS[key]);
  }

  /** Clicks the handle of the row at a 0-based position, which opens its move menu. */
  async clickHandle(index: number): Promise<void> {
    return (await this.handleAt(index)).click();
  }

  /** Whether the handle of the row at a 0-based position shows its move menu as open. */
  async isMoveMenuOpen(index: number): Promise<boolean> {
    return (await (await this.handleAt(index)).getAttribute('aria-expanded')) === 'true';
  }

  /**
   * Opens the move menu of the row at a 0-based position by clicking its
   * handle, and returns the menu, which renders in the overlay.
   */
  async openMoveMenu(index: number): Promise<TmMenuHarness> {
    await this.clickHandle(index);
    return this.documentRootLocatorFactory().locatorFor(TmMenuHarness)();
  }

  /** The handle of the row at a 0-based position. */
  private async handleAt(index: number): Promise<TestElement> {
    const handle = (await this.handles())[index];
    if (handle === undefined) {
      throw new Error(`The list has no row at position ${index}.`);
    }
    return handle;
  }
}
