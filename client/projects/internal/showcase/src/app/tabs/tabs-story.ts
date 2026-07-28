// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import { Component, signal } from '@angular/core';

import { TmTab, TmTabContent, TmTabGroup, TmTabLabel } from '@tellma/core-ui/tabs';

/**
 * tm-tab-group demo host — active-only DOM, `preserveContent` state
 * survival, explicit selection, a disabled tab, rich labels, and a
 * 20-tab overflow strip. Drives the Playwright battery.
 */
@Component({
  imports: [TmTabGroup, TmTab, TmTabContent, TmTabLabel],
  template: `
    <h2>Tabs</h2>

    <section>
      <h3>Basic (follow selection)</h3>
      <tm-tab-group [(selectedId)]="selected" data-testid="basic-group">
        <tm-tab id="details" label="Details">
          <ng-template tmTabContent>
            <p data-testid="panel-details">Record details panel.</p>
          </ng-template>
        </tm-tab>
        <tm-tab id="lines" label="Lines" preserveContent>
          <ng-template tmTabContent>
            <label>
              Scratch note (survives switching):
              <input data-testid="preserved-input" />
            </label>
          </ng-template>
        </tm-tab>
        <tm-tab id="audit" label="Audit" disabled>
          <ng-template tmTabContent><p>Audit trail.</p></ng-template>
        </tm-tab>
        <tm-tab id="stats">
          <ng-template tmTabLabel>Stats <strong data-testid="rich-label">(7)</strong></ng-template>
          <ng-template tmTabContent><p data-testid="panel-stats">Stats panel.</p></ng-template>
        </tm-tab>
      </tm-tab-group>
      <output data-testid="selected-id">{{ selected() }}</output>
    </section>

    <section>
      <h3>Explicit selection</h3>
      <tm-tab-group selectionMode="explicit" data-testid="explicit-group">
        <tm-tab id="a" label="Alpha">
          <ng-template tmTabContent><p data-testid="panel-a">Alpha panel.</p></ng-template>
        </tm-tab>
        <tm-tab id="b" label="Beta">
          <ng-template tmTabContent><p data-testid="panel-b">Beta panel.</p></ng-template>
        </tm-tab>
        <tm-tab id="c" label="Gamma">
          <ng-template tmTabContent><p data-testid="panel-c">Gamma panel.</p></ng-template>
        </tm-tab>
      </tm-tab-group>
    </section>

    <section>
      <h3>Overflow strip</h3>
      <div class="narrow">
        <tm-tab-group data-testid="overflow-group">
          @for (n of many; track n) {
            <tm-tab [id]="'t' + n" [label]="'Tab ' + n">
              <ng-template tmTabContent><p>Panel {{ n }}.</p></ng-template>
            </tm-tab>
          }
        </tm-tab-group>
      </div>
    </section>
  `,
  styles: `
    section {
      margin-block-end: 24px;
    }
    .narrow {
      max-inline-size: 320px;
    }
  `,
})
export class TabsStory {
  readonly selected = signal<string | undefined>(undefined);
  readonly many = Array.from({ length: 20 }, (_, i) => i + 1);
}
