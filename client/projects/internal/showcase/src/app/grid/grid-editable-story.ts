// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import {
  Component,
  computed,
  ElementRef,
  inject,
  signal,
  viewChild,
  type Signal,
} from '@angular/core';
import { applyEach, form, min, required } from '@angular/forms/signals';

import type { TmCellEditor, TmLabelResolution } from '@tellma/core-ui/contracts';
import { TM_CELL_EDITOR_HOST } from '@tellma/core-ui';
import { TmGrid, TmGridColumn, TmGridEditorDef } from '@tellma/core-ui/grid';

import { mulberry32 } from './seeded-random';

interface Category {
  readonly value: string;
  readonly label: string;
}

const CATEGORIES: readonly Category[] = [
  { value: 'goods', label: 'Goods' },
  { value: 'services', label: 'Services' },
  { value: 'freight', label: 'Freight' },
  { value: 'other', label: 'Other' },
];

interface Agent {
  readonly id: number;
  readonly label: string;
}

/**
 * The mock agent directory the entity column resolves against. The label
 * 'Adam Brown' deliberately maps to TWO ids so the async resolver has an
 * `ambiguous` case to report.
 */
const AGENTS: readonly Agent[] = [
  { id: 11, label: 'Alice Green' },
  { id: 12, label: 'Bob Stone' },
  { id: 13, label: 'Carol White' },
  { id: 14, label: 'Adam Brown' },
  { id: 15, label: 'Adam Brown' },
  { id: 16, label: 'Dana Reed' },
];

const WORDS = ['consulting', 'hardware', 'freight', 'support', 'license', 'training'] as const;

interface InvoiceLine {
  readonly id: number;
  readonly description: string | null;
  readonly quantity: number | null;
  readonly unitPrice: number | null;
  readonly discount: number | null;
  readonly isPosted: boolean;
  readonly category: string | null;
  readonly agentId: number | null;
}

const ROW_COUNT = 40;

/** Row i is a pure function of i, so every run (and assertion) sees the same data. */
function makeSeedLine(i: number): InvoiceLine {
  const random = mulberry32(0x51f0a1 ^ i);
  const pick = <T>(values: readonly T[]): T => values[Math.floor(random() * values.length)];
  return {
    id: i + 1,
    description: `Line ${i} ${pick(WORDS)}`,
    quantity: 1 + Math.floor(random() * 20),
    unitPrice: Math.round(random() * 90000) / 100,
    discount: Math.round(random() * 1000) / 100,
    isPosted: i % 5 === 0,
    category: CATEGORIES[i % CATEGORIES.length].value,
    agentId: AGENTS[i % AGENTS.length].id,
  };
}

/**
 * A consumer editor for the `agentId` entity column: a native select over
 * the mock directory that implements `TmCellEditor<number | null>` and
 * registers itself through TM_CELL_EDITOR_HOST. Its `text` is `null`
 * (content not representable as text), so the grid commits the VALUE
 * channel — the picked agent id — directly, no column `parse` involved.
 */
@Component({
  selector: 'app-demo-agent-editor',
  template: `
    <select
      #select
      class="agent-editor"
      aria-label="Agent"
      [value]="selectValue()"
      (change)="onChange($event)"
    >
      <option value=""></option>
      @for (agent of agents; track $index) {
        <option [value]="agent.id">{{ agent.label }} (#{{ agent.id }})</option>
      }
    </select>
  `,
  styles: `
    .agent-editor {
      inline-size: 100%;
      block-size: 100%;
      border: none;
      background: transparent;
      font: inherit;
      color: inherit;
    }
  `,
})
export class DemoAgentEditor implements TmCellEditor<number | null> {
  private readonly cellHost = inject(TM_CELL_EDITOR_HOST, { optional: true });
  private readonly select = viewChild.required<ElementRef<HTMLSelectElement>>('select');

  protected readonly agents = AGENTS;

  /** The picked agent id — the grid seeds and commits through this channel. */
  readonly value = signal<number | null>(null);
  /** `null`: a picked entity has no text representation — commit by value. */
  readonly text: Signal<string | null> = computed(() => null);
  /** The native select's string value. */
  protected readonly selectValue = computed(() => {
    const value = this.value();
    return value === null ? '' : `${value}`;
  });

  constructor() {
    this.cellHost?.register(this as TmCellEditor<unknown>);
  }

  /** Nothing pending to flush — the change handler writes the value live. */
  commit(): void {}

  /** The grid never reads the editor after cancel; nothing to restore. */
  cancel(): void {}

  /** Focuses the select so editing keys reach it and bubble to the grid. */
  focus(): void {
    this.select().nativeElement.focus();
  }

  /** Type-to-edit: jump to the first agent whose label starts with `text`. */
  seed(text: string): void {
    const query = text.trim().toLowerCase();
    if (query === '') {
      return;
    }
    const match = AGENTS.find((agent) => agent.label.toLowerCase().startsWith(query));
    if (match !== undefined) {
      this.value.set(match.id);
    }
  }

  protected onChange(event: Event): void {
    const raw = (event.target as HTMLSelectElement).value;
    this.value.set(raw === '' ? null : Number(raw));
  }
}

/**
 * Editable tm-grid demo host: an invoice-lines grid over a Signal Forms
 * field tree with consumer validators (`required`, `min`), a per-cell
 * readonly column, a boolean toggle column, a built-in enum editor, an
 * entity column with a consumer editor + async paste resolver, an accessor
 * column, and the new-row placeholder. The toolbar exposes the readonly
 * flip, the resolver delay/call-counter, `clearHistory()`, and a live JSON
 * dump of the model the Playwright battery asserts commits against.
 */
