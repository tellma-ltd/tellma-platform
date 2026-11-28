// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import { ComponentHarness, HarnessPredicate, type TestElement } from '@angular/cdk/testing';
import { TmButtonHarness, TmSwitchHarness } from '@tellma/core-ui-testing';

// The harness of the Notifications section of My settings: its save
// status and Try again, the pop-up switch, the grid's cells by type and
// channel key, and the test delivery rows. It reads what the user sees and
// what a screen reader reads, never the section's model.

/** What a grid cell holds: a switch, a locked inbox, or a channel-only type's dash. */
export type TmNotificationCellKind = 'switch' | 'locked' | 'none';

/** One grid cell's state. */
export interface TmNotificationCellState {
  /** What the cell holds. */
  readonly kind: TmNotificationCellKind;
  /** Whether its switch is on; `false` for a dash. */
  readonly checked: boolean;
  /** Whether its switch is disabled; `true` for a dash. */
  readonly disabled: boolean;
  /** The switch's accessible name, "{type}, {channel}", or the dash's screen-reader line. */
  readonly name: string;
  /** What its switch's description reads, such as "Always on" or why it is disabled; `null` without one. */
  readonly description: string | null;
}

/** One test delivery row's state. */
export interface TmNotificationTestRowState {
  /** The channel's label. */
  readonly channel: string;
  /** The address it shows, or `null` for a channel that resolves none. */
  readonly address: string | null;
  /** Whether "Send a test" is disabled, natively or with a reason. */
  readonly disabled: boolean;
  /** Why it is disabled, which its tooltip shows; `null` without a reason. */
  readonly reason: string | null;
  /** Whether a test is in flight. */
  readonly pending: boolean;
  /** The inline result, or `null` before a test. */
  readonly result: string | null;
  /** The result's link, for a missing address; `null` otherwise. */
  readonly link: string | null;
}

/** The text an element shows, its runs of white space folded to one space. */
async function folded(element: TestElement): Promise<string> {
  return (await element.text()).replace(/\s+/g, ' ').trim();
}

/** A CSS attribute selector's quoted value. */
function quoted(value: string): string {
  return `"${value.replaceAll('\\', '\\\\').replaceAll('"', '\\"')}"`;
}

/** Harness for `tm-notification-settings`, the Notifications section of My settings. */
export class TmNotificationSettingsHarness extends ComponentHarness {
  /** The selector of the section. */
  static hostSelector = 'tm-notification-settings';

  readonly #status = this.locatorFor('.tm-notification-settings__status');
  readonly #retry = this.locatorForOptional(new HarnessPredicate(TmButtonHarness, { ancestor: '.tm-notification-settings__header' }));
  readonly #lock = this.locatorForOptional('.tm-notification-settings__lock');
  readonly #popups = this.locatorForOptional(new HarnessPredicate(TmSwitchHarness, { ancestor: '.tm-notification-settings__popups' }));
  readonly #headings = this.locatorForAll('.tm-notification-settings__card[data-category] .tm-card__heading');
  readonly #tests = this.locatorForAll('.tm-notification-settings__test');
  readonly #testCard = this.locatorForOptional('.tm-notification-settings__test-card');
  readonly #error = this.locatorForOptional('tm-error-state, tm-route-error, .tm-notification-settings__failed');

