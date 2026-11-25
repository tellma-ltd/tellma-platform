// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import { ComponentHarness, HarnessPredicate, type HarnessQuery, type TestElement } from '@angular/cdk/testing';
import { TmPopoverHarness } from '@tellma/core-ui-testing';

import { TmSessionMenuHarness } from './tm-session-menu-harness';

// The tenant shell at /{t}: what its main landmark shows (the routed page,
// the error state of a failed cold sync, or what replaces the route host),
// and its app bar: the wordmark, the installed-mode history, the slots, the
// reserved boxes, the markers with their popovers and the session button
// with its menu. The menu and the popovers live in a chunk the shell loads
// after its first frame, and the installed-mode Back and Forward in a chunk
// of their own, so opening one or reading the other waits for it.

/** What the tenant shell's main landmark shows. */
export type TmTenantShellMain = 'page' | 'error' | 'replaced';

/** One distribution slot's button. */
export interface TmTenantShellSlot {
  /** The slot's id. */
  readonly id: string;
  /** The button's accessible name: `{label}`, or `{label}, {n}` with a count. */
  readonly name: string | null;
}

/** How long an overlay may take to open, its chunk's load included. */
const OPEN_DEADLINE_MS = 4_000;

/** The text an element shows, its runs of white space folded to one space. */
async function folded(element: TestElement): Promise<string> {
  return (await element.text()).replace(/\s+/g, ' ').trim();
}

/** Waits a macrotask, so a lazily loaded chunk can land. */
function tick(): Promise<void> {
  return new Promise((resolve) => setTimeout(resolve, 10));
}

/**
 * Harness for `tm-tenant-shell`, the tenant routes' shell.
 *
 * @example
 * ```ts
 * const shell = await TestbedHarnessEnvironment.documentRootLoader(fixture).getHarness(TmTenantShellHarness);
 * expect(await shell.getTenantName()).toBe('Acme');
 * const menu = await shell.openSessionMenu();
 * await menu.clickItem('About');
 * ```
 */
export class TmTenantShellHarness extends ComponentHarness {
  /** The selector of the `tm-tenant-shell` element. */
  static hostSelector = 'tm-tenant-shell';

  readonly #wordmark = this.locatorFor('.tm-app-bar__wordmark');
  readonly #session = this.locatorFor('.tm-app-bar__session');
  readonly #user = this.locatorFor('.tm-app-bar__user');
  readonly #tenant = this.locatorForOptional('.tm-app-bar__tenant');
  readonly #tenantSkeleton = this.locatorForOptional('.tm-app-bar__tenant-skeleton');
  readonly #face = this.locatorForOptional('.tm-app-bar__session tm-avatar, .tm-app-bar__face');
  readonly #markers = this.locatorForAll('.tm-app-bar__marker');
  readonly #slots = this.locatorForAll('button.tm-app-bar__slot');
  readonly #reserved = this.locatorForAll('span[data-tm-slot]');
  readonly #historyBox = this.locatorForOptional('.tm-app-bar__history');
  readonly #history = this.locatorForAll('.tm-shell-history__button');
  readonly #error = this.locatorForOptional('.tm-tenant-shell__error');
  readonly #outlet = this.locatorForOptional('main router-outlet');
  readonly #errorActions = this.locatorForAll('.tm-tenant-shell__error button');

