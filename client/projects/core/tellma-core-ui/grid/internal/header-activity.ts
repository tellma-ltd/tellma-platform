// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import { InjectionToken } from '@angular/core';

/**
 * Where a header action reports its `active` state, so the grid can describe
 * the header holding it: keyboard focus rests on the header cell rather than
 * on the action, and a screen reader hears only the focused element's
 * description.
 */
export interface ɵTmGridHeaderActivity {
  /**
   * Records whether a header action's controlled state is on; an action
   * reports `false` once more as it is destroyed.
   *
   * @param element the action's element, inside the header it belongs to
   * @param active whether the state the action controls is on
   */
  setHeaderActionActive(element: Element, active: boolean): void;
}

/**
 * The grid's {@link ɵTmGridHeaderActivity}, provided to its header templates
 * through the template outlet's injector; absent outside a grid.
 */
export const ɵTM_GRID_HEADER_ACTIVITY = new InjectionToken<ɵTmGridHeaderActivity>(
  'ɵTM_GRID_HEADER_ACTIVITY',
);
