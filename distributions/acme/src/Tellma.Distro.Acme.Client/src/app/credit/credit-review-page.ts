// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import { ChangeDetectionStrategy, Component, inject } from '@angular/core';
import { TmPreferences, TmServerText, TmTitleStrategy, tmPreferenceKey } from '@tellma/core';
import { TmPageHeader } from '@tellma/core-ui/page-header';
import { TmSegment, TmSegmented } from '@tellma/core-ui/segmented';

// The credit-review page, `/{t}/credit-review` under Financials (spec 0024
// §13, spec 0027 §2): the work queue of the customers near or over their
// credit limit. Its threshold, the share of a customer's limit from which
// the customer is listed, is the user's own preference, remembered behind
// their writes (spec 0025 §9). The queue's rows read the credit-exposure
// report, which the coded reports add (spec 0033 §11); until then the page
// is its title and its threshold.

/** The threshold the page lists customers from, as a percentage of their credit limit. */
const THRESHOLD = tmPreferenceKey<number>('acme.creditReview.threshold', 'write-behind', 90);

/** The thresholds the page offers, in percent. */
const THRESHOLDS = [75, 90, 100] as const;

/** The credit-review page: its title, and the threshold its queue starts at. */
@Component({
  selector: 'acme-credit-review-page',
  imports: [TmPageHeader, TmSegment, TmSegmented],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <!-- The title, then the threshold in the band below it. -->
    <tm-page-header>
      <h1 tmPageTitle tabindex="-1">{{ title() }}</h1>
      <div tmPageHeaderBand>
        <tm-segmented [ariaLabel]="thresholdLabel()" [value]="threshold()" (valueChange)="choose($event)">
          @for (percent of thresholds; track percent) {
            <tm-segment [value]="percent" [label]="percent + '%'" />
          }
        </tm-segmented>
      </div>
    </tm-page-header>
  `,
})
export default class AcmeCreditReviewPage {
  /** The user's preferences in the routed tenant. */
  readonly #preferences = inject(TmPreferences);

  /** The page's title, as the route names it. */
  protected readonly title = inject(TmTitleStrategy).pageTitle;

  /** The threshold control's name. */
  protected readonly thresholdLabel = inject(TmServerText).message('Acme_CreditReview_Threshold');

  /** The threshold the user last chose. */
  protected readonly threshold = this.#preferences.get(THRESHOLD);

  /** The thresholds offered. */
  protected readonly thresholds = THRESHOLDS;

  /**
   * Remembers a threshold the user chose.
   *
   * @param percent - The threshold, or `null` when nothing is selected.
   */
  protected choose(percent: number | null): void {
    if (percent !== null) {
      this.#preferences.set(THRESHOLD, percent);
    }
  }
}
