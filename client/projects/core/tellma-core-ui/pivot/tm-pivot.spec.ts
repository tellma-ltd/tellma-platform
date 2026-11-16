// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import { LiveAnnouncer } from '@angular/cdk/a11y';
import { Dir } from '@angular/cdk/bidi';
import type { ModifierKeys } from '@angular/cdk/testing';
import { TestbedHarnessEnvironment } from '@angular/cdk/testing/testbed';
import { Component, signal, viewChild } from '@angular/core';
import { TestBed, type ComponentFixture } from '@angular/core/testing';
import { TranslocoService } from '@jsverse/transloco';

import { provideTellmaUi, type TmControlSize } from '@tellma/core-ui';
import { tmIsApplePlatform } from '@tellma/core-ui/kbd';
import { tmEmitCss, tmTokensDefault } from '@tellma/core-ui-tokens';
import { TmPivotHarness } from '@tellma/core-ui-testing';

import { TmPivot } from './tm-pivot';
import type { TmPivotCell, TmPivotMember, TmPivotModel, TmPivotWidths } from './tm-pivot-types';

/** The platform's Mod key, as harness modifiers. */
const MOD: ModifierKeys = tmIsApplePlatform() ? { meta: true } : { control: true };

/** A member under a parent; the total is id 0. */
function member(id: number, parent: number | null, label: string, attributes?: string[]): TmPivotMember {
  return { id, parent, kind: 'member', label, attributes };
}

/**
 * Rows: Riyadh (1) with Olaya (11: its phantom 111 and Tower 112) and Malaz
 * (12); Jeddah (2); the total (0). One attribute, the code.
 */
const ROWS: readonly TmPivotMember[] = [
  { id: 0, parent: null, kind: 'total', label: 'Total', attributes: [''] },
  member(1, 0, 'Riyadh', ['R']),
  member(11, 1, 'Olaya', ['RO']),
  { id: 111, parent: 11, kind: 'phantom', label: 'Olaya', attributes: ['RO'] },
  member(112, 11, 'Tower', ['RT']),
  member(12, 1, 'Malaz', ['RM']),
  member(2, 0, 'Jeddah', ['J']),
];

/** Columns: Q1 (1) with Jan (11) and Feb (12); Q2 (2), a leaf; the total (0). */
const COLUMNS: readonly TmPivotMember[] = [
  { id: 0, parent: null, kind: 'total', label: 'Total' },
  member(1, 0, 'Q1'),
  member(11, 1, 'Jan'),
  member(12, 1, 'Feb'),
  member(2, 0, 'Q2'),
];

/** A model and the calls the pivot made into it. */
interface Cube {
  /** The model to render. */
  readonly model: TmPivotModel;
  /** Every `text` call's `row/column/measure`, in order. */
  readonly texts: string[];
  /** Every `widest` call's measure, in order. */
  readonly widest: number[];
}

/** Options for {@link cube}. */
interface CubeOptions {
  /** The row members; the default regions. */
  readonly rows?: readonly TmPivotMember[];
  /** The column members; the default quarters. */
  readonly columns?: readonly TmPivotMember[];
  /** The measure names; one "Amount" by default. */
  readonly measures?: readonly string[];
  /** The attribute headers; one "Code" by default. */
  readonly attributeLabels?: readonly string[];
  /** The column level labels; quarter and month by default. */
  readonly columnLevelLabels?: readonly string[];
  /** A cell's text; by default its `row/column/measure` key. */
  readonly text?: (row: number, column: number, measure: number) => string | null;
  /** A measure's widest value; "8,888.00" by default. */
  readonly widest?: (measure: number) => string | null;
  /** Which cells drill; every one when absent. */
  readonly activatable?: (row: number, column: number) => boolean;
}

/** A counting model over the given members. */
function cube(options: CubeOptions = {}): Cube {
  const texts: string[] = [];
  const widest: number[] = [];
  const text = options.text ?? ((row, column, measure) => `${row}/${column}/${measure}`);
  return {
    texts,
    widest,
    model: {
      rows: options.rows ?? ROWS,
      columns: options.columns ?? COLUMNS,
      measures: options.measures ?? ['Amount'],
      rowLabel: 'Region',
      columnLevelLabels: options.columnLevelLabels ?? ['Quarter', 'Month'],
      attributeLabels: options.attributeLabels ?? ['Code'],
      text: (row, column, measure) => {
        texts.push(`${row}/${column}/${measure}`);
        return text(row, column, measure);
      },
      widest: (measure) => {
        widest.push(measure);
        return options.widest?.(measure) ?? '8,888.00';
      },
      ...(options.activatable === undefined ? {} : { activatable: options.activatable }),
    },
  };
}

/** `count` first-level members with ids from `from`, without a total. */
function flat(count: number, prefix: string, from = 1): TmPivotMember[] {
  return Array.from({ length: count }, (_, i) => member(from + i, null, `${prefix} ${from + i}`));
}

/** Hosts the pivot inside a sized, directed box, with every input and model bound. */
@Component({
  imports: [Dir, TmPivot],
  template: `
    <div
      [dir]="dir()"
      [style.inline-size.px]="width()"
      [style.block-size.px]="height()"
      [style.display]="hidden() ? 'none' : null"
    >
      <tm-pivot
        ariaLabel="Sales"
        [model]="model()"
        [activatable]="activatable()"
        [size]="size()"
        [(expandedRows)]="expandedRows"
        [(expandedColumns)]="expandedColumns"
        [(widths)]="widths"
        (cellActivate)="activations.push($event)"
      />
    </div>
  `,
})
class Host {
  /** The pivot under test. */
  readonly pivot = viewChild.required(TmPivot);
  /** The reading direction around it. */
  readonly dir = signal<'ltr' | 'rtl'>('ltr');
  /** The host's inline size, px. */
  readonly width = signal(1000);
  /** The host's block size, px. */
  readonly height = signal(420);
  /** Whether the pivot sits inside `display: none`, as in an unselected tab panel. */
  readonly hidden = signal(false);
  /** The model rendered. */
  readonly model = signal<TmPivotModel>(cube().model);
  /** Whether value cells drill. */
  readonly activatable = signal(true);
  /** The row density. */
  readonly size = signal<TmControlSize | undefined>(undefined);
  /** The expanded row members, two-way. */
  readonly expandedRows = signal<ReadonlySet<number>>(new Set());
  /** The expanded column members, two-way. */
  readonly expandedColumns = signal<ReadonlySet<number>>(new Set());
  /** The user's widths, two-way. */
  readonly widths = signal<TmPivotWidths>({});
  /** Every activation emitted, in order. */
  readonly activations: TmPivotCell[] = [];
}

