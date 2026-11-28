// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import { type BaseHarnessFilters, ComponentHarness, HarnessPredicate, type ModifierKeys, type TestElement, TestKey } from '@angular/cdk/testing';

// The tenant shell's launcher while it is open: its listbox, which holds a
// group per band, section, the typed state's Pages and each provider's
// bucket, and the combobox input that controls it. The listbox renders in
// the panel at the end of the body from md up, and in the Menu modal's
// overlay below it; the input is the app bar's field from lg up, the panel's
// own at md, and the modal's below md. A document root loader finds the
// listbox in either place, and the harness finds the input by the listbox's
// id, so every method works on each of the three surfaces.

/** Which of the launcher's surfaces shows: the panel with the app bar's field, the panel with its own field, or the Menu modal. */
export type TmLauncherSurfaceKind = 'panel' | 'field' | 'sheet';

/** What a launcher group is: a band of chips, a section of entries, the typed state's Pages, or a provider's bucket. */
export type TmLauncherGroupKind = 'band' | 'section' | 'pages' | 'bucket';

/** Filters for {@link TmLauncherGroupHarness}. */
export interface TmLauncherGroupHarnessFilters extends BaseHarnessFilters {
  /** The group's heading. */
  readonly label?: string | RegExp;
}

/** Filters for {@link TmLauncherOptionHarness}. */
export interface TmLauncherOptionHarnessFilters extends BaseHarnessFilters {
  /** The option's label. */
  readonly label?: string | RegExp;
}

/** The text an element shows, its runs of white space folded to one space. */
function folded(text: string): string {
  return text.replace(/\s+/g, ' ').trim();
}

/** The `tm-launcher__group--{kind}` class's kind. */
const GROUP_KIND = /\btm-launcher__group--(band|section|pages|bucket)\b/;

/**
 * Harness for one of the launcher's options: an entry, a pin, a recent
 * page, a create row or a provider's result.
 */
export class TmLauncherOptionHarness extends ComponentHarness {
  /** The selector of an option. */
  static hostSelector = '.tm-launcher__option';

  readonly #label = this.locatorFor('.tm-launcher__label');
  readonly #marks = this.locatorForAll('.tm-launcher__label mark');
  readonly #description = this.locatorForOptional('.tm-launcher__description');
  readonly #pin = this.locatorForOptional('.tm-launcher__pin');

  /**
   * A predicate for an option with the given label.
   *
   * @param options - The filters.
   * @returns The predicate.
   */
  static with(options: TmLauncherOptionHarnessFilters = {}): HarnessPredicate<TmLauncherOptionHarness> {
    return new HarnessPredicate(TmLauncherOptionHarness, options).addOption('label', options.label, (harness, label) =>
      HarnessPredicate.stringMatches(harness.getLabel(), label),
    );
  }

