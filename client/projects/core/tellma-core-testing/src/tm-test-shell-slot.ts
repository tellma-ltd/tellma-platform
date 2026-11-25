// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import { ChangeDetectionStrategy, Component, type Signal } from '@angular/core';
import type { TmLazyComponent, TmShellSlot } from '@tellma/core';

// An app-bar slot for tests: a feature declares it under `appBar`, and the
// tenant shell draws its button, its badge's count and, on a click, its
// panel, which says only that it is the test panel.

/** The panel a test slot opens. */
@Component({
  selector: 'tm-test-shell-slot-panel',
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `<p class="tm-test-shell-slot-panel">Test panel</p>`,
})
export class TmTestShellSlotPanel {}

/** What a test slot changes from the defaults. */
export interface TmTestShellSlotOptions {
  /** The string key of the button's name; the slot's id by default. */
  readonly labelKey?: string;
  /** A registered icon name; `arrow-right` by default. */
  readonly icon?: string;
  /** The badge's count; no badge by default. */
  readonly count?: Signal<number | null>;
  /** The panel; the test panel by default, loaded at once. */
  readonly panel?: TmLazyComponent;
}

/**
 * An app-bar slot for a test feature's `appBar`.
 *
 * @param id - The slot's id, which must not be `help` or `assistant`.
 * @param options - What the slot changes from the defaults.
 * @returns The slot.
 *
 * @example
 * ```ts
 * const pending = signal<number | null>(3);
 * const feature = tmFeature({ key: 'acme', appBar: [tmTestShellSlot('acme-approvals', { count: pending })] });
 * ```
 */
export function tmTestShellSlot(id: string, options: TmTestShellSlotOptions = {}): TmShellSlot {
  const count = options.count;
  return {
    id,
    icon: options.icon ?? 'arrow-right',
    labelKey: options.labelKey ?? id,
    ...(count === undefined ? {} : { badge: () => count }),
    panel: options.panel ?? (() => Promise.resolve(TmTestShellSlotPanel)),
  };
}
