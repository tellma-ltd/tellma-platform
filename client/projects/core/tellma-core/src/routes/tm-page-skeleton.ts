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
// geometry; the page specs pin zero shift against their own pages.

/** The skeleton of one page family, drawn while its page loads. */
@Component({
  selector: 'tm-page-skeleton',
  imports: [TmPageHeader, TmSkeleton],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    @switch (kind()) {
      @case ('home') {
        <!-- Home: two greeting lines over three tile frames, each a header and five rows. -->
        <div class="tm-page-skeleton__column">
          <tm-skeleton shape="line" [lines]="2" width="20rem" />
          <div class="tm-page-skeleton__tiles">
            @for (tile of tiles; track tile) {
              <div class="tm-page-skeleton__tile">
                <tm-skeleton shape="line" width="8rem" />
                <tm-skeleton shape="line" [lines]="5" />
              </div>
            }
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
        <div class="tm-page-skeleton__column">
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
    :host {
      display: block;
    }
    .tm-page-skeleton__body {
      margin-inline: var(--page-header-padding-x);
      max-inline-size: calc(100% - 2 * var(--page-header-padding-x));
    }
    .tm-page-skeleton__column {
      display: flex;
      flex-direction: column;
      gap: var(--space-4);
      max-inline-size: var(--tm-dashboard-max-inline, 1200px);
      margin-inline: auto;
      padding: var(--page-header-padding-y) var(--page-header-padding-x);
    }
    .tm-page-skeleton__tiles {
      display: grid;
      grid-template-columns: repeat(auto-fit, minmax(16rem, 1fr));
      gap: var(--space-4);
    }
    .tm-page-skeleton__tile {
      display: flex;
      flex-direction: column;
      gap: var(--space-3);
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

  /** The home page's three tile frames. */
  protected readonly tiles = [0, 1, 2];
}