  /**
   * The save status the header reads: "Saving…", "All changes saved",
   * "Couldn't save your changes", or empty.
   *
   * @returns The status.
   */
  async getStatus(): Promise<string> {
    return folded(await this.#status());
  }

  /**
   * Whether the header offers Try again, after a failed save.
   *
   * @returns Whether it does.
   */
  async canRetry(): Promise<boolean> {
    return (await this.#retry()) !== null;
  }

  /** Presses Try again. */
  async retry(): Promise<void> {
    const button = await this.#retry();
    if (button === null) {
      throw new Error('The section offers no Try again: no save has failed.');
    }
    await button.click();
  }

  /**
   * Whether the section shows a failure instead of its cards: a failed read,
   * or code of the section that did not load.
   *
   * @returns Whether it does.
   */
  async hasLoadError(): Promise<boolean> {
    return (await this.#error()) !== null;
  }

  /**
   * Why every control is disabled, which each one's description reads.
   *
   * @returns The reason, or `null` while the controls are enabled.
   */
  async getLockReason(): Promise<string | null> {
    const lock = await this.#lock();
    return lock === null ? null : folded(lock);
  }

  /**
   * The pop-up switch, once the settings have loaded.
   *
   * @returns The switch.
   */
  async getPopupSwitch(): Promise<TmSwitchHarness> {
    const popups = await this.#popups();
    if (popups === null) {
      throw new Error('The section shows no pop-up switch: its settings have not loaded.');
    }
    return popups;
  }

  /**
   * The categories' card headings, in their order.
   *
   * @returns The headings.
   */
  async getCategories(): Promise<string[]> {
    return Promise.all((await this.#headings()).map(folded));
  }

  /**
   * A grid cell's state.
   *
   * @param type - The notification type's key.
   * @param channel - The channel's key.
   * @returns The state.
   */
  async getCell(type: string, channel: string): Promise<TmNotificationCellState> {
    const cell = await this.#cell(type, channel);
    const input = await this.locatorForOptional(`${this.#cellSelector(type, channel)} input[role="switch"]`)();
    if (input === null) {
      const line = await this.locatorFor(`${this.#cellSelector(type, channel)} .tm-notification-settings__hidden`)();
      return { kind: 'none', checked: false, disabled: true, name: await folded(line), description: null };
    }
    const describedBy = (await input.getAttribute('aria-describedby')) ?? '';
    const descriptions = await Promise.all(
      describedBy
        .split(/\s+/)
        .filter((id) => id !== '')
        .map(async (id) => {
          const target = await this.locatorForOptional(`[id=${quoted(id)}]`)();
          return target === null ? '' : folded(target);
        }),
    );
    const description = descriptions.filter((text) => text !== '').join(' ');
    return {
      kind: (await cell.getAttribute('data-kind')) === 'locked' ? 'locked' : 'switch',
      checked: await input.getProperty<boolean>('checked'),
      disabled: await input.getProperty<boolean>('disabled'),
      name: (await input.getAttribute('aria-label')) ?? '',
      description: description === '' ? null : description,
    };
  }

  /**
   * Turns a grid cell's switch over, as a click does.
   *
   * @param type - The notification type's key.
   * @param channel - The channel's key.
   */
  async toggleCell(type: string, channel: string): Promise<void> {
    const input = await this.locatorForOptional(`${this.#cellSelector(type, channel)} input[role="switch"]`)();
    if (input === null) {
      throw new Error(`The cell of ${type} on ${channel} holds no switch.`);
    }
    await input.click();
  }

  /**
   * Whether the section offers test delivery: only the signed-in user's does.
   *
   * @returns Whether it does.
   */
  async hasTestDelivery(): Promise<boolean> {
    return (await this.#testCard()) !== null;
  }

  /**
   * The test delivery rows' states, in channel order.
   *
   * @returns The rows.
   */
  async getTestRows(): Promise<TmNotificationTestRowState[]> {
    const rows = await this.#tests();
    return Promise.all(rows.map(async (row) => this.#testRow((await row.getAttribute('data-channel')) ?? '')));
  }

  /**
   * Presses a test row's "Send a test".
   *
   * @param channel - The channel's key.
   */
  async sendTest(channel: string): Promise<void> {
    await (await this.#sendButton(channel)).click();
  }

  /**
   * A test row's state.
   *
   * @param channel - The channel's key.
   * @returns The state.
   */
  async #testRow(channel: string): Promise<TmNotificationTestRowState> {
    const scope = `.tm-notification-settings__test[data-channel=${quoted(channel)}]`;
    const [label, address, result, link] = await Promise.all([
      this.locatorFor(`${scope} .tm-notification-settings__test-channel`)(),
      this.locatorForOptional(`${scope} .tm-notification-settings__address`)(),
      this.locatorForOptional(`${scope} .tm-notification-settings__result`)(),
      this.locatorForOptional(`${scope} .tm-notification-settings__result a`)(),
    ]);
    const button = await this.#sendButton(channel);
    const reason = await button.getDisabledReason();
    return {
      channel: await folded(label),
      address: address === null ? null : await folded(address),
      // A reason disables the button through aria-disabled, keeping it focusable for its tooltip.
      disabled: (await button.isDisabled()) || reason !== null,
      reason,
      pending: await button.isPending(),
      result: result === null ? null : await folded(result),
      link: link === null ? null : await link.getAttribute('href'),
    };
  }

  /**
   * A test row's "Send a test".
   *
   * @param channel - The channel's key.
   * @returns The button.
   */
  async #sendButton(channel: string): Promise<TmButtonHarness> {
    return this.locatorFor(new HarnessPredicate(TmButtonHarness, { ancestor: `.tm-notification-settings__test[data-channel=${quoted(channel)}]` }))();
  }

  /**
   * A grid cell.
   *
   * @param type - The notification type's key.
   * @param channel - The channel's key.
   * @returns The cell.
   */
  async #cell(type: string, channel: string): Promise<TestElement> {
    const cell = await this.locatorForOptional(this.#cellSelector(type, channel))();
    if (cell === null) {
      throw new Error(`The grid shows no cell of ${type} on ${channel}.`);
    }
    return cell;
  }

  /**
   * A grid cell's selector.
   *
   * @param type - The notification type's key.
   * @param channel - The channel's key.
   * @returns The selector.
   */
  #cellSelector(type: string, channel: string): string {
    return `tr[data-type=${quoted(type)}] td[data-channel=${quoted(channel)}]`;
  }
}
