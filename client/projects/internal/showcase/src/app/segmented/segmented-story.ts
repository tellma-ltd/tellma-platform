// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import { Component, signal } from '@angular/core';
import { form, FormField } from '@angular/forms/signals';

import { TmSegment, TmSegmented } from '@tellma/core-ui/segmented';

/**
 * tm-segmented demo host: a bound text toggle, an icon toggle whose labels
 * drop below a container breakpoint (in a container whose width the page
 * switches), icons only, a disabled segment, the disabled and readonly
 * states, and the three sizes.
 */
@Component({
  imports: [FormField, TmSegment, TmSegmented],
  template: `
    <h2>Segmented</h2>

    <div class="stack">
      <!-- Bound: text segments. -->
      <tm-segmented ariaLabel="Period" [formField]="f.period" data-testid="seg-period">
        <tm-segment value="day" label="Day" />
        <tm-segment value="week" label="Week" />
        <tm-segment value="month" label="Month" />
        <tm-segment value="year" label="Year" disabled />
      </tm-segmented>

      <!-- Labels while the container is at least md (768 px) wide. -->
      <div class="row">
        <button type="button" data-testid="container-wide" (click)="narrow.set(false)">
          Wide container
        </button>
        <button type="button" data-testid="container-narrow" (click)="narrow.set(true)">
          Narrow container
        </button>
      </div>
      <div class="container" [class.container--narrow]="narrow()" data-testid="seg-container">
        <tm-segmented ariaLabel="View" labels="md" [(value)]="view" data-testid="seg-view">
          <tm-segment value="grid" label="Grid" icon="table-2" />
          <tm-segment value="tiles" label="Tiles" icon="layout-grid" />
          <tm-segment value="list" label="List" icon="list" />
        </tm-segmented>
      </div>

      <!-- Icons only, at every width. -->
      <tm-segmented ariaLabel="Layout" labels="never" value="grid" data-testid="seg-icons">
        <tm-segment value="grid" label="Grid" icon="table-2" />
        <tm-segment value="tiles" label="Tiles" icon="layout-grid" />
      </tm-segmented>

      <tm-segmented ariaLabel="Disabled" disabled value="b" data-testid="seg-disabled">
        <tm-segment value="a" label="Disabled" />
        <tm-segment value="b" label="Checked" />
      </tm-segmented>

      <tm-segmented ariaLabel="Readonly" readonly value="b" data-testid="seg-readonly">
        <tm-segment value="a" label="Readonly" />
        <tm-segment value="b" label="Checked" />
      </tm-segmented>

      <!-- The size ladder: the field heights. -->
      @for (size of sizes; track size) {
        <tm-segmented
          [ariaLabel]="'Size ' + size"
          [size]="size"
          value="one"
          [attr.data-testid]="'seg-size-' + size"
        >
          <tm-segment value="one" [label]="'Size ' + size" />
          <tm-segment value="two" label="Other" />
        </tm-segmented>
      }
    </div>

    <output data-testid="model-json">{{ modelJson() }}</output>
  `,
  styles: `
    .stack {
      display: grid;
      justify-items: start;
      gap: 16px;
    }
    .row {
      display: flex;
      gap: 8px;
    }
    .container {
      container-type: inline-size;
      inline-size: 800px;
      max-inline-size: 100%;
      padding: 8px;
      border: 1px dashed var(--border-default);
    }
    .container--narrow {
      inline-size: 360px;
    }
    output {
      display: block;
      margin-block-start: 16px;
      font-size: 12px;
      color: var(--text-secondary);
    }
  `,
})
export class SegmentedStory {
  /** The three steps of the size ladder. */
  protected readonly sizes = ['sm', 'md', 'lg'] as const;
  /** Whether the breakpoint container is narrower than md. */
  protected readonly narrow = signal(false);
  /** The view toggle's two-way value. */
  protected readonly view = signal<string | null>('grid');

  /** The bound period. */
  protected readonly model = signal<{ period: string | null }>({ period: 'week' });
  /** The form over the model. */
  protected readonly f = form(this.model);

  /** The bound model and the view value, for the battery. */
  protected modelJson(): string {
    return JSON.stringify({ ...this.model(), view: this.view() });
  }
}