/** The unit page loads no token stylesheet; the geometry reads the tokens. */
let tokenSheet: HTMLStyleElement;

beforeAll(() => {
  tokenSheet = document.createElement('style');
  tokenSheet.textContent = tmEmitCss(tmTokensDefault);
  document.head.appendChild(tokenSheet);
});

afterAll(() => tokenSheet.remove());

/** Waits for a frame (resize observers report after layout), then for the render it causes. */
async function settle(fixture: ComponentFixture<Host>): Promise<void> {
  for (let pass = 0; pass < 2; pass++) {
    await new Promise((resolve) => requestAnimationFrame(() => setTimeout(resolve)));
    await fixture.whenStable();
  }
}

/** Renders the host, lets `configure` set it up first, and settles; returns the fixture, host, harness and elements. */
async function setup(configure: (host: Host) => void = () => undefined) {
  TestBed.configureTestingModule({ providers: [provideTellmaUi()] });
  const fixture = TestBed.createComponent(Host);
  configure(fixture.componentInstance);
  fixture.autoDetectChanges();
  await settle(fixture);
  const pivot = await TestbedHarnessEnvironment.loader(fixture).getHarness(TmPivotHarness);
  const element = (fixture.nativeElement as HTMLElement).querySelector<HTMLElement>('tm-pivot')!;
  const scroller = element.querySelector<HTMLElement>('.tm-pivot__scroller')!;
  const announce = vi.spyOn(TestBed.inject(LiveAnnouncer), 'announce');
  return { fixture, host: fixture.componentInstance, pivot, element, scroller, announce };
}

/** The rendered value cells' texts. */
function renderedTexts(element: HTMLElement): string[] {
  return [...element.querySelectorAll('td.tm-pivot__value')].map((cell) => cell.textContent!.trim());
}

/** Scrolls the body to logical offsets (`left` from the inline start), as a user's scroll would. */
function scrollTo(scroller: HTMLElement, offsets: { readonly top?: number; readonly left?: number }): void {
  if (offsets.top !== undefined) {
    scroller.scrollTop = offsets.top;
  }
  if (offsets.left !== undefined) {
    scroller.scrollLeft = getComputedStyle(scroller).direction === 'rtl' ? -offsets.left : offsets.left;
  }
  scroller.dispatchEvent(new Event('scroll'));
}

/** The element at a logical position. */
function cellAt(element: HTMLElement, row: number, column: number): HTMLElement {
  return element.querySelector<HTMLElement>(`[data-r="${row}"][data-c="${column}"]`)!;
}

