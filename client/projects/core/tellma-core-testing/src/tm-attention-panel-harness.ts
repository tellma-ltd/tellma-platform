// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import { ComponentHarness, HarnessPredicate, type TestElement } from '@angular/cdk/testing';
import { TmButtonHarness, TmEmptyStateHarness, TmMenuHarness } from '@tellma/core-ui-testing';

// The bell's and the tray's panels, as a test reads and drives them: the
// heading and its verbs, the bell's In progress rows with their badges,
// lines and controls, the notification rows with their links, Download and
// Clear, the pinned "new" button, the status line, the empty and loading
// states, the tray's assignment rows and the footer link.

/** The text an element shows, its runs of white space folded to one space. */
async function folded(element: TestElement): Promise<string> {
  return (await element.text()).replace(/\s+/g, ' ').trim();
}

/** One of the bell's In progress rows, as it reads. */
export interface TmBellPanelJob {
  /** The job's id. */
  readonly id: number;
  /** The title link's text: the job's label. */
  readonly title: string;
  /** The title link's address. */
  readonly href: string | null;
  /** The status badge's text, such as `Held`, or `null`. */
  readonly badge: string | null;
  /** The percentage beside the title, or `null`. */
  readonly percent: string | null;
  /** Whether the row shows a progress bar. */
  readonly progress: boolean;
  /** The second line. */
  readonly line: string;
  /** The row's buttons' texts, in order, such as `['Stop']`. */
  readonly controls: readonly string[];
}

/** One of the bell's notification rows, as it reads. */
export interface TmBellPanelNotification {
  /** The notification's id. */
  readonly id: number;
  /** The title. */
  readonly title: string;
  /** The second line: the detail, else the actor's name. */
  readonly line: string;
  /** The link's accessible name. */
  readonly label: string;
  /** The link's address: the notification's open route. */
  readonly href: string | null;
  /** The severity the icon shows. */
  readonly severity: string | null;
  /** Whether the row is unread. */
  readonly unread: boolean;
  /** The Download link's address, or `null` without one. */
  readonly download: string | null;
  /** Whether the row offers Clear. */
  readonly clearable: boolean;
}

/** One of the tray's assignment rows, as it reads. */
export interface TmTrayPanelRow {
  /** The row's key: the assignment's id. */
  readonly id: string;
  /** The title: the entity's label and the target's code. */
  readonly title: string;
  /** The second line. */
  readonly line: string;
  /** The link's accessible name. */
  readonly label: string;
  /** The link's address, or `null` for a target with no page. */
  readonly href: string | null;
  /** Whether the row is marked "New". */
  readonly new: boolean;
}

/**
 * What the bell's and the tray's panel harnesses share: the heading, the
 * loading and empty states, the footer link and the focused control.
 */
export abstract class TmAttentionPanelHarness extends ComponentHarness {
  readonly #heading = this.locatorForOptional('.tm-attention-panel__heading');
  readonly #busy = this.locatorForOptional('[aria-busy="true"]');
  readonly #empty = this.locatorForOptional(TmEmptyStateHarness);
  readonly #seeAll = this.locatorForOptional('.tm-attention-panel__see-all');

  /**
   * The heading, which the drawer's own title replaces below lg.
   *
   * @returns The heading's text, or `null` in the drawer.
   */
  async getHeading(): Promise<string | null> {
    const heading = await this.#heading();
    return heading === null ? null : folded(heading);
  }

