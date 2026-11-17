// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import type { ModifierKeys } from '@angular/cdk/testing';
import { TestbedHarnessEnvironment } from '@angular/cdk/testing/testbed';
import { Component, signal } from '@angular/core';
import { TestBed, type ComponentFixture } from '@angular/core/testing';

import { provideTellmaUi, TM_ACTIVE_LOCALE } from '@tellma/core-ui';
import { TmGridStateStore } from '@tellma/core-ui/grid';
import { tmIsApplePlatform } from '@tellma/core-ui/kbd';
import { tmEmitCss, tmTokensDefault } from '@tellma/core-ui-tokens';
import { TmChartHarness } from '@tellma/core-ui-testing';

import { TmChart } from './tm-chart';
import type { TmChartPointActivation, TmChartSpec } from './tm-chart-types';

/** The platform's Mod key, as harness modifiers. */
const MOD: ModifierKeys = tmIsApplePlatform() ? { meta: true } : { control: true };

/** Any bidi control character. */
const BIDI = /[\u061C\u200E\u200F\u202A-\u202E\u2066-\u2069]/;

/** Months by customer type. */
const COLUMN: TmChartSpec = {
  type: 'column',
  title: 'Sales',
  dimensions: [
    { label: 'Month', members: [{ label: 'Sep' }, { label: 'Oct' }] },
    { label: 'Customer type', members: [{ label: 'Corporate' }, { label: 'Retail' }] },
  ],
  measure: { label: 'Sales', format: { maxDecimals: 0 } },
  rows: [
    [0, 0, 300],
    [0, 1, 100],
    [1, 0, 1200],
    [1, 1, null],
  ],
};

/** One value. */
const CARD: TmChartSpec = {
  type: 'card',
  title: 'Revenue',
  dimensions: [],
  measure: { label: 'Revenue', format: { maxDecimals: 0 } },
  rows: [[null, null, 1500]],
  options: {
    thresholds: [
      { value: 1000, tone: 'positive' },
      { value: 1400, tone: 'warning' },
      { value: 2000, tone: 'negative' },
    ],
  },
};

/** A test host: one chart, its two-way table toggle, and its activations. */
@Component({
  imports: [TmChart],
  template: `
    <tm-chart
      style="inline-size: 480px; block-size: 240px"
      [spec]="spec()"
      [(asTable)]="asTable"
      [gridId]="gridId()"
      [activatable]="activatable()"
      (pointActivate)="activations.push($event)"
    >
      <p class="no-data">Nothing to show</p>
    </tm-chart>
  `,
})
class Host {
  /** The chart's spec. */
  readonly spec = signal<TmChartSpec>(COLUMN);
  /** Whether the table view shows, two-way. */
  readonly asTable = signal(false);
  /** The table view's grid id. */
  readonly gridId = signal<string | undefined>(undefined);
  /** Whether the chart drills. */
  readonly activatable = signal(true);
  /** Every activation emitted, in order. */
  readonly activations: TmChartPointActivation[] = [];
}

/** Two charts side by side, sharing one table toggle. */
@Component({
  imports: [TmChart],
  template: `
    <tm-chart style="inline-size: 300px; block-size: 200px" [spec]="spec" [asTable]="asTable()" />
    <tm-chart style="inline-size: 300px; block-size: 200px" [spec]="spec" [asTable]="asTable()" />
  `,
})
class Pair {
  /** The charts' spec. */
  readonly spec = COLUMN;
  /** Whether both show their table views. */
  readonly asTable = signal(false);
}

/** The unit page loads no token stylesheet; the chart reads its tokens. */
let tokenSheet: HTMLStyleElement;

beforeAll(() => {
  tokenSheet = document.createElement('style');
  tokenSheet.textContent = tmEmitCss(tmTokensDefault);
  document.head.appendChild(tokenSheet);
});

afterAll(() => tokenSheet.remove());

