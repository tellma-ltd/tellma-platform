// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import { Component, inject } from '@angular/core';

import { TM_NOW } from '@tellma/core-ui';
import { TmRelativeTime } from '@tellma/core-ui/relative-time';

/** One minute, in milliseconds. */
const MINUTE = 60_000;

/**
 * tm-relative-time for instants a few seconds, minutes, hours and days
 * before the page opened, in each wording. The instants are fixed when the
 * page opens, so the texts age as the minutes pass.
 */
@Component({
  imports: [TmRelativeTime],
  template: `
    <h2>Relative time</h2>

    <!-- The ages, short wording (the default). -->
    <ul data-testid="ages">
      @for (age of ages; track age.id) {
        <li>
          {{ age.caption }}:
          <tm-relative-time [value]="age.value" [attr.data-testid]="age.id" />
        </li>
      }
    </ul>

    <!-- The wordings side by side. -->
    <p data-testid="styles">
      <tm-relative-time [value]="ages[1].value" textStyle="long" data-testid="long" /> ·
      <tm-relative-time [value]="ages[1].value" textStyle="short" data-testid="short" /> ·
      <tm-relative-time [value]="ages[1].value" textStyle="narrow" data-testid="narrow" />
    </p>
  `,
})
export class RelativeTimeStory {
  /** The clock reading the page opened at. */
  private readonly opened = inject(TM_NOW)();

  /** The instants the story shows, by how long before the opening. */
  protected readonly ages: ReadonlyArray<{
    readonly id: string;
    readonly caption: string;
    readonly value: string;
  }> = [
    { id: 'seconds', caption: 'Seconds ago', value: this.before(20_000) },
    { id: 'minutes', caption: 'Two minutes ago', value: this.before(2 * MINUTE) },
    { id: 'hours', caption: 'Three hours ago', value: this.before(3 * 60 * MINUTE) },
    { id: 'days', caption: 'Two days ago', value: this.before(2 * 24 * 60 * MINUTE) },
    { id: 'weeks', caption: 'Three weeks ago', value: this.before(21 * 24 * 60 * MINUTE) },
  ];

  /** The ISO instant `ms` milliseconds before the opening. */
  private before(ms: number): string {
    return new Date(this.opened - ms).toISOString();
  }
}
