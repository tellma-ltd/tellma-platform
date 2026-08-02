// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import { Component, computed, inject, signal } from '@angular/core';
import { form, required, FormField } from '@angular/forms/signals';

import { TmButton } from '@tellma/core-ui/button';
import {
  TmEntityPicker,
  type TmEntityPick,
  type TmEntityPickerPageData,
  type TmEntitySearchFn,
  type TmEntitySearchResult,
} from '@tellma/core-ui/entity-picker';
import { TmFormField } from '@tellma/core-ui/form-field';
import { TM_MODAL_DATA, TmModal, TmModalFooter, TmModalRef } from '@tellma/core-ui/modal';

interface Agent {
  readonly id: number;
  readonly name: string;
}

/**
 * The demo directory: 'Adam Brown' appears twice so blur resolution has an
 * ambiguous case; 'Alice Green'/'Alan Grey' share a prefix so 'Al' is a
 * multi-match while 'Alice Gr' is unique.
 */
const DIRECTORY: Agent[] = [
  { id: 1, name: 'Adam Brown' },
  { id: 2, name: 'Adam Brown' },
  { id: 3, name: 'Alice Green' },
  { id: 4, name: 'Alan Grey' },
  { id: 5, name: 'Bob Stone' },
  { id: 6, name: 'Carol White' },
  { id: 7, name: 'Dana Reed' },
];

/** Browse results are capped here; a larger match set flags `hasMore`. */
const RESULT_CAP = 5;

let nextCreatedId = 100;

/** The advanced-search page: its own filter prefilled from the picker's query. */
@Component({
  imports: [TmButton, TmModalFooter],
  template: `
    <p>
      Query at launch: <output data-testid="page-query">{{ query }}</output>
    </p>
    <label>
      Filter
      <input data-testid="adv-filter" [value]="filter()" (input)="onFilter($event)" />
    </label>
    <ul class="adv-list">
      @for (agent of matches(); track agent.id) {
        <li>
          <button tmButton [attr.data-testid]="'adv-pick-' + agent.id" (click)="pick(agent)">
            {{ agent.name }} (#{{ agent.id }})
          </button>
        </li>
      }
    </ul>
    <div tmModalFooter>
      <button tmButton data-testid="adv-cancel" (click)="ref.close()">Cancel</button>
    </div>
  `,
  styles: `
    .adv-list {
      display: flex;
      flex-direction: column;
      gap: 4px;
      list-style: none;
      padding: 0;
    }
  `,
})
export class DemoAdvancedSearchPage {
  protected readonly ref = inject(TmModalRef) as TmModalRef<TmEntityPick<number, Agent> | null>;
  protected readonly query = (inject(TM_MODAL_DATA) as TmEntityPickerPageData<number>).query;
  protected readonly filter = signal(this.query);
  protected readonly matches = computed(() => {
    const q = this.filter().trim().toLowerCase();
    return q === '' ? DIRECTORY : DIRECTORY.filter((a) => a.name.toLowerCase().includes(q));
  });

  protected onFilter(event: Event): void {
    this.filter.set((event.target as HTMLInputElement).value);
  }

  protected pick(agent: Agent): void {
    this.ref.close({ id: agent.id, label: agent.name, item: agent });
  }
}

/** The create page: the new entity's name prefilled from the picker's query. */
@Component({
  imports: [TmButton, TmModalFooter],
  template: `
    <label>
      Name
      <input data-testid="create-name" [value]="name()" (input)="onName($event)" />
    </label>
    <div tmModalFooter>
      <button tmButton data-testid="create-save" (click)="save()">Create</button>
      <button tmButton data-testid="create-cancel" (click)="ref.close()">Cancel</button>
    </div>
  `,
})
export class DemoCreatePage {
  protected readonly ref = inject(TmModalRef) as TmModalRef<TmEntityPick<number, Agent> | null>;
  protected readonly name = signal(
    (inject(TM_MODAL_DATA) as TmEntityPickerPageData<number>).query,
  );

  protected onName(event: Event): void {
    this.name.set((event.target as HTMLInputElement).value);
  }