@Component({
  imports: [TmGrid, TmGridColumn, TmGridEditorDef, DemoAgentEditor],
  template: `
    <h2>Grid (editable)</h2>

    <div class="toolbar">
      <label>
        <input
          type="checkbox"
          data-testid="toggle-readonly"
          [checked]="readonly()"
          (change)="onReadonlyChange($event)"
        />
        Readonly
      </label>
      <label>
        Resolver delay (ms)
        <input
          type="number"
          min="0"
          data-testid="resolver-delay"
          [value]="resolverDelay()"
          (input)="onDelayChange($event)"
        />
      </label>
      <span>Resolver calls: <span data-testid="resolver-calls">{{ resolverCalls() }}</span></span>
      <button type="button" data-testid="clear-history" (click)="clearHistory()">
        Clear history
      </button>
    </div>

    <tm-grid
      class="demo-grid"
      gridId="grid-editable"
      data-testid="grid-editable"
      contentKey="demo"
      [field]="f.lines"
      [rowId]="rowId"
      [newRow]="newLine"
      [readonly]="readonly()"
    >
      <tm-grid-column key="description" header="Description" [flex]="2" [minWidth]="160" />
      <tm-grid-column key="quantity" type="number" header="Qty" [width]="90" />
      <tm-grid-column key="unitPrice" type="number" header="Unit price" [width]="110" />
      <tm-grid-column
        key="discount"
        type="number"
        header="Discount"
        [readonly]="discountReadonly"
        [width]="100"
      />
      <tm-grid-column key="isPosted" type="boolean" header="Posted" [width]="90" />
      <tm-grid-column
        key="category"
        type="enum"
        header="Category"
        [options]="categories"
        [optionLabel]="categoryLabel"
        [optionValue]="categoryValue"
        [width]="120"
      />
      <tm-grid-column
        key="agentId"
        type="entity"
        header="Agent"
        [format]="agentLabel"
        [resolvePastedLabels]="resolveAgents"
        [width]="150"
      >
        <app-demo-agent-editor *tmGridEditor />
      </tm-grid-column>
      <tm-grid-column type="number" header="Total" [value]="total" [width]="110" />
    </tm-grid>

    <h3>Model</h3>
    <pre class="model-dump" data-testid="model-json">{{ modelJson() }}</pre>
  `,
  styles: `
    .toolbar {
      display: flex;
      flex-wrap: wrap;
      gap: 16px;
      align-items: center;
      margin-block-end: 12px;
    }
    .toolbar input[type='number'] {
      inline-size: 80px;
    }
    .demo-grid {
      display: block;
      block-size: 480px;
    }
    .model-dump {
      max-block-size: 160px;
      overflow: auto;
      font-size: 11px;
      background: var(--surface-raised, transparent);
      padding: 8px;
    }
  `,
})
export class GridEditableStory {
  readonly model = signal({ lines: Array.from({ length: ROW_COUNT }, (_, i) => makeSeedLine(i)) });
  readonly f = form(this.model, (p) => {
    applyEach(p.lines, (line) => {
      required(line.description);
      required(line.quantity);
      min(line.quantity, 1);
    });
  });

  readonly readonly = signal(false);
  readonly resolverDelay = signal(150);
  readonly resolverCalls = signal(0);

  private readonly grid = viewChild.required(TmGrid);
  private nextTempId = -1;

  /** The live model dump the e2e battery parses (one JSON line). */
  readonly modelJson = computed(() => JSON.stringify(this.model().lines));

  readonly rowId = (line: InvoiceLine): number => line.id;
  /** New rows mint negative client-side temp ids. */
  readonly newLine = (): InvoiceLine => ({
    id: this.nextTempId--,
    description: null,
    quantity: null,
    unitPrice: null,
    discount: null,
    isPosted: false,
    category: null,
    agentId: null,
  });

  /** Posted lines lock their discount (per-cell readonly, §6.1). */
  readonly discountReadonly = (line: InvoiceLine): boolean => line.isPosted;
  readonly total = (line: InvoiceLine): number =>
    Math.round((line.quantity ?? 0) * (line.unitPrice ?? 0) * 100) / 100;

  readonly categories = CATEGORIES;
  readonly categoryLabel = (category: Category): string => category.label;
  readonly categoryValue = (category: Category): string => category.value;

  /** id → directory label (the entity column's text representation). */
  readonly agentLabel = (value: number | null): string =>
    AGENTS.find((agent) => agent.id === value)?.label ?? '';

  /**
   * The async label→value resolver (§9.4 mock): resolves against the
   * directory after the configurable delay; unknown labels come back
   * `notFound`, 'Adam Brown' (two ids) comes back `ambiguous`.
   */
  readonly resolveAgents = async (
    labels: string[],
  ): Promise<ReadonlyMap<string, TmLabelResolution<number | null>>> => {
    this.resolverCalls.update((count) => count + 1);
    await new Promise((resolve) => setTimeout(resolve, this.resolverDelay()));
    const map = new Map<string, TmLabelResolution<number | null>>();
    for (const label of labels) {
      const matches = AGENTS.filter(
        (agent) => agent.label.toLowerCase() === label.trim().toLowerCase(),
      );
      if (matches.length === 1) {
        map.set(label, { value: matches[0].id });
      } else if (matches.length > 1) {
        map.set(label, { error: 'ambiguous' });
      } else {
        map.set(label, { error: 'notFound' });
      }
    }
    return map;
  };

  onReadonlyChange(event: Event): void {
    this.readonly.set((event.target as HTMLInputElement).checked);
  }

  onDelayChange(event: Event): void {
    const value = Number((event.target as HTMLInputElement).value);
    this.resolverDelay.set(Number.isFinite(value) && value >= 0 ? value : 0);
  }

  clearHistory(): void {
    this.grid().clearHistory();
  }
}
