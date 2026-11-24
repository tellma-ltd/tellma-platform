// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import { ComponentHarness } from '@angular/cdk/testing';
import { TmSearchFieldHarness, TmTreeHarness, TmTreeNodeHarness, type TmTreeNodeHarnessFilters } from '@tellma/core-ui-testing';

// A harness for @tellma/core/path-picker's picker: its search field and its
// tree are the component library's, so the harness hands both out, and
// reads the rows' labels, the navigation pills and the search's count.

/** Harness for `tm-path-picker`, the tree of a root entity's fields. */
export class TmPathPickerHarness extends ComponentHarness {
  /** The selector for the `tm-path-picker` host element. */
  static hostSelector = 'tm-path-picker';

  /** The search field. */
  readonly getSearchField = this.locatorFor(TmSearchFieldHarness);

  /** The tree of fields. */
  readonly getTree = this.locatorFor(TmTreeHarness);

  /**
   * Types a search into the search field.
   *
   * @param query - The search; empty to show every field again.
   */
  async search(query: string): Promise<void> {
    return (await this.getSearchField()).setValue(query);
  }

  /** The shown nodes, filtered by key or text, in document order. */
  async getNodes(filter: TmTreeNodeHarnessFilters = {}): Promise<TmTreeNodeHarness[]> {
    return (await this.getTree()).getNodes(filter);
  }

  /**
   * A shown node by its key: a column key or a Queryex path.
   *
   * @param key - The key.
   * @returns The node.
   */
  async getNode(key: string): Promise<TmTreeNodeHarness> {
    return (await this.getTree()).getNode({ key });
  }

  /** The shown nodes' keys, in document order. */
  async getKeys(): Promise<string[]> {
    const nodes = await this.getNodes();
    return (await Promise.all(nodes.map((node) => node.getKey()))).filter((key): key is string => key !== null);
  }

  /** The shown rows' labels, without the navigation pills, in document order. */
  async getLabels(): Promise<string[]> {
    const labels = await this.locatorForAll('.tm-path-picker__label')();
    return Promise.all(labels.map(async (label) => (await label.text()).trim()));
  }

  /**
   * The label of a navigation's target, as its row's pill shows it, or
   * `null` for a row without one.
   *
   * @param key - The row's key.
   * @returns The pill's text, or `null`.
   */
  async getPill(key: string): Promise<string | null> {
    const pill = await this.locatorForOptional(`.tm-tree__item[data-key="${CSS.escape(key)}"] > .tm-tree__row .tm-path-picker__pill`)();
    return pill === null ? null : (await pill.text()).trim();
  }

  /**
   * The kind of a shown row: `value`, `text`, `navigation`, `image` or `file`.
   *
   * @param key - The row's key.
   * @returns The kind.
   */
  async getKind(key: string): Promise<string | null> {
    const row = await this.locatorFor(`.tm-tree__item[data-key="${CSS.escape(key)}"] > .tm-tree__row .tm-path-picker__row`)();
    return row.getAttribute('data-kind');
  }

  /** What the search's polite status reads: the count of matches, or that none match; empty without a search. */
  async getStatusText(): Promise<string> {
    return (await (await this.locatorFor('.tm-path-picker__status')()).text()).trim();
  }
}