describe('tm-pivot', () => {
  describe('the compact layout', () => {
    it('shows each parent’s row before its children and the total last', async () => {
      const { fixture, host, pivot } = await setup();
      expect(await pivot.getRowLabels()).toEqual(['Riyadh', 'Jeddah', 'Total']);
      host.expandedRows.set(new Set([1, 11]));
      await fixture.whenStable();
      expect(await pivot.getRowLabels()).toEqual([
        'Riyadh',
        'Olaya',
        'Olaya (direct)',
        'Tower',
        'Malaz',
        'Jeddah',
        'Total',
      ]);
      // A parent's row carries its subtotal: the cell is the member's own.
      expect(await (await pivot.getValueCell('Riyadh', 0)).getText()).toBe('1/1/0');
    });

    it('follows an expanded column’s children with its subtotal column, the total last', async () => {
      const { fixture, host, pivot } = await setup();
      expect(await pivot.getHeaderTexts()).toEqual([
        ['Quarter', 'Code', 'Q1', 'Q2', 'Total'],
        ['Region Month'],
      ]);
      host.expandedColumns.set(new Set([1]));
      await fixture.whenStable();
      expect(await pivot.getHeaderTexts()).toEqual([
        ['Quarter', 'Code', 'Q1', 'Q1 total', 'Q2', 'Total'],
        ['Region Month', 'Jan', 'Feb'],
      ]);
      expect(await pivot.getColumnCount()).toBe(2 + 5);
      const q1 = await pivot.getCell({ role: 'columnheader', text: 'Q1' });
      expect(await (await q1.host()).getAttribute('aria-colspan')).toBe('2');
      expect(await (await q1.host()).getAttribute('colspan')).toBe('2');
      // The subtotal and the total span down to the last header row.
      const subtotal = await pivot.getCell({ text: 'Q1 total' });
      expect(await (await subtotal.host()).getAttribute('rowspan')).toBe('2');
      expect(await (await pivot.getValueCell('Jeddah', 2)).getText()).toBe('2/1/0');
      expect(await (await pivot.getValueCell('Jeddah', 4)).getText()).toBe('2/0/0');
    });

    it('adds a row of measure names for two measures, each group holding both', async () => {
      const { fixture, host, pivot } = await setup((h) => h.model.set(cube({ measures: ['Debit', 'Credit'] }).model));
      host.expandedColumns.set(new Set([1]));
      await fixture.whenStable();
      const headers = await pivot.getHeaderTexts();
      expect(headers).toHaveLength(3);
      expect(headers[2]).toEqual(['Region', ...Array.from({ length: 5 }, () => ['Debit', 'Credit']).flat()]);
      expect(await pivot.getColumnCount()).toBe(2 + 10);
      expect(await (await pivot.getValueCell('Riyadh', 1)).getText()).toBe('1/11/1');
      const q1 = await pivot.getCell({ role: 'columnheader', text: 'Q1' });
      expect(await (await q1.host()).getAttribute('aria-colspan')).toBe('4');
    });

    it('leaves a cell without facts empty, never "0"', async () => {
      const { pivot } = await setup((h) =>
        h.model.set(cube({ text: (row, column) => (row === 2 && column === 2 ? null : '5') }).model),
      );
      expect(await (await pivot.getValueCell('Jeddah', 1)).getText()).toBe('');
      expect(await (await pivot.getValueCell('Jeddah', 0)).getText()).toBe('5');
    });

    it('isolates labels and figures, and reads numbers left to right', async () => {
      const { element } = await setup();
      const value = element.querySelector('td.tm-pivot__value .tm-pivot__text')!;
      expect(value.getAttribute('dir')).toBe('ltr');
      expect(getComputedStyle(value.parentElement!).fontVariantNumeric).toBe('tabular-nums');
      expect(element.querySelector('[role="rowheader"] bdi')!.textContent).toBe('Riyadh');
    });
  });

  describe('the looks', () => {
    /** The look of a body row's value cell. */
    async function look(pivot: TmPivotHarness, row: string, column: number) {
      return (await pivot.getValueCell(row, column)).getStyle();
    }

    it('draws each member’s row in its look, the phantom’s label italic with "(direct)"', async () => {
      const { pivot, element } = await setup((h) => h.expandedRows.set(new Set([1, 11])));
      const styles = await Promise.all(
        (await pivot.getCells({ role: 'rowheader' })).map((cell) => cell.getStyle()),
      );
      expect(styles).toEqual(['subtotal-1', 'subtotal-2', 'phantom', 'leaf', 'leaf', 'leaf', 'total']);
      const phantom = [...element.querySelectorAll<HTMLElement>('[role="rowheader"]')][2];
      expect(getComputedStyle(phantom.querySelector('.tm-pivot__label')!).fontStyle).toBe('italic');
      const resolved = (name: string) => getComputedStyle(element).getPropertyValue(name).trim();
      expect(getComputedStyle(phantom).color).toBe(colorOf(resolved('--text-secondary')));
    });

    it('gives a value cell the stronger of its row’s and its column’s look', async () => {
      const { pivot } = await setup((h) => {
        h.expandedRows.set(new Set([1, 11]));
        h.expandedColumns.set(new Set([1]));
      });
      // Data columns: Jan, Feb, Q1 total, Q2, Total.
      expect(await look(pivot, 'Tower', 0)).toBe('leaf');
      expect(await look(pivot, 'Tower', 2)).toBe('subtotal-1');
      expect(await look(pivot, 'Tower', 4)).toBe('total');
      expect(await look(pivot, 'Olaya (direct)', 0)).toBe('phantom');
      expect(await look(pivot, 'Olaya (direct)', 2)).toBe('subtotal-1');
      expect(await look(pivot, 'Olaya', 0)).toBe('subtotal-2');
      expect(await look(pivot, 'Olaya', 2)).toBe('subtotal-1');
      expect(await look(pivot, 'Riyadh', 0)).toBe('subtotal-1');
      expect(await look(pivot, 'Total', 0)).toBe('total');
    });

    it('paints the looks from the pivot tokens: fills, weights and the total’s 2px rules', async () => {
      const { pivot, element } = await setup((h) => h.expandedRows.set(new Set([1, 11])));
      const resolved = (name: string) => getComputedStyle(element).getPropertyValue(name).trim();
      const cell = async (row: string, column: number) =>
        cellAt(element, await (await pivot.getValueCell(row, column)).getRow(), 2 + column);
      const total = getComputedStyle(await cell('Total', 0));
      expect(total.backgroundColor).toBe(colorOf(resolved('--surface-sunken')));
      expect(total.fontWeight).toBe('600');
      const rule = getComputedStyle(await cell('Total', 0), '::before');
      expect(rule.height).toBe('2px');
      expect(rule.backgroundColor).toBe(colorOf(resolved('--border-strong')));
      // The row's rule runs across its attribute cell too, unbroken.
      const totalRow = await (await pivot.getValueCell('Total', 0)).getRow();
      expect(getComputedStyle(cellAt(element, totalRow, 1), '::before').height).toBe('2px');
      // The total column's rule sits on its inline-start edge.
      const column = getComputedStyle(await cell('Tower', 2), '::after');
      expect(column.width).toBe('2px');
      // Q2 is a leaf column; a collapsed Q1 still shows its subtotal.
      const leaf = getComputedStyle(await cell('Tower', 1));
      expect(leaf.backgroundColor).toBe(colorOf(resolved('--surface-card')));
      expect(leaf.fontWeight).toBe('400');
      expect(leaf.color).toBe(colorOf(resolved('--text-body')));
      const subtotal = getComputedStyle(await cell('Riyadh', 0));
      expect(subtotal.fontWeight).toBe('600');
      expect(subtotal.color).toBe(colorOf(resolved('--text-strong')));
    });

    it('rows are 30px tall at the default size and follow the size ladder', async () => {
      const { fixture, host, element } = await setup();
      const row = () => element.querySelector('tbody tr[role="row"]')!.getBoundingClientRect().height;
      expect(row()).toBe(30);
      host.size.set('lg');
      await settle(fixture);
      expect(row()).toBe(38);
    });
  });

  describe('ARIA', () => {
    it('is a readonly treegrid with its row and column counts and one tab stop', async () => {
      const { fixture, host, pivot, element } = await setup();
      const table = element.querySelector('table')!;
      expect(table.getAttribute('role')).toBe('treegrid');
      expect(table.getAttribute('aria-readonly')).toBe('true');
      expect(table.getAttribute('aria-label')).toBe('Sales');
      expect(await pivot.getRowCount()).toBe(2 + 3);
      expect(await pivot.getColumnCount()).toBe(2 + 3);
      expect(element.querySelectorAll('[tabindex="0"]')).toHaveLength(1);
      expect(await (await pivot.getTabStop()).getText()).toBe('Riyadh');
      host.expandedRows.set(new Set([1]));
      await fixture.whenStable();
      expect(await pivot.getRowCount()).toBe(2 + 5);
    });

    it('marks body rows with their level, position, set size and expansion', async () => {
      const { element } = await setup((h) => h.expandedRows.set(new Set([1])));
      const rows = [...element.querySelectorAll('tbody tr[role="row"]')].map((row) => [
        row.getAttribute('aria-rowindex'),
        row.getAttribute('aria-level'),
        row.getAttribute('aria-posinset'),
        row.getAttribute('aria-setsize'),
        row.getAttribute('aria-expanded'),
      ]);
      expect(rows).toEqual([
        ['3', '1', '1', '3', 'true'],
        ['4', '2', '1', '2', 'false'],
        ['5', '2', '2', '2', null],
        ['6', '1', '2', '3', null],
        ['7', '1', '3', '3', null],
      ]);
      // Each body row starts with a rowheader; its cells carry their column.
      const first = element.querySelector('tbody tr[role="row"]')!;
      expect(first.children[0].getAttribute('role')).toBe('rowheader');
      expect([...first.children].map((cell) => cell.getAttribute('aria-colindex'))).toEqual(['1', '2', '3', '4', '5']);
    });

    it('marks column headers with their index, span and expansion', async () => {
      const { element } = await setup((h) => h.expandedColumns.set(new Set([1])));
      const headers = [...element.querySelectorAll('thead [role="columnheader"]')].map((cell) => [
        cell.textContent!.replace(/\s+/g, ' ').trim(),
        cell.getAttribute('aria-colindex'),
        cell.getAttribute('aria-colspan'),
        cell.getAttribute('aria-expanded'),
      ]);
      expect(headers).toEqual([
        ['Quarter', '1', null, null],
        ['Code', '2', null, null],
        ['Q1', '3', '2', 'true'],
        ['Q1 total', '5', null, null],
        ['Q2', '6', null, null],
        ['Total', '7', null, null],
        ['Region Month', '1', null, null],
        ['Jan', '3', null, null],
        ['Feb', '4', null, null],
      ]);
    });
  });

  describe('lazy text', () => {
    it('renders a 500,000-cell model calling text only for the cells it renders', async () => {
      const big = cube({ rows: flat(1000, 'Row'), columns: flat(500, 'Column'), columnLevelLabels: [] });
      const { fixture, pivot, element, scroller } = await setup((h) => h.model.set(big.model));
      expect(await pivot.getRowCount()).toBe(1 + 1000);
      expect(await pivot.getColumnCount()).toBe(2 + 500);
      // Every call is for a rendered cell, once; nothing else was asked.
      const rendered = renderedTexts(element);
      expect(rendered.length).toBeGreaterThan(0);
      expect(rendered.length).toBeLessThan(1000);
      expect(new Set(big.texts).size).toBe(big.texts.length);
      expect(new Set(big.texts)).toEqual(new Set(rendered));
      // A scroll asks only for the cells it brings into view.
      const before = new Set(big.texts);
      scrollTo(scroller, { top: 600 * 30, left: 200 * 112 });
      await settle(fixture);
      const now = renderedTexts(element);
      const asked = big.texts.slice(before.size);
      expect(asked.length).toBeGreaterThan(0);
      expect(asked.every((key) => now.includes(key) && !before.has(key))).toBe(true);
      expect(now.every((key) => big.texts.includes(key))).toBe(true);
      expect(big.texts.length).toBeLessThan(3000);
      // The widest value is asked once per measure per model.
      expect(big.widest).toEqual([0]);
    });

    it('renders 100,000 rows of five columns the same way', async () => {
      const tall = cube({ rows: flat(100_000, 'Row'), columns: flat(5, 'Column'), columnLevelLabels: [] });
      const { element } = await setup((h) => h.model.set(tall.model));
      const rendered = renderedTexts(element);
      expect(rendered.length).toBeLessThanOrEqual(5 * 30);
      expect(new Set(tall.texts)).toEqual(new Set(rendered));
      expect(tall.texts).toHaveLength(rendered.length);
    });

    it('reuses the texts of cells that stay rendered, and starts over for a new model', async () => {
      const first = cube();
      const { fixture, host, element } = await setup((h) => h.model.set(first.model));
      const count = first.texts.length;
      host.expandedRows.set(new Set([1]));
      await fixture.whenStable();
      // Only Olaya's and Malaz's rows are new: two rows of three cells.
      expect(first.texts.length).toBe(count + 6);
      const second = cube();
      host.model.set(second.model);
      await fixture.whenStable();
      expect(second.texts).toHaveLength(renderedTexts(element).length);
      expect(second.widest).toEqual([0]);
      expect(first.widest).toEqual([0]);
    });
  });

  describe('windowing', () => {
    it('renders the rows the viewport shows, gaps standing in for the rest', async () => {
      const { pivot, element } = await setup((h) =>
        h.model.set(cube({ rows: flat(1000, 'Row'), columns: flat(3, 'Column') }).model),
      );
      const labels = await pivot.getRowLabels();
      // 420px − two 30px header rows leaves twelve rows; four more of overscan.
      expect(labels[0]).toBe('Row 1');
      expect(labels.length).toBeGreaterThanOrEqual(12);
      expect(labels.length).toBeLessThanOrEqual(12 + 4 + 1);
      const gap = element.querySelector<HTMLElement>('tbody .tm-pivot__gap')!;
      expect(gap.getBoundingClientRect().height).toBe((1000 - labels.length) * 30);
    });

    it('keeps the active row rendered when it scrolls away', async () => {
      const { fixture, pivot, scroller } = await setup((h) =>
        h.model.set(cube({ rows: flat(1000, 'Row'), columns: flat(3, 'Column') }).model),
      );
      await pivot.press('ArrowDown');
      scrollTo(scroller, { top: 500 * 30 });
      await settle(fixture);
      const labels = await pivot.getRowLabels();
      expect(labels[0]).toBe('Row 2');
      expect(labels[1]).not.toBe('Row 3');
      expect(labels).toContain('Row 505');
    });

    it('renders every column up to 40, and windows past 40', async () => {
      const { fixture, host, element, pivot, scroller } = await setup((h) =>
        h.model.set(cube({ rows: flat(3, 'Row'), columns: flat(40, 'Column'), columnLevelLabels: [] }).model),
      );
      const firstRow = () => element.querySelectorAll('tbody tr[role="row"]:first-of-type td.tm-pivot__value');
      expect(firstRow()).toHaveLength(40);
      host.model.set(cube({ rows: flat(3, 'Row'), columns: flat(41, 'Column'), columnLevelLabels: [] }).model);
      await settle(fixture);
      const windowed = firstRow().length;
      expect(windowed).toBeLessThan(41);
      // A spacer column stands in for the rest; the header row adds up the same.
      expect(element.querySelectorAll('colgroup col').length).toBe(2 + windowed + 1);
      expect(await pivot.getColumnCount()).toBe(2 + 41);
      scrollTo(scroller, { left: 30 * 112 });
      await settle(fixture);
      const texts = renderedTexts(element);
      expect(texts).toContain('1/36/0');
      expect(texts).not.toContain('1/1/0');
    });
  });

  describe('the keyboard', () => {
    it('moves with the arrows, headers included, left and right following the direction', async () => {
      const { fixture, host, pivot } = await setup();
      await pivot.press('ArrowRight');
      expect(await (await pivot.getTabStop()).getText()).toBe('R');
      await pivot.press('ArrowRight');
      expect(await (await pivot.getTabStop()).getText()).toBe('1/1/0');
      expect(await (await pivot.getTabStop()).isFocused()).toBe(true);
      // Up reaches Q1, which spans both header rows; past the top it stays.
      await pivot.press('ArrowUp');
      expect(await (await pivot.getTabStop()).getText()).toBe('Q1');
      await pivot.press('ArrowUp');
      expect(await (await pivot.getTabStop()).getText()).toBe('Q1');
      await pivot.press('ArrowDown');
      expect(await (await pivot.getTabStop()).getText()).toBe('1/1/0');
      host.dir.set('rtl');
      await fixture.whenStable();
      await pivot.press('ArrowLeft');
      expect(await (await pivot.getTabStop()).getText()).toBe('1/2/0');
      await pivot.press('ArrowRight');
      expect(await (await pivot.getTabStop()).getText()).toBe('1/1/0');
    });

    it('goes to a row’s ends with Home and End, the corners with Mod, and pages', async () => {
      const { pivot } = await setup((h) => h.model.set(cube({ rows: flat(100, 'Row') }).model));
      await pivot.press('End');
      expect(await (await pivot.getTabStop()).getText()).toBe('1/0/0');
      await pivot.press('Home');
      expect(await (await pivot.getTabStop()).getText()).toBe('Row 1');
      await pivot.press('PageDown');
      expect(await (await pivot.getTabStop()).getText()).toBe('Row 13');
      await pivot.press('PageUp');
      expect(await (await pivot.getTabStop()).getText()).toBe('Row 1');
      await pivot.press('End', MOD);
      expect(await (await pivot.getTabStop()).getText()).toBe('100/0/0');
      await pivot.press('Home', MOD);
      expect(await (await pivot.getTabStop()).getText()).toBe('Quarter');
    });

    it('activates a value cell with Enter or a click; with Mod, in a new tab', async () => {
      const { host, pivot } = await setup();
      await pivot.press('ArrowRight');
      await pivot.press('ArrowRight');
      await pivot.press('Enter');
      await pivot.press('Enter', MOD);
      await (await pivot.getValueCell('Jeddah', 1)).click();
      await (await pivot.getValueCell('Jeddah', 2)).click(MOD);
      expect(host.activations).toEqual([
        { row: 1, column: 1, measure: 0, newTab: false },
        { row: 1, column: 1, measure: 0, newTab: true },
        { row: 2, column: 2, measure: 0, newTab: false },
        { row: 2, column: 0, measure: 0, newTab: true },
      ]);
    });

    it('activates nothing when activatable is off', async () => {
      const { pivot, host, element } = await setup((h) => h.activatable.set(false));
      expect(element.querySelector('.tm-pivot__value--link')).toBeNull();
      await (await pivot.getValueCell('Riyadh', 0)).click();
      await pivot.press('ArrowRight');
      await pivot.press('ArrowRight');
      await pivot.press('Enter');
      expect(host.activations).toEqual([]);
    });

    it('toggles an expandable header with Enter, Space, its twisty or its label, and announces it', async () => {
      const { fixture, host, pivot, announce } = await setup();
      await pivot.press('Enter');
      expect([...host.expandedRows()]).toEqual([1]);
      expect(announce).toHaveBeenLastCalledWith('Riyadh expanded, 2 rows', 'polite');
      await pivot.press('Space');
      expect([...host.expandedRows()]).toEqual([]);
      expect(announce).toHaveBeenLastCalledWith('Riyadh collapsed', 'polite');
      await (await pivot.getCell({ role: 'columnheader', text: 'Q1' })).toggle();
      expect([...host.expandedColumns()]).toEqual([1]);
      expect(announce).toHaveBeenLastCalledWith('Q1 expanded, 2 columns', 'polite');
      await fixture.whenStable();
      const label = fixture.nativeElement.querySelector('[aria-expanded="true"] .tm-pivot__label') as HTMLElement;
      label.click();
      await fixture.whenStable();
      expect([...host.expandedColumns()]).toEqual([]);
      expect(host.activations).toEqual([]);
    });

    it('expands and collapses a row with Alt+Right and Alt+Left from any of its cells', async () => {
      const { host, pivot } = await setup();
      await pivot.press('End');
      await pivot.press('ArrowRight', { alt: true });
      expect([...host.expandedRows()]).toEqual([1]);
      await pivot.press('ArrowRight', { alt: true });
      expect([...host.expandedRows()]).toEqual([1]);
      await pivot.press('ArrowLeft', { alt: true });
      expect([...host.expandedRows()]).toEqual([]);
      // Focus stays in the row it was in.
      expect(await (await pivot.getTabStop()).getText()).toBe('1/0/0');
    });

    it('expands and collapses a column with Alt+Down and Alt+Up from its header', async () => {
      const { host, pivot } = await setup();
      await pivot.press('ArrowRight');
      await pivot.press('ArrowRight');
      await pivot.press('ArrowUp');
      await pivot.press('ArrowUp');
      await pivot.press('ArrowDown', { alt: true });
      expect([...host.expandedColumns()]).toEqual([1]);
      // Focus follows the member to its group header.
      expect(await (await pivot.getTabStop()).getText()).toBe('Q1');
      await pivot.press('ArrowUp', { alt: true });
      expect([...host.expandedColumns()]).toEqual([]);
    });

    it('expands and collapses everything on an axis, and focuses its tab stop', async () => {
      const { fixture, host, pivot } = await setup();
      host.pivot().expandAll('rows');
      host.pivot().expandAll('columns');
      expect([...host.expandedRows()].sort()).toEqual([1, 11]);
      expect([...host.expandedColumns()]).toEqual([1]);
      host.pivot().collapseAll('rows');
      expect([...host.expandedRows()]).toEqual([]);
      host.pivot().focus();
      await fixture.whenStable();
      expect(await (await pivot.getTabStop()).isFocused()).toBe(true);
    });

    it('copies the focused cell’s full text with Mod+C', async () => {
      const { element, pivot } = await setup((h) =>
        h.model.set(cube({ text: () => '1,234,567,890.12 long enough to be cut' }).model),
      );
      await pivot.press('ArrowRight');
      await pivot.press('ArrowRight');
      const cell = cellAt(element, 2, 2);
      const event = new ClipboardEvent('copy', { bubbles: true, cancelable: true, clipboardData: new DataTransfer() });
      cell.dispatchEvent(event);
      expect(event.defaultPrevented).toBe(true);
      expect(event.clipboardData!.getData('text/plain')).toBe('1,234,567,890.12 long enough to be cut');
    });

    it('writes the focused cell’s text through the clipboard API when no copy event follows Mod+C', async () => {
      const write = vi.spyOn(navigator.clipboard, 'writeText').mockResolvedValue(undefined);
      try {
        const { element, pivot } = await setup();
        await pivot.press('ArrowRight');
        await pivot.press('ArrowRight');
        const text = cellAt(element, 2, 2).textContent!.trim();
        // A copy event that follows the keys takes the copy: nothing is written.
        await pivot.press('c', MOD);
        cellAt(element, 2, 2).dispatchEvent(
          new ClipboardEvent('copy', { bubbles: true, cancelable: true, clipboardData: new DataTransfer() }),
        );
        await new Promise((resolve) => setTimeout(resolve));
        expect(write).not.toHaveBeenCalled();
        // Without one, the fallback writes the cell's text.
        await pivot.press('c', MOD);
        await new Promise((resolve) => setTimeout(resolve));
        expect(write).toHaveBeenCalledExactlyOnceWith(text);
      } finally {
        write.mockRestore();
      }
    });

    it('leaves a text selection the user made inside the pivot to the browser on Mod+C', async () => {
      const write = vi.spyOn(navigator.clipboard, 'writeText').mockResolvedValue(undefined);
      try {
        const { element, pivot } = await setup();
        await pivot.press('ArrowRight');
        await pivot.press('ArrowRight');
        // The user drags across two value cells.
        const range = document.createRange();
        range.setStartBefore(cellAt(element, 2, 2).firstChild!);
        range.setEndAfter(cellAt(element, 2, 3).lastChild!);
        window.getSelection()!.removeAllRanges();
        window.getSelection()!.addRange(range);
        await pivot.press('c', MOD);
        expect(window.getSelection()!.isCollapsed).toBe(false);
        // No fallback is armed: nothing is written, whether or not a copy event follows.
        await new Promise((resolve) => setTimeout(resolve));
        expect(write).not.toHaveBeenCalled();
        const copy = new ClipboardEvent('copy', { bubbles: true, cancelable: true, clipboardData: new DataTransfer() });
        cellAt(element, 2, 2).dispatchEvent(copy);
        await new Promise((resolve) => setTimeout(resolve));
        expect(copy.defaultPrevented).toBe(false);
        expect(write).not.toHaveBeenCalled();
      } finally {
        window.getSelection()!.removeAllRanges();
        write.mockRestore();
      }
    });
  });

  describe('widths', () => {
    /** A cube too wide for its host, so each measure is max(112, fit). */
    function wide(widest: string, measures = ['Amount']): Cube {
      return cube({ rows: flat(3, 'Row'), columns: flat(20, 'Column'), measures, widest: () => widest });
    }

    it('fits a measure’s columns to its widest value, at 112px or more when scrolling', async () => {
      const short = wide('1');
      const { pivot } = await setup((h) => h.model.set(short.model));
      expect(await (await pivot.getValueCell('Row 1', 0)).getWidth()).toBe(112);
      TestBed.resetTestingModule();
      const long = wide('-1,234,567,890,123.45');
      const { pivot: other } = await setup((h) => h.model.set(long.model));
      const width = await (await other.getValueCell('Row 1', 0)).getWidth();
      expect(width).toBeGreaterThan(112);
      expect(await (await other.getValueCell('Row 2', 7)).getWidth()).toBe(width);
    });

    it('shares the slack among the measures when everything fits', async () => {
      const { element } = await setup((h) => h.model.set(cube({ widest: () => '1' }).model));
      const table = element.querySelector('table')!.getBoundingClientRect().width;
      const scroller = element.querySelector('.tm-pivot__scroller')!.clientWidth;
      // 1,000px: row header 280 + attribute 96; three columns share the rest.
      expect(scroller - table).toBeLessThan(3);
      expect(cellAt(element, 2, 2).getBoundingClientRect().width).toBe(Math.floor((scroller - 376) / 3));
    });

    it('only widens a measure for a later result, and never changes a user’s width', async () => {
      const { fixture, host, pivot } = await setup((h) => h.model.set(wide('-1,234,567,890,123.45').model));
      const width = await (await pivot.getValueCell('Row 1', 0)).getWidth();
      host.model.set(wide('1').model);
      await settle(fixture);
      expect(await (await pivot.getValueCell('Row 1', 0)).getWidth()).toBe(width);
      host.model.set(wide('-1,234,567,890,123,456,789.45').model);
      await settle(fixture);
      expect(await (await pivot.getValueCell('Row 1', 0)).getWidth()).toBeGreaterThan(width);
      host.widths.set({ measures: [150] });
      await settle(fixture);
      expect(await (await pivot.getValueCell('Row 1', 0)).getWidth()).toBe(150);
      host.model.set(wide('-1,234,567,890,123,456,789,000,000.45').model);
      await settle(fixture);
      expect(await (await pivot.getValueCell('Row 1', 0)).getWidth()).toBe(150);
    });

    it('measures the fits once a pivot first rendered hidden is shown', async () => {
      const long = '-1,234,567,890,123.45';
      const visible = await setup((h) => h.model.set(wide(long).model));
      const fitted = await (await visible.pivot.getValueCell('Row 1', 0)).getWidth();
      expect(fitted).toBeGreaterThan(112);
      TestBed.resetTestingModule();
      const { fixture, host, pivot } = await setup((h) => {
        h.hidden.set(true);
        h.model.set(wide(long).model);
      });
      host.hidden.set(false);
      await settle(fixture);
      expect(await (await pivot.getValueCell('Row 1', 0)).getWidth()).toBe(fitted);
    });

    it('keeps a measure’s width when a later result renames it with narrower values', async () => {
      const { fixture, host, pivot } = await setup((h) => h.model.set(wide('-1,234,567,890,123.45').model));
      const width = await (await pivot.getValueCell('Row 1', 0)).getWidth();
      // A language switch: the measure's label is translated, its widest value shorter.
      host.model.set(wide('1', ['المبلغ']).model);
      await settle(fixture);
      expect(await (await pivot.getValueCell('Row 1', 0)).getWidth()).toBe(width);
    });

    it('resizes every column of a measure by its handle, writing the user’s widths', async () => {
      const { fixture, host, pivot, element } = await setup((h) =>
        h.model.set(wide('1', ['Debit', 'Credit']).model),
      );
      const handle = element.querySelector<HTMLElement>('[data-tm-resize="m1"]')!;
      const x = handle.getBoundingClientRect().left;
      // Widths are whole pixels; the layout may snap a cell to a sub-pixel edge.
      const width = async (row: string, column: number) =>
        Math.round(await (await pivot.getValueCell(row, column)).getWidth());
      const pointer = (type: string, clientX: number) =>
        handle.dispatchEvent(new PointerEvent(type, { pointerId: 1, isPrimary: true, button: 0, clientX }));
      pointer('pointerdown', x);
      pointer('pointermove', x + 40);
      await fixture.whenStable();
      // The drag shows live, before the widths are written.
      expect(await width('Row 1', 3)).toBe(152);
      expect(host.widths()).toEqual({});
      pointer('pointerup', x + 40);
      await fixture.whenStable();
      expect(host.widths()).toEqual({ measures: [undefined, 152] });
      expect(await width('Row 1', 1)).toBe(152);
      expect(await width('Row 3', 9)).toBe(152);
      expect(await width('Row 1', 0)).toBe(112);
    });

    it('resizes in the reading direction, inside each column’s range', async () => {
      const { fixture, host, element } = await setup((h) => {
        h.dir.set('rtl');
        h.model.set(wide('1').model);
      });
      const handle = element.querySelector<HTMLElement>('[data-tm-resize="m0"]')!;
      const pointer = (type: string, clientX: number) =>
        handle.dispatchEvent(new PointerEvent(type, { pointerId: 1, isPrimary: true, button: 0, clientX }));
      pointer('pointerdown', 500);
      pointer('pointermove', 400);
      pointer('pointerup', 400);
      await fixture.whenStable();
      expect(host.widths()).toEqual({ measures: [212] });
      pointer('pointerdown', 500);
      pointer('pointermove', 2000);
      pointer('pointerup', 2000);
      await fixture.whenStable();
      expect(host.widths()).toEqual({ measures: [64] });
    });

    it('fits a column to its content on a double-click of its handle', async () => {
      const { fixture, host, pivot } = await setup((h) => h.model.set(wide('-1,234,567,890,123.45').model));
      const fitted = await (await pivot.getValueCell('Row 1', 0)).getWidth();
      host.widths.set({ measures: [64] });
      await settle(fixture);
      await (await pivot.getCell({ role: 'columnheader', text: 'Column 1' })).fitWidth();
      await fixture.whenStable();
      expect(host.widths()).toEqual({ measures: [fitted] });
      await (await pivot.getCell({ role: 'columnheader', text: /^Region/ })).fitWidth();
      await fixture.whenStable();
      expect(host.widths().rowHeader).toBeGreaterThanOrEqual(120);
      expect(host.widths().rowHeader).toBeLessThan(280);
    });

    it('stops a sticky column’s resize where the sticky block would leave the data 112px', async () => {
      const { fixture, host, element } = await setup((h) => {
        h.width.set(500);
        h.model.set(wide('1').model);
      });
      const handle = element.querySelector<HTMLElement>('[data-tm-resize="r"]')!;
      const pointer = (type: string, clientX: number) =>
        handle.dispatchEvent(new PointerEvent(type, { pointerId: 1, isPrimary: true, button: 0, clientX }));
      pointer('pointerdown', 100);
      pointer('pointermove', 900);
      pointer('pointerup', 900);
      await fixture.whenStable();
      // 500 − 112 = 388: the row header alone sticks at 500px.
      expect(host.widths()).toEqual({ rowHeader: 388 });
    });
  });

  describe('sticky columns', () => {
    /** Two attributes. */
    const twoAttributes = () =>
      cube({
        rows: [member(1, null, 'A', ['a1', 'a2']), member(2, null, 'B', ['b1', 'b2'])],
        attributeLabels: ['First', 'Second'],
      }).model;

    it('sticks the prefix whose default widths fit in 40% of the host, decided on defaults', async () => {
      const counts: number[] = [];
      for (const width of [1280, 1180, 1179, 940, 939, 300, 299]) {
        const { pivot } = await setup((h) => {
          h.width.set(width);
          h.model.set(twoAttributes());
          // A user's widths never move the sticky set.
          h.widths.set({ rowHeader: 120, attributes: [64, 64] });
        });
        counts.push(await pivot.getStickyColumnCount());
        TestBed.resetTestingModule();
      }
      expect(counts).toEqual([3, 3, 2, 2, 1, 1, 0]);
    });

    it('recomputes the sticky set when the host resizes', async () => {
      const { fixture, host, pivot } = await setup((h) => {
        h.width.set(1280);
        h.model.set(twoAttributes());
      });
      expect(await pivot.getStickyColumnCount()).toBe(3);
      host.width.set(600);
      await settle(fixture);
      expect(await pivot.getStickyColumnCount()).toBe(1);
      const header = await pivot.getCell({ role: 'rowheader', text: 'A' });
      expect(await header.isSticky()).toBe(true);
      // Below 300px nothing sticks and the row header takes 120px.
      host.width.set(280);
      await settle(fixture);
      expect(await pivot.getStickyColumnCount()).toBe(0);
      expect(await (await pivot.getCell({ role: 'rowheader', text: 'A' })).getWidth()).toBe(120);
    });
  });

  describe('the model’s callbacks', () => {
    it('run untracked, so a callback may write a signal of its own', async () => {
      const calls = signal(0);
      const model = cube({
        text: (row, column, measure) => {
          calls.update((count) => count + 1);
          return `${row}/${column}/${measure}`;
        },
        activatable: () => {
          calls.update((count) => count + 1);
          return true;
        },
      }).model;
      const { element } = await setup((h) => h.model.set(model));
      expect(renderedTexts(element).length).toBeGreaterThan(0);
      expect(calls()).toBeGreaterThan(renderedTexts(element).length);
    });
  });

  describe('strings', () => {
    it('are the pivot’s own English, which a tmUi translation overrides', async () => {
      const { fixture, pivot, announce } = await setup();
      await pivot.press('Enter');
      expect(announce).toHaveBeenLastCalledWith('Riyadh expanded, 2 rows', 'polite');
      TestBed.inject(TranslocoService).setTranslation({ tmUi: { pivot: { collapsed: '{label} folded' } } }, 'en', {
        merge: true,
      });
      await fixture.whenStable();
      await pivot.press('Enter');
      expect(announce).toHaveBeenLastCalledWith('Riyadh folded', 'polite');
    });
  });

  describe('cut values and cells that do not drill', () => {
    it('shows a cut value’s full text in its tooltip, its accessible text and the clipboard', async () => {
      const full = '-1,234,567,890,123.45 and a long note';
      const { fixture, element, pivot } = await setup((h) => {
        h.model.set(cube({ text: () => full }).model);
        h.widths.set({ measures: [64] });
      });
      await pivot.press('ArrowRight');
      await pivot.press('ArrowRight');
      await fixture.whenStable();
      const cell = cellAt(element, 2, 2);
      expect(cell.scrollWidth).toBeGreaterThan(cell.clientWidth);
      expect(cell.textContent!.trim()).toBe(full);
      const panel = document.querySelector('.tm-tooltip__panel');
      expect(panel?.textContent?.trim()).toBe(full);
      // The text is the cell's own; no description repeats it.
      expect(cell.getAttribute('aria-describedby')).toBeNull();
      // The copy carries the full text, not what shows.
      const copy = new ClipboardEvent('copy', { bubbles: true, cancelable: true, clipboardData: new DataTransfer() });
      cell.dispatchEvent(copy);
      expect(copy.clipboardData!.getData('text/plain')).toBe(full);
    });

    it('gives a refused cell no link, a tooltip and description of why, and announces it on Enter', async () => {
      const refusal = 'Only the finest cells open their rows in this report.';
      const { fixture, element, host, pivot, announce } = await setup((h) =>
        h.model.set(cube({ activatable: (row) => row === 2 }).model),
      );
      const refused = await pivot.getValueCell('Riyadh', 0);
      expect(await refused.isRefused()).toBe(true);
      expect(await refused.isActivatable()).toBe(false);
      expect(await (await pivot.getValueCell('Jeddah', 0)).isActivatable()).toBe(true);
      await pivot.press('ArrowRight');
      await pivot.press('ArrowRight');
      await fixture.whenStable();
      const cell = cellAt(element, 2, 2);
      expect(document.querySelector('.tm-tooltip__panel')?.textContent?.trim()).toBe(refusal);
      const described = cell.getAttribute('aria-describedby')!;
      expect(document.getElementById(described)!.textContent).toBe(refusal);
      await pivot.press('Enter');
      expect(announce).toHaveBeenLastCalledWith(refusal, 'polite');
      await refused.click();
      expect(host.activations).toEqual([]);
      // A refused cell still takes focus.
      expect(await (await pivot.getTabStop()).isFocused()).toBe(true);
    });
  });
});

/** A CSS colour as `getComputedStyle` reports it. */
function colorOf(value: string): string {
  const probe = document.createElement('span');
  probe.style.color = value;
  document.body.appendChild(probe);
  const color = getComputedStyle(probe).color;
  probe.remove();
  return color;
}
