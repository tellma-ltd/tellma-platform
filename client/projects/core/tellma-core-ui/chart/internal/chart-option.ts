// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import type { BarSeriesOption, LineSeriesOption, PieSeriesOption } from 'echarts/charts';
import type {
  AriaComponentOption,
  GridComponentOption,
  LegendComponentOption,
  MarkLineComponentOption,
  TooltipComponentOption,
} from 'echarts/components';
import type { ComposeOption } from 'echarts/core';
import { tmFormatNumber } from '@tellma/core-ui/l10n';
import type { TmChartMember, TmChartThreshold } from '../tm-chart-types';
import {
  ɵTM_CHART_LABEL_SLOT,
  ɵTM_CHART_SHARE_SLOT,
  ɵTM_CHART_VALUE_SLOT,
  type ɵTmChartOption,
  type ɵTmChartPlot,
  type ɵTmChartPlotInput,
} from './chart-plot';
import { ɵtmCanvasText, type ɵTmCanvasPiece } from './chart-text';

// The adapter's option builder (spec 0034 §7.2–§7.7): a pure function from
// a spec, the resolved tokens and the page's locale and direction to an
// ECharts option. It imports ECharts' types only, so its unit tests need no
// engine. Every canvas-drawn string goes through the canvas-text function;
// series, categories and slices are named by private ids, so no label is
// ever an ECharts name and events resolve by index.

/** The option shape this builder produces: the modules the engine registers. */
type ChartOption = ComposeOption<
  | BarSeriesOption
  | LineSeriesOption
  | PieSeriesOption
  | GridComponentOption
  | TooltipComponentOption
  | LegendComponentOption
  | MarkLineComponentOption
  | AriaComponentOption
>;

/** One ECharts decal pattern, all of its properties given so nothing merges in. */
interface DecalPattern {
  /** The shape the pattern repeats. */
  readonly symbol: string;
  /** The shape's size, as a share of its cell. */
  readonly symbolSize: number;
  /** The pattern's rotation, in radians. */
  readonly rotation: number;
  /** The horizontal dash and gap lengths, or one list per row. */
  readonly dashArrayX: number | readonly (number | readonly number[])[];
  /** The vertical dash and gap lengths. */
  readonly dashArrayY: number | readonly number[];
}

/** Past this many points, bars draw in ECharts' large mode and lines sample. */
const LARGE_POINTS = 2000;
/** The space between the plot's edge and anything drawn, px. */
const PAD = 8;
/** A legend item's swatch, px. */
const ICON_WIDTH = 14;
/** A legend item's swatch height, px. */
const ICON_HEIGHT = 10;
/** The gap between a legend swatch and its text, px. */
const ICON_GAP = 5;
/** The gap between legend items, px. */
const ITEM_GAP = 16;
/**
 * A paged legend's pager beside its items, px, less its page text: two
 * page icons and the gaps around the text (ECharts' defaults, 15 and 5),
 * the gap before the items, and a little room for the text's own measure.
 */
const PAGER = 2 * 15 + 2 * 5 + ITEM_GAP + 4;
/** The pie's outer radius as a share of the half of its box's shorter side. */
const PIE_RADIUS = 0.8;
/** The pie's outer radius when its labels sit outside, leaving them room. */
const PIE_RADIUS_LABELLED = 0.6;
/** The ring's inner radius as a share of its outer one. */
const RING_INNER = 0.55;
/** The share of the plot's inline size a vertical legend or a bar label may take. */
const SIDE_SHARE = 0.3;

/**
 * Eight decal patterns: ECharts' own six, then vertical lines and a dot grid.
 * The categorical palette's ninth series reuses the first colour with the
 * fourth pattern, and so on, so no two series share colour and pattern.
 */
