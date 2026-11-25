// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import { TmMenuHarness } from '@tellma/core-ui-testing';

// The tenant shell's session menu: a tm-menu panel with the session menu's
// class, so every TmMenuHarness method works on its rows, plus its header
// (the avatar, the user's name and email) and its tenant card.

/** The tenant card, the session menu's first row. */
export interface TmSessionMenuTenantCard {
  /** The tenant's name. */
  readonly label: string;
  /** The second line, such as `Tenant 2 · Sandbox`. */
  readonly description: string | null;
  /** The trailing detail, `Switch`. */
  readonly detail: string | null;
}

/**
 * Harness for the tenant shell's session menu. The panel lives in the
 * overlay container while the menu is open, so a document root loader finds
 * it; `TmTenantShellHarness.openSessionMenu` opens it and returns it.
 *
 * @example
 * ```ts
 * const menu = await shell.openSessionMenu();
 * expect(await menu.getItemLabels()).toContain('My settings');
 * expect(await menu.getAvatarTone()).toBe(tmAvatarTone('core.User:42'));
 * ```
 */
export class TmSessionMenuHarness extends TmMenuHarness {
  /** The selector of the session menu's panel. */
  static override hostSelector = '.tm-menu__panel.tm-session-menu';

  readonly #name = this.locatorFor('.tm-session-menu__name');
  readonly #email = this.locatorFor('.tm-session-menu__email');
  readonly #avatar = this.locatorFor('.tm-menu__header tm-avatar');
  readonly #card = this.locatorFor('.tm-menu__item');

  /**
   * The header's name.
   *
   * @returns The user's name.
   */
  async getUserName(): Promise<string> {
    return (await (await this.#name()).text()).trim();
  }

  /**
   * The header's email.
   *
   * @returns The user's email.
   */
  async getEmail(): Promise<string> {
    return (await (await this.#email()).text()).trim();
  }

  /**
   * The header avatar's tone, which the user's record key picks.
   *
   * @returns The categorical tone, 1 to 8, or `null` for the accent tone.
   */
  async getAvatarTone(): Promise<number | null> {
    const tone = await (await this.#avatar()).getAttribute('data-tone');
    return tone === null ? null : Number(tone);
  }

  /**
   * The tenant card.
   *
   * @returns Its label, second line and detail.
   */
  async getTenantCard(): Promise<TmSessionMenuTenantCard> {
    const [card] = await this.getItems();
    return { label: await card.getText(), description: await card.getDescription(), detail: await card.getDetail() };
  }

  /**
   * The tenant card's accessible name.
   *
   * @returns The name, such as `Acme, Switch tenant`, or `null` without one.
   */
  async getTenantCardName(): Promise<string | null> {
    return (await this.#card()).getAttribute('aria-label');
  }
}
