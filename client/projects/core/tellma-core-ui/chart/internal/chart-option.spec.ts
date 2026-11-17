// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import type { TmChartOptions, TmChartSpec, TmChartType } from '../tm-chart-types';
import { ɵtmBuildChartOption } from './chart-option';
import {
  ɵTM_CHART_LABEL_SLOT,
  ɵTM_CHART_SHARE_SLOT,
  ɵTM_CHART_VALUE_SLOT,
  type ɵTmChartPlotInput,
  type ɵTmChartTheme,
} from './chart-plot';

// The option builder runs engine-free: these tests read the option it hands
// ECharts, which pins the four guards the canvas-text exception depends on
// (spec 0034 §7.4) alongside the per-type and per-option behaviour.

/** LEFT-TO-RIGHT MARK. */
const LRM = '\u200E';
/** RIGHT-TO-LEFT MARK. */
const RLM = '\u200F';

/** A theme with a distinct, recognisable colour per role. */
const THEME: ɵTmChartTheme = {
  series: ['s1', 's2', 's3', 's4', 's5', 's6', 's7', 's8'],
  sequential: ['q1', 'q2', 'q3', 'q4', 'q5', 'q6', 'q7'],
  divergingNegative: 'neg',
  divergingPositive: 'pos',
  grid: 'grid',
  axis: 'axis',
  label: 'label',
  tones: { positive: 'tone-positive', warning: 'tone-warning', negative: 'tone-negative' },
  surface: 'surface',
  tooltipBg: 'tooltip-bg',
  tooltipBorder: 'tooltip-border',
  decal: 'decal',
  fontFamily: 'Test Sans',
  fontSize: 12,
};

/** A loose view of the option's parts, for reading in assertions. */
type Loose = { [key: string]: any }; // eslint-disable-line @typescript-eslint/no-explicit-any

/** Months by customer type: two dimensions, two series. */
function twoSeries(type: TmChartType, options?: TmChartOptions): TmChartSpec {
  return {
    type,
    title: 'Sales',
    dimensions: [
      { label: 'Month', members: [{ label: 'Sep' }, { label: 'Oct' }] },
      { label: 'Customer type', members: [{ label: 'Corporate' }, { label: 'Retail' }] },
    ],
    measure: { label: 'Sales', format: { maxDecimals: 0 } },
    rows: [
      [0, 0, 300],
      [0, 1, 100],
      [1, 0, 100],
      [1, 1, 300],
    ],
    options,
  };
}

/** Values over one dimension: one series, or a pie's slices. */
function oneSeries(type: TmChartType, values: readonly (number | null)[], options?: TmChartOptions): TmChartSpec {
  return {
    type,
    title: 'Sales',
    dimensions: [{ label: 'Region', members: values.map((_, index) => ({ label: `R${index}` })) }],
    measure: { label: 'Sales' },
    rows: values.map((value, index) => [index, null, value] as const),
    options,
  };
}

/** The builder's input for a spec, in English, left to right, 600 × 300. */
function input(spec: TmChartSpec, overrides: Partial<ɵTmChartPlotInput> = {}): ɵTmChartPlotInput {
  return {
    spec,
    theme: THEME,
    locale: 'en',
    direction: 'ltr',
    forceDecals: false,
    activatable: true,
    width: 600,
    height: 300,
    templates: {
      share: `${ɵTM_CHART_SHARE_SLOT} (${ɵTM_CHART_VALUE_SLOT})`,
      point: `${ɵTM_CHART_LABEL_SLOT}: ${ɵTM_CHART_VALUE_SLOT}`,
    },
    measureText: (text) => text.length * 7,
    ...overrides,
  };
}

/** Builds and returns the option, loosely typed. */
function build(spec: TmChartSpec, overrides: Partial<ɵTmChartPlotInput> = {}): Loose {
  return ɵtmBuildChartOption(input(spec, overrides)).option as Loose;
}

