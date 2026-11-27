// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import { afterNextRender, ChangeDetectionStrategy, Component, effect, inject, Injector, untracked, viewChild } from '@angular/core';
import { TmLocalization } from '@tellma/core';

import { tmPrepareDashboards } from './tm-dashboard-setup';
import { TmDashboardGrid } from './tm-dashboard-grid';
import { TmHomeGreeting } from './tm-home-greeting';
import { TmHomeRendered } from './tm-home-telemetry';
import { TM_HOME_WIDGETS } from './tm-home-widgets';

// The platform's home page (spec 0035 §15): the greeting and date line over
// the code-defined grid of the built-in widgets, labelled "Your work", both
// in the dashboard column that caps the tiles' width and centres them, over
// the subtle band that fills the rest of the page. Every part renders from
// stored documents. Once every tile's body has rendered, the home's visit
// is timed.

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
  /** The home widgets. */
  protected readonly widgets = TM_HOME_WIDGETS;

  /** The grid's accessible name. */
  protected readonly label = inject(TmLocalization).ɵtext('tm.home.grid');

  /** The grid. */
  private readonly grid = viewChild.required(TmDashboardGrid);

  /** Registers the strings the label reads, and times the visit once every tile's body has rendered. */
  constructor() {
    tmPrepareDashboards();

    // --- The visit's timing: the first frame after every tile has its body, outside the home's host none ---
    const rendered = inject(TmHomeRendered, { optional: true });
    if (rendered !== null) {
      const injector = inject(Injector);
      const watch = effect(() => {
        const tiles = this.grid().tiles();
        if (tiles.length === this.widgets.length && tiles.every((tile) => tile.status !== 'loading')) {
          watch.destroy();
          untracked(() => afterNextRender({ read: () => rendered.emit(false) }, { injector }));
        }
      });
    }
  }
}
