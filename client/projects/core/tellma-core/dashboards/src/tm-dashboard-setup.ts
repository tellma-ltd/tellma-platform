// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import { inject } from '@angular/core';
import { TmLocalization } from '@tellma/core';
import { TmIconRegistry } from '@tellma/core-ui/icon';

import { tmRegisterDashboardIcons } from './tm-dashboard-icons';
import { TM_DASHBOARD_STRINGS_EN, TM_HOME_STRINGS_EN } from './tm-dashboard-strings';

/**
 * Registers what the entry point's components read from the app: its
 * English strings, under `tm.home` and `tm.dashboard`, and the icon names
 * it draws that the app has not registered. Each component that renders
 * either calls it from its constructor; only the first call registers.
 */
export function tmPrepareDashboards(): void {
  const l10n = inject(TmLocalization);
  l10n.ɵregisterStrings('home', TM_HOME_STRINGS_EN);
  l10n.ɵregisterStrings('dashboard', TM_DASHBOARD_STRINGS_EN);
  tmRegisterDashboardIcons(inject(TmIconRegistry));
}