  /**
   * What the main landmark shows.
   *
   * @returns `page` for the route host, `error` for a failed cold sync's
   *   error state (or the shell's own line while the error views cannot
   *   load), and `replaced` for what replaces the route host, such as the
   *   tenant-state page.
   */
  async getMain(): Promise<TmTenantShellMain> {
    if ((await this.#error()) !== null) {
      return 'error';
    }
    return (await this.#outlet()) !== null ? 'page' : 'replaced';
  }

  /**
   * The error state of a failed cold sync.
   *
   * @returns Its text, or `null` while main shows no error.
   */
  async getError(): Promise<string | null> {
    const error = await this.#error();
    return error === null ? null : folded(error);
  }

  /**
   * Clicks one of the error state's actions, such as Retry or Reload.
   *
   * @param label - The action's label.
   * @throws Error when main shows no error, or it has no such action.
   */
  async clickErrorAction(label: string): Promise<void> {
    for (const button of await this.#errorActions()) {
      if ((await folded(button)) === label) {
        await button.click();
        return;
      }
    }
    throw new Error(`The tenant shell's error state has no action "${label}".`);
  }

  /**
   * Where the wordmark leads: the tenant's home.
   *
   * @returns The link's `href`.
   */
  async getHomeHref(): Promise<string | null> {
    return (await this.#wordmark()).getAttribute('href');
  }

  /**
   * The session button's accessible name.
   *
   * @returns The name, such as `Account: Farah, Acme, sandbox`.
   */
  async getAccountName(): Promise<string | null> {
    return (await this.#session()).getAttribute('aria-label');
  }

  /**
   * The user's name on the session button.
   *
   * @returns The name, which the button hides below xl.
   */
  async getUserName(): Promise<string> {
    return folded(await this.#user());
  }

  /**
   * The tenant's name on the session button.
   *
   * @returns The name, or `null` while a cold launch's sync runs and a skeleton stands in.
   */
  async getTenantName(): Promise<string | null> {
    const tenant = await this.#tenant();
    return tenant === null ? null : folded(tenant);
  }

  /**
   * Whether the tenant's name is a skeleton, while a cold launch's sync runs.
   *
   * @returns Whether it is.
   */
  async isTenantNameLoading(): Promise<boolean> {
    return (await this.#tenantSkeleton()) !== null;
  }

  /**
   * The session button's avatar tone, which the user's record key picks.
   *
   * @returns The categorical tone, 1 to 8, or `null` for the accent tone.
   */
  async getAvatarTone(): Promise<number | null> {
    const tone = await (await this.#face())?.getAttribute('data-tone');
    return tone === null || tone === undefined ? null : Number(tone);
  }

  /**
   * The markers' labels, in order.
   *
   * @returns The labels, such as `['Sandbox', 'Read-only']`; none for a live, active tenant.
   */
  async getMarkers(): Promise<string[]> {
    return Promise.all((await this.#markers()).map(folded));
  }

  /**
   * Opens a marker's popover and returns it.
   *
   * @param label - The marker's label.
   * @returns The popover's harness.
   * @throws Error when no marker has the label, or its popover does not open.
   */
  async openMarker(label: string): Promise<TmPopoverHarness> {
    for (const marker of await this.#markers()) {
      if ((await folded(marker)) === label) {
        await marker.click();
        return this.#waitFor(new HarnessPredicate(TmPopoverHarness, { selector: '.tm-session-panels__marker' }), `the ${label} popover`);
      }
    }
    throw new Error(`The tenant shell shows no marker "${label}".`);
  }

  /**
   * The distribution slots' buttons, in order.
   *
   * @returns Each slot's id and accessible name.
   */
  async getSlots(): Promise<TmTenantShellSlot[]> {
    return Promise.all(
      (await this.#slots()).map(async (button) => ({ id: (await button.getAttribute('data-tm-slot')) ?? '', name: await button.getAttribute('aria-label') })),
    );
  }

  /**
   * Clicks a slot's button, which opens its panel.
   *
   * @param id - The slot's id.
   * @throws Error when no slot has the id.
   */
  async clickSlot(id: string): Promise<void> {
    for (const button of await this.#slots()) {
      if ((await button.getAttribute('data-tm-slot')) === id) {
        await button.click();
        return;
      }
    }
    throw new Error(`The tenant shell shows no slot "${id}".`);
  }

  /**
   * The ids of the boxes the app bar reserves for slots other specs fill.
   *
   * @returns The ids, such as `['bell', 'tray', 'help', 'assistant']`.
   */
  async getReservedSlots(): Promise<string[]> {
    return Promise.all((await this.#reserved()).map(async (box) => (await box.getAttribute('data-tm-slot')) ?? ''));
  }

  /**
   * Whether the bar has the installed-mode Back and Forward, whose box shows
   * before their chunk fills it.
   *
   * @returns Whether they show.
   */
  async hasHistoryButtons(): Promise<boolean> {
    return (await this.#historyBox()) !== null;
  }

  /**
   * Whether Back and Forward are enabled.
   *
   * @returns Each button's state.
   * @throws Error when the buttons do not show.
   */
  async getHistoryState(): Promise<{ readonly back: boolean; readonly forward: boolean }> {
    const [back, forward] = await this.#requireHistory();
    return { back: !(await back.getProperty<boolean>('disabled')), forward: !(await forward.getProperty<boolean>('disabled')) };
  }

  /** Clicks Back. */
  async back(): Promise<void> {
    await (await this.#requireHistory())[0].click();
  }

  /** Clicks Forward. */
  async forward(): Promise<void> {
    await (await this.#requireHistory())[1].click();
  }

  /**
   * Opens the session menu, waiting for its chunk, and returns it.
   *
   * @returns The menu's harness.
   * @throws Error when the menu does not open.
   */
  async openSessionMenu(): Promise<TmSessionMenuHarness> {
    const button = await this.#session();
    if ((await button.getAttribute('aria-expanded')) !== 'true') {
      await button.click();
    }
    return this.#waitFor(TmSessionMenuHarness, 'the session menu');
  }

  /** The history buttons, which must show, waiting for their chunk. */
  async #requireHistory(): Promise<TestElement[]> {
    if ((await this.#historyBox()) === null) {
      throw new Error('The tenant shell shows no Back and Forward: they show in installed mode only.');
    }
    const deadline = Date.now() + OPEN_DEADLINE_MS;
    for (;;) {
      await this.forceStabilize();
      const buttons = await this.#history();
      if (buttons.length === 2) {
        return buttons;
      }
      if (Date.now() > deadline) {
        throw new Error('The tenant shell did not load its Back and Forward.');
      }
      await tick();
    }
  }

  /** Waits for an overlay in the document, its chunk's load included. */
  async #waitFor<T extends ComponentHarness>(query: HarnessQuery<T>, description: string): Promise<T> {
    const locate = this.documentRootLocatorFactory().locatorForOptional(query);
    const deadline = Date.now() + OPEN_DEADLINE_MS;
    for (;;) {
      await this.forceStabilize();
      const found = await locate();
      if (found !== null) {
        return found;
      }
      if (Date.now() > deadline) {
        throw new Error(`The tenant shell did not open ${description}.`);
      }
      await tick();
    }
  }
}