  /**
   * The label.
   *
   * @returns Its text.
   */
  async getLabel(): Promise<string> {
    return folded(await (await this.#label()).text());
  }

  /**
   * The accessible name, which differs from the label for a pinned entry.
   *
   * @returns The `aria-label`, or the label when it has none.
   */
  async getName(): Promise<string> {
    return (await (await this.host()).getAttribute('aria-label')) ?? this.getLabel();
  }

  /**
   * The runs of the label that a query's tokens start, each in a `<mark>`.
   *
   * @returns The marked texts, in order.
   */
  async getMarks(): Promise<string[]> {
    return Promise.all((await this.#marks()).map(async (mark) => mark.text()));
  }

  /**
   * The muted line beside the label: an entry's section, "Pinned",
   * "Recently visited" or a result's description.
   *
   * @returns Its text, or `null` for none.
   */
  async getDescription(): Promise<string | null> {
    const description = await this.#description();
    return description === null ? null : folded(await description.text());
  }

  /**
   * Where it leads.
   *
   * @returns The link's `href`.
   */
  async getHref(): Promise<string | null> {
    return (await this.host()).getAttribute('href');
  }

  /**
   * Whether it is the active option, the one Enter opens.
   *
   * @returns `true` when `aria-selected` is `true`.
   */
  async isActive(): Promise<boolean> {
    return (await (await this.host()).getAttribute('aria-selected')) === 'true';
  }

  /**
   * Whether it is the routed page's entry, tinted in the browse state.
   *
   * @returns `true` for the current entry.
   */
  async isCurrent(): Promise<boolean> {
    return (await this.host()).hasClass('tm-launcher__option--current');
  }

  /**
   * Whether it shows the pinned glyph: a pin opens the same page.
   *
   * @returns `true` when the glyph shows.
   */
  async isPinned(): Promise<boolean> {
    return (await this.#pin()) !== null;
  }

  /** Moves the pointer over it, which makes it active. */
  async hover(): Promise<void> {
    await (await this.host()).dispatchEvent('pointermove');
  }

  /**
   * Clicks it, which opens its page and closes the launcher.
   *
   * @param modifiers - The keys held, for a new tab.
   */
  async click(modifiers: ModifierKeys = {}): Promise<void> {
    await (await this.host()).click(modifiers);
  }
}

/** Harness for one of the launcher's groups: a band, a section, the Pages of the typed state, or a provider's bucket. */
export class TmLauncherGroupHarness extends ComponentHarness {
  /** The selector of a group. */
  static hostSelector = '.tm-launcher__group';

  readonly #heading = this.locatorFor('.tm-launcher__heading');
  readonly #options = this.locatorForAll(TmLauncherOptionHarness);
  readonly #empty = this.locatorForOptional('.tm-launcher__empty');

  /**
   * A predicate for a group with the given heading.
   *
   * @param options - The filters.
   * @returns The predicate.
   */
  static with(options: TmLauncherGroupHarnessFilters = {}): HarnessPredicate<TmLauncherGroupHarness> {
    return new HarnessPredicate(TmLauncherGroupHarness, options).addOption('label', options.label, (harness, label) =>
      HarnessPredicate.stringMatches(harness.getLabel(), label),
    );
  }

  /**
   * The heading.
   *
   * @returns Its text.
   */
  async getLabel(): Promise<string> {
    return folded(await (await this.#heading()).text());
  }

  /**
   * What the group is.
   *
   * @returns Its kind.
   */
  async getKind(): Promise<TmLauncherGroupKind> {
    const match = GROUP_KIND.exec((await (await this.host()).getAttribute('class')) ?? '');
    if (match === null) {
      throw new Error('The launcher group has no kind.');
    }
    return match[1] as TmLauncherGroupKind;
  }

  /**
   * The options.
   *
   * @returns Their harnesses, in reading order.
   */
  async getOptions(): Promise<TmLauncherOptionHarness[]> {
    return this.#options();
  }

  /**
   * The options' labels.
   *
   * @returns The labels, in reading order.
   */
  async getOptionLabels(): Promise<string[]> {
    return Promise.all((await this.#options()).map(async (option) => option.getLabel()));
  }

  /**
   * The line the Pages heading shows when no page matched.
   *
   * @returns Its text, or `null` when a page matched.
   */
  async getEmptyText(): Promise<string | null> {
    const empty = await this.#empty();
    return empty === null ? null : folded(await empty.text());
  }
}

/**
 * Harness for the tenant shell's launcher while it is open: its groups and
 * options, its input, and its keys. `TmTenantShellHarness.openLauncher`
 * opens it, waiting for its chunk, and returns it; a document root loader
 * finds an open one.
 *
 * @example
 * ```ts
 * const launcher = await shell.openLauncher('menu');
 * await launcher.type('cent');
 * expect(await launcher.getOptionLabels()).toEqual(['Centers', 'Center · Create']);
 * await launcher.pressKey(TestKey.ENTER);
 * ```
 */
export class TmLauncherHarness extends ComponentHarness {
  /** The selector of the launcher's listbox. */
  static hostSelector = '.tm-launcher__list';

  readonly #groups = this.locatorForAll(TmLauncherGroupHarness);
  readonly #options = this.locatorForAll(TmLauncherOptionHarness);
  readonly #active = this.locatorForOptional(TmLauncherOptionHarness.with({ selector: '[aria-selected="true"]' }));

  /**
   * Whether the launcher browses or lists the matches of its text.
   *
   * @returns `typed` with text, else `browse`.
   */
  async getState(): Promise<'browse' | 'typed'> {
    return (await (await this.host()).hasClass('tm-launcher__list--typed')) ? 'typed' : 'browse';
  }

  /**
   * Which surface shows the listbox.
   *
   * @returns `panel` with the app bar's field from lg up, `field` with the
   *   panel's own at md, and `sheet` for the Menu modal below md.
   */
  async getSurface(): Promise<TmLauncherSurfaceKind> {
    const input = await this.#input();
    if (await input.hasClass('tm-launcher__sheet-input')) {
      return 'sheet';
    }
    return (await input.hasClass('tm-launcher__own-input')) ? 'field' : 'panel';
  }

  /**
   * The groups.
   *
   * @returns Their harnesses, in reading order.
   */
  async getGroups(): Promise<TmLauncherGroupHarness[]> {
    return this.#groups();
  }

  /**
   * The groups' headings.
   *
   * @returns The headings, in reading order.
   */
  async getGroupLabels(): Promise<string[]> {
    return Promise.all((await this.#groups()).map(async (group) => group.getLabel()));
  }

  /**
   * A group by its heading.
   *
   * @param label - The heading.
   * @returns Its harness, or `null` when no group has it.
   */
  async getGroup(label: string | RegExp): Promise<TmLauncherGroupHarness | null> {
    return this.locatorForOptional(TmLauncherGroupHarness.with({ label }))();
  }

  /**
   * Every option.
   *
   * @returns Their harnesses, in reading order.
   */
  async getOptions(): Promise<TmLauncherOptionHarness[]> {
    return this.#options();
  }

  /**
   * Every option's label.
   *
   * @returns The labels, in reading order.
   */
  async getOptionLabels(): Promise<string[]> {
    return Promise.all((await this.#options()).map(async (option) => option.getLabel()));
  }

  /**
   * The active option, the one Enter opens.
   *
   * @returns Its harness, or `null` when none is active.
   */
  async getActiveOption(): Promise<TmLauncherOptionHarness | null> {
    return this.#active();
  }

  /**
   * The option the input's `aria-activedescendant` points at, as the APG
   * combobox has it.
   *
   * @returns The active option's label, or `null` when the input points at none.
   */
  async getActiveDescendantLabel(): Promise<string | null> {
    const id = await (await this.#input()).getAttribute('aria-activedescendant');
    if (id === null || id === '') {
      return null;
    }
    const option = await this.locatorForOptional(TmLauncherOptionHarness.with({ selector: `#${id}` }))();
    return option === null ? null : option.getLabel();
  }

  /**
   * The input's text.
   *
   * @returns The text.
   */
  async getQuery(): Promise<string> {
    return (await this.#input()).getProperty<string>('value');
  }

  /**
   * Whether the input has focus.
   *
   * @returns `true` when it is the focused element.
   */
  async isInputFocused(): Promise<boolean> {
    return (await this.#input()).isFocused();
  }

  /**
   * Replaces the input's text, key by key.
   *
   * @param text - The new text; `''` clears it.
   */
  async type(text: string): Promise<void> {
    const input = await this.#input();
    await input.clear();
    if (text !== '') {
      await input.sendKeys(text);
    }
  }

  /**
   * Presses a key in the input, such as an arrow key, Enter or Escape.
   *
   * @param key - The key.
   * @param modifiers - The keys held with it.
   */
  async pressKey(key: TestKey, modifiers: ModifierKeys = {}): Promise<void> {
    await (await this.#input()).sendKeys(modifiers, key);
  }

  /** Closes the launcher with Escape, which keeps its text. */
  async close(): Promise<void> {
    await this.pressKey(TestKey.ESCAPE);
  }

  /** The combobox input that controls the listbox. */
  async #input(): Promise<TestElement> {
    const id = await (await this.host()).getAttribute('id');
    return this.documentRootLocatorFactory().locatorFor(`input[role="combobox"][aria-controls="${id}"]`)();
  }
}
