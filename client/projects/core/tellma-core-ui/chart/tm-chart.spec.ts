// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import type { ModifierKeys } from '@angular/cdk/testing';
import { TestbedHarnessEnvironment } from '@angular/cdk/testing/testbed';
import { Component, signal, type Provider, type WritableSignal } from '@angular/core';
import { TestBed, type ComponentFixture } from '@angular/core/testing';
import { By } from '@angular/platform-browser';

import { provideTellmaUi, TM_ACTIVE_LOCALE } from '@tellma/core-ui';
import { TmGridStateStore } from '@tellma/core-ui/grid';
import { tmIsApplePlatform } from '@tellma/core-ui/kbd';
import { tmEmitCss, tmTokensDefault } from '@tellma/core-ui-tokens';
import { TmChartHarness } from '@tellma/core-ui-testing';
import { getInstanceByDom } from 'echarts/core';

import { ɵTM_CHART_ENGINE_LOADER } from './internal/chart-loader';
import type { ɵTmChartEngine, ɵTmChartEngineFactory, ɵTmChartFail } from './internal/chart-plot';
import { ɵtmResolveChartColor } from './internal/chart-theme';
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

/**
 * Settles until a condition holds, for up to four seconds (inside a test's
 * own time limit): the engine chunk and the fonts load asynchronously, and
 * slower while other suites run.
 */
async function settleUntil(
  fixture: ComponentFixture<unknown>,
  condition: () => Promise<boolean>,
): Promise<void> {
  const deadline = performance.now() + 4_000;
  while (!(await condition()) && performance.now() < deadline) {
    await settle(fixture);
  }
}

/** Renders the host in a locale, with any extra providers, lets `configure` set it up, and settles. */
async function setup(
  configure: (host: Host) => void = () => undefined,
  locale = 'en',
  providers: Provider[] = [],
) {
  TestBed.configureTestingModule({
    providers: [
      provideTellmaUi(),
      { provide: TM_ACTIVE_LOCALE, useValue: signal(locale) },
      ...providers,
    ],
  });
  const fixture = TestBed.createComponent(Host);
  configure(fixture.componentInstance);
  fixture.autoDetectChanges();
  await settle(fixture);
  const chart = await TestbedHarnessEnvironment.loader(fixture).getHarness(TmChartHarness);
  const element = (fixture.nativeElement as HTMLElement).querySelector<HTMLElement>('tm-chart')!;
  return { fixture, host: fixture.componentInstance, chart, element };
}

/** One text the engine drew: its string without marks, and its anchor's x on the canvas. */
interface DrawnText {
  /** The string, its bidi marks removed. */
  readonly text: string;
  /** The anchor's x on the canvas, px. */
  readonly x: number;
}

/** The renderer's view of a drawn text span, as far as these tests read it. */
interface TextSpan {
  /** The element's kind; text spans are `'tspan'`. */
  readonly type: string;
  /** The span's string and anchor. */
  readonly style: { readonly text?: string; readonly x?: number };
  /** The span's transform to the canvas, or `null` for none. */
  getComputedTransform(): readonly number[] | null;
}

/** Every text the engine drew in a chart, read off its renderer's display list. */
function drawnTexts(element: HTMLElement): DrawnText[] {
  const plot = element.querySelector<HTMLElement>('.tm-chart__plot')!;
  const spans = getInstanceByDom(plot)!.getZr().storage.getDisplayList() as unknown as TextSpan[];
  return spans
    .filter((span) => span.type === 'tspan')
    .map((span) => ({
      text: (span.style.text ?? '').replace(/[\u200E\u200F]/g, ''),
      x: (span.getComputedTransform()?.[4] ?? 0) + (span.style.x ?? 0),
    }));
}

/** The `rgb()` a CSS colour resolves to on this page. */
function resolved(color: string): [number, number, number] {
  const probe = document.createElement('span');
  probe.style.color = color;
  document.body.appendChild(probe);
  const [r, g, b] = getComputedStyle(probe).color.match(/\d+/g)!.map(Number);
  probe.remove();
  return [r!, g!, b!];
}

