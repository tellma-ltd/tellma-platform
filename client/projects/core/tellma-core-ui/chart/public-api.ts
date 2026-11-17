/**
 * Charts from a JSON spec: columns, bars, lines, areas, pies, doughnuts
 * and single-value cards, drawn on a canvas by an engine that loads on the
 * first chart the page draws, with a table view as the keyboard path, the
 * spec's JSON Schema and its validator.
 *
 * @packageDocumentation
 */

// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

export { TmChart } from './tm-chart';
// The canvas text the engine draws with, for the browser battery that
// checks its marks against each engine's canvas.
export { ɵtmCanvasText, type ɵTmCanvasPiece } from './internal/chart-text';
export { TM_CHART_SPEC_SCHEMA, tmValidateChartSpec } from './tm-chart-schema';
export { TM_CHART_STRINGS_EN } from './tm-chart-strings';
export type {
  TmChartDimension,
  TmChartJson,
  TmChartJsonSchema,
  TmChartLegend,
  TmChartMeasure,
  TmChartMember,
  TmChartNumberFormat,
  TmChartOptions,
  TmChartPalette,
  TmChartPointActivation,
  TmChartRow,
  TmChartSpec,
  TmChartSpecProblem,
  TmChartStack,
  TmChartThreshold,
  TmChartThresholdTone,
  TmChartType,
} from './tm-chart-types';
