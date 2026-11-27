// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import { ChangeDetectionStrategy, Component, input } from '@angular/core';
import { TmPageHeader } from '@tellma/core-ui/page-header';
import { TmSkeleton } from '@tellma/core-ui/skeleton';

import type { TmRouteSkeleton } from '../app/tm-route-data';

// The skeletons a route host draws in the click's frame (spec 0024 §4.4), on
// 0023's page-header layout: the standard header's two rows for search,
// details, the My settings hub and plain pages; the compact header's one row
// for reports and dashboards; and the home page's greeting over its three
// tile frames, in the dashboard column. Each body has its family's final
// geometry; the page specs pin zero shift against their own pages. The home
// skeleton's boxes are the global dashboard stylesheet's, which the home page
// itself draws with, so the two share one set of rules.

/** The skeleton of one page family, drawn while its page loads. */
@Component({
  selector: 'tm-page-skeleton',
  imports: [TmPageHeader, TmSkeleton],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    @switch (kind()) {
      @case ('home') {
        <!-- Home: the greeting's two lines over the home grid's tile frames, each its header and five list rows,
             in the boxes the home page draws (the global dashboard stylesheet's). -->
        <div class="tm-home">
          <div class="tm-dashboard-column tm-home-greeting">
            <tm-skeleton class="tm-home-greeting__lines" shape="line" [lines]="2" width="20rem" />
          </div>
          <div class="tm-dashboard-band">
            <div class="tm-dashboard-column tm-dashboard-grid tm-home-skeleton">
              <!-- Three frames, which the stylesheet places where the home page places its widgets. -->
              <div class="tm-dashboard-grid__cell tm-dashboard-frame">
                <div class="tm-dashboard-frame__header"><tm-skeleton shape="line" width="8rem" /></div>
                <tm-skeleton class="tm-dashboard-frame__rows" shape="line" [lines]="5" />
              </div>
              <div class="tm-dashboard-grid__cell tm-dashboard-frame">
                <div class="tm-dashboard-frame__header"><tm-skeleton shape="line" width="8rem" /></div>
                <tm-skeleton class="tm-dashboard-frame__rows" shape="line" [lines]="5" />
              </div>
              <div class="tm-dashboard-grid__cell tm-dashboard-frame">
                <div class="tm-dashboard-frame__header"><tm-skeleton shape="line" width="8rem" /></div>
                <tm-skeleton class="tm-dashboard-frame__rows" shape="line" [lines]="5" />
              </div>
            </div>
          </div>
        </div>
      }
      @case ('report') {
        <!-- Reports and dashboards: the compact header's one row, then the body. -->
        <tm-page-header>
          <span tmPageTitle><tm-skeleton shape="line" width="14rem" /></span>
        </tm-page-header>
        <tm-skeleton class="tm-page-skeleton__body" shape="block" height="24rem" />
      }
      @case ('dashboard') {
        <!-- A dashboard: the compact header and the body, in the dashboard column. -->
        <div class="tm-dashboard-column tm-page-skeleton__column">
          <tm-page-header>
            <span tmPageTitle><tm-skeleton shape="line" width="14rem" /></span>
          </tm-page-header>
          <tm-skeleton class="tm-page-skeleton__body" shape="block" height="24rem" />
        </div>
      }
      @default {
        <!-- Every other family: the standard header's title row and toolbar row. -->
        <tm-page-header>
          <span tmPageTitle><tm-skeleton shape="line" width="14rem" /></span>
          <span tmPageToolbar><tm-skeleton shape="line" width="10rem" /></span>
        </tm-page-header>
        @switch (kind()) {
          @case ('search') {
            <tm-skeleton class="tm-page-skeleton__body" shape="table" [lines]="10" [columns]="4" />
          }
          @case ('details') {
            <div class="tm-page-skeleton__body">
              <tm-skeleton shape="line" [lines]="6" width="32rem" />
            </div>
          }
          @case ('me') {
            <div class="tm-page-skeleton__body tm-page-skeleton__hub">
              <tm-skeleton shape="line" [lines]="4" width="220px" />
              <tm-skeleton shape="line" [lines]="6" width="32rem" />
            </div>
          }
        }
      }
    }
  `,
  styles: `
    /* The skeleton fills the route host, so the home skeleton's band reaches the bottom of main. */
    :host {
      display: block;
      block-size: 100%;
    }
    .tm-page-skeleton__body {
      margin-inline: var(--page-header-padding-x);
      max-inline-size: calc(100% - 2 * var(--page-header-padding-x));
    }
    .tm-page-skeleton__column {
      display: flex;
      flex-direction: column;
      gap: var(--space-4);
      padding: var(--page-header-padding-y) var(--page-header-padding-x);
    }
    .tm-page-skeleton__hub {
      display: flex;
      gap: var(--space-6);
    }
  `,
})
export class TmPageSkeleton {
  /** The page family whose skeleton to draw. */
  readonly kind = input.required<TmRouteSkeleton>();

}
