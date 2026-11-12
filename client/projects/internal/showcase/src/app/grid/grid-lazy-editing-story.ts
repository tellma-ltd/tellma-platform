// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import { Component, computed, signal } from '@angular/core';
import { form } from '@angular/forms/signals';

import {
  TmGrid,
  TmGridColumn,
  ɵTM_GRID_EDITING_LOAD_TIMEOUT,
  ɵTM_GRID_EDITING_LOADER,
} from '@tellma/core-ui/grid';

declare global {
  interface Window {
    /**
     * Lets the grid on the lazy-editing story receive its editing code. Until
     * it (or the failure hook) is called, the grid's request for that code
     * stays open, so a browser test can type into the window in which the
     * code is on its way.
     */
    tmShowcaseReleaseGridEditing?: () => void;
    /**
     * Fails the lazy-editing story grid's request for its editing code, as
     * a dropped network would. The grid asks again on the next gesture.
     */
    tmShowcaseFailGridEditing?: () => void;
  }
}

/** One line of the demo. */
interface Line {
  /** The line's identity. */
  readonly id: number;
  /** Free text. */
  readonly description: string | null;
  /** A number. */
  readonly quantity: number | null;
  /** An agent's id. */
  readonly agentId: number | null;
}

/** An agent the entity column searches. */
interface Agent {
  /** The agent's id. */
  readonly id: number;
  /** The agent's name. */
  readonly name: string;
}

/** The agents; two share a name. */
const AGENTS: readonly Agent[] = [
  { id: 1, name: 'Alice Green' },
  { id: 2, name: 'Bob Stone' },
  { id: 3, name: 'Dana Reed' },
  { id: 4, name: 'Dana Reed' },
];

/**
 * An editable grid whose editing code is held back until the page calls
 * `window.tmShowcaseReleaseGridEditing()` (or fails it with
 * `window.tmShowcaseFailGridEditing()`): the window a slow or dropped
 * network opens between a grid becoming editable and its editing code
 * arriving. Typing in that window goes into the cell's draft input, which
 * commits on the user's own gestures or becomes the editor's text when the
 * code arrives. The agent column searches but has no resolver, and its
 * search answers every commit.
 */
@Component({
  imports: [TmGrid, TmGridColumn],
  providers: [
    {
      provide: ɵTM_GRID_EDITING_LOADER,
      // Each request waits for the page's own release or failure. The hooks
      // are one-shot, so a test can tell a new request by their return.
      useValue: () =>
        new Promise<void>((resolve, reject) => {
          const settle = (fail: boolean): void => {
            delete window.tmShowcaseReleaseGridEditing;
            delete window.tmShowcaseFailGridEditing;
            if (fail) {
              reject(new Error('Editing code unreachable.'));
            } else {
              resolve();
            }
          };
          window.tmShowcaseReleaseGridEditing = () => settle(false);
          window.tmShowcaseFailGridEditing = () => settle(true);
        }).then(() => import('@tellma/core-ui/grid-editing')),
    },
    // Long enough that no test outruns it before releasing the code.
    { provide: ɵTM_GRID_EDITING_LOAD_TIMEOUT, useValue: 10 * 60_000 },
  ],
  template: `
    <h2>Grid (editing code on its way)</h2>
    <p>The editing code arrives only when the test releases it.</p>

    <tm-grid
      class="demo-grid"
      gridId="grid-lazy-editing"
      [field]="f"
      [rowId]="rowId"
    >
      <tm-grid-column key="description" header="Description" [flex]="2" [minWidth]="160" />
      <tm-grid-column key="quantity" type="number" header="Qty" [width]="90" />
      <tm-grid-column
        key="agentId"
        type="entity"
        header="Agent"
        [width]="160"
        [search]="searchAgents"
        [itemId]="agentId"
        [itemLabel]="agentName"
        [format]="formatAgent"
      />
    </tm-grid>

    <button type="button" data-testid="outside-button">Outside</button>
    <p>Searches: <span data-testid="search-calls">{{ searchCalls() }}</span></p>

    <h3>Model</h3>
    <!-- tabindex: the dump scrolls, so keyboard users must be able to reach it. -->
    <pre class="model-dump" data-testid="model-json" tabindex="0" aria-label="Model JSON">{{
      modelJson()
    }}</pre>
  `,
  styles: `
    .demo-grid {
      display: block;
      block-size: 240px;
    }
    .model-dump {
      max-block-size: 200px;
      overflow: auto;
    }
  `,
})
export class GridLazyEditingStory {
  /** The lines' model. */
  readonly model = signal<Line[]>([
    { id: 1, description: 'Consulting', quantity: 2, agentId: 1 },
    { id: 2, description: 'Hardware', quantity: 5, agentId: null },
    { id: 3, description: 'Support', quantity: 1, agentId: null },
  ]);
  /** The field tree the grid edits. */
  readonly f = form(this.model);
  /** Reads a line's identity. */
  readonly rowId = (line: Line): number => line.id;
  /** How many searches the agent column ran. */
  readonly searchCalls = signal(0);
  /** The agent column's search: the agents whose name contains the query. */
  readonly searchAgents = (query: string): readonly Agent[] => {
    this.searchCalls.update((count) => count + 1);
    const needle = query.toLowerCase();
    return AGENTS.filter((agent) => agent.name.toLowerCase().includes(needle));
  };
  /** An agent's id. */
  readonly agentId = (agent: Agent): number => agent.id;
  /** An agent's name. */
  readonly agentName = (agent: Agent): string => agent.name;
  /** Displays an agent id as the agent's name. */
  readonly formatAgent = (id: unknown): string =>
    AGENTS.find((agent) => agent.id === id)?.name ?? '';
  /** The model as JSON, read by the browser tests. */
  readonly modelJson = computed(() => JSON.stringify(this.model(), null, 2));
}
