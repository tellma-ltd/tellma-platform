// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import {
  ComponentHarness,
  HarnessPredicate,
  TestKey,
  type BaseHarnessFilters,
  type ModifierKeys,
} from '@angular/cdk/testing';

/** The keys a tree answers to. */
export type TmTreeKey =
  'ArrowUp' | 'ArrowDown' | 'ArrowLeft' | 'ArrowRight' | 'Home' | 'End' | 'Enter' | 'Space';

/** The CDK test keys for each tree key. */
const TEST_KEYS: Record<TmTreeKey, TestKey | string> = {
  ArrowUp: TestKey.UP_ARROW,
  ArrowDown: TestKey.DOWN_ARROW,
  ArrowLeft: TestKey.LEFT_ARROW,
  ArrowRight: TestKey.RIGHT_ARROW,
  Home: TestKey.HOME,
  End: TestKey.END,
  Enter: TestKey.ENTER,
  Space: ' ',
};

/** Filters for {@link TmTreeNodeHarness}. */
export interface TmTreeNodeHarnessFilters extends BaseHarnessFilters {
  /** The node's key. */
  key?: string;
  /** The node's content text, or a pattern it matches. */
  text?: string | RegExp;
}

/** Harness for one node of a `tm-tree`: its row, state and keys. */
export class TmTreeNodeHarness extends ComponentHarness {
  /** The selector for a node's list item. */
  static hostSelector = '.tm-tree__item';

  /** Gets a predicate that finds nodes by key or text. */
  static with(options: TmTreeNodeHarnessFilters = {}): HarnessPredicate<TmTreeNodeHarness> {
    return new HarnessPredicate(TmTreeNodeHarness, options)
      .addOption('key', options.key, async (harness, key) => (await harness.getKey()) === key)
      .addOption('text', options.text, (harness, text) =>
        HarnessPredicate.stringMatches(harness.getText(), text),
      );
  }

  /** The node's own row (not a descendant's). */
  private readonly row = this.locatorFor(':scope > .tm-tree__row');

  /** The node's own twisty. */
  private readonly twisty = this.locatorFor(':scope > .tm-tree__row > .tm-tree__twisty');

  /** The node's own content. */
  private readonly content = this.locatorFor(':scope > .tm-tree__row > .tm-tree__content');

  /** The node's key. */
  async getKey(): Promise<string | null> {
    return (await this.host()).getAttribute('data-key');
  }

  /** The node's content text, trimmed. */
  async getText(): Promise<string> {
    return (await (await this.content()).text()).trim();
  }

  /** The node's depth: 1 for a root. */
  async getLevel(): Promise<number> {
    return Number(await (await this.host()).getAttribute('aria-level'));
  }

  /** Whether the node can expand: it is not a known leaf. */
  async isExpandable(): Promise<boolean> {
    return (await (await this.host()).getAttribute('aria-expanded')) !== null;
  }

  /** Whether the node is expanded. */
  async isExpanded(): Promise<boolean> {
    return (await (await this.host()).getAttribute('aria-expanded')) === 'true';
  }

  /** Whether the node is selected. */
  async isSelected(): Promise<boolean> {
    return (await (await this.host()).getAttribute('aria-selected')) === 'true';
  }

  /** Whether the node's children are loading. */
  async isLoading(): Promise<boolean> {
    return (await (await this.host()).getAttribute('aria-busy')) === 'true';
  }

  /** Whether the node shows the loading spinner. */
  async hasSpinner(): Promise<boolean> {
    const spinner = await this.locatorForOptional(
      ':scope > .tm-tree__row > .tm-tree__spinner-slot > tm-spinner',
    )();
    return spinner !== null;
  }

  /** Whether the node is a context node, drawn muted. */
  async isContext(): Promise<boolean> {
    return (await this.row()).hasClass('tm-tree__row--context');
  }

  /** Whether the node is the tree's tab stop. */
  async isActive(): Promise<boolean> {
    return (await (await this.host()).getAttribute('tabindex')) === '0';
  }

  /** Whether the node has focus. */
  async isFocused(): Promise<boolean> {
    return (await this.host()).isFocused();
  }

  /** Whether the node's row is a drag source. */
  async isDraggable(): Promise<boolean> {
    return (await (await this.row()).getAttribute('draggable')) === 'true';
  }

  /** Clicks the node's content: selects in `single`, toggles in `multiple`. */
  async click(modifiers: ModifierKeys = {}): Promise<void> {
    return (await this.content()).click(modifiers);
  }

  /** Double-clicks the node's content: activates a leaf, toggles a branch. */
  async doubleClick(): Promise<void> {
    const content = await this.content();
    await content.click();
    await content.click();
    // A test element's events do not bubble: the row listens for the double-click.
    return (await this.row()).dispatchEvent('dblclick');
  }

  /** Clicks the twisty, expanding or collapsing the node. */
  async toggle(): Promise<void> {
    return (await this.twisty()).click();
  }

  /** Expands the node if it is collapsed. */
  async expand(): Promise<void> {
    if (!(await this.isExpanded())) {
      await this.toggle();
    }
  }

  /** Collapses the node if it is expanded. */
  async collapse(): Promise<void> {
    if (await this.isExpanded()) {
      await this.toggle();
    }
  }

  /** Focuses the node. */
  async focus(): Promise<void> {
    return (await this.host()).focus();
  }

  /** Presses a key on the node, with modifiers. */
  async press(key: TmTreeKey, modifiers: ModifierKeys = {}): Promise<void> {
    return (await this.host()).sendKeys(modifiers, TEST_KEYS[key]);
  }

  /** Types text on the node, for typeahead. */
  async type(text: string): Promise<void> {
    return (await this.host()).sendKeys(text);
  }
}

/** Harness for `tm-tree`: its nodes, in document order, open ones only. */
export class TmTreeHarness extends ComponentHarness {
  /** The selector for the `tm-tree` host element. */
  static hostSelector = 'tm-tree';

  /** The tree element. */
  private readonly tree = this.locatorFor('[role="tree"]');

  /** The tree's accessible name. */
  async getLabel(): Promise<string | null> {
    return (await this.tree()).getAttribute('aria-label');
  }

  /** Whether the tree allows more than one selected node. */
  async isMultiSelectable(): Promise<boolean> {
    return (await (await this.tree()).getAttribute('aria-multiselectable')) === 'true';
  }

  /** The shown nodes, in document order, filtered by key or text. */
  async getNodes(filter: TmTreeNodeHarnessFilters = {}): Promise<TmTreeNodeHarness[]> {
    return this.locatorForAll(TmTreeNodeHarness.with(filter))();
  }

  /** The first shown node matching a filter. */
  async getNode(filter: TmTreeNodeHarnessFilters): Promise<TmTreeNodeHarness> {
    return this.locatorFor(TmTreeNodeHarness.with(filter))();
  }

  /** The shown nodes' texts, in document order. */
  async getNodeTexts(): Promise<string[]> {
    const nodes = await this.getNodes();
    return Promise.all(nodes.map((node) => node.getText()));
  }

  /** The node that is the tree's tab stop, if any. */
  async getActiveNode(): Promise<TmTreeNodeHarness | null> {
    for (const node of await this.getNodes()) {
      if (await node.isActive()) {
        return node;
      }
    }
    return null;
  }

  /** Presses a key on the tree's tab stop. */
  async press(key: TmTreeKey, modifiers: ModifierKeys = {}): Promise<void> {
    const active = await this.getActiveNode();
    if (active === null) {
      throw new Error('The tree has no node to take the key.');
    }
    await active.focus();
    return active.press(key, modifiers);
  }
}