  protected save(): void {
    const agent: Agent = { id: nextCreatedId++, name: this.name() };
    DIRECTORY.push(agent);
    this.ref.close({ id: agent.id, label: agent.name, item: agent });
  }
}

/** The edit page: rename re-applies the id; Delete reports `close(null)`. */
@Component({
  imports: [TmButton, TmModalFooter],
  template: `
    <p>
      Editing id <output data-testid="edit-id">{{ id }}</output>
    </p>
    <label>
      Name
      <input data-testid="edit-name" [value]="name()" (input)="onName($event)" />
    </label>
    <div tmModalFooter>
      <button tmButton data-testid="edit-save" (click)="save()">Save</button>
      <button tmButton data-testid="edit-delete" (click)="remove()">Delete</button>
      <button tmButton data-testid="edit-cancel" (click)="ref.close()">Cancel</button>
    </div>
  `,
})
export class DemoEditPage {
  protected readonly ref = inject(TmModalRef) as TmModalRef<TmEntityPick<number, Agent> | null>;
  protected readonly id = (inject(TM_MODAL_DATA) as TmEntityPickerPageData<number>).id!;
  protected readonly name = signal(DIRECTORY.find((a) => a.id === this.id)?.name ?? '');

  protected onName(event: Event): void {
    this.name.set((event.target as HTMLInputElement).value);
  }

  protected save(): void {
    const index = DIRECTORY.findIndex((a) => a.id === this.id);
    if (index !== -1) {
      DIRECTORY[index] = { id: this.id, name: this.name() };
    }
    this.ref.close({ id: this.id, label: this.name() });
  }

  protected remove(): void {
    const index = DIRECTORY.findIndex((a) => a.id === this.id);
    if (index !== -1) {
      DIRECTORY.splice(index, 1);
    }
    this.ref.close(null);
  }
}

/** A trivial host page proving a picker works INSIDE a modal (stacking). */
@Component({
  imports: [TmEntityPicker, TmFormField],
  template: `
    <tm-form-field label="Supplier (in a modal)">
      <tm-entity-picker
        data-testid="picker-in-modal"
        [search]="search"
        [itemId]="agentId"
        [itemLabel]="agentName"
        [advancedSearch]="advancedPage"
      />
    </tm-form-field>
  `,
})
export class DemoPickerModalPage {
  protected readonly search = (query: string): readonly Agent[] => {
    const q = query.trim().toLowerCase();
    return q === '' ? DIRECTORY : DIRECTORY.filter((a) => a.name.toLowerCase().includes(q));
  };
  protected readonly agentId = (agent: Agent): number => agent.id;
  protected readonly agentName = (agent: Agent): string => agent.name;
  protected readonly advancedPage = DemoAdvancedSearchPage;
}

/**
 * Entity-picker demo host: a required field-wrapped picker inside a native
 * form (submit counting proves Enter-never-submits-while-open), a bare
 * standalone picker, a no-magnifier picker, and a prepopulated picker whose
 * label only `displayWith` knows. The toolbar switches sync/async search,
 * tunes the artificial latency, arms a one-shot failure, toggles
 * abort-signal honoring, and counts search calls; the model dump is the
 * Playwright battery's committed-value oracle.
 */
