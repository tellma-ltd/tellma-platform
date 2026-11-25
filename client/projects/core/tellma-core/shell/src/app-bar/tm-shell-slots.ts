// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import { inject, Injectable, type Signal } from '@angular/core';
import { TM_APP_DECLARATIONS, TmLocalization, type TmShellSlot } from '@tellma/core';

// The distribution's app-bar slots (spec 0027 §7.2), in feature order, each
// with its label and its badge's count. A badge function is called once, in
// the tenant shell's injection context, so the app bar's buttons and the
// session menu's rows below md read the same count.

/** One distribution slot as the shell shows it. */
export interface TmShellSlotView {
  /** The declaration. */
  readonly slot: TmShellSlot;
  /** Its label, from its string key. */
  readonly label: Signal<string>;
  /** Its badge's count, or `null` without a badge. */
  readonly count: Signal<number | null> | null;
}

/** The distribution's slots, with their labels and counts. Provided by the tenant shell. */
@Injectable()
export class TmShellSlots {
  /** The slots, in feature order. */
  readonly items: readonly TmShellSlotView[];

  /** Creates the slots, calling each badge function once. */
  constructor() {
    const l10n = inject(TmLocalization);
    this.items = inject(TM_APP_DECLARATIONS).appBar.map(
      (slot): TmShellSlotView => ({ slot, label: l10n.ɵtext(slot.labelKey), count: slot.badge?.() ?? null }),
    );
  }
}
