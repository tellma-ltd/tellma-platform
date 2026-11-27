// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import { ChangeDetectionStrategy, Component, inject } from '@angular/core';
import { TM_APP_DECLARATIONS, TmLocalization, type TmDashboardWidgetModel } from '@tellma/core';

import { tmPrepareDashboards } from './tm-dashboard-setup';
import { TmDashboardGrid } from './tm-dashboard-grid';
import { TmHomeGreeting } from './tm-home-greeting';
import { TM_HOME_WIDGETS } from './tm-home-widgets';

// The platform's home page (spec 0035 §15): the greeting and date line over
// the code-defined grid, labelled "Your work", both in the dashboard column
// that caps the tiles' width and centres them, over the subtle band that
// fills the rest of the page. It renders the home widgets whose types the
// app declares, so the grid never shows a tile for a type that has not
// shipped. Every part renders from stored documents.

/** The platform's home page: the greeting over the home widgets. */
@Component({
  selector: 'tm-home-page',
  imports: [TmDashboardGrid, TmHomeGreeting],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <!-- The greeting, on the column's edge. -->
    <tm-home-greeting class="tm-dashboard-column" />
    <!-- The grid, in the column, over the band. -->
    <div class="tm-dashboard-band">
      <tm-dashboard-grid class="tm-dashboard-column" [widgets]="widgets" [label]="label()" />
    </div>
  `,
  host: { class: 'tm-home' },
})
export class TmHomePage {
  /** The widget types the app declares. */
  readonly #declared: ReadonlySet<string> = new Set(inject(TM_APP_DECLARATIONS).widgets.map((declaration) => declaration.key));

  /** The home widgets whose types the app declares. */
  protected readonly widgets: readonly TmDashboardWidgetModel[] = TM_HOME_WIDGETS.filter((widget) => this.#declared.has(widget.type));

  /** The grid's accessible name. */
  protected readonly label = inject(TmLocalization).ɵtext('tm.home.grid');

  /** Registers the strings the label reads. */
  constructor() {
    tmPrepareDashboards();
  }
}