  /**
   * Whether the panel shows its skeleton rows: no copy of its document yet.
   *
   * @returns Whether the rows are loading.
   */
  async isLoading(): Promise<boolean> {
    return (await this.#busy()) !== null;
  }

  /**
   * The empty state's heading.
   *
   * @returns The heading, or `null` while the panel shows rows.
   */
  async getEmptyText(): Promise<string | null> {
    return (await (await this.#empty())?.getHeading()) ?? null;
  }

  /**
   * The footer link's address: every item, on its page.
   *
   * @returns The address, or `null` without the link.
   */
  async getSeeAllHref(): Promise<string | null> {
    return (await (await this.#seeAll())?.getAttribute('href')) ?? null;
  }

  /** Follows the footer link with a plain click, which closes the panel. */
  async seeAll(): Promise<void> {
    const link = await this.#seeAll();
    if (link === null) {
      throw new Error('The panel shows no footer link.');
    }
    await link.click();
  }

  /**
   * Which element of the panel has focus.
   *
   * @returns The focused element's text, or `null` when focus is elsewhere.
   */
  async getFocusedText(): Promise<string | null> {
    for (const control of await this.locatorForAll('a, button, [tabindex]')()) {
      if (await control.isFocused()) {
        return (await control.getAttribute('aria-label')) ?? folded(control);
      }
    }
    return null;
  }
}

/**
 * Harness for `tm-bell-panel`, the bell's panel: the header's verbs, the
 * In progress rows, the notification rows, the status line and the states.
 * The panel opens in a popover or a drawer, outside the bell's own element,
 * so a test reaches it through the document's root loader.
 *
 * @example
 * ```ts
 * const bell = await TestbedHarnessEnvironment.documentRootLoader(fixture).getHarness(TmBellPanelHarness);
 * expect((await bell.getNotifications()).map((row) => row.title)).toEqual(['Export is ready']);
 * await bell.stopJob(31);
 * ```
 */
export class TmBellPanelHarness extends TmAttentionPanelHarness {
  /** The selector of the `tm-bell-panel` element. */
  static hostSelector = 'tm-bell-panel';

  readonly #header = this.locatorForAll('.tm-attention-panel__header [tmButton]');
  readonly #jobs = this.locatorForAll('.tm-attention-panel__job');
  readonly #notices = this.locatorForAll('.tm-attention-panel__notice');
  readonly #more = this.locatorForOptional('.tm-attention-panel__more');
  readonly #statusText = this.locatorForOptional('.tm-attention-panel__status > span');
  readonly #statusRefresh = this.locatorForOptional('.tm-attention-panel__status > button');
  readonly #fresh = this.locatorForOptional('.tm-attention-panel__fresh');

  /**
   * The header's buttons, by their visible text or accessible name.
   *
   * @returns The names, in order, such as `['Mark all as read', 'More options']`.
   */
  async getHeaderVerbs(): Promise<string[]> {
    return Promise.all((await this.#header()).map(async (button) => (await button.getAttribute('aria-label')) ?? folded(button)));
  }

  /**
   * Why a header verb is unavailable.
   *
   * @param name - The verb's text or accessible name.
   * @returns The reason its tooltip gives, or `null` while it is available.
   */
  async getHeaderVerbReason(name: string): Promise<string | null> {
    return (await this.#headerButton(name)).getDisabledReason();
  }

  /** Presses Mark all as read. */
  async markAllRead(): Promise<void> {
    await (await this.#headerButton('Mark all as read')).click();
  }

  /**
   * Opens More options.
   *
   * @returns The menu, whose items are Notification settings and Clear read notifications.
   */
  async openMore(): Promise<TmMenuHarness> {
    await (await this.#headerButton('More options')).click();
    return this.documentRootLocatorFactory().locatorFor(TmMenuHarness)();
  }

  /**
   * The In progress rows: the newest three running jobs.
   *
   * @returns The rows, in order.
   */
  async getJobs(): Promise<TmBellPanelJob[]> {
    return Promise.all(
      (await this.#jobs()).map(async (row) => {
        const title = await this.#within(row, '.tm-attention-panel__title');
        const badge = await this.#optionalWithin(row, 'tm-badge');
        const percent = await this.#optionalWithin(row, '.tm-attention-panel__percent');
        return {
          id: Number(await row.getAttribute('data-tm-id')),
          title: await folded(title),
          href: await title.getAttribute('href'),
          badge: badge === null ? null : await folded(badge),
          percent: percent === null ? null : await folded(percent),
          progress: (await this.#optionalWithin(row, 'tm-progress-bar')) !== null,
          line: await folded(await this.#within(row, '.tm-attention-panel__line')),
          controls: await Promise.all((await this.#buttonsOf(row)).map((button) => button.getText())),
        };
      }),
    );
  }

  /**
   * A job's button.
   *
   * @param id - The job's id.
   * @param name - The button's text, such as `Stop` or `Review`.
   * @returns The button.
   */
  async getJobControl(id: number, name: string): Promise<TmButtonHarness> {
    for (const button of await this.#buttonsOf(await this.#row(this.#jobs, id))) {
      if ((await button.getText()) === name) {
        return button;
      }
    }
    throw new Error(`Job ${id} has no ${name} button.`);
  }

  /**
   * Presses a job's Stop.
   *
   * @param id - The job's id.
   */
  async stopJob(id: number): Promise<void> {
    await (await this.getJobControl(id, 'Stop')).click();
  }

  /**
   * Presses a job's Review.
   *
   * @param id - The job's id.
   */
  async reviewJob(id: number): Promise<void> {
    await (await this.getJobControl(id, 'Review')).click();
  }

  /**
   * The link to the rest of the running jobs, past the three shown.
   *
   * @returns The link's text, such as `+2 more in My jobs`, or `null` without one.
   */
  async getMoreJobs(): Promise<string | null> {
    const more = await this.#more();
    return more === null ? null : folded(more);
  }

  /**
   * The notification rows.
   *
   * @returns The rows, newest first.
   */
  async getNotifications(): Promise<TmBellPanelNotification[]> {
    return Promise.all(
      (await this.#notices()).map(async (row) => {
        const link = await this.#within(row, '.tm-attention-panel__link');
        const download = await this.#optionalWithin(row, 'a[download]');
        const icon = await this.#optionalWithin(row, '.tm-attention-panel__severity');
        return {
          id: Number(await row.getAttribute('data-tm-id')),
          title: await folded(await this.#within(row, '.tm-attention-panel__title')),
          line: await folded(await this.#within(row, '.tm-attention-panel__line')),
          label: (await link.getAttribute('aria-label')) ?? '',
          href: await link.getAttribute('href'),
          severity: icon === null ? null : await icon.getAttribute('data-severity'),
          unread: await row.hasClass('tm-attention-panel__notice--unread'),
          download: download === null ? null : await download.getAttribute('href'),
          clearable: (await this.#optionalWithin(row, '.tm-attention-panel__hover')) !== null,
        };
      }),
    );
  }

  /**
   * Follows a notification's link with a plain click, which closes the panel.
   *
   * @param id - The notification's id.
   */
  async openNotification(id: number): Promise<void> {
    await (await this.#within(await this.#row(this.#notices, id), '.tm-attention-panel__link')).click();
  }

  /**
   * Presses a notification's Clear.
   *
   * @param id - The notification's id.
   */
  async clearNotification(id: number): Promise<void> {
    await (await this.#within(await this.#row(this.#notices, id), '.tm-attention-panel__hover')).click();
  }

  /**
   * Why a notification's Clear is unavailable.
   *
   * @param id - The notification's id.
   * @returns The reason its tooltip gives, or `null` while it is available.
   */
  async getClearReason(id: number): Promise<string | null> {
    const clear = await this.locatorForOptional(
      new HarnessPredicate(TmButtonHarness, { ancestor: `.tm-attention-panel__notice[data-tm-id="${id}"]`, selector: '.tm-attention-panel__hover' }),
    )();
    if (clear === null) {
      throw new Error(`Notification ${id} has no Clear button.`);
    }
    return clear.getDisabledReason();
  }

  /**
   * The status line shown while live updates are paused.
   *
   * @returns The line's text without its Refresh, or `null` while live.
   */
  async getStatusLine(): Promise<string | null> {
    const text = await this.#statusText();
    return text === null ? null : folded(text);
  }

  /** Presses the status line's Refresh. */
  async refresh(): Promise<void> {
    const refresh = await this.#statusRefresh();
    if (refresh === null) {
      throw new Error('The panel shows no status line.');
    }
    await refresh.click();
  }

  /**
   * The pinned button over the rows that arrived while the list was scrolled down.
   *
   * @returns Its text, such as `2 new`, or `null` without one.
   */
  async getFresh(): Promise<string | null> {
    const fresh = await this.#fresh();
    return fresh === null ? null : folded(fresh);
  }

  /** Presses the pinned "new" button, which shows the rows it holds. */
  async showFresh(): Promise<void> {
    const fresh = await this.#fresh();
    if (fresh === null) {
      throw new Error('The panel shows no pinned "new" button.');
    }
    await fresh.click();
  }

  /** A header button by its text or accessible name. */
  async #headerButton(name: string): Promise<TmButtonHarness> {
    for (const button of await this.locatorForAll(new HarnessPredicate(TmButtonHarness, { ancestor: '.tm-attention-panel__header' }))()) {
      const host = await button.host();
      if (((await host.getAttribute('aria-label')) ?? (await button.getText())) === name) {
        return button;
      }
    }
    throw new Error(`The header has no ${name} button.`);
  }

  /** A row by its id. */
  async #row(rows: () => Promise<TestElement[]>, id: number): Promise<TestElement> {
    for (const row of await rows()) {
      if ((await row.getAttribute('data-tm-id')) === String(id)) {
        return row;
      }
    }
    throw new Error(`The panel shows no row ${id}.`);
  }

  /** A row's buttons, in order. */
  async #buttonsOf(row: TestElement): Promise<TmButtonHarness[]> {
    const id = await row.getAttribute('data-tm-id');
    const kind = (await row.hasClass('tm-attention-panel__job')) ? 'job' : 'notice';
    return this.locatorForAll(new HarnessPredicate(TmButtonHarness, { ancestor: `.tm-attention-panel__${kind}[data-tm-id="${id ?? ''}"]` }))();
  }

  /** An element within a row. */
  async #within(row: TestElement, selector: string): Promise<TestElement> {
    const found = await this.#optionalWithin(row, selector);
    if (found === null) {
      throw new Error(`The row has no ${selector}.`);
    }
    return found;
  }

  /** An element within a row, or `null`. */
  async #optionalWithin(row: TestElement, selector: string): Promise<TestElement | null> {
    const id = await row.getAttribute('data-tm-id');
    return this.locatorForOptional(`[data-tm-row][data-tm-id="${id ?? ''}"] ${selector}`)();
  }
}

/**
 * Harness for `tm-tray-panel`, the tray's panel: the heading with its open
 * count, the assignment rows and the states. The panel opens in a popover
 * or a drawer, outside the tray's own element, so a test reaches it through
 * the document's root loader.
 *
 * @example
 * ```ts
 * const tray = await TestbedHarnessEnvironment.documentRootLoader(fixture).getHarness(TmTrayPanelHarness);
 * expect((await tray.getRows()).map((row) => row.title)).toEqual(['Center C-1']);
 * await tray.openRow(0);
 * ```
 */
export class TmTrayPanelHarness extends TmAttentionPanelHarness {
  /** The selector of the `tm-tray-panel` element. */
  static hostSelector = 'tm-tray-panel';

  readonly #rows = this.locatorForAll('.tm-assignment-list__row[data-tm-row]');
  readonly #count = this.locatorForOptional('.tm-attention-panel__hidden, .tm-attention-panel__count');

  /**
   * The open count, as the heading speaks it.
   *
   * @returns The count's text, such as `3 open`, or `null` with nothing open.
   */
  async getOpenCount(): Promise<string | null> {
    const count = await this.#count();
    return count === null ? null : folded(count);
  }

  /**
   * The assignment rows.
   *
   * @returns The rows, newest first.
   */
  async getRows(): Promise<TmTrayPanelRow[]> {
    return Promise.all(
      (await this.#rows()).map(async (row) => {
        const id = (await row.getAttribute('data-tm-id')) ?? '';
        const at = (selector: string): Promise<TestElement | null> =>
          this.locatorForOptional(`.tm-assignment-list__row[data-tm-id="${id}"] ${selector}`)();
        const link = (await at('.tm-assignment-list__link'))!;
        return {
          id,
          title: await folded((await at('.tm-assignment-list__title'))!),
          line: await folded((await at('.tm-assignment-list__line'))!),
          label: (await link.getAttribute('aria-label')) ?? '',
          href: await link.getAttribute('href'),
          new: (await at('.tm-assignment-list__new')) !== null,
        };
      }),
    );
  }

  /**
   * Follows a row's link with a plain click, which closes the panel.
   *
   * @param index - The row's place, from 0.
   */
  async openRow(index: number): Promise<void> {
    const row = (await this.#rows())[index];
    if (row === undefined) {
      throw new Error(`The tray shows no row ${index}.`);
    }
    const id = (await row.getAttribute('data-tm-id')) ?? '';
    await (await this.locatorFor(`.tm-assignment-list__row[data-tm-id="${id}"] .tm-assignment-list__link`)()).click();
  }
}
