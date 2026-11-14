// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import { Component, signal } from '@angular/core';

import type { TmDragPayload } from '@tellma/core-ui/drag-drop';
import { TmReorderEmpty, TmReorderItem, TmReorderList } from '@tellma/core-ui/reorder-list';

/** A column of the story's lists. */
export interface StoryColumn {
  /** The column's key. */
  readonly key: string;
  /** The column's name. */
  readonly label: string;
}

/** The story's first columns. */
const COLUMNS: readonly StoryColumn[] = ['Name', 'Code', 'Date', 'Amount', 'Status'].map(
  (label) => ({ key: label.toLowerCase(), label }),
);

/**
 * tm-reorder-list in every state its section names: a list reordered by
 * pointer and keyboard with each move reported, a custom row template,
 * the sizes, and an empty list showing its empty state.
 */
@Component({
  imports: [TmReorderEmpty, TmReorderItem, TmReorderList],
  template: `
    <h2>Reorder list</h2>

    <!-- The default rows: drag a row, or use a handle's keys. -->
    <h3>Shown columns</h3>
    <tm-reorder-list
      data-testid="reorder-columns"
      [(items)]="columns"
      [itemKey]="keyOf"
      [itemLabel]="labelOf"
      ariaLabel="Shown columns"
      (moved)="lastMove.set($event.item.label + ' ' + ($event.from + 1) + '→' + ($event.to + 1))"
    />
    <p data-testid="reorder-order">Order: {{ orderOf(columns()) }}</p>
    <p data-testid="reorder-last-move">Last move: {{ lastMove() ?? 'none' }}</p>

    <!-- A custom row with the position, at the large size. -->
    <h3>Numbered, large</h3>
    <tm-reorder-list
      data-testid="reorder-numbered"
      [(items)]="numbered"
      [itemKey]="keyOf"
      [itemLabel]="labelOf"
      ariaLabel="Numbered columns"
      size="lg"
    >
      <ng-template tmReorderItem [tmReorderItemOf]="numbered()" let-column let-index="index">
        <span class="position">{{ index + 1 }}</span> {{ column.label }}
      </ng-template>
    </tm-reorder-list>

    <!-- An empty list: its empty state is the drop target. -->
    <h3>Empty</h3>
    <tm-reorder-list
      data-testid="reorder-empty"
      [(items)]="empty"
      [itemKey]="keyOf"
      [itemLabel]="labelOf"
      [accepts]="['tm.path']"
      ariaLabel="Sort order"
      size="sm"
      (received)="receive($event.payload, $event.index)"
    >
      <ng-template tmReorderEmpty>No sort columns yet.</ng-template>
    </tm-reorder-list>
  `,
  styles: `
    tm-reorder-list {
      max-inline-size: 320px;
    }
    .position {
      display: inline-block;
      min-inline-size: 2ch;
      font-variant-numeric: tabular-nums;
      color: var(--text-secondary);
    }
  `,
})
export class ReorderListStory {
  /** The first list's columns. */
  protected readonly columns = signal<readonly StoryColumn[]>(COLUMNS);

  /** The numbered list's columns. */
  protected readonly numbered = signal<readonly StoryColumn[]>(COLUMNS.slice(0, 3));

  /** The empty list's columns. */
  protected readonly empty = signal<readonly StoryColumn[]>([]);

  /** The first list's last move, as text. */
  protected readonly lastMove = signal<string | null>(null);

  /** A column's key. */
  protected readonly keyOf = (column: StoryColumn): string => column.key;

  /** A column's label. */
  protected readonly labelOf = (column: StoryColumn): string => column.label;

  /** The labels, in order. */
  protected orderOf(columns: readonly StoryColumn[]): string {
    return columns.map((column) => column.label).join(', ');
  }

  /** Inserts a dropped field into the empty list. */
  protected receive(payload: TmDragPayload, index: number): void {
    const column = { key: payload.text, label: payload.label };
    const next = [...this.empty()];
    next.splice(index, 0, column);
    this.empty.set(next);
  }
}
