// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import { type BaseHarnessFilters, ComponentHarness, HarnessPredicate, type HarnessQuery, type ModifierKeys, type TestElement, TestKey } from '@angular/cdk/testing';
import { TmButtonHarness, TmMenuHarness, TmModalHarness, TmPopoverHarness } from '@tellma/core-ui-testing';

// The tenant shell's pinned bar: its chips (the pins, the folders, the
// revoked or unavailable pins and More), the empty line, the pin control and
// Manage pins. The chips' menus, the pin popover and the Manage pins dialog
// live in chunks the shell loads after its first frame, so opening one
// waits for it, and they render at the end of the body, where a document
// root loader finds them.

/** What a chip of the pinned bar is: a pin's link, a revoked or unavailable pin, a folder, or More. */
export type TmPinnedBarChipKind = 'pin' | 'revoked' | 'folder' | 'more';

/** Filters for {@link TmPinnedBarChipHarness}. */
export interface TmPinnedBarChipHarnessFilters extends BaseHarnessFilters {
  /** The chip's label. */
  readonly label?: string | RegExp;
}

/** The pin control's state. */
export interface TmPinnedBarControl {
  /** The accessible name: Pin this page, or Edit pin on a pinned page. */
  readonly name: string | null;
  /** Whether the page is pinned, which fills the glyph. */
  readonly pinned: boolean;
  /** Whether it is disabled, natively or with a reason. */
  readonly disabled: boolean;
  /** Why it is disabled, which its tooltip shows; `null` without a reason. */
  readonly reason: string | null;
  /** The `aria-keyshortcuts`. */
  readonly keys: string | null;
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

/** Harness for one chip of the pinned bar: a pin, a revoked or unavailable pin, a folder, or More. */
export class TmPinnedBarChipHarness extends ComponentHarness {
  /** The selector of a chip in the bar's list. */
  static hostSelector = '.tm-pinned-bar__list [data-tm-pin]';

  readonly #label = this.locatorForOptional('.tm-pinned-bar__label');

  /**
   * A predicate for a chip with the given label.
   *
   * @param options - The filters.
   * @returns The predicate.
   */
  static with(options: TmPinnedBarChipHarnessFilters = {}): HarnessPredicate<TmPinnedBarChipHarness> {
    return new HarnessPredicate(TmPinnedBarChipHarness, options).addOption('label', options.label, (harness, label) =>
      HarnessPredicate.stringMatches(harness.getLabel(), label),
    );
  }

  /**
   * The text the chip shows.
   *
   * @returns The label, or More's text.
   */
  async getLabel(): Promise<string> {
    const label = await this.#label();
    return folded(label ?? (await this.host()));
  }

  /**
   * The accessible name, which names a revoked or unavailable pin's state.
   *
   * @returns The `aria-label`, or the label when it has none.
   */
  async getName(): Promise<string> {
    return (await (await this.host()).getAttribute('aria-label')) ?? this.getLabel();
  }

  /**
   * What the chip is.
   *
   * @returns Its kind.
   */
  async getKind(): Promise<TmPinnedBarChipKind> {
    const host = await this.host();
    if ((await host.getAttribute('data-tm-pin')) === 'more') {
      return 'more';
    }
    if ((await host.getProperty<string>('localName')) === 'a') {
      return 'pin';
    }
    return (await host.hasClass('tm-pinned-bar__chip--revoked')) ? 'revoked' : 'folder';
  }

  /**
   * Where a pin's chip leads.
   *
   * @returns The link's `href`, or `null` for a menu chip.
   */
  async getHref(): Promise<string | null> {
    return (await this.host()).getAttribute('href');
  }

  /**
   * Whether the chip carries the current styling: the routed page's pin, the
   * folder that holds it, or More while More holds it.
   *
   * @returns Whether it does.
   */
  async isCurrent(): Promise<boolean> {
    return (await this.host()).hasClass('tm-pinned-bar__chip--current');
  }

  /**
   * Whether a pin's chip is the routed page's, as assistive technology reads it.
   *
   * @returns `true` when `aria-current` is `page`.
   */
  async isAriaCurrent(): Promise<boolean> {
    return (await (await this.host()).getAttribute('aria-current')) === 'page';
  }

  /**
   * Whether the chip is the bar's tab stop, the one chip Tab reaches.
   *
   * @returns `true` when its `tabindex` is 0.
   */
  async isTabStop(): Promise<boolean> {
    return (await (await this.host()).getAttribute('tabindex')) === '0';
  }

  /**
   * Whether a menu chip's menu is open.
   *
   * @returns The chip's `aria-expanded`; `false` for a pin's link.
   */
  async isExpanded(): Promise<boolean> {
    return (await (await this.host()).getAttribute('aria-expanded')) === 'true';
  }

