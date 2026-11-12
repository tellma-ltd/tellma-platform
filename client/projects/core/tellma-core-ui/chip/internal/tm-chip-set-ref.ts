// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import { InjectionToken } from '@angular/core';

/**
 * What a chip asks of the chip set it sits in. The set provides itself
 * under {@link TM_CHIP_SET} so a chip reaches it without importing the set
 * (the set imports the chip for its content query).
 */
export interface TmChipSetRef {
  /**
   * Called when a chip is destroyed, so the set can move focus to a
   * neighbour if the chip held it, instead of letting it fall to the page.
   */
  chipDestroyed(chip: object): void;
}

/** The chip set a chip sits in, if any. */
export const TM_CHIP_SET = new InjectionToken<TmChipSetRef>('TM_CHIP_SET');
