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
} from '@angular/core';
import { form } from '@angular/forms/signals';

import type { TmCellEditor } from '@tellma/core-ui/contracts';
import { TM_CELL_EDITOR_HOST } from '@tellma/core-ui';
import { TmGrid, TmGridColumn, TmGridEditorDef } from '@tellma/core-ui/grid';

interface Priority {
  readonly value: string;
  readonly label: string;
}

const PRIORITIES: readonly Priority[] = [
  { value: 'low', label: 'Low' },
  { value: 'medium', label: 'Medium' },
  { value: 'high', label: 'High' },
  { value: 'urgent', label: 'Urgent' },
];

interface Review {
  readonly id: number;
  readonly product: string | null;
  readonly rating: number | null;
  readonly priority: string | null;
}

function makeSeedReviews(): Review[] {
  return Array.from({ length: 10 }, (_, i) => ({
    id: i + 1,
    product: `Product ${i}`,
    rating: (i % 5) + 1,
    priority: PRIORITIES[i % PRIORITIES.length].value,
  }));
}

/**
 * The DoD-14 proof: a CONSUMER control implementing `TmCellEditor<number>`
 * — five star buttons plus a free-text input — that injects
 * TM_CELL_EDITOR_HOST optionally and registers itself on construction, so
 * the grid discovers it through the same self-registration path every
 * tm-* control uses. Its `text` view feeds the number column's built-in
 * parse on commit (star presses and typed digits both round-trip; typed
 * garbage becomes an invalid input, §10).
 */
@Component({
  selector: 'app-demo-rating-editor',
  template: `
    <div class="rating-editor">
      @for (star of stars; track $index) {
        <button
          type="button"
          class="rating-editor__star"
          [class.rating-editor__star--on]="(value() ?? 0) >= star"
          [attr.aria-label]="'Rate ' + star"
          (click)="setRating(star)"
        >
          ★
        </button>
      }
      <input
        #ratingInput
        class="rating-editor__input"
        aria-label="Rating"
        [value]="text()"
        (input)="onInput($event)"
      />
    </div>
  `,
  styles: `
    .rating-editor {
      display: flex;
      align-items: center;
      gap: 2px;
      inline-size: 100%;
      block-size: 100%;
    }
    .rating-editor__star {
      border: none;
      background: transparent;
      padding: 0;
      cursor: pointer;
      color: var(--text-secondary, currentColor);
      font-size: 14px;
      line-height: 1;
    }
    .rating-editor__star--on {
      color: var(--text-primary, currentColor);
    }
    .rating-editor__input {
      inline-size: 40px;
      min-inline-size: 0;
      border: none;
      background: transparent;
      font: inherit;
      color: inherit;
    }
  `,
})
export class DemoRatingEditor implements TmCellEditor<number | null> {
  private readonly cellHost = inject(TM_CELL_EDITOR_HOST, { optional: true });
  private readonly input = viewChild.required<ElementRef<HTMLInputElement>>('ratingInput');

  protected readonly stars = [1, 2, 3, 4, 5] as const;

  /** The rating — the grid seeds this channel with the cell value at open. */
  readonly value = signal<number | null>(null);
  /** Raw text typed into the input; `null` while the stars drive the value. */
  private readonly rawText = signal<string | null>(null);
  /** The committed-text view the grid parses through the number column. */
  readonly text = computed(() => {
    const raw = this.rawText();
    if (raw !== null) {
      return raw;
    }
    const value = this.value();
    return value === null ? '' : String(value);
  });

  constructor() {
    this.cellHost?.register(this as TmCellEditor<unknown>);
  }

  /** Nothing pending to flush — stars and input write live. */
  commit(): void {}

  /** The grid never reads the editor after cancel; nothing to restore. */
  cancel(): void {}

  /** Focuses the text input, caret at the end. */
  focus(): void {
    const element = this.input().nativeElement;
    element.focus();
    element.setSelectionRange(element.value.length, element.value.length);
  }

  /** Type-to-edit seed: replaces the content with `text`, caret at the end. */
  seed(text: string): void {
    this.rawText.set(text);
    const parsed = Number(text);
    this.value.set(Number.isFinite(parsed) && text.trim() !== '' ? parsed : null);
    const element = this.input().nativeElement;
    element.value = text;
    element.setSelectionRange(text.length, text.length);
  }

  protected setRating(star: number): void {
    this.rawText.set(String(star));
    this.value.set(star);
  }

  protected onInput(event: Event): void {
    const raw = (event.target as HTMLInputElement).value;
    this.rawText.set(raw);
    const parsed = Number(raw);
    this.value.set(Number.isFinite(parsed) && raw.trim() !== '' ? parsed : null);
  }
}

/**
 * Custom-editor demo host: the `rating` number column edits through the
 * consumer `app-demo-rating-editor` above (registered via `*tmGridEditor`),
 * while the `priority` enum column keeps the BUILT-IN tm-select editor for
 * contrast — both discovered through the same TM_CELL_EDITOR_HOST path.
 */
@Component({
  imports: [TmGrid, TmGridColumn, TmGridEditorDef, DemoRatingEditor],
  template: `
    <h2>Grid (custom editor)</h2>

    <tm-grid
      class="demo-grid"
      gridId="grid-custom-editor"
      data-testid="grid-custom-editor"
      [field]="f"
      [rowId]="rowId"
    >
      <tm-grid-column key="product" header="Product" [flex]="1" [minWidth]="140" />
      <tm-grid-column key="rating" type="number" header="Rating" [width]="130">
        <app-demo-rating-editor *tmGridEditor />
      </tm-grid-column>
      <tm-grid-column
        key="priority"
        type="enum"
        header="Priority"
        [options]="priorities"
        [optionLabel]="priorityLabel"
        [optionValue]="priorityValue"
        [width]="120"
      />
    </tm-grid>

    <h3>Model</h3>
    <pre class="model-dump" data-testid="model-json">{{ modelJson() }}</pre>
  `,
  styles: `
    .demo-grid {
      display: block;
      block-size: 400px;
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
export class GridCustomEditorStory {
  readonly model = signal<Review[]>(makeSeedReviews());
  readonly f = form(this.model);

  readonly rowId = (review: Review): number => review.id;

  readonly priorities = PRIORITIES;
  readonly priorityLabel = (priority: Priority): string => priority.label;
  readonly priorityValue = (priority: Priority): string => priority.value;

  /** The live model dump the e2e battery parses (one JSON line). */
  readonly modelJson = computed(() => JSON.stringify(this.model()));
}