  /**
   * Whether the chip has focus.
   *
   * @returns Whether it is the focused element.
   */
  async isFocused(): Promise<boolean> {
    return (await this.host()).isFocused();
  }

  /**
   * The chip's box, which the fit sizes to its measured width.
   *
   * @returns Its width and height, in pixels.
   */
  async getSize(): Promise<{ readonly width: number; readonly height: number }> {
    const { width, height } = await (await this.host()).getDimensions();
    return { width, height };
  }

  /**
   * Where the chip's item starts, along the bar.
   *
   * @returns The item's left edge, in pixels from the viewport's.
   */
  async getLeft(): Promise<number> {
    return (await (await this.host()).getDimensions()).left;
  }

  /** Focuses the chip. */
  async focus(): Promise<void> {
    await (await this.host()).focus();
  }

  /** Moves the pointer over the chip. */
  async hover(): Promise<void> {
    await (await this.host()).hover();
  }

  /**
   * Clicks the chip: a pin's opens its page; a menu chip's toggles its menu.
   *
   * @param modifiers - The keys held, for a new tab.
   */
  async click(modifiers: ModifierKeys = {}): Promise<void> {
    await (await this.host()).click(modifiers);
  }

  /**
   * Presses a key on the chip, such as an arrow key, Home or End.
   *
   * @param key - The key.
   * @param modifiers - The keys held with it.
   */
  async pressKey(key: TestKey, modifiers: ModifierKeys = {}): Promise<void> {
    await (await this.host()).sendKeys(modifiers, key);
  }
}

/**
 * Harness for `tm-pinned-bar`, the tenant shell's row of pins under its app
 * bar. `TmTenantShellHarness.getPinnedBar` finds it.
 *
 * @example
 * ```ts
 * const bar = await loader.getHarness(TmPinnedBarHarness);
 * expect(await bar.getChipLabels()).toEqual(['Centers', 'Reports', 'More']);
 * const more = await bar.openMenu('More');
 * expect(await more.getItemLabels()).toEqual(['Agents']);
 * ```
 */
export class TmPinnedBarHarness extends ComponentHarness {
  /** The selector of the `tm-pinned-bar` element. */
  static hostSelector = 'tm-pinned-bar';

  readonly #nav = this.locatorFor('nav');
  readonly #list = this.locatorForOptional('.tm-pinned-bar__list');
  readonly #empty = this.locatorForOptional('.tm-pinned-bar__empty');
  readonly #longEmpty = this.locatorForOptional('.tm-pinned-bar__long');
  readonly #shortEmpty = this.locatorForOptional('.tm-pinned-bar__short');
  readonly #chips = this.locatorForAll(TmPinnedBarChipHarness);
  readonly #controls = this.locatorForAll('.tm-pinned-bar__control');

