// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import { ComponentHarness, HarnessPredicate } from '@angular/cdk/testing';
import { TmButtonHarness, TmGridHarness, TmSwitchHarness } from '@tellma/core-ui-testing';

// The Diagnostics panel of @tellma/core/support as a test reads and drives
// it: the header's labels and values, the request grid (through the
// component library's grid harness, which reads cells and activates rows),
// the slow and failed switch, and the three buttons. Beside it, the line
// that shows when the support tools cannot load, with its keys.

/** The panel's buttons, by their English labels. */
export type TmDiagnosticsPanelButton = 'Copy diagnostics' | 'Download diagnostic file' | 'Report a problem';

/**
 * Harness for `tm-diagnostics`, the Diagnostics panel.
 *
 * @example
 * ```ts
 * const panel = await TestbedHarnessEnvironment.documentRootLoader(fixture).getHarness(TmDiagnosticsPanelHarness);
 * expect((await panel.getHeader())['Connection']).toBe('Online');
 * await (await panel.getGrid()).activateRow(0);
 * ```
 */
export class TmDiagnosticsPanelHarness extends ComponentHarness {
  /** The selector of the `tm-diagnostics` element. */
  static hostSelector = 'tm-diagnostics';

  /** The header's labels. */
  readonly #labels = this.locatorForAll('dl > div > dt');

  /** The header's values. */
  readonly #values = this.locatorForAll('dl > div > dd');

  /** The action row's buttons. */
  readonly #buttons = this.locatorForAll(new HarnessPredicate(TmButtonHarness, { ancestor: '.tm-diagnostics__actions' }));

  /**
   * The header, label by label.
   *
   * @returns Each pair's value text, its whitespace collapsed, by its label text.
   */
  async getHeader(): Promise<Record<string, string>> {
    const [labels, values] = await Promise.all([this.#labels(), this.#values()]);
    const texts = await Promise.all([...labels, ...values].map((element) => element.text()));
    const collapse = (text: string): string => text.replace(/\s+/g, ' ').trim();
    return Object.fromEntries(labels.map((_, index) => [collapse(texts[index]!), collapse(texts[labels.length + index]!)]));
  }

  /**
   * The request grid.
   *
   * @returns The grid's harness.
   */
  getGrid(): Promise<TmGridHarness> {
    return this.locatorFor(TmGridHarness)();
  }

  /**
   * Whether the grid keeps only the slow and failed requests.
   *
   * @returns Whether the switch is on.
   */
  async isFlaggedOnly(): Promise<boolean> {
    return (await this.locatorFor(TmSwitchHarness)()).isChecked();
  }

  /** Turns the slow and failed switch over. */
  async toggleFlaggedOnly(): Promise<void> {
    await (await this.locatorFor(TmSwitchHarness)()).toggle();
  }

  /**
   * The action row's buttons' labels, in order.
   *
   * @returns The labels.
   */
  async getButtons(): Promise<string[]> {
    return Promise.all((await this.#buttons()).map((button) => button.getText()));
  }

  /**
   * One of the action row's buttons.
   *
   * @param label - The button's label.
   * @returns The button's harness.
   * @throws Error when the panel shows no such button.
   */
  async getButton(label: TmDiagnosticsPanelButton | string): Promise<TmButtonHarness> {
    for (const button of await this.#buttons()) {
      if ((await button.getText()) === label) {
        return button;
      }
    }
    throw new Error(`The Diagnostics panel shows no "${label}" button.`);
  }
}

/**
 * Harness for `tm-support-offline`, the line that shows when the support
 * tools cannot load: its text, and the keys it names.
 *
 * @example
 * ```ts
 * const line = await loader.getHarness(TmSupportOfflineHarness);
 * expect(await line.getText()).toContain('to copy diagnostics');
 * ```
 */
export class TmSupportOfflineHarness extends ComponentHarness {
  /** The selector of the `tm-support-offline` element. */
  static hostSelector = 'tm-support-offline';

  /** The keycaps. */
  readonly #keys = this.locatorForAll('tm-kbd kbd');

  /**
   * The line, its whitespace collapsed.
   *
   * @returns The text, the keys' caps included.
   */
  async getText(): Promise<string> {
    return (await (await this.host()).text()).replace(/\s+/g, ' ').trim();
  }

  /**
   * The keys the line names.
   *
   * @returns Each keycap's text, in order.
   */
  async getKeys(): Promise<string[]> {
    return Promise.all((await this.#keys()).map(async (key) => (await key.text()).trim()));
  }
}
