// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import {
  type BaseHarnessFilters,
  ComponentHarness,
  HarnessPredicate,
  TestKey,
} from '@angular/cdk/testing';

/** Filters for {@link TmChipHarness}. */
export interface TmChipHarnessFilters extends BaseHarnessFilters {
  /** Only the chip with this label (its accessible name). */
  readonly label?: string;
}

/** Harness for `tm-chip`. */
export class TmChipHarness extends ComponentHarness {
  /** The selector for the `tm-chip` host element. */
  static hostSelector = 'tm-chip';

  /** A predicate matching chips by label. */
  static with(filters: TmChipHarnessFilters = {}): HarnessPredicate<TmChipHarness> {
    return new HarnessPredicate(TmChipHarness, filters).addOption(
      'label',
      filters.label,
      async (harness, label) => (await harness.getLabel()) === label,
    );
  }

  /** The label element: a link, a button or plain text. */
  private readonly labelElement = this.locatorFor('.tm-chip__label');

  /** The remove button, if the chip is removable. */
  private readonly removeButton = this.locatorForOptional('.tm-chip__remove');

  /**
   * The chip's label: the link's or button's accessible name, else the
   * visible text.
   */
  async getLabel(): Promise<string> {
    const label = await this.labelElement();
    return (await label.getAttribute('aria-label')) ?? (await label.text()).trim();
  }

  /** The visible text, which projected content may make differ from the label. */
  async getText(): Promise<string> {
    return (await (await this.locatorFor('.tm-chip__text')()).text()).trim();
  }

  /** What the label renders as: a link, a button, or plain text. */
  async getKind(): Promise<'link' | 'button' | 'text'> {
    const label = await this.labelElement();
    if (await label.matchesSelector('a')) {
      return 'link';
    }
    return (await label.matchesSelector('button')) ? 'button' : 'text';
  }

  /** The link's URL, or `null` when the chip is not a link. */
  async getHref(): Promise<string | null> {
    return (await this.getKind()) === 'link'
      ? (await this.labelElement()).getAttribute('href')
      : null;
  }

  /** The colour tone. */
  async getTone(): Promise<string | null> {
    return (await this.host()).getAttribute('data-tone');
  }

  /** The resolved size step (`sm`, `md` or `lg`). */
  async getSize(): Promise<string | null> {
    return (await this.host()).getAttribute('data-size');
  }

  /** Whether the chip has a remove button. */
  async isRemovable(): Promise<boolean> {
    return (await this.removeButton()) !== null;
  }

  /** The remove button's accessible name ("Remove Service"), or `null`. */
  async getRemoveLabel(): Promise<string | null> {
    const button = await this.removeButton();
    return button === null ? null : button.getAttribute('aria-label');
  }

  /** Whether a chip set's collapse mode has hidden this chip. */
  async isCollapsed(): Promise<boolean> {
    return (await this.host()).hasClass('tm-chip--collapsed');
  }

  /** Clicks the label button (activates) or the link. */
  async activate(): Promise<void> {
    return (await this.labelElement()).click();
  }

  /** Clicks the remove button. */
  async remove(): Promise<void> {
    const button = await this.removeButton();
    if (button === null) {
      throw new Error('The chip is not removable.');
    }
    return button.click();
  }

  /** Presses Delete on the chip's first control, as a keyboard user removes it. */
  async pressDelete(): Promise<void> {
    const control =
      (await this.locatorForOptional('button.tm-chip__label, a.tm-chip__label')()) ??
      (await this.removeButton());
    if (control === null) {
      throw new Error('The chip has no control to press Delete on.');
    }
    return control.sendKeys(TestKey.DELETE);
  }

  /** Whether focus is on one of the chip's controls. */
  async isFocused(): Promise<boolean> {
    const controls = await this.locatorForAll('.tm-chip__label, .tm-chip__remove')();
    for (const control of controls) {
      if (await control.isFocused()) {
        return true;
      }
    }
    return false;
  }
}

/** Harness for `tm-chip-set`. */
export class TmChipSetHarness extends ComponentHarness {
  /** The selector for the `tm-chip-set` host element. */
  static hostSelector = 'tm-chip-set';

  /** The "+N" chip of collapse mode, while it stands for hidden chips. */
  private readonly more = this.locatorForOptional(
    '.tm-chip-set__more:not(.tm-chip-set__more--idle) button',
  );

  /** The chips, in order, optionally filtered. */
  async getChips(filters: TmChipHarnessFilters = {}): Promise<TmChipHarness[]> {
    return this.locatorForAll(TmChipHarness.with(filters))();
  }

  /** The chips on show (all of them, unless collapse mode hides some). */
  async getVisibleChips(): Promise<TmChipHarness[]> {
    const chips = await this.getChips();
    const visible: TmChipHarness[] = [];
    for (const chip of chips) {
      if (!(await chip.isCollapsed())) {
        visible.push(chip);
      }
    }
    return visible;
  }

  /** How the set lays out chips that do not fit: `wrap`, `collapse` or `scroll`. */
  async getOverflow(): Promise<string | null> {
    return (await this.host()).getAttribute('data-overflow');
  }

  /** The list's accessible name. */
  async getAriaLabel(): Promise<string | null> {
    return (await this.host()).getAttribute('aria-label');
  }

  /** The "+N" chip's accessible name ("2 more: Sale, Operation"), or `null` when nothing hides. */
  async getMoreLabel(): Promise<string | null> {
    const more = await this.more();
    return more === null ? null : more.getAttribute('aria-label');
  }

  /** The "+N" chip's visible count ("+2"), or `null` when nothing hides. */
  async getMoreText(): Promise<string | null> {
    const more = await this.more();
    if (more === null) {
      return null;
    }
    return (await (await this.locatorFor('.tm-chip-set__more-count')()).text()).trim();
  }

  /** The controls that hold the set's single tab stop (`tabindex="0"`); one at most. */
  async getTabStopCount(): Promise<number> {
    return (await this.locatorForAll('[tabindex="0"]')()).length;
  }
}