@Component({
  imports: [TmEntityPicker, TmFormField, TmButton, FormField],
  template: `
    <h2>Entity picker</h2>

    <div class="toolbar">
      <label>
        <input
          type="checkbox"
          data-testid="search-async"
          [checked]="asyncMode()"
          (change)="onAsyncChange($event)"
        />
        Async search
      </label>
      <label>
        Delay (ms)
        <input
          type="number"
          min="0"
          data-testid="search-delay"
          [value]="searchDelay()"
          (input)="onDelayChange($event)"
        />
      </label>
      <label>
        Debounce (ms)
        <input
          type="number"
          min="0"
          data-testid="search-debounce"
          [value]="searchDebounce()"
          (input)="onDebounceChange($event)"
        />
      </label>
      <span>Search calls: <span data-testid="search-calls">{{ searchCalls() }}</span></span>
      <label>
        <input
          type="checkbox"
          data-testid="toggle-fail"
          [checked]="failNext()"
          (change)="onFailChange($event)"
        />
        Fail next search
      </label>
      <label>
        <input
          type="checkbox"
          data-testid="toggle-ignore-abort"
          [checked]="ignoreAbort()"
          (change)="onIgnoreAbortChange($event)"
        />
        Ignore abort
      </label>
      <button type="button" tmButton data-testid="open-host-modal" (click)="openHostModal()">
        Open picker in a modal
      </button>
    </div>

    <form class="grid" (submit)="onSubmit($event)">
      <tm-form-field label="Supplier" hint="Type to search the directory" data-testid="ff">
        <tm-entity-picker
          data-testid="picker-field"
          [formField]="f.supplierId"
          [search]="search"
          [itemId]="agentId"
          [itemLabel]="agentName"
          [displayWith]="agentDisplay"
          [advancedSearch]="advancedPage"
          [create]="createPage"
          [edit]="editPage"
          [searchDebounce]="searchDebounce()"
          createLabel="Create supplier…"
          placeholder="Search suppliers"
        />
      </tm-form-field>
      <button type="submit" tmButton data-testid="submit">
        Submit (<span data-testid="submit-count">{{ submitCount() }}</span>)
      </button>
    </form>

    <div class="grid">
      <tm-form-field label="No magnifier (no advanced search)">
        <tm-entity-picker
          data-testid="picker-plain"
          [search]="search"
          [itemId]="agentId"
          [itemLabel]="agentName"
        />
      </tm-form-field>

      <tm-form-field label="Prepopulated (displayWith resolves the label)">
        <tm-entity-picker
          data-testid="picker-populated"
          [(value)]="populatedId"
          [search]="search"
          [itemId]="agentId"
          [itemLabel]="agentName"
          [displayWith]="agentDisplay"
          [advancedSearch]="advancedPage"
          [edit]="editPage"
        />
      </tm-form-field>

      <tm-entity-picker
        data-testid="picker-bare"
        aria-label="Bare supplier picker"
        [search]="search"
        [itemId]="agentId"
        [itemLabel]="agentName"
      />

      <!-- An uncapped, longer directory: the panel overflows its max height,
           so arrow navigation has somewhere to scroll to. -->
      <tm-form-field label="Long list (scrolls)">
        <tm-entity-picker
          data-testid="picker-long"
          [search]="longSearch"
          [itemId]="agentId"
          [itemLabel]="agentName"
        />
      </tm-form-field>
    </div>

    <h3>Model</h3>
    <output class="model-dump" data-testid="model-json">{{ modelJson() }}</output>

    <!-- Pinned near the viewport bottom: the panel must open UPWARD from the
         first paint and stay there as results replace the spinner. -->
    <div class="flip-anchor">
      <tm-entity-picker
        data-testid="picker-flip"
        aria-label="Flip demo"
        [search]="search"
        [itemId]="agentId"
        [itemLabel]="agentName"
      />
    </div>
  `,
  styles: `
    .toolbar {
      display: flex;
      flex-wrap: wrap;
      gap: 16px;
      align-items: center;
      margin-block-end: 16px;
    }
    .toolbar input[type='number'] {
      inline-size: 80px;
    }
    .grid {
      display: flex;
      flex-direction: column;
      align-items: start;
      gap: 16px;
      max-inline-size: 340px;
      margin-block-end: 24px;
    }
    .grid tm-form-field {
      inline-size: 100%;
    }
    .model-dump {
      display: block;
      font-size: 12px;
      font-family: var(--font-mono, monospace);
    }
    .flip-anchor {
      position: fixed;
      inset-block-end: 8px;
      inset-inline-start: 24px;
      inline-size: 260px;
    }
  `,
})
export class EntityPickerStory {
  private readonly modal = inject(TmModal);

  readonly model = signal<{ supplierId: number | null }>({ supplierId: null });
  readonly f = form(this.model, (p) => {
    required(p.supplierId);
  });
  readonly populatedId = signal<number | null>(5);
  readonly submitCount = signal(0);

