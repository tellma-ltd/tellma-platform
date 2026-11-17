// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

// The chart spec (spec 0034 §7.1): plain JSON, so an agent or the MCP server
// can write one, and a report renderer builds one from a result. Rows index
// the member lists; every label is text, never markup.

/** What a chart draws: bars, lines, a disc or a ring, or a single value. */
export type TmChartType = 'column' | 'bar' | 'line' | 'area' | 'pie' | 'doughnut' | 'card';

/** One member of a dimension: a category on the axis, a series, or a slice. */
export interface TmChartMember {
  /** The member's text. */
  readonly label: string;
  /**
   * A display name's parts, such as a code and a name, which joined equal
   * {@link TmChartMember.label}. Each part takes its own direction when the
   * chart draws it.
   */
  readonly parts?: readonly string[];
}

/** One dimension of a chart: the categories, or the series. */
export interface TmChartDimension {
  /** The dimension's name, as the table view's column header. */
  readonly label: string;
  /** The members, in display order. */
  readonly members: readonly TmChartMember[];
}

/** How the measure's values are written. */
export interface TmChartNumberFormat {
  /** The fewest fraction digits shown. */
  readonly minDecimals?: number;
  /** The most fraction digits shown. */
  readonly maxDecimals?: number;
  /** Whether digits are grouped ("1,234"); `true` unless set. */
  readonly grouping?: boolean;
  /** Whether the value is a fraction shown as a percent. */
  readonly percent?: boolean;
}

/** The one measure a chart plots on its one value axis. */
export interface TmChartMeasure {
  /** The measure's name. */
  readonly label: string;
  /** How its values are written in labels, tooltips and the table view. */
  readonly format?: TmChartNumberFormat;
}

/**
 * One point: the index of its member in the first dimension, in the
 * second, and its value. An index is `null` where the chart has no such
 * dimension; a value is `null` where the point has none.
 */
export type TmChartRow = readonly [d0: number | null, d1: number | null, value: number | null];

/** How series share a category: side by side, stacked, or stacked as shares of the category. */
export type TmChartStack = 'none' | 'stacked' | 'normalized';

/**
 * Where the legend sits. `start` and `end` are logical sides, which follow
 * the page's direction; `auto` puts it at the bottom for two or more series
 * and shows none for one.
 */
export type TmChartLegend = 'auto' | 'none' | 'top' | 'bottom' | 'start' | 'end';

/**
 * How series are coloured: `categorical` (a hue per member, in member
 * order), `sequential` (the magnitude ramp, spread over the series) or
 * `diverging` (one series' marks coloured by their sign).
 */
export type TmChartPalette = 'categorical' | 'sequential' | 'diverging';

/** The status tone a threshold is drawn in. */
export type TmChartThresholdTone = 'positive' | 'warning' | 'negative';

/** A value worth marking on the value axis. */
export interface TmChartThreshold {
  /** Where on the value axis the line sits. */
  readonly value: number;
  /** The tone of its line, or of the card's badge once the value reaches it. */
  readonly tone: TmChartThresholdTone;
}

/** The presentation options of a chart. */
export interface TmChartOptions {
  /** How series share a category; `none` unless set. Applies to `column`, `bar` and `area`. */
  readonly stack?: TmChartStack;
  /** Where the legend sits; `auto` unless set. */
  readonly legend?: TmChartLegend;
  /** Whether `line` and `area` draw a marker at each point. */
  readonly markers?: boolean;
  /** Whether marks are labelled with their values (`pie` and `doughnut`: the member and its share). */
  readonly labels?: boolean;
  /** How series are coloured; `categorical` unless set. */
  readonly palette?: TmChartPalette;
  /** Fixes the low end of the value axis. */
  readonly min?: number;
  /** Fixes the high end of the value axis. */
  readonly max?: number;
  /** Values marked on the value axis by a dashed line in a status tone. */
  readonly thresholds?: readonly TmChartThreshold[];
}

/**
 * A chart, as JSON: its type, its accessible title, zero to two
 * dimensions, one measure, the points and the presentation options.
 *
 * | Type | Dimensions |
 * |---|---|
 * | `column`, `bar`, `line`, `area` | 1 or 2: the categories, then the series |
 * | `pie`, `doughnut` | 1: the slices |
 * | `card` | 0: one row holding the value |
 *
 * @example
 * ```ts
 * const spec: TmChartSpec = {
 *   type: 'column',
 *   title: 'Monthly sales',
 *   dimensions: [
 *     { label: 'Month', members: [{ label: 'Sep 2026' }, { label: 'Oct 2026' }] },
 *     { label: 'Customer type', members: [{ label: 'Corporate' }, { label: 'Retail' }] },
 *   ],
 *   measure: { label: 'Sales', format: { maxDecimals: 0 } },
 *   rows: [[0, 0, 365000], [0, 1, 120000], [1, 0, 298000], [1, 1, null]],
 *   options: { stack: 'stacked' },
 * };
 * ```
 */
export interface TmChartSpec {
  /** What the chart draws. */
  readonly type: TmChartType;
  /** The chart's accessible name. Hosts draw visible titles themselves. */
  readonly title: string;
  /** Zero to two dimensions, in display order. */
  readonly dimensions: readonly TmChartDimension[];
  /** The measure the value axis plots. */
  readonly measure: TmChartMeasure;
  /** One row per point. */
  readonly rows: readonly TmChartRow[];
  /** The presentation options. */
  readonly options?: TmChartOptions;
}

/**
 * A point or a table row the user opened: the indexes of its members in
 * the two dimensions (`null` where the chart has no such dimension), and
 * whether the consumer should open a new tab (Ctrl or ⌘ held, or the
 * middle button).
 */
export interface TmChartPointActivation {
  /** The point's member indexes in the first and the second dimension. */
  readonly members: readonly [number | null, number | null];
  /** Whether the consumer opens the result in a new tab. */
  readonly newTab: boolean;
}

/**
 * One problem {@link tmValidateChartSpec} found: where (a JSON Pointer into
 * the spec, `''` for the spec itself) and what (a stable code).
 */
export interface TmChartSpecProblem {
  /** The JSON Pointer of the offending value, such as `/rows/3/0`. */
  readonly pointer: string;
  /** What is wrong, as a stable code such as `required` or `bidi-control`. */
  readonly code: string;
}

/** A JSON value, as plain `JSON.parse` produces it. */
export type TmChartJson =
  | null
  | boolean
  | number
  | string
  | readonly TmChartJson[]
  | { readonly [key: string]: TmChartJson };

/** A JSON Schema (draft 2020-12) object schema, as plain JSON. */
export type TmChartJsonSchema = { readonly [key: string]: TmChartJson };