const DECALS: readonly DecalPattern[] = [
  { symbol: 'rect', symbolSize: 1, rotation: Math.PI / 6, dashArrayX: [1, 0], dashArrayY: [2, 5] },
  {
    symbol: 'circle',
    symbolSize: 0.8,
    rotation: 0,
    dashArrayX: [
      [8, 8],
      [0, 8, 8, 0],
    ],
    dashArrayY: [6, 0],
  },
  { symbol: 'rect', symbolSize: 1, rotation: -Math.PI / 4, dashArrayX: [1, 0], dashArrayY: [4, 3] },
  {
    symbol: 'rect',
    symbolSize: 1,
    rotation: 0,
    dashArrayX: [
      [6, 6],
      [0, 6, 6, 0],
    ],
    dashArrayY: [6, 0],
  },
  {
    symbol: 'rect',
    symbolSize: 1,
    rotation: Math.PI / 4,
    dashArrayX: [
      [1, 0],
      [1, 6],
    ],
    dashArrayY: [1, 0, 6, 0],
  },
  {
    symbol: 'triangle',
    symbolSize: 0.75,
    rotation: 0,
    dashArrayX: [
      [9, 9],
      [0, 9, 9, 0],
    ],
    dashArrayY: [7, 2],
  },
  { symbol: 'rect', symbolSize: 1, rotation: Math.PI / 2, dashArrayX: [1, 0], dashArrayY: [2, 6] },
  { symbol: 'rect', symbolSize: 0.5, rotation: 0, dashArrayX: [[4, 6]], dashArrayY: [4, 6] },
];

/** The marker shape of each line series, in slot order: lines differ by shape, not colour alone. */
const SYMBOLS: readonly string[] = [
  'circle',
  'rect',
  'triangle',
  'diamond',
  'pin',
  'arrow',
  'roundRect',
  'path://M2,0H4V2H6V4H4V6H2V4H0V2H2Z',
];

/** The line style of each eight-series cycle. */
const LINE_TYPES: readonly ('solid' | 'dashed' | 'dotted')[] = ['solid', 'dashed', 'dotted'];

/** The mark-line tone colours' keys by threshold tone. */
const TONES = ['positive', 'warning', 'negative'] as const;

/** A tooltip formatter's view of one point. */
interface PointParams {
  /** The point's series, absent for a pie's slice. */
  readonly seriesIndex?: number;
  /** The point's index in its series' data. */
  readonly dataIndex: number;
}

/** The series or slices of a chart, with everything drawn about them precomputed. */
interface Series {
  /** The private name ECharts keys the series or slice by. */
  readonly id: string;
  /** The member in the series dimension (axis types) or the slice dimension (pies), `null` for none. */
  readonly member: number | null;
  /** The legend and tooltip text's runs. */
  readonly runs: readonly ɵTmCanvasPiece[];
  /** The legend text, as the canvas draws it. */
  readonly text: string;
  /** The text's advance without marks, px. */
  readonly width: number;
}

/**
 * A member's runs: its parts, each in its own direction, joined by the
 * display-name separator in the base direction, or else its label.
 */
function memberRuns(member: TmChartMember | undefined): readonly ɵTmCanvasPiece[] {
  if (member === undefined) {
    return [''];
  }
  if (member.parts === undefined || member.parts.length === 0) {
    return [member.label];
  }
  return member.parts.flatMap((part, index): ɵTmCanvasPiece[] =>
    index === 0 ? [part] : [{ base: ' - ' }, part],
  );
}

/**
 * Fills a translated template whose arguments are the private-use slots:
 * the words around the slots read in the base direction, and each slot
 * becomes its argument's pieces.
 */
function fill(
  template: string,
  args: Readonly<Record<string, readonly ɵTmCanvasPiece[]>>,
): ɵTmCanvasPiece[] {
  const pieces: ɵTmCanvasPiece[] = [];
  let words = '';
  for (const character of template) {
    const arg = args[character];
    if (arg === undefined) {
      words += character;
      continue;
    }
    if (words !== '') {
      pieces.push({ base: words });
      words = '';
    }
    pieces.push(...arg);
  }
  if (words !== '') {
    pieces.push({ base: words });
  }
  return pieces;
}

/** The index a private id (`s3`, `c12`) carries. */
function indexOf(id: string | number): number {
  return Number(String(id).slice(1));
}

/**
 * A style's decal for a slot, in the decal colour, to spread into the
 * style; nothing where decals do not apply. The key is left out rather than
 * set to `'none'`: ECharts types allow `'none'` on a style, but its style
 * visual marks the decal dirty by assigning a property to it, which throws
 * on a string.
 */
function decalFor(
  slot: number,
  color: string,
  applies: boolean,
): { readonly decal?: DecalPattern } {
  if (!applies) {
    return {};
  }
  const pattern = DECALS[(slot + 3 * Math.floor(slot / DECALS.length)) % DECALS.length]!;
  return { decal: { ...pattern, color } as DecalPattern };
}

