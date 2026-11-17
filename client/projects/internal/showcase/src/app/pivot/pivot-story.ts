// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import { Component, signal, viewChild } from '@angular/core';

import { TmButton } from '@tellma/core-ui/button';
import {
  TmPivot,
  type TmPivotCell,
  type TmPivotMember,
  type TmPivotModel,
  type TmPivotWidths,
} from '@tellma/core-ui/pivot';

/** A member of the story's axes. */
function member(id: number, parent: number | null, label: string, attributes?: string[]): TmPivotMember {
  return { id, parent, kind: 'member', label, attributes };
}

/**
 * Centers: Riyadh (with Olaya, whose own facts show as its phantom, and
 * Malaz), Jeddah (with Corniche) and Dammam, a leaf; the total last. Each
 * carries its code as an attribute.
 */
const ROWS: readonly TmPivotMember[] = [
  { id: 0, parent: null, kind: 'total', label: 'Total', attributes: [''] },
  member(1, 0, 'Riyadh', ['RUH']),
  member(11, 1, 'Olaya', ['RUH-OL']),
  { id: 111, parent: 11, kind: 'phantom', label: 'Olaya', attributes: ['RUH-OL'] },
  member(112, 11, 'Olaya Towers', ['RUH-OT']),
  member(113, 11, 'King Fahd Road', ['RUH-KF']),
  member(12, 1, 'Malaz', ['RUH-MZ']),
  member(2, 0, 'Jeddah', ['JED']),
  member(21, 2, 'Corniche', ['JED-CO']),
  member(22, 2, 'Al Balad', ['JED-AB']),
  member(3, 0, 'Dammam', ['DMM']),
  { id: 4, parent: 0, kind: 'none', label: '(None)', attributes: [''] },
];

/** Periods: two quarters of three months each, the total last. */
const COLUMNS: readonly TmPivotMember[] = [
  { id: 0, parent: null, kind: 'total', label: 'Total' },
  member(1, 0, 'Q1'),
  member(11, 1, 'Jan'),
  member(12, 1, 'Feb'),
  member(13, 1, 'Mar'),
  member(2, 0, 'Q2'),
  member(21, 2, 'Apr'),
  member(22, 2, 'May'),
  member(23, 2, 'Jun'),
];

/** The deepest row members: under "leaves only", only their cells drill. */
const ROW_LEAVES = new Set([111, 112, 113, 12, 21, 22, 3, 4]);
/** The deepest column members. */
const COLUMN_LEAVES = new Set([11, 12, 13, 21, 22, 23]);

/** Two-decimal amounts with grouping, as a report formats them. */
const FORMAT = new Intl.NumberFormat('en', { minimumFractionDigits: 2, maximumFractionDigits: 2 });

/** A deterministic amount for a cell; the "(None)" row has no facts in Q2. */
function amount(row: number, column: number, measure: number): number | null {
  if (row === 4 && (column === 2 || column >= 21)) {
    return null;
  }
  const seed = ((row + 7) * 7919 + (column + 3) * 104729 + measure * 15485863) % 1000003;
  return (seed / 10) * (measure === 1 ? -0.4 : 1);
}

/**
 * tm-pivot over sales by center and period: two measures, a code
 * attribute, a phantom and a "(None)" row; the leaf cells drill, the
 * others refuse. Expand all and Collapse all drive both axes, and the
 * last activation shows below.
 */
@Component({
  imports: [TmPivot, TmButton],
  template: `
    <h2>Pivot (tm-pivot)</h2>

    <!-- The axis controls. -->
    <div class="demo-actions">
      <button tmButton variant="secondary" data-testid="pivot-expand-all" (click)="expandAll()">
        Expand all
      </button>
      <button tmButton variant="secondary" data-testid="pivot-collapse-all" (click)="collapseAll()">
        Collapse all
      </button>
    </div>

    <tm-pivot
      class="demo-pivot"
      data-testid="pivot"
      ariaLabel="Sales by center"
      [model]="model"
      [(expandedRows)]="expandedRows"
      [(expandedColumns)]="expandedColumns"
      [(widths)]="widths"
      (cellActivate)="last.set($event)"
    />

    <!-- The last drill, for the browser tests to read. -->
    <p>
      Last activation:
      <output data-testid="pivot-activation">{{ describe(last()) }}</output>
    </p>
  `,
  styles: `
    .demo-actions {
      display: flex;
      gap: 8px;
      margin-block-end: 12px;
    }

    .demo-pivot {
      block-size: 420px;
    }
  `,
})
export class PivotStory {
  /** The pivot, for the axis controls. */
  private readonly pivot = viewChild.required(TmPivot);

  /** The story's model: two measures over centers and periods. */
  readonly model: TmPivotModel = {
    rows: ROWS,
    columns: COLUMNS,
    measures: ['Revenue', 'Discount'],
    rowLabel: 'Center',
    columnLevelLabels: ['Quarter', 'Month'],
    attributeLabels: ['Code'],
    text: (row, column, measure) => {
      const value = amount(row, column, measure);
      return value === null ? null : FORMAT.format(value);
    },
    widest: (measure) => FORMAT.format(measure === 1 ? -40000.01 : 100000.29),
    activatable: (row, column) => ROW_LEAVES.has(row) && COLUMN_LEAVES.has(column),
  };

  /** The expanded centers: Riyadh, to start. */
  readonly expandedRows = signal<ReadonlySet<number>>(new Set([1]));
  /** The expanded periods: Q1, to start. */
  readonly expandedColumns = signal<ReadonlySet<number>>(new Set([1]));

  /** The user's column widths. */
  readonly widths = signal<TmPivotWidths>({});

  /** The last activated cell. */
  readonly last = signal<TmPivotCell | null>(null);

  /** Expands both axes. */
  expandAll(): void {
    this.pivot().expandAll('rows');
    this.pivot().expandAll('columns');
  }

  /** Collapses both axes. */
  collapseAll(): void {
    this.pivot().collapseAll('rows');
    this.pivot().collapseAll('columns');
  }

  /** A cell as `row/column/measure`, with `new tab` when asked. */
  describe(cell: TmPivotCell | null): string {
    return cell === null ? 'none' : `${cell.row}/${cell.column}/${cell.measure}${cell.newTab ? ' new tab' : ''}`;
  }
}