/** Waits for a frame (resize observers report after layout), then for the render it causes. */
async function settle(fixture: ComponentFixture<unknown>): Promise<void> {
  for (let pass = 0; pass < 2; pass++) {
    await new Promise((resolve) => requestAnimationFrame(() => setTimeout(resolve)));
    await fixture.whenStable();
  }
}

/** Settles until a condition holds: the engine chunk loads asynchronously. */
async function settleUntil(fixture: ComponentFixture<unknown>, condition: () => Promise<boolean>): Promise<void> {
  for (let attempt = 0; attempt < 50 && !(await condition()); attempt++) {
    await settle(fixture);
  }
}

/** Renders the host in a locale, lets `configure` set it up, and settles. */
async function setup(configure: (host: Host) => void = () => undefined, locale = 'en') {
  TestBed.configureTestingModule({
    providers: [provideTellmaUi(), { provide: TM_ACTIVE_LOCALE, useValue: signal(locale) }],
  });
  const fixture = TestBed.createComponent(Host);
  configure(fixture.componentInstance);
  fixture.autoDetectChanges();
  await settle(fixture);
  const chart = await TestbedHarnessEnvironment.loader(fixture).getHarness(TmChartHarness);
  const element = (fixture.nativeElement as HTMLElement).querySelector<HTMLElement>('tm-chart')!;
  return { fixture, host: fixture.componentInstance, chart, element };
}

