// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import { computed, inject, type Signal } from '@angular/core';

import { TmBreakpoints } from '../layout/tm-breakpoints';
import { TM_FORM_FIELD_DEFAULTS, type TmControlSize } from './provide-tellma-forms';

/**
 * Resolves the size step a control on the size ladder renders at.
 * Internal to the library; call it in an injection context.
 *
 * An explicit `size` always wins. Unset, the control takes the
 * workspace-wide `TM_FORM_FIELD_DEFAULTS.size` from the `sm` breakpoint up,
 * and the `lg` step (the 46 px touch height) below it, so controls in page
 * content stay comfortable to tap on a phone. The resolved size changes
 * only when the viewport crosses the `sm` threshold — as on a rotation —
 * never on input. Grids keep their own row size at every width.
 */
export function ɵtmControlSize(explicit: Signal<TmControlSize | undefined>): Signal<TmControlSize> {
  const defaults = inject(TM_FORM_FIELD_DEFAULTS);
  const wide = inject(TmBreakpoints).atLeast('sm');
  return computed(() => explicit() ?? (wide() ? defaults.size : 'lg'));
}
