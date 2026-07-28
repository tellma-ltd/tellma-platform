// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import { ComponentHarness } from '@angular/cdk/testing';
import { TabHarness, TabsHarness } from '@angular/aria/tabs/testing';

/** Harness for one tab of a `tm-tab-group` (composes the aria `TabHarness`). */
export class TmTabHarness extends ComponentHarness {
  /** The selector for a rendered tab of the strip. */
  static hostSelector = '.tm-tab-group__tab';

  private readonly aria = this.locatorFor(TabHarness);

  /** Gets the tab's visible label text, trimmed. */
  async getLabel(): Promise<string> {
    return (await (await this.host()).text()).trim();
  }

  /** Whether the tab is selected. */
  async isSelected(): Promise<boolean> {
    return (await this.aria()).isSelected();
  }

  /** Whether the tab is disabled. */
  async isDisabled(): Promise<boolean> {
    return (await this.aria()).isDisabled();
  }

  /** Clicks the tab to select it. */
  async select(): Promise<void> {
    return (await this.aria()).select();
  }
}

/** Harness for `tm-tab-group` (composes the aria `TabsHarness`). */
export class TmTabGroupHarness extends ComponentHarness {
  /** The selector for the `tm-tab-group` host element. */
  static hostSelector = 'tm-tab-group';

  private readonly aria = this.locatorFor(TabsHarness);

  /** Gets the harnesses of every tab in the strip, in display order. */
  async getTabs(): Promise<TmTabHarness[]> {
    return this.locatorForAll(TmTabHarness)();
  }

  /** Gets the selected tab's harness, or null when nothing is selected. */
  async getSelectedTab(): Promise<TmTabHarness | null> {
    for (const tab of await this.getTabs()) {
      if (await tab.isSelected()) {
        return tab;
      }
    }
    return null;
  }

  /** Selects the tab whose visible label matches. */
  async selectTab(label: string): Promise<void> {
    const tabs = await this.aria();
    return tabs.selectTab({ title: label });
  }

  /** The visible (non-inert) panel's text content, trimmed. */
  async getActivePanelText(): Promise<string> {
    const panels = await this.locatorForAll('.tm-tab-group__panel:not([inert])')();
    return panels.length === 0 ? '' : (await panels[0].text()).trim();
  }
}
