// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import { Component } from '@angular/core';

import { TmDescriptionList } from '@tellma/core-ui/description-list';

/** The facts every list shows. */
const FACTS: ReadonlyArray<{ readonly label: string; readonly value: string }> = [
  { label: 'Code', value: 'JED-01' },
  { label: 'Manager', value: 'Sara Ali' },
  { label: 'Opened', value: '2024-03-01' },
  { label: 'Currency', value: 'SAR' },
  { label: 'Reference', value: 'c0ffee00-1234-4bcd-9a7e-5f00d2a1b3c4' },
];

/**
 * dl[tmDescriptionList] stacked in one and two columns, in `auto` columns
 * inside a wide and a narrow container, and inline.
 */
@Component({
  imports: [TmDescriptionList],
  template: `
    <h2>Description list</h2>

    <!-- One column, stacked (the default). -->
    <h3>One column</h3>
    <dl tmDescriptionList data-testid="dl-one">
      @for (fact of facts; track fact.label) {
        <div>
          <dt>{{ fact.label }}</dt>
          <dd>{{ fact.value }}</dd>
        </div>
      }
    </dl>

    <!-- Two columns. -->
    <h3>Two columns</h3>
    <dl tmDescriptionList [columns]="2" data-testid="dl-two">
      @for (fact of facts; track fact.label) {
        <div>
          <dt>{{ fact.label }}</dt>
          <dd>{{ fact.value }}</dd>
        </div>
      }
    </dl>

    <!-- auto: two columns from 576 px, one below. -->
    <h3>Auto, wide and narrow</h3>
    <div class="wide">
      <dl tmDescriptionList columns="auto" data-testid="dl-auto-wide">
        @for (fact of facts; track fact.label) {
          <div>
            <dt>{{ fact.label }}</dt>
            <dd>{{ fact.value }}</dd>
          </div>
        }
      </dl>
    </div>
    <div class="narrow">
      <dl tmDescriptionList columns="auto" data-testid="dl-auto-narrow">
        @for (fact of facts; track fact.label) {
          <div>
            <dt>{{ fact.label }}</dt>
            <dd>{{ fact.value }}</dd>
          </div>
        }
      </dl>
    </div>

    <!-- Inline: label and value on one line. -->
    <h3>Inline</h3>
    <dl tmDescriptionList layout="inline" data-testid="dl-inline">
      @for (fact of facts; track fact.label) {
        <div>
          <dt>{{ fact.label }}</dt>
          <dd>{{ fact.value }}</dd>
        </div>
      }
    </dl>
  `,
  styles: `
    .wide {
      inline-size: 640px;
      max-inline-size: 100%;
    }
    .narrow {
      inline-size: 360px;
      max-inline-size: 100%;
    }
    h3 {
      margin-block: 16px 8px;
    }
  `,
})
export class DescriptionListStory {
  /** The facts every list shows. */
  protected readonly facts = FACTS;
}
