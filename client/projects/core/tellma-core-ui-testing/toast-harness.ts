// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import { ComponentHarness, TestKey } from '@angular/cdk/testing';

/**
 * Harness for one toast on show from `TmToasts`.
 *
 * Toasts render in a host on `<body>`, outside any fixture — locate them
 * with a document-root loader
 * (`TestbedHarnessEnvironment.documentRootLoader(fixture)`). The stack
 * lists the newest toast first, and only the toasts on show (the ones
 * hidden behind the overflow pill are not drawn).
 */
export class TmToastHarness extends ComponentHarness {
  /** The selector for one toast card. */
  static hostSelector = '.tm-toast';

  /** The tone (`neutral`, `accent`, `success`, `warning`, `error`). */
  async getTone(): Promise<string | null> {
    return (await this.host()).getAttribute('data-tone');
  }

  /** The title line. */
  async getTitle(): Promise<string> {
    return (await (await this.locatorFor('.tm-toast__title')()).text()).trim();
  }

  /** The detail line, or `null` when the toast has none. */
  async getDetail(): Promise<string | null> {
    const detail = await this.locatorForOptional('.tm-toast__detail')();
    return detail === null ? null : (await detail.text()).trim();
  }

  /** The action button's label, or `null` when the toast has no action. */
  async getActionLabel(): Promise<string | null> {
    const action = await this.locatorForOptional('.tm-toast__action')();
    return action === null ? null : (await action.text()).trim();
  }

  /** Clicks the action button (it runs, then the toast closes). */
  async runAction(): Promise<void> {
    return (await this.locatorFor('.tm-toast__action')()).click();
  }

  /** Clicks the close button. */
  async close(): Promise<void> {
    return (await this.locatorFor('.tm-toast__close')()).click();
  }

  /** The close button's accessible name ("Close"). */
  async getCloseLabel(): Promise<string | null> {
    return (await this.locatorFor('.tm-toast__close')()).getAttribute('aria-label');
  }

  /** Whether focus is inside the toast. */
  async isFocused(): Promise<boolean> {
    for (const button of await this.locatorForAll('button')()) {
      if (await button.isFocused()) {
        return true;
      }
    }
    return false;
  }

  /** Presses Escape on the focused button of this toast, which closes it. */
  async pressEscape(): Promise<void> {
    for (const button of await this.locatorForAll('button')()) {
      if (await button.isFocused()) {
        return button.sendKeys(TestKey.ESCAPE);
      }
    }
    throw new Error('Focus is not inside the toast.');
  }
}

/**
 * Harness for the stack `TmToasts` draws: its overflow pill and its two
 * live regions. Locate it with a document-root loader, as for
 * {@link TmToastHarness}.
 */
export class TmToastStackHarness extends ComponentHarness {
  /** The selector for the toasts' host. */
  static hostSelector = '.tm-toast-host';

  /** The toasts on show, newest first. */
  async getToasts(): Promise<TmToastHarness[]> {
    return this.locatorForAll(TmToastHarness)();
  }

  /** The overflow pill's text ("2 more"), or `null` while nothing hides. */
  async getPillText(): Promise<string | null> {
    const pill = await this.locatorForOptional('.tm-toasts__pill')();
    return pill === null ? null : (await pill.text()).trim();
  }

  /** Clicks the overflow pill (when it is a button). */
  async clickPill(): Promise<void> {
    return (await this.locatorFor('button.tm-toasts__pill')()).click();
  }

  /** The text in the polite live region. */
  async getPoliteAnnouncement(): Promise<string> {
    return (await (await this.locatorFor('[role="status"]')()).text()).trim();
  }

  /** The text in the assertive live region (error toasts). */
  async getAssertiveAnnouncement(): Promise<string> {
    return (await (await this.locatorFor('[role="alert"]')()).text()).trim();
  }

  /** Moves the pointer over the stack, which pauses every timer. */
  async hover(): Promise<void> {
    return (await this.locatorFor('.tm-toasts')()).dispatchEvent('pointerenter');
  }

  /** Moves the pointer off the stack, which resumes the timers. */
  async unhover(): Promise<void> {
    return (await this.locatorFor('.tm-toasts')()).dispatchEvent('pointerleave');
  }
}