describe('TmChart', () => {
  describe('the plot', () => {
    it('is a figure holding a named, described image out of the tab order', async () => {
      const { chart, element } = await setup();
      expect(await chart.getView()).toBe('plot');
      expect(await chart.getName()).toBe('Sales, column chart');
      expect(await chart.getDescription()).toBe(
        '3 points, from 100 to 1,200. Use Show as table to read every value.',
      );
      const plot = element.querySelector('figure [role="img"]')!;
      expect(plot.hasAttribute('tabindex')).toBe(false);
      expect(element.querySelector('figure')!.getAttribute('dir')).toBe('ltr');
    });

    it('loads the engine and draws on a canvas, never in SVG', async () => {
      const { fixture, chart, element } = await setup();
      await settleUntil(fixture, () => chart.hasCanvas());
      expect(await chart.hasCanvas()).toBe(true);
      expect(element.querySelector('svg')).toBeNull();
      // The engine's generated label is off: the only name is the figure's.
      const plot = element.querySelector('[role="img"]')!;
      expect(plot.querySelectorAll('[aria-label]').length).toBe(0);
      expect(plot.querySelectorAll('[role]').length).toBe(0);
    });

    it('gives the canvas the page’s direction inside the left-to-right figure', async () => {
      const { fixture, chart, element } = await setup();
      await settleUntil(fixture, () => chart.hasCanvas());
      const canvas = element.querySelector('canvas')!;
      expect(getComputedStyle(canvas).direction).toBe('ltr');
      document.documentElement.dir = 'rtl';
      try {
        await settle(fixture);
        expect(getComputedStyle(canvas).direction).toBe('rtl');
        expect(getComputedStyle(element.querySelector('figure')!).direction).toBe('ltr');
      } finally {
        document.documentElement.removeAttribute('dir');
      }
    });

    it('draws every type and option through the engine, never falling back to the table', async () => {
      // The option builder's output meets the real engine here: a spec the
      // engine rejects would fall back to the table and log the error.
      const errors = vi.spyOn(console, 'error').mockImplementation(() => undefined);
      try {
        const { fixture, host, chart } = await setup();
        const single: TmChartSpec = {
          ...COLUMN,
          dimensions: [COLUMN.dimensions[0]!],
          rows: [
            [0, null, 300],
            [1, null, -120],
          ],
        };
        const specs: readonly TmChartSpec[] = [
          ...(['column', 'bar', 'line', 'area', 'pie', 'doughnut'] as const).flatMap((type) => [
            { ...COLUMN, type },
            { ...single, type },
          ]),
          { ...single, type: 'bar', options: { palette: 'diverging', labels: true } },
          { ...single, type: 'column', options: { palette: 'sequential', thresholds: [{ value: 500, tone: 'warning' }] } },
          { ...COLUMN, type: 'area', options: { stack: 'normalized', legend: 'end', markers: true } },
          { ...COLUMN, type: 'column', options: { stack: 'stacked', legend: 'start', min: -100, max: 2000 } },
          { ...single, type: 'doughnut', options: { palette: 'sequential', legend: 'top', labels: true } },
        ];
        for (const spec of specs) {
          host.spec.set(spec);
          // One settle first, so the previous spec's canvas cannot pass for this one's.
          await settle(fixture);
          await settleUntil(fixture, async () => (await chart.getView()) === 'plot' && chart.hasCanvas());
          const label = `${spec.type} ${JSON.stringify(spec.options ?? {})} over ${spec.dimensions.length} dimensions`;
          expect(await chart.getView(), label).toBe('plot');
          expect(await chart.hasCanvas(), label).toBe(true);
        }
        expect(errors).not.toHaveBeenCalled();
      } finally {
        errors.mockRestore();
      }
    });

    it('names each type', async () => {
      const { fixture, host, chart } = await setup();
      for (const [type, name] of [
        ['bar', 'Sales, bar chart'],
        ['line', 'Sales, line chart'],
        ['area', 'Sales, area chart'],
      ] as const) {
        host.spec.set({ ...COLUMN, type });
        await settle(fixture);
        expect(await chart.getName()).toBe(name);
      }
      host.spec.set({ ...COLUMN, type: 'pie', dimensions: [COLUMN.dimensions[0]!], rows: [[0, null, 1], [1, null, 2]] });
      await settle(fixture);
      expect(await chart.getName()).toBe('Sales, pie chart');
      host.spec.set({ ...host.spec(), type: 'doughnut' });
      await settle(fixture);
      expect(await chart.getName()).toBe('Sales, doughnut chart');
    });
  });

  describe('the card', () => {
    it('shows the value over the measure label and the reached threshold, without the engine', async () => {
      const { fixture, chart } = await setup((host) => host.spec.set(CARD));
      await settle(fixture);
      expect(await chart.getView()).toBe('card');
      expect(await chart.getCardValue()).toBe('1,500');
      expect(await chart.getCardLabel()).toBe('Revenue');
      // The farthest threshold the value reaches.
      expect(await chart.getCardBadge()).toBe('1,400');
      expect(await chart.hasCanvas()).toBe(false);
    });

    it('reaches a negative threshold from below and shows no badge short of one', async () => {
      const { fixture, host, chart } = await setup((h) =>
        h.spec.set({ ...CARD, rows: [[null, null, -60]], options: { thresholds: [{ value: -50, tone: 'negative' }] } }),
      );
      expect(await chart.getCardBadge()).toBe('-50');
      host.spec.set({ ...CARD, rows: [[null, null, 900]] });
      await settle(fixture);
      expect(await chart.getCardBadge()).toBeNull();
    });

    it('drills from its button, Mod opening a new tab', async () => {
      const { host, chart } = await setup((h) => h.spec.set(CARD));
      expect(await chart.isCardActivatable()).toBe(true);
      await chart.activateCard();
      await chart.activateCard(MOD);
      expect(host.activations).toEqual([
        { members: [null, null], newTab: false },
        { members: [null, null], newTab: true },
      ]);
    });

    it('is no button when the chart does not drill', async () => {
      const { chart } = await setup((h) => {
        h.spec.set(CARD);
        h.activatable.set(false);
      });
      expect(await chart.isCardActivatable()).toBe(false);
    });
  });

  describe('the table view', () => {
    it('swaps the plot for a readonly grid with one row per point', async () => {
      const { fixture, host, chart } = await setup();
      host.asTable.set(true);
      await settle(fixture);
      expect(await chart.getView()).toBe('table');
      const grid = (await chart.getTable())!;
      expect(await grid.getHeaderTexts()).toEqual(['Month', 'Customer type', 'Sales']);
      // A header row and the three points.
      expect(await grid.getRowCount()).toBe(4);
      expect(await grid.getCellText(0, 0)).toBe('Sep');
      expect(await grid.getCellText(0, 1)).toBe('Corporate');
      expect(await grid.getCellText(2, 2)).toBe('1,200');
    });

    it('drills from a row to its point’s members', async () => {
      const { fixture, host, chart } = await setup((h) => h.asTable.set(true));
      await settle(fixture);
      const grid = (await chart.getTable())!;
      await grid.activateRow(1);
      expect(host.activations).toEqual([{ members: [0, 1], newTab: false }]);
    });

    it('takes the grid id it is given, and else one of its own', async () => {
      const { fixture, host } = await setup();
      const register = vi.spyOn(TestBed.inject(TmGridStateStore), 'register');
      host.asTable.set(true);
      await settle(fixture);
      const own = register.mock.calls[0]?.[0];
      expect(own).toMatch(/^tm-chart-\d+$/);
      host.asTable.set(false);
      host.gridId.set('sales-chart');
      await settle(fixture);
      host.asTable.set(true);
      await settle(fixture);
      expect(register.mock.calls.at(-1)?.[0]).toBe('sales-chart');
    });

    it('gives two charts without a grid id their own grids', async () => {
      TestBed.configureTestingModule({ providers: [provideTellmaUi()] });
      const fixture = TestBed.createComponent(Pair);
      fixture.autoDetectChanges();
      await settle(fixture);
      const register = vi.spyOn(TestBed.inject(TmGridStateStore), 'register');
      fixture.componentInstance.asTable.set(true);
      await settle(fixture);
      const ids = register.mock.calls.map((call) => call[0]);
      expect(ids.length).toBe(2);
      expect(new Set(ids).size).toBe(2);
    });

    it('shows a spec the engine cannot draw as a table', async () => {
      const { fixture, host, chart } = await setup();
      host.spec.set({ ...COLUMN, type: 'radar' as TmChartSpec['type'] });
      await settle(fixture);
      expect(await chart.getView()).toBe('table');
      host.spec.set({ ...COLUMN, type: 'pie', dimensions: [COLUMN.dimensions[0]!], rows: [[0, null, -1]] });
      await settle(fixture);
      expect(await chart.getView()).toBe('table');
    });
  });

  describe('no data', () => {
    it('shows the host’s no-data state, never empty axes', async () => {
      const { element, chart } = await setup((h) => h.spec.set({ ...COLUMN, rows: [[0, 0, null]] }));
      expect(await chart.getView()).toBe('empty');
      expect(element.querySelector('.no-data')?.textContent).toBe('Nothing to show');
      expect(element.querySelector('figure')).toBeNull();
    });
  });

  describe('text', () => {
    it('keeps every bidi mark out of the markup, in Arabic too', async () => {
      const spec: TmChartSpec = {
        ...COLUMN,
        title: 'المبيعات',
        dimensions: [
          { label: 'الشهر', members: [{ label: '1100النقدية', parts: ['1100', 'النقدية'] }, { label: 'Oct' }] },
          COLUMN.dimensions[1]!,
        ],
        rows: [
          [0, 0, -300],
          [1, 1, 0.5],
        ],
      };
      const { fixture, chart, element, host } = await setup((h) => h.spec.set(spec), 'ar');
      await settleUntil(fixture, () => chart.hasCanvas());
      const attributes = [...element.querySelectorAll('*')].flatMap((node) =>
        [...node.attributes].map((attribute) => attribute.value),
      );
      expect(attributes.some((value) => BIDI.test(value))).toBe(false);
      expect(BIDI.test(element.textContent ?? '')).toBe(false);
      host.asTable.set(true);
      await settle(fixture);
      expect(BIDI.test(element.textContent ?? '')).toBe(false);
    });
  });
});