/**
 * A value-axis bound that widens the data's extent to take in every
 * threshold line, or `undefined` (ECharts' own bound) when there is none.
 */
function extentWith(thresholds: readonly TmChartThreshold[], edge: 'min' | 'max') {
  const values = thresholds.map((threshold) => threshold.value);
  if (values.length === 0) {
    return undefined;
  }
  const bound = edge === 'min' ? Math.min(...values) : Math.max(...values);
  return (extent: { min: number; max: number }) =>
    edge === 'min' ? Math.min(extent.min, bound) : Math.max(extent.max, bound);
}

/**
 * Builds the ECharts option for a spec of a type other than `card`, and the
 * maps from the option's series and data indexes back to the spec's
 * members, which the adapter resolves clicks with.
 *
 * @param input - The spec, the tokens, the locale, the direction, the size and the templates.
 * @returns The option and its index maps.
 */
export function ɵtmBuildChartOption(input: ɵTmChartPlotInput): ɵTmChartPlot {
  const { spec, theme, locale, direction } = input;
  const options = spec.options ?? {};
  const type = spec.type;
  const pie = type === 'pie' || type === 'doughnut';
  const horizontal = type === 'bar';
  const lines = type === 'line' || type === 'area';
  const text = (pieces: readonly ɵTmCanvasPiece[]): string => ɵtmCanvasText(pieces, direction);

  // Section: number formatting: values per the measure, shares as percents,
  // axis ticks compact (percent ticks for a percent measure or a normalized stack).
  const format = spec.measure.format;
  const valueText = (value: number): string => tmFormatNumber(value, locale, format);
  const shareText = (share: number): string =>
    tmFormatNumber(share, locale, { percent: true, maxDecimals: 1 });
  const compact = new Intl.NumberFormat(locale, { notation: 'compact' });
  const stack = type === 'line' || pie ? 'none' : (options.stack ?? 'none');
  const normalized = stack === 'normalized';
  const tickText = (value: number): string =>
    normalized || format?.percent === true
      ? tmFormatNumber(value, locale, { percent: true, maxDecimals: 1 })
      : compact.format(value);

  // Section: the values, by series and category.
  const categoryDimension = spec.dimensions[0];
  const seriesDimension = pie ? undefined : spec.dimensions[1];
  const categoryCount = categoryDimension?.members.length ?? 0;
  const seriesCount = seriesDimension?.members.length ?? 1;
  const values: (number | null)[][] = Array.from({ length: seriesCount }, () =>
    new Array<number | null>(categoryCount).fill(null),
  );
  let points = 0;
  for (const [category, series, value] of spec.rows) {
    if (category === null || value === null || category >= categoryCount) {
      continue;
    }
    const row = values[seriesDimension === undefined ? 0 : (series ?? 0)];
    if (row !== undefined) {
      row[category] = value;
      points++;
    }
  }

  // Section: shares: of the category's plotted values in a normalized stack,
  // of the whole in a pie, both over absolute values.
  const shares = values.map((row) => row.slice());
  if (normalized) {
    for (let c = 0; c < categoryCount; c++) {
      const total = values.reduce((sum, row) => sum + Math.abs(row[c] ?? 0), 0);
      for (const row of shares) {
        row[c] = row[c] === null || total === 0 ? null : row[c]! / total;
      }
    }
  }
  const pieTotal = pie
    ? values[0]!.reduce<number>((sum, value) => sum + Math.max(0, value ?? 0), 0)
    : 0;

  // Section: the series (axis types) or slices (pies), with their legend text.
  const measureRuns: readonly ɵTmCanvasPiece[] = [spec.measure.label];
  const slices = pie
    ? values[0]!.flatMap((value, member) => (value !== null && value > 0 ? [member] : []))
    : [];
  const entries: Series[] = (
    pie
      ? slices.map((member) => ({ member, runs: memberRuns(categoryDimension?.members[member]) }))
      : values.map((_, s) => ({
          member: seriesDimension === undefined ? null : s,
          runs:
            seriesDimension === undefined ? measureRuns : memberRuns(seriesDimension.members[s]),
        }))
  ).map((entry, index) => {
    const plain = entry.runs
      .map((piece) => (typeof piece === 'string' ? piece : piece.base))
      .join('');
    return {
      id: `s${index}`,
      member: entry.member,
      runs: entry.runs,
      text: text(entry.runs),
      width: input.measureText(plain),
    };
  });

  // Section: colours and decals per slot (spec 0034 §7.7). Categorical slots
  // follow members; the ramp spreads over the series; diverging colours one
  // series' marks by sign. Decals apply where colour alone separates series.
  const palette = options.palette ?? 'categorical';
  const diverging = palette === 'diverging' && !pie && seriesCount === 1;
  // The theme's ramp holds only the steps that meet 3:1 against the ground.
  const ramp = theme.sequential;
  const slotColor = (slot: number): string => {
    if (palette === 'sequential' && !diverging) {
      const steps = ramp.length;
      const count = entries.length;
      const step = count === 1 ? steps - 1 : Math.round((slot * (steps - 1)) / (count - 1));
      return ramp[Math.min(step, steps - 1)]!;
    }
    return theme.series[slot % theme.series.length]!;
  };
  // Lines differ by marker shape instead. An area's fill takes the decal
  // from the series' item style, which feeds ECharts' decal visual; its
  // area style has no decal of its own.
  const decals = type !== 'line' && (input.forceDecals || pie || entries.length >= 2);
  // A line's markers show wherever colour alone would tell its series
  // apart, or its points apart under the diverging palette: two or more
  // lines, forced colours or more contrast, or colour by sign. Otherwise
  // the spec's `markers` decides.
  const markers =
    options.markers === true ||
    (lines && diverging) ||
    (type === 'line' && (entries.length >= 2 || input.forceDecals));
  const signColor = (value: number | null): string =>
    value !== null && value < 0 ? theme.divergingNegative : theme.divergingPositive;

  // Section: the legend (spec 0034 §7.5): at the logical side, its item
  // order following the page. Horizontal legends are laid out here, a
  // legend component per line, so a right-to-left page reads its first
  // item at the right of the first line.
  const legendSetting = options.legend ?? 'auto';
  const legendSide =
    legendSetting === 'auto' ? (entries.length >= 2 ? 'bottom' : 'none') : legendSetting;
  const physicalSide =
    legendSide === 'start'
      ? direction === 'rtl'
        ? 'right'
        : 'left'
      : legendSide === 'end'
        ? direction === 'rtl'
          ? 'left'
          : 'right'
        : legendSide;
  const lineHeight = Math.max(ICON_HEIGHT, Math.ceil(theme.fontSize * 1.4)) + 6;
  const itemWidth = (entry: Series): number => ICON_WIDTH + ICON_GAP + entry.width;
  const legendIcon = (index: number): string | undefined =>
    lines ? SYMBOLS[index % SYMBOLS.length] : pie ? 'circle' : 'roundRect';
  const legendBase: LegendComponentOption = {
    selectedMode: false,
    itemWidth: ICON_WIDTH,
    itemHeight: ICON_HEIGHT,
    itemGap: ITEM_GAP,
    padding: 0,
    align: direction === 'rtl' ? 'right' : 'left',
    textStyle: { color: theme.label, fontFamily: theme.fontFamily, fontSize: theme.fontSize },
    formatter: (name: string) => entries[indexOf(name)]?.text ?? '',
  };
  const legends: LegendComponentOption[] = [];
  let legendBlock = 0;
  let legendInline = 0;
  if (physicalSide === 'top' || physicalSide === 'bottom') {
    // Lines are packed in logical order; a line's items are reversed for a
    // right-to-left page, and the lines stack away from the plot.
    const available = Math.max(1, input.width - 2 * PAD);
    const rows: Series[][] = [];
    let row: Series[] = [];
    let used = 0;
    for (const entry of entries) {
      const width = itemWidth(entry);
      if (row.length > 0 && used + ITEM_GAP + width > available) {
        rows.push(row);
        row = [];
        used = 0;
      }
      used += (row.length > 0 ? ITEM_GAP : 0) + width;
      row.push(entry);
    }
    if (row.length > 0) {
      rows.push(row);
    }
    const maxRows = Math.max(1, Math.floor((input.height * SIDE_SHARE) / lineHeight));
    if (rows.length > maxRows) {
      // Too many lines for the box: one paged line instead. Its items read
      // from the start side: reversed for a right-to-left page, which opens
      // on the page holding the first item. The engine starts its window at
      // the item it is given and never fills the window back, so that page
      // is found here: the items that fit the window, walking back from the
      // last.
      const paged = entries.map((entry, index) => ({ name: entry.id, icon: legendIcon(index) }));
      let start = 0;
      if (direction === 'rtl') {
        let room = available - PAGER - input.measureText(`${paged.length}/${paged.length}`);
        start = entries.length - 1;
        room -= itemWidth(entries[0]!);
        while (start > 0 && room >= ITEM_GAP + itemWidth(entries[entries.length - start]!)) {
          room -= ITEM_GAP + itemWidth(entries[entries.length - start]!);
          start--;
        }
      }
      legends.push({
        ...legendBase,
        type: 'scroll',
        [physicalSide]: PAD,
        left: 'center',
        width: available,
        pageIconColor: theme.axis,
        pageIconInactiveColor: theme.grid,
        pageTextStyle: { color: theme.label },
        data: direction === 'rtl' ? paged.reverse() : paged,
        scrollDataIndex: start,
      });
      legendBlock = lineHeight;
    } else {
      rows.forEach((line, index) => {
        const ordered = direction === 'rtl' ? line.slice().reverse() : line;
        legends.push({
          ...legendBase,
          type: 'plain',
          [physicalSide]:
            PAD + (physicalSide === 'top' ? index : rows.length - 1 - index) * lineHeight,
          left: 'center',
          data: ordered.map((entry) => ({
            name: entry.id,
            icon: legendIcon(entries.indexOf(entry)),
          })),
        });
      });
      legendBlock = rows.length * lineHeight;
    }
  } else if (physicalSide === 'left' || physicalSide === 'right') {
    // A vertical legend at the physical side the logical one maps to.
    const widest = Math.max(0, ...entries.map(itemWidth));
    legendInline = Math.min(Math.ceil(widest), Math.floor(input.width * SIDE_SHARE));
    legends.push({
      ...legendBase,
      type: 'scroll',
      orient: 'vertical',
      [physicalSide]: PAD,
      top: 'middle',
      height: Math.max(lineHeight, input.height - 2 * PAD),
      itemGap: 8,
      pageIconColor: theme.axis,
      pageIconInactiveColor: theme.grid,
      pageTextStyle: { color: theme.label },
      textStyle: {
        ...legendBase.textStyle,
        width: Math.max(0, legendInline - ICON_WIDTH - ICON_GAP),
        overflow: 'truncate',
      },
      data: entries.map((entry, index) => ({ name: entry.id, icon: legendIcon(index) })),
    });
  }

  // Section: the plot's box, clear of the legend.
  const box = {
    left: PAD + (physicalSide === 'left' ? legendInline + ITEM_GAP : 0),
    right: PAD + (physicalSide === 'right' ? legendInline + ITEM_GAP : 0),
    top: PAD + (physicalSide === 'top' ? legendBlock + PAD : 0),
    bottom: PAD + (physicalSide === 'bottom' ? legendBlock + PAD : 0),
  };

  // Section: the tooltip (spec 0034 §7.5): rich text drawn on the canvas,
  // a line per token, per category for axis types and per item for pies.
  const point = input.templates.point;
  const shareTemplate = input.templates.share;
  const pointLine = (label: readonly ɵTmCanvasPiece[], value: readonly ɵTmCanvasPiece[]): string =>
    text(fill(point, { [ɵTM_CHART_LABEL_SLOT]: label, [ɵTM_CHART_VALUE_SLOT]: value }));
  const withShare = (share: number, value: number): ɵTmCanvasPiece[] =>
    fill(shareTemplate, {
      [ɵTM_CHART_SHARE_SLOT]: [shareText(share)],
      [ɵTM_CHART_VALUE_SLOT]: [valueText(value)],
    });
  const tooltipFormatter = (raw: unknown): string => {
    const params = (Array.isArray(raw) ? raw : [raw]) as PointParams[];
    const first = params[0];
    if (first === undefined) {
      return '';
    }
    if (pie) {
      // A slice: its member, then the measure with the share and the value.
      const member = slices[first.dataIndex];
      const value = member === undefined ? null : values[0]![member]!;
      if (member === undefined || value === null) {
        return '';
      }
      return [
        text(memberRuns(categoryDimension?.members[member])),
        pointLine(measureRuns, withShare(value / pieTotal, value)),
      ].join('\n');
    }
    // A category: its member, then a line per series with a value.
    const lines: string[] = [text(memberRuns(categoryDimension?.members[first.dataIndex]))];
    for (const param of params) {
      const s = param.seriesIndex ?? 0;
      const value = values[s]?.[param.dataIndex] ?? null;
      if (value === null) {
        continue;
      }
      const share = shares[s]![param.dataIndex] ?? null;
      const shown = normalized && share !== null ? withShare(share, value) : [valueText(value)];
      lines.push(pointLine(entries[s]!.runs, shown));
    }
    return lines.join('\n');
  };
  const tooltip: TooltipComponentOption = {
    show: true,
    trigger: pie ? 'item' : 'axis',
    renderMode: 'richText',
    confine: true,
    transitionDuration: 0,
    backgroundColor: theme.tooltipBg,
    borderColor: theme.tooltipBorder,
    borderWidth: 1,
    padding: [6, 10],
    textStyle: {
      color: theme.label,
      fontFamily: theme.fontFamily,
      fontSize: theme.fontSize,
      align: direction === 'rtl' ? 'right' : 'left',
    },
    axisPointer: {
      type: horizontal || type === 'column' ? 'shadow' : 'line',
      lineStyle: { color: theme.grid },
      shadowStyle: { color: theme.grid, opacity: 0.35 },
    },
    formatter: tooltipFormatter as TooltipComponentOption['formatter'],
  };

  // Section: the base: no animation, no automatic dark mode, the tokens'
  // text style, and aria for decals only, its generated label off.
  const aria: AriaComponentOption = {
    enabled: true,
    label: { enabled: false },
    decal: { show: decals },
  };
  const base: ChartOption = {
    animation: false,
    darkMode: false,
    backgroundColor: 'transparent',
    color: entries.map((_, slot) => slotColor(slot)),
    textStyle: { color: theme.label, fontFamily: theme.fontFamily, fontSize: theme.fontSize },
    aria,
    tooltip,
    legend: legends,
  };

  // Section: pies and rings: clockwise from 12 o'clock, slices by member.
  if (pie) {
    const width = input.width - box.left - box.right;
    const height = input.height - box.top - box.bottom;
    const outer =
      (Math.max(0, Math.min(width, height)) / 2) *
      (options.labels === true ? PIE_RADIUS_LABELLED : PIE_RADIUS);
    const series: PieSeriesOption = {
      type: 'pie',
      name: 'p',
      center: [box.left + width / 2, box.top + height / 2],
      radius: [type === 'doughnut' ? outer * RING_INNER : 0, outer],
      startAngle: 90,
      clockwise: true,
      avoidLabelOverlap: true,
      cursor: input.activatable ? 'pointer' : 'default',
      itemStyle: { borderColor: theme.surface, borderWidth: 2 },
      label: {
        show: options.labels === true,
        color: theme.label,
        formatter: (params: { dataIndex: number }) => {
          const member = slices[params.dataIndex]!;
          return text(
            fill(point, {
              [ɵTM_CHART_LABEL_SLOT]: memberRuns(categoryDimension?.members[member]),
              [ɵTM_CHART_VALUE_SLOT]: [shareText(values[0]![member]! / pieTotal)],
            }),
          );
        },
      },
      labelLine: { show: options.labels === true, lineStyle: { color: theme.grid } },
      emphasis: { scale: false },
      data: slices.map((member, slot) => ({
        name: entries[slot]!.id,
        value: values[0]![member]!,
        itemStyle: { color: slotColor(slot), ...decalFor(slot, theme.decal, decals) },
      })),
    } as PieSeriesOption;
    return {
      option: { ...base, series: [series] } as ɵTmChartOption,
      series: [null],
      categories: slices,
    };
  }

  // Section: the axes (spec 0034 §7.6): the plot never mirrors. The value
  // axis sits at the physical left (bottom for bars), categories run left to
  // right, and bars top to bottom.
  const categories = Array.from({ length: categoryCount }, (_, c) => c);
  const categoryTexts = categories.map((c) => text(memberRuns(categoryDimension?.members[c])));
  const thresholds = normalized ? [] : (options.thresholds ?? []);
  const categoryAxis = {
    type: 'category' as const,
    data: categories.map((c) => `c${c}`),
    inverse: horizontal,
    boundaryGap: !lines,
    axisLine: { lineStyle: { color: theme.grid } },
    axisTick: { alignWithLabel: true, lineStyle: { color: theme.grid } },
    axisLabel: {
      color: theme.axis,
      hideOverlap: true,
      formatter: (value: string | number) => categoryTexts[indexOf(value)] ?? '',
      ...(horizontal
        ? { width: Math.floor(input.width * SIDE_SHARE), overflow: 'truncate' as const }
        : {}),
    },
  };
  const valueAxis = {
    type: 'value' as const,
    min: normalized ? undefined : (options.min ?? extentWith(thresholds, 'min')),
    max: normalized ? undefined : (options.max ?? extentWith(thresholds, 'max')),
    axisLine: { show: false },
    axisTick: { show: false },
    splitLine: { lineStyle: { color: theme.grid } },
    axisLabel: {
      color: theme.axis,
      hideOverlap: true,
      formatter: (value: number) => text([tickText(value)]),
    },
  };

  // Section: thresholds: dashed lines in their tone on the value axis,
  // labelled with their value; carried by the first series.
  const markLine: MarkLineComponentOption | undefined =
    thresholds.length === 0
      ? undefined
      : ({
          silent: true,
          symbol: 'none',
          animation: false,
          data: thresholds.map((threshold) => ({
            [horizontal ? 'xAxis' : 'yAxis']: threshold.value,
            lineStyle: {
              color: theme.tones[TONES.includes(threshold.tone) ? threshold.tone : 'warning'],
              type: 'dashed',
              width: 1.5,
            },
            label: {
              show: true,
              position: 'insideEndTop',
              color: theme.label,
              formatter: () => text([valueText(threshold.value)]),
            },
          })),
        } as MarkLineComponentOption);

  // Section: the series: one per member of the second dimension, or one for
  // the measure; values (or shares) by category, labels in text tokens.
  const large = points > LARGE_POINTS;
  const stackName = stack === 'none' ? undefined : 'total';
  const labelPosition = stackName !== undefined ? 'inside' : horizontal ? 'right' : 'top';
  const series = entries.map((entry, s): BarSeriesOption | LineSeriesOption => {
    const plotted = normalized ? shares[s]! : values[s]!;
    const label = {
      show: options.labels === true,
      position: labelPosition,
      color: theme.label,
      textBorderColor: theme.surface,
      textBorderWidth: 2,
      formatter: (params: { dataIndex: number }) => {
        const value = values[s]![params.dataIndex] ?? null;
        const share = shares[s]![params.dataIndex] ?? null;
        if (value === null) {
          return '';
        }
        return text([normalized && share !== null ? shareText(share) : valueText(value)]);
      },
    };
    const color = slotColor(s);
    const common = {
      name: entry.id,
      stack: stackName,
      cursor: input.activatable ? 'pointer' : 'default',
      label,
      ...(s === 0 && markLine !== undefined ? { markLine } : {}),
    };
    if (lines) {
      const cycle = Math.floor(s / SYMBOLS.length);
      return {
        ...common,
        type: 'line',
        data: diverging
          ? plotted.map((value) => ({ value, itemStyle: { color: signColor(value) } }))
          : plotted,
        symbol: SYMBOLS[s % SYMBOLS.length],
        symbolSize: 8,
        showSymbol: markers,
        connectNulls: false,
        // The line and the area report clicks too; the adapter finds the
        // category under the pointer, so a point drills without its marker.
        triggerEvent: true,
        sampling: large ? 'lttb' : undefined,
        itemStyle: { color, ...decalFor(s, theme.decal, decals) },
        lineStyle: { color, width: 2, type: LINE_TYPES[cycle % LINE_TYPES.length] },
        ...(type === 'area'
          ? { areaStyle: { color, opacity: stackName !== undefined ? 0.85 : 0.3 } }
          : {}),
      } as LineSeriesOption;
    }
    return {
      ...common,
      type: 'bar',
      data: diverging
        ? plotted.map((value) => ({ value, itemStyle: { color: signColor(value) } }))
        : plotted,
      large,
      largeThreshold: LARGE_POINTS,
      barMaxWidth: 48,
      itemStyle: { color, ...decalFor(s, theme.decal, decals) },
    } as BarSeriesOption;
  });

  const grid: GridComponentOption = {
    ...box,
    outerBoundsMode: 'same',
    outerBoundsContain: 'axisLabel',
  };
  const option: ChartOption = {
    ...base,
    grid,
    xAxis: horizontal ? valueAxis : categoryAxis,
    yAxis: horizontal ? categoryAxis : valueAxis,
    series,
  };
  return {
    option: option as ɵTmChartOption,
    series: entries.map((entry) => entry.member),
    categories,
  };
}