  readonly asyncMode = signal(true);
  readonly searchDelay = signal(200);
  /** The picker's coalescing window, exposed so the burst behavior is drivable. */
  readonly searchDebounce = signal(50);
  readonly searchCalls = signal(0);
  readonly failNext = signal(false);
  readonly ignoreAbort = signal(false);

  readonly modelJson = computed(() =>
    JSON.stringify({ supplierId: this.model().supplierId, populatedId: this.populatedId() }),
  );

  readonly advancedPage = DemoAdvancedSearchPage;
  readonly createPage = DemoCreatePage;
  readonly editPage = { component: DemoEditPage, size: 'sm' as const, title: 'Edit supplier' };

  readonly agentId = (agent: Agent): number => agent.id;
  readonly agentName = (agent: Agent): string => agent.name;
  readonly agentDisplay = (id: number): string | null =>
    DIRECTORY.find((agent) => agent.id === id)?.name ?? null;

  /**
   * The demo search: sync mode returns instantly (the zero-flicker fast
   * path); async mode resolves after the configured delay, honoring the
   * abort signal unless the ignore toggle simulates a misbehaving consumer
   * (the picker's discard-on-arrival covers that). Browse results above the
   * cap come back truncated with `hasMore`.
   */
  readonly search: TmEntitySearchFn<Agent> = (query, signal) => {
    this.searchCalls.update((count) => count + 1);
    const compute = (): TmEntitySearchResult<Agent> => {
      if (this.failNext()) {
        this.failNext.set(false);
        throw new Error('demo: search failure');
      }
      const q = query.trim().toLowerCase();
      const matches =
        q === '' ? DIRECTORY : DIRECTORY.filter((a) => a.name.toLowerCase().includes(q));
      return matches.length > RESULT_CAP
        ? { items: matches.slice(0, RESULT_CAP), hasMore: true }
        : [...matches];
    };
    if (!this.asyncMode()) {
      return compute();
    }
    return new Promise<TmEntitySearchResult<Agent>>((resolve, reject) => {
      const timer = setTimeout(() => {
        try {
          resolve(compute());
        } catch (error) {
          reject(error as Error);
        }
      }, this.searchDelay());
      if (!this.ignoreAbort()) {
        signal.addEventListener('abort', () => {
          clearTimeout(timer);
          reject(new Error('aborted'));
        });
      }
    });
  };

  /** A 24-entry directory returned uncapped — enough rows to scroll. */
  private readonly longDirectory: readonly Agent[] = Array.from({ length: 24 }, (_, i) => ({
    id: 1000 + i,
    name: `Supplier ${String(i + 1).padStart(2, '0')}`,
  }));
  readonly longSearch = (query: string): readonly Agent[] => {
    const q = query.trim().toLowerCase();
    return q === ''
      ? this.longDirectory
      : this.longDirectory.filter((a) => a.name.toLowerCase().includes(q));
  };

  protected onSubmit(event: Event): void {
    event.preventDefault();
    this.submitCount.update((count) => count + 1);
  }

  protected onAsyncChange(event: Event): void {
    this.asyncMode.set((event.target as HTMLInputElement).checked);
  }

  protected onDelayChange(event: Event): void {
    const value = Number((event.target as HTMLInputElement).value);
    this.searchDelay.set(Number.isFinite(value) && value >= 0 ? value : 0);
  }

  protected onDebounceChange(event: Event): void {
    const value = Number((event.target as HTMLInputElement).value);
    this.searchDebounce.set(Number.isFinite(value) && value >= 0 ? value : 0);
  }

  protected onFailChange(event: Event): void {
    this.failNext.set((event.target as HTMLInputElement).checked);
  }

  protected onIgnoreAbortChange(event: Event): void {
    this.ignoreAbort.set((event.target as HTMLInputElement).checked);
  }

  protected openHostModal(): void {
    this.modal.open(DemoPickerModalPage, { title: 'Pick inside a modal', size: 'md' });
  }
}
