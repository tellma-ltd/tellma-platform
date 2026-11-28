// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import { ComponentHarness } from '@angular/cdk/testing';

/** Harness for the `tmButton` directive, on a button or a link. */
export class TmButtonHarness extends ComponentHarness {
  /** The selector for the `button[tmButton]` and `a[tmButton]` host elements. */
  static hostSelector = 'button[tmButton], a[tmButton]';

  /** Gets the button's visible text, trimmed. */
  async getText(): Promise<string> {
    return (await (await this.host()).text()).trim();
  }

  /** Clicks the button (a no-op activation while pending). */
  async click(): Promise<void> {
    return (await this.host()).click();
  }

  /** Whether the button is in the pending (aria-busy) state. */
  async isPending(): Promise<boolean> {
    return (await (await this.host()).getAttribute('aria-busy')) === 'true';
  }

  /** Whether the button is natively disabled (a link never is). */
  async isDisabled(): Promise<boolean> {
    return (await (await this.host()).getProperty<boolean | undefined>('disabled')) === true;
  }

  /**
   * The reason the button is disabled with (`disabledReason`), as the
   * button's accessible description carries it; null when it has none.
   */
  async getDisabledReason(): Promise<string | null> {
    const host = await this.host();
    if ((await host.getAttribute('aria-disabled')) !== 'true') {
      return null;
    }
    // The reason is registered through the CDK describer, whose message
    // elements carry its id prefix; other descriptions are the page's own.
    const describedBy = (await host.getAttribute('aria-describedby')) ?? '';
    const root = this.documentRootLocatorFactory();
    for (const id of describedBy.trim().split(/\s+/)) {
      if (!id.startsWith('cdk-describedby-message')) {
        continue;
      }
      const message = await root.locatorForOptional(`[id="${id}"]`)();
      if (message !== null) {
        return (await message.text()).trim();
      }
    }
    return null;
  }

  /** Whether the host is a link (`a[tmButton]`). */
  async isLink(): Promise<boolean> {
    return (await (await this.host()).getProperty<string>('tagName')) === 'A';
  }

  /** The visual variant, read off the host classes. */
  async getVariant(): Promise<'primary' | 'secondary' | 'ghost' | 'danger'> {
    const host = await this.host();
    for (const variant of ['primary', 'ghost', 'danger'] as const) {
      if (await host.hasClass(`tm-button--${variant}`)) {
        return variant;
      }
    }
    return 'secondary';
  }

  /** The size, read off the host classes. */
  async getSize(): Promise<'sm' | 'md' | 'lg'> {
    const host = await this.host();
    if (await host.hasClass('tm-button--sm')) {
      return 'sm';
    }
    return (await host.hasClass('tm-button--lg')) ? 'lg' : 'md';
  }

  /** Focuses the button. */
  async focus(): Promise<void> {
    return (await this.host()).focus();
  }

  /** Whether the button is focused. */
  async isFocused(): Promise<boolean> {
    return (await this.host()).isFocused();
  }
}