/** How many of a chart canvas' pixels are a colour, within 2 per channel. */
function pixelsIn(element: HTMLElement, [r, g, b]: readonly [number, number, number]): number {
  const canvas = element.querySelector('canvas')!;
  const data = canvas.getContext('2d')!.getImageData(0, 0, canvas.width, canvas.height).data;
  let count = 0;
  for (let offset = 0; offset < data.length; offset += 4) {
    if (
      Math.abs(data[offset]! - r) <= 2 &&
      Math.abs(data[offset + 1]! - g) <= 2 &&
      Math.abs(data[offset + 2]! - b) <= 2
    ) {
      count++;
    }
  }
  return count;
}

/** A stand-in engine: each render runs `render`, which may throw. */
function fakeEngine(render: () => void = () => undefined): ɵTmChartEngine {
  return { render: vi.fn(render), dispose: vi.fn() };
}

/** The chart component in a fixture. */
function chartOf(fixture: ComponentFixture<unknown>): TmChart {
  return fixture.debugElement.query(By.directive(TmChart)).componentInstance as TmChart;
}

/** Spies on console errors for a test's length, quietly. */
function quietErrors() {
  return vi.spyOn(console, 'error').mockImplementation(() => undefined);
}

describe('TmChart', () => {
  // The engine chunk's first import, cold, can take seconds while other
  // suites share the browser; it is loaded once here, so each test's wait
  // covers a draw, not the download.
  beforeAll(() => import('./internal/chart-engine'), 60_000);

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
          // Axis types over one and two dimensions; pies take one.
          ...(['column', 'bar', 'line', 'area'] as const).flatMap((type) => [
            { ...COLUMN, type },
            { ...single, type },
          ]),
          { ...single, type: 'pie' },
          { ...single, type: 'doughnut' },
          { ...single, type: 'bar', options: { palette: 'diverging', labels: true } },
          {
            ...single,
            type: 'column',
            options: { palette: 'sequential', thresholds: [{ value: 500, tone: 'warning' }] },
          },
          {
            ...COLUMN,
            type: 'area',
            options: { stack: 'normalized', legend: 'end', markers: true },
          },
          {
            ...COLUMN,
            type: 'column',
            options: { stack: 'stacked', legend: 'start', min: -100, max: 2000 },
          },
          {
            ...single,
            type: 'doughnut',
            options: { palette: 'sequential', legend: 'top', labels: true },
          },
        ];
        for (const spec of specs) {
          host.spec.set(spec);
          // One settle first, so the previous spec's canvas cannot pass for this one's.
          await settle(fixture);
          await settleUntil(
            fixture,
            async () => (await chart.getView()) === 'plot' && chart.hasCanvas(),
          );
          const label = `${spec.type} ${JSON.stringify(spec.options ?? {})} over ${spec.dimensions.length} dimensions`;
          expect(await chart.getView(), label).toBe('plot');
          expect(await chart.hasCanvas(), label).toBe(true);
        }
        expect(errors).not.toHaveBeenCalled();
      } finally {
        errors.mockRestore();
      }
    });

    it('redraws in the page’s new direction: the legend reads from the right', async () => {
      const { fixture, chart, element } = await setup();
      await settleUntil(fixture, () => chart.hasCanvas());
      const order = (): number => {
        const texts = drawnTexts(element);
        return Math.sign(
          texts.find((t) => t.text === 'Retail')!.x - texts.find((t) => t.text === 'Corporate')!.x,
        );
      };
      expect(order()).toBe(1);
      document.documentElement.dir = 'rtl';
      try {
        await settle(fixture);
        expect(order()).toBe(-1);
      } finally {
        document.documentElement.removeAttribute('dir');
      }
      await settle(fixture);
      expect(order()).toBe(1);
    });

    it('redraws in the new locale: the ticks change their digits', async () => {
      const { fixture, chart, element } = await setup();
      await settleUntil(fixture, () => chart.hasCanvas());
      const arabicIndic = (): boolean => drawnTexts(element).some((t) => /[٠-٩]/.test(t.text));
      expect(arabicIndic()).toBe(false);
      (TestBed.inject(TM_ACTIVE_LOCALE) as WritableSignal<string>).set('ar-EG');
      await settle(fixture);
      expect(arabicIndic()).toBe(true);
    });

    it('redraws in the new theme’s colours on the canvas', async () => {
      const { fixture, chart, element } = await setup();
      await settleUntil(fixture, () => chart.hasCanvas());
      const light = resolved('var(--chart-series-1)');
      expect(pixelsIn(element, light)).toBeGreaterThan(50);
      document.documentElement.setAttribute('data-theme', 'dark');
      try {
        const dark = resolved('var(--chart-series-1)');
        expect(dark).not.toEqual(light);
        await settle(fixture);
        expect(pixelsIn(element, dark)).toBeGreaterThan(50);
        expect(pixelsIn(element, light)).toBeLessThan(5);
      } finally {
        document.documentElement.removeAttribute('data-theme');
      }
    });

    it('patterns an area in the decal colour, through the engine’s decal visual', async () => {
      const { fixture, chart, element } = await setup((h) =>
        h.spec.set({ ...COLUMN, type: 'area' }),
      );
      await settleUntil(fixture, () => chart.hasCanvas());
      const plot = element.querySelector<HTMLElement>('.tm-chart__plot')!;
      const engine = getInstanceByDom(plot) as unknown as {
        getModel(): {
          getSeriesByIndex(index: number): { getData(): { getVisual(key: string): unknown } };
        };
      };
      const decal = engine.getModel().getSeriesByIndex(0).getData().getVisual('decal') as
        { readonly color?: string } | undefined;
      const token = getComputedStyle(plot).getPropertyValue('--chart-decal').trim();
      expect(decal?.color).toBe(ɵtmResolveChartColor(token));
    });

    it('opens a paged right-to-left legend on a full page holding its first item, at the right', async () => {
      const members = Array.from({ length: 30 }, (_, index) => ({ label: `Member ${index + 1}` }));
      const spec: TmChartSpec = {
        ...COLUMN,
        dimensions: [COLUMN.dimensions[0]!, { label: 'Member', members }],
        rows: members.map((_, index) => [0, index, index + 1] as const),
      };
      document.documentElement.dir = 'rtl';
      try {
        const { fixture, chart, element } = await setup((h) => h.spec.set(spec));
        await settleUntil(fixture, () => chart.hasCanvas());
        const width = element.querySelector('canvas')!.clientWidth;
        // The items inside the canvas: the window's; the others lie past its edges.
        const shown = drawnTexts(element).filter(
          (text) => /^Member \d+$/.test(text.text) && text.x >= 0 && text.x <= width,
        );
        expect(shown.length).toBeGreaterThan(2);
        const rightmost = shown.reduce((a, b) => (b.x > a.x ? b : a));
        expect(rightmost.text).toBe('Member 1');
      } finally {
        document.documentElement.removeAttribute('dir');
      }
    });

    it('waits, after a font load fails, for the faces still loading before it measures', async () => {
      // A face that fails (a metric fallback whose local() font the system
      // lacks) rejects the load at once, while the other faces may still be
      // loading. The renderer caches text widths per font, so the engine
      // draws only once every face loading has settled.
      let settleFaces: () => void = () => undefined;
      const facesSettled = new Promise<FontFaceSet>((resolve) => {
        settleFaces = () => resolve(document.fonts);
      });
      const check = vi.spyOn(document.fonts, 'check').mockReturnValue(false);
      const load = vi
        .spyOn(document.fonts, 'load')
        .mockRejectedValue(new DOMException('A font face failed to load.', 'NetworkError'));
      const ready = vi.spyOn(document.fonts, 'ready', 'get').mockReturnValue(facesSettled);
      try {
        const { fixture, chart } = await setup(undefined, 'fr');
        await settleUntil(fixture, async () => ready.mock.calls.length > 0);
        expect(load).toHaveBeenCalled();
        await settle(fixture);
        expect(await chart.hasCanvas()).toBe(false);
        settleFaces();
        await settleUntil(fixture, () => chart.hasCanvas());
        expect(await chart.hasCanvas()).toBe(true);
      } finally {
        check.mockRestore();
        load.mockRestore();
        ready.mockRestore();
      }
    });

    it('waits for the fonts of the locale’s digits, compact words and templates before measuring', async () => {
      const check = vi.spyOn(document.fonts, 'check');
      try {
        const { fixture, chart } = await setup(undefined, 'ar-EG');
        await settleUntil(fixture, () => chart.hasCanvas());
        const sample = check.mock.calls.map(([, text]) => text ?? '').join('');
        expect(sample).toMatch(/[٠-٩]/);
        expect(sample).toContain('ألف');
        expect(sample).toContain('Customer type');
      } finally {
        check.mockRestore();
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
      host.spec.set({
        ...COLUMN,
        type: 'pie',
        dimensions: [COLUMN.dimensions[0]!],
        rows: [
          [0, null, 1],
          [1, null, 2],
        ],
      });
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
        h.spec.set({
          ...CARD,
          rows: [[null, null, -60]],
          options: { thresholds: [{ value: -50, tone: 'negative' }] },
        }),
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

    it('isolates each part of a member’s display name in its cell', async () => {
      const spec: TmChartSpec = {
        ...COLUMN,
        dimensions: [
          {
            label: 'Account',
            members: [{ label: '1100 - النقدية', parts: ['1100', 'النقدية'] }, { label: 'Oct' }],
          },
          COLUMN.dimensions[1]!,
        ],
      };
      const { fixture, element } = await setup((h) => {
        h.spec.set(spec);
        h.asTable.set(true);
      });
      await settle(fixture);
      const runs = [...element.querySelectorAll('bdi')].map((run) => run.textContent?.trim());
      expect(runs).toContain('1100');
      expect(runs).toContain('النقدية');
    });

    it('formats its values in the new locale', async () => {
      const { fixture, chart } = await setup((h) => h.asTable.set(true));
      await settle(fixture);
      const grid = (await chart.getTable())!;
      expect(await grid.getCellText(2, 2)).toBe('1,200');
      (TestBed.inject(TM_ACTIVE_LOCALE) as WritableSignal<string>).set('ar-EG');
      await settle(fixture);
      expect(await grid.getCellText(2, 2)).toMatch(/^[٠-٩٬]+$/);
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

    it('shows a spec the engine cannot draw as a table, and reports an invalid one', async () => {
      const errors = quietErrors();
      try {
        const { fixture, host, chart } = await setup();
        await settleUntil(fixture, () => chart.hasCanvas());
        expect(chartOf(fixture).fallback()).toBe(false);
        // The table the host asks for is no fallback.
        host.asTable.set(true);
        await settle(fixture);
        expect(chartOf(fixture).fallback()).toBe(false);
        host.asTable.set(false);
        host.spec.set({ ...COLUMN, type: 'radar' as TmChartSpec['type'] });
        await settle(fixture);
        expect(await chart.getView()).toBe('table');
        expect(chartOf(fixture).fallback()).toBe(true);
        // Development builds name the problems.
        expect(errors).toHaveBeenCalledWith('tm-chart: the spec is not valid.', expect.anything());
        host.spec.set({
          ...COLUMN,
          type: 'pie',
          dimensions: [COLUMN.dimensions[0]!],
          rows: [[0, null, -1]],
        });
        await settle(fixture);
        expect(await chart.getView()).toBe('table');
        // Rows whose members do not exist place no point: a table, not empty axes.
        host.spec.set({ ...COLUMN, rows: [[5, 0, 10]] });
        await settle(fixture);
        expect(await chart.getView()).toBe('table');
        // Nor do rows of a spec with no dimensions.
        host.spec.set({ ...COLUMN, dimensions: [] });
        await settle(fixture);
        expect(await chart.getView()).toBe('table');
      } finally {
        errors.mockRestore();
      }
    });
  });

  describe('engine failures', () => {
    it('falls back to the table when the engine chunk fails to load, and asks again for the next spec', async () => {
      const errors = quietErrors();
      try {
        const render = vi.fn();
        let loads = 0;
        const loader = vi.fn((): Promise<ɵTmChartEngineFactory> =>
          ++loads === 1
            ? Promise.reject(new Error('offline'))
            : Promise.resolve(() => ({ render, dispose: vi.fn() })),
        );
        const { fixture, host, chart } = await setup(undefined, 'en', [
          { provide: ɵTM_CHART_ENGINE_LOADER, useValue: loader },
        ]);
        await settleUntil(fixture, async () => (await chart.getView()) === 'table');
        expect(await chart.getView()).toBe('table');
        expect(chartOf(fixture).fallback()).toBe(true);
        expect(errors).toHaveBeenCalled();
        // Back online: the next spec loads the chunk again and draws.
        host.spec.set({ ...COLUMN, title: 'Sales again' });
        await settleUntil(fixture, async () => (await chart.getView()) === 'plot');
        await settleUntil(fixture, async () => render.mock.calls.length > 0);
        expect(loader).toHaveBeenCalledTimes(2);
        expect(await chart.getView()).toBe('plot');
        expect(chartOf(fixture).fallback()).toBe(false);
        expect(render).toHaveBeenCalled();
      } finally {
        errors.mockRestore();
      }
    });

    it('shows a spec the engine fails to draw as a table, and draws the next spec', async () => {
      const errors = quietErrors();
      try {
        let renders = 0;
        const factory: ɵTmChartEngineFactory = () =>
          fakeEngine(() => {
            if (++renders === 1) {
              throw new Error('cannot draw');
            }
          });
        const loader = (): Promise<ɵTmChartEngineFactory> => Promise.resolve(factory);
        const { fixture, host, chart } = await setup(undefined, 'en', [
          { provide: ɵTM_CHART_ENGINE_LOADER, useValue: loader },
        ]);
        await settleUntil(fixture, async () => (await chart.getView()) === 'table');
        expect(await chart.getView()).toBe('table');
        expect(errors).toHaveBeenCalledWith(new Error('cannot draw'));
        host.spec.set({ ...COLUMN, title: 'Sales again' });
        await settleUntil(fixture, async () => renders === 2);
        expect(await chart.getView()).toBe('plot');
        expect(renders).toBe(2);
      } finally {
        errors.mockRestore();
      }
    });

    it('shows the table when a draw the engine makes later on its own fails', async () => {
      const errors = quietErrors();
      try {
        let fail: ɵTmChartFail | undefined;
        const factory: ɵTmChartEngineFactory = (_element, _activate, onFail) => {
          fail = onFail;
          return fakeEngine();
        };
        const loader = (): Promise<ɵTmChartEngineFactory> => Promise.resolve(factory);
        const { fixture, chart } = await setup(undefined, 'en', [
          { provide: ɵTM_CHART_ENGINE_LOADER, useValue: loader },
        ]);
        await settleUntil(fixture, async () => fail !== undefined);
        expect(await chart.getView()).toBe('plot');
        fail!(new Error('a draw after the fonts loaded'));
        await settle(fixture);
        expect(await chart.getView()).toBe('table');
      } finally {
        errors.mockRestore();
      }
    });

    it('never creates an engine whose chunk arrives after the chart is destroyed', async () => {
      let arrive: (factory: ɵTmChartEngineFactory) => void = () => undefined;
      const chunk = new Promise<ɵTmChartEngineFactory>((resolve) => (arrive = resolve));
      const factory = vi.fn<ɵTmChartEngineFactory>(() => fakeEngine());
      const { fixture } = await setup(undefined, 'en', [
        { provide: ɵTM_CHART_ENGINE_LOADER, useValue: () => chunk },
      ]);
      fixture.destroy();
      arrive(factory);
      await chunk;
      await new Promise((resolve) => setTimeout(resolve));
      expect(factory).not.toHaveBeenCalled();
    });
  });

  describe('no data', () => {
    it('shows the host’s no-data state, never empty axes', async () => {
      const { element, chart } = await setup((h) =>
        h.spec.set({ ...COLUMN, rows: [[0, 0, null]] }),
      );
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
          {
            label: 'الشهر',
            members: [{ label: '1100 - النقدية', parts: ['1100', 'النقدية'] }, { label: 'Oct' }],
          },
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
