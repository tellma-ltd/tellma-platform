// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import { Component, signal } from '@angular/core';

import { TmBadge } from '@tellma/core-ui/badge';
import { TmButton } from '@tellma/core-ui/button';
import { TmChip, TmChipSet } from '@tellma/core-ui/chip';
import { TmPageHeader } from '@tellma/core-ui/page-header';

/** The report parameters the band shows. */
const PARAMETERS: readonly string[] = [
  'Period: January 2026',
  'Center: Jeddah sales',
  'Currency: SAR',
  'Account type: Revenue',
  'Posted only',
  'Including sub-centers',
];

/**
 * tm-page-header on a details page (every slot filled, with a switch that
 * hides the first item of the end slot), on a report page whose band of
 * parameters wraps in a narrow container, and with the title alone.
 */
@Component({
  imports: [TmBadge, TmButton, TmChip, TmChipSet, TmPageHeader],
  template: `
    <h2>Page header</h2>

    <!-- Every slot filled. -->
    <section class="frame" data-testid="details-frame">
      <tm-page-header data-testid="header-details">
        <a tmPageBack href="#centers">Centers</a>
        <h1 tmPageTitle>Jeddah sales and distribution center</h1>
        <tm-badge tmPageTitleExtra tone="success">Active</tm-badge>
        @if (showShare()) {
          <button tmButton tmPageHeaderEnd variant="ghost" type="button" data-testid="end-share">
            Share
          </button>
        }
        <button tmButton tmPageHeaderEnd variant="ghost" type="button" data-testid="end-more">
          More
        </button>
        <button tmButton tmPageToolbar variant="primary" type="button">Edit</button>
        <button tmButton tmPageToolbar type="button">Duplicate</button>
        <button tmButton tmPageToolbar type="button">Delete</button>
        <button tmButton tmPageToolbarEnd variant="ghost" type="button">Previous</button>
        <button tmButton tmPageToolbarEnd variant="ghost" type="button">Next</button>
      </tm-page-header>
    </section>
    <button tmButton type="button" data-testid="toggle-share" (click)="showShare.set(!showShare())">
      {{ showShare() ? 'Hide Share' : 'Show Share' }}
    </button>

    <!-- A report header whose band wraps in a narrow container. -->
    <section class="frame frame--narrow" data-testid="report-frame">
      <tm-page-header data-testid="header-report">
        <h1 tmPageTitle>Trial balance</h1>
        <button tmButton tmPageToolbar variant="primary" type="button">Run</button>
        <div tmPageHeaderBand data-testid="report-band">
          <tm-chip-set ariaLabel="Parameters">
            @for (parameter of parameters; track parameter) {
              <tm-chip [label]="parameter" />
            }
          </tm-chip-set>
        </div>
      </tm-page-header>
    </section>

    <!-- The title alone: no toolbar row, no band. -->
    <section class="frame">
      <tm-page-header data-testid="header-plain">
        <h1 tmPageTitle>Settings</h1>
      </tm-page-header>
    </section>
  `,
  styles: `
    .frame {
      margin-block-end: 16px;
      border: 1px dashed var(--border-default);
    }
    .frame--narrow {
      max-inline-size: 420px;
    }
  `,
})
export class PageHeaderStory {
  /** The report parameters. */
  protected readonly parameters = PARAMETERS;

  /** Whether the end slot's first item shows. */
  protected readonly showShare = signal(true);
}