/** Every string anywhere in a value, formatter results excluded. */
function strings(value: unknown): string[] {
  if (typeof value === 'string') {
    return [value];
  }
  if (Array.isArray(value)) {
    return value.flatMap(strings);
  }
  if (typeof value === 'object' && value !== null) {
    return Object.values(value).flatMap(strings);
  }
  return [];
}

describe('ɵtmBuildChartOption', () => {
  describe('the four guards', () => {
    it('draws the tooltip as rich text on the canvas, never as HTML', () => {
      const option = build(twoSeries('column'));
      expect(option['tooltip'].renderMode).toBe('richText');
      expect(option['tooltip'].appendTo).toBeUndefined();
      expect(option['tooltip'].extraCssText).toBeUndefined();
      const text = option['tooltip'].formatter([
        { seriesIndex: 0, dataIndex: 0 },
        { seriesIndex: 1, dataIndex: 0 },
      ]);
      expect(text).toBe('Sep\nCorporate: 300\nRetail: 100');
      expect(text).not.toMatch(/[<>]/);
    });

    it('turns the generated aria label off', () => {
      const option = build(twoSeries('column'));
      expect(option['aria'].enabled).toBe(true);
      expect(option['aria'].label).toEqual({ enabled: false });
    });

    it('names series, categories and slices by private ids, so events resolve by index', () => {
      const column = build(twoSeries('column'));
      expect(column['series'].map((series: Loose) => series['name'])).toEqual(['s0', 's1']);
      expect(column['xAxis'].data).toEqual(['c0', 'c1']);
      const pie = build(oneSeries('pie', [3, 1]));
      expect(pie['series'][0].data.map((item: Loose) => item['name'])).toEqual(['s0', 's1']);
      // No member label is ever an ECharts name.
      const names = [...strings(column['series']), ...strings(column['xAxis'].data)];
      expect(names).not.toContain('Corporate');
      expect(names).not.toContain('Sep');
    });

    it('maps the series and data indexes back to members', () => {
      const plot = ɵtmBuildChartOption(input(twoSeries('bar')));
      expect(plot.series).toEqual([0, 1]);
      expect(plot.categories).toEqual([0, 1]);
      const single = ɵtmBuildChartOption(input(oneSeries('line', [1, 2, 3])));
      expect(single.series).toEqual([null]);
      // A pie's data indexes are its drawn slices: a slice without a positive value is left out.
      const pie = ɵtmBuildChartOption(input(oneSeries('pie', [3, null, 0, 1])));
      expect(pie.categories).toEqual([0, 3]);
    });

    it('has no HTML legend or tooltip and no animation', () => {
      const option = build(twoSeries('column'));
      expect(option['animation']).toBe(false);
      expect(option['darkMode']).toBe(false);
      for (const legend of option['legend']) {
        expect(legend.formatter('s0')).toBe('Corporate');
        expect(legend.selectedMode).toBe(false);
      }
    });
  });

  describe('per type', () => {
    it('draws columns over the categories with the value axis at the left', () => {
      const option = build(twoSeries('column'));
      expect(option['xAxis'].type).toBe('category');
      expect(option['yAxis'].type).toBe('value');
      expect(option['series'].map((series: Loose) => series['type'])).toEqual(['bar', 'bar']);
      expect(option['series'][0].data).toEqual([300, 100]);
      expect(option['series'][1].data).toEqual([100, 300]);
      expect(option['tooltip'].trigger).toBe('axis');
    });

    it('draws bars top to bottom, the value axis at the bottom', () => {
      const option = build(twoSeries('bar'));
      expect(option['yAxis'].type).toBe('category');
      expect(option['yAxis'].inverse).toBe(true);
      expect(option['xAxis'].type).toBe('value');
    });

    it('draws lines without markers unless asked, each series its own shape', () => {
      const plain = build(twoSeries('line'));
      expect(plain['series'].map((series: Loose) => series['type'])).toEqual(['line', 'line']);
      expect(plain['series'][0].showSymbol).toBe(false);
      expect(plain['series'][0].symbol).not.toBe(plain['series'][1].symbol);
      expect(plain['series'][0].areaStyle).toBeUndefined();
      // The line reports clicks, so a point drills without its marker.
      expect(plain['series'][0].triggerEvent).toBe(true);
      const marked = build(twoSeries('line', { markers: true }));
      expect(marked['series'][0].showSymbol).toBe(true);
      expect(marked['series'][0].symbolSize).toBeGreaterThanOrEqual(8);
    });

    it('fills areas, and stacks them when asked; lines never stack', () => {
      const area = build(twoSeries('area', { stack: 'stacked' }));
      expect(area['series'][0].areaStyle).toBeDefined();
      expect(area['series'][0].stack).toBe('total');
      const line = build(twoSeries('line', { stack: 'stacked' }));
      expect(line['series'][0].stack).toBeUndefined();
    });

    it('draws a pie clockwise from 12 o’clock and a ring at 55% of its radius', () => {
      const pie = build(oneSeries('pie', [3, 1]));
      const series = pie['series'][0];
      expect(series.type).toBe('pie');
      expect(series.startAngle).toBe(90);
      expect(series.clockwise).toBe(true);
      expect(series.radius[0]).toBe(0);
      expect(pie['tooltip'].trigger).toBe('item');
      const ring = build(oneSeries('doughnut', [3, 1]))['series'][0];
      expect(ring.radius[0] / ring.radius[1]).toBeCloseTo(0.55, 5);
    });

    it('labels a slice with its member and share, and its tooltip with share and value', () => {
      const pie = build(oneSeries('pie', [3, 1], { labels: true }));
      const series = pie['series'][0];
      expect(series.label.show).toBe(true);
      expect(series.label.formatter({ dataIndex: 0 })).toBe('R0: 75%');
      expect(pie['tooltip'].formatter({ seriesIndex: 0, dataIndex: 1 })).toBe('R1\nSales: 25% (1)');
    });

    it('turns large mode on for bars and sampling for lines past 2,000 points', () => {
      const many = Array.from({ length: 2001 }, (_, index) => index);
      expect(build(oneSeries('column', many))['series'][0].large).toBe(true);
      expect(build(oneSeries('column', many.slice(0, 2000)))['series'][0].large).toBe(false);
      expect(build(oneSeries('line', many))['series'][0].sampling).toBe('lttb');
      expect(build(oneSeries('line', many.slice(0, 2000)))['series'][0].sampling).toBeUndefined();
    });
  });

  describe('palettes and decals', () => {
    it('gives categorical slots in member order, and past eight a new decal per cycle', () => {
      const members = Array.from({ length: 10 }, (_, index) => ({ label: `M${index}` }));
      const spec: TmChartSpec = {
        type: 'column',
        title: 'Ten',
        dimensions: [
          { label: 'Month', members: [{ label: 'Sep' }] },
          { label: 'Member', members },
        ],
        measure: { label: 'Sales' },
        rows: members.map((_, index) => [0, index, index + 1] as const),
      };
      const option = build(spec);
      const colors = option['series'].map((series: Loose) => series['itemStyle'].color);
      expect(colors).toEqual(['s1', 's2', 's3', 's4', 's5', 's6', 's7', 's8', 's1', 's2']);
      const decals = option['series'].map((series: Loose) => JSON.stringify(series['itemStyle'].decal));
      expect(new Set(decals.slice(0, 8)).size).toBe(8);
      // The ninth series shares the first's colour, never its pattern.
      expect(decals[8]).not.toBe(decals[0]);
      expect(decals[9]).not.toBe(decals[1]);
      expect(option['series'][0].itemStyle.decal.color).toBe('decal');
    });

    it('applies decals for two or more series and for every pie, never for lines', () => {
      expect(build(twoSeries('column'))['series'][0].itemStyle.decal).toBeTypeOf('object');
      expect(build(twoSeries('column'))['aria'].decal.show).toBe(true);
      // No decal key at all: ECharts throws on a style's decal of 'none'.
      expect(build(oneSeries('column', [1, 2]))['series'][0].itemStyle).not.toHaveProperty('decal');
      expect(build(oneSeries('column', [1, 2]))['aria'].decal.show).toBe(false);
      expect(build(oneSeries('pie', [1, 2]))['series'][0].data[0].itemStyle.decal).toBeTypeOf('object');
      expect(build(twoSeries('area'))['series'][0].areaStyle.decal).toBeTypeOf('object');
      expect(build(twoSeries('line'))['aria'].decal.show).toBe(false);
    });

    it('applies decals to every chart under forced colours or more contrast', () => {
      const option = build(oneSeries('column', [1, 2]), { forceDecals: true });
      expect(option['series'][0].itemStyle.decal).toBeTypeOf('object');
      expect(option['aria'].decal.show).toBe(true);
    });

    it('spreads the sequential ramp over the series', () => {
      const spec = twoSeries('column', { palette: 'sequential' });
      expect(build(spec)['series'].map((series: Loose) => series['itemStyle'].color)).toEqual(['q1', 'q7']);
      const single = oneSeries('column', [1], { palette: 'sequential' });
      expect(build(single)['series'][0].itemStyle.color).toBe('q7');
    });

    it('colours one series’ marks by sign in the diverging palette', () => {
      const option = build(oneSeries('column', [2, -1, 0], { palette: 'diverging' }));
      const colors = option['series'][0].data.map((item: Loose) => item['itemStyle'].color);
      expect(colors).toEqual(['pos', 'neg', 'pos']);
    });
  });

  describe('stacks and thresholds', () => {
    it('plots a normalized stack as shares of each category, the tooltip giving share and value', () => {
      const option = build(twoSeries('column', { stack: 'normalized' }));
      expect(option['series'][0].data).toEqual([0.75, 0.25]);
      expect(option['series'][1].data).toEqual([0.25, 0.75]);
      expect(option['series'][0].stack).toBe('total');
      const text = option['tooltip'].formatter([
        { seriesIndex: 0, dataIndex: 0 },
        { seriesIndex: 1, dataIndex: 0 },
      ]);
      expect(text).toBe('Sep\nCorporate: 75% (300)\nRetail: 25% (100)');
      expect(option['yAxis'].axisLabel.formatter(0.5)).toBe('50%');
    });

    it('takes shares over absolute values', () => {
      const spec: TmChartSpec = { ...twoSeries('column', { stack: 'normalized' }), rows: [[0, 0, -1], [0, 1, 3]] };
      expect(build(spec)['series'][0].data[0]).toBeCloseTo(-0.25, 10);
    });

    it('draws thresholds as dashed lines in their tone, labelled with their value', () => {
      const option = build(
        twoSeries('column', {
          thresholds: [
            { value: 250, tone: 'warning' },
            { value: 500, tone: 'negative' },
          ],
        }),
      );
      const markLine = option['series'][0].markLine;
      expect(option['series'][1].markLine).toBeUndefined();
      expect(markLine.data.map((line: Loose) => line['yAxis'])).toEqual([250, 500]);
      expect(markLine.data.map((line: Loose) => line['lineStyle'].color)).toEqual(['tone-warning', 'tone-negative']);
      expect(markLine.data[0].lineStyle.type).toBe('dashed');
      expect(markLine.data[0].label.formatter()).toBe('250');
      expect(markLine.data[0].label.color).toBe('label');
      // The value axis takes in a threshold beyond the data.
      expect(option['yAxis'].max({ min: 0, max: 300 })).toBe(500);
    });

    it('marks thresholds on the horizontal value axis for bars', () => {
      const option = build(twoSeries('bar', { thresholds: [{ value: 250, tone: 'positive' }] }));
      expect(option['series'][0].markLine.data[0].xAxis).toBe(250);
    });

    it('fixes the value axis where the options say', () => {
      const option = build(twoSeries('column', { min: -100, max: 1000 }));
      expect(option['yAxis'].min).toBe(-100);
      expect(option['yAxis'].max).toBe(1000);
    });
  });

  describe('text', () => {
    it('writes compact ticks, with Arabic-Indic digits in Arabic', () => {
      const english = build(twoSeries('column'));
      expect(english['yAxis'].axisLabel.formatter(100000)).toBe('100K');
      // Egyptian Arabic writes Arabic-Indic digits (plain `ar` may write European ones).
      const arabic = build(twoSeries('column'), { locale: 'ar-EG', direction: 'rtl' });
      const tick = arabic['yAxis'].axisLabel.formatter(100000);
      expect(tick).toMatch(/[٠-٩]/);
      expect(tick.replace(/[\u200E\u200F]/g, '')).toBe(
        new Intl.NumberFormat('ar-EG', { notation: 'compact' }).format(100000).replace(/[\u061C\u200E\u200F]/g, ''),
      );
    });

    it('builds every canvas string through the canvas text, from the base direction', () => {
      const spec: TmChartSpec = {
        ...twoSeries('column'),
        dimensions: [
          { label: 'Account', members: [{ label: '1100النقدية', parts: ['1100', 'النقدية'] }, { label: 'Cash' }] },
          { label: 'Type', members: [{ label: 'A' }, { label: 'B' }] },
        ],
      };
      const option = build(spec, { direction: 'rtl' });
      expect(option['xAxis'].axisLabel.formatter('c0')).toBe(`${RLM}${LRM}1100${LRM}${RLM}النقدية`);
      expect(option['xAxis'].axisLabel.formatter('c1')).toBe(`${RLM}${LRM}Cash${LRM}${RLM}`);
      expect(option['legend'][0].formatter('s0')).toBe(`${RLM}${LRM}A${LRM}${RLM}`);
    });

    it('lays a right-to-left legend out from the right, its start side at the right', () => {
      const ltr = build(twoSeries('column'));
      expect(ltr['legend'][0].data.map((item: Loose) => item['name'])).toEqual(['s0', 's1']);
      expect(ltr['legend'][0].bottom).toBeDefined();
      const rtl = build(twoSeries('column'), { direction: 'rtl' });
      expect(rtl['legend'][0].data.map((item: Loose) => item['name'])).toEqual(['s1', 's0']);
      expect(rtl['legend'][0].align).toBe('right');
      const start = build(twoSeries('column', { legend: 'start' }), { direction: 'rtl' });
      expect(start['legend'][0].orient).toBe('vertical');
      expect(start['legend'][0].right).toBeDefined();
      expect(start['legend'][0].left).toBeUndefined();
    });

    it('wraps a long horizontal legend onto lines, one legend per line', () => {
      const members = Array.from({ length: 6 }, (_, index) => ({ label: `A long member name ${index}` }));
      const spec: TmChartSpec = {
        type: 'column',
        title: 'Wide',
        dimensions: [
          { label: 'Month', members: [{ label: 'Sep' }] },
          { label: 'Member', members },
        ],
        measure: { label: 'Sales' },
        rows: members.map((_, index) => [0, index, index + 1] as const),
      };
      const option = build(spec);
      expect(option['legend'].length).toBeGreaterThan(1);
      const names = option['legend'].flatMap((legend: Loose) => legend['data'].map((item: Loose) => item['name']));
      expect(names).toEqual(['s0', 's1', 's2', 's3', 's4', 's5']);
    });

    it('shows no legend for one series, unless asked', () => {
      expect(build(oneSeries('column', [1, 2]))['legend']).toEqual([]);
      expect(build(oneSeries('column', [1, 2], { legend: 'top' }))['legend'].length).toBe(1);
      expect(build(twoSeries('column', { legend: 'none' }))['legend']).toEqual([]);
    });

    it('labels marks with their values in the text tokens when asked', () => {
      const option = build(twoSeries('column', { labels: true }));
      const label = option['series'][0].label;
      expect(label.show).toBe(true);
      expect(label.color).toBe('label');
      expect(label.formatter({ dataIndex: 0 })).toBe('300');
    });
  });
});