  /**
   * The bar's landmark name.
   *
   * @returns The navigation landmark's `aria-label`.
   */
  async getName(): Promise<string | null> {
    return (await this.#nav()).getAttribute('aria-label');
  }

  /**
   * Whether the bar shows its content, which waits for the launch documents.
   *
   * @returns Whether the controls render.
   */
  async isReady(): Promise<boolean> {
    return (await this.#controls()).length > 0;
  }

  /**
   * Whether the chips have had their first fit and show; before it they
   * are hidden, so none paints at a width the fonts would change.
   *
   * @returns Whether they show; `true` with no pins.
   */
  async isFitted(): Promise<boolean> {
    const list = await this.#list();
    return list === null || list.hasClass('tm-pinned-bar__list--fitted');
  }

  /**
   * The empty line, in the form that shows at the current width.
   *
   * @returns Its text, or `null` while the bar has pins.
   */
  async getEmptyText(): Promise<string | null> {
    if ((await this.#empty()) === null) {
      return null;
    }
    const long = await this.#longEmpty();
    const short = await this.#shortEmpty();
    for (const form of [long, short]) {
      if (form !== null && (await form.getCssValue('display')) !== 'none') {
        return folded(form);
      }
    }
    return null;
  }

  /**
   * The chips that show, More last.
   *
   * @returns Their harnesses, in the bar's order.
   */
  async getChips(): Promise<TmPinnedBarChipHarness[]> {
    return this.#chips();
  }

  /**
   * The labels of the chips that show, More's last.
   *
   * @returns The labels, in the bar's order.
   */
  async getChipLabels(): Promise<string[]> {
    return Promise.all((await this.#chips()).map(async (chip) => chip.getLabel()));
  }

  /**
   * A chip by its label.
   *
   * @param label - The label, or `More`'s text.
   * @returns Its harness.
   * @throws Error when no chip shows it.
   */
  async getChip(label: string | RegExp): Promise<TmPinnedBarChipHarness> {
    return this.locatorFor(TmPinnedBarChipHarness.with({ label }))();
  }

  /**
   * The chip with the current styling.
   *
   * @returns Its label, or `null` when none has it.
   */
  async getCurrentChipLabel(): Promise<string | null> {
    for (const chip of await this.#chips()) {
      if (await chip.isCurrent()) {
        return chip.getLabel();
      }
    }
    return null;
  }

  /**
   * The chip Tab reaches.
   *
   * @returns Its label, or `null` with no chips.
   */
  async getTabStopLabel(): Promise<string | null> {
    for (const chip of await this.#chips()) {
      if (await chip.isTabStop()) {
        return chip.getLabel();
      }
    }
    return null;
  }

  /**
   * The chip that has focus.
   *
   * @returns Its label, or `null` when focus is elsewhere.
   */
  async getFocusedChipLabel(): Promise<string | null> {
    for (const chip of await this.#chips()) {
      if (await chip.isFocused()) {
        return chip.getLabel();
      }
    }
    return null;
  }

  /**
   * Whether More shows, holding the items that do not fit.
   *
   * @returns Whether it shows.
   */
  async hasMore(): Promise<boolean> {
    return (await this.locatorForOptional('.tm-pinned-bar__list [data-tm-pin="more"]')()) !== null;
  }

  /**
   * Opens a folder's, a revoked pin's or More's menu, waiting for its chunk,
   * and returns it.
   *
   * @param label - The chip's label, or More's text.
   * @returns The menu's harness.
   * @throws Error when no menu chip shows it, or its menu does not open.
   */
  async openMenu(label: string | RegExp): Promise<TmMenuHarness> {
    const chip = await this.getChip(label);
    if ((await chip.getKind()) === 'pin') {
      throw new Error(`The pinned bar's chip "${String(label)}" is a link, which opens no menu.`);
    }
    if (!(await chip.isExpanded())) {
      await chip.click();
    }
    return this.#waitFor(new HarnessPredicate(TmMenuHarness, { selector: '.tm-pin-overlays__menu' }), 'the menu');
  }

  /**
   * The pin control's state.
   *
   * @returns Its name, pinned and disabled states and reason.
   */
  async getControl(): Promise<TmPinnedBarControl> {
    const control = await this.#control(0);
    const [button] = await this.locatorForAll(new HarnessPredicate(TmButtonHarness, { selector: '.tm-pinned-bar__control' }))();
    const reason = await button.getDisabledReason();
    return {
      name: await control.getAttribute('aria-label'),
      pinned: await control.hasClass('tm-pinned-bar__control--pinned'),
      disabled: reason !== null || (await control.getProperty<boolean>('disabled')),
      reason,
      keys: await control.getAttribute('aria-keyshortcuts'),
    };
  }

  /**
   * Presses the pin control: on an unpinned page it pins the page, and on a
   * pinned page it opens the pin popover.
   */
  async clickControl(): Promise<void> {
    await (await this.#control(0)).click();
  }

  /**
   * Opens the pin popover from the pin control, pinning the page first when
   * it is not pinned, and returns it once its chunk is in.
   *
   * @returns The popover's harness.
   * @throws Error when the popover does not open.
   */
  async openPopover(): Promise<TmPopoverHarness> {
    const popover = new HarnessPredicate(TmPopoverHarness, { selector: '.tm-pin-overlays__popover' });
    const open = this.documentRootLocatorFactory().locatorForOptional(popover);
    if ((await open()) === null) {
      await this.clickControl();
    }
    return this.#waitFor(popover, 'the pin popover');
  }

  /**
   * Clicks Manage pins and returns the dialog once its chunk is in.
   *
   * @returns The dialog's modal.
   * @throws Error when the dialog does not open.
   */
  async openManagePins(): Promise<TmModalHarness> {
    await (await this.#control(1)).click();
    return this.#waitFor(new HarnessPredicate(TmModalHarness, { selector: ':has(tm-manage-pins-dialog)' }), 'the Manage pins dialog');
  }

  /**
   * Manage pins' accessible name.
   *
   * @returns Its `aria-label`.
   */
  async getManageName(): Promise<string | null> {
    return (await this.#control(1)).getAttribute('aria-label');
  }

  /** One of the bar's two icon buttons: the pin control, then Manage pins. */
  async #control(index: 0 | 1): Promise<TestElement> {
    const controls = await this.#controls();
    if (controls.length < 2) {
      throw new Error('The pinned bar shows no controls: they wait for the launch documents.');
    }
    return controls[index];
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
        throw new Error(`The pinned bar did not open ${description}.`);
      }
      await tick();
    }
  }
}
