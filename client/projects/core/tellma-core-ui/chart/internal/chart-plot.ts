// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import type { TmChartSpec } from '../tm-chart-types';

// The seam between the facade and its lazily loaded engine: what the facade
// hands the adapter to draw, and what the adapter answers. Types only, so
// the facade's chunk imports nothing of the engine's.

/** The colours and the font the chart reads from its tokens, resolved to sRGB. */
export interface ɵTmChartTheme {
  /** The eight categorical series colours, in slot order. */
  readonly series: readonly string[];
  /**
   * The ramp steps that meet 3:1 against the plot's ground, the faintest
   * first; all seven when none does.
   */
  readonly sequential: readonly string[];
  /** The diverging palette's colour for a negative value. */
  readonly divergingNegative: string;
  /** The diverging palette's colour for a positive value or zero. */
  readonly divergingPositive: string;
  /** Grid lines, axis lines and the axis pointer. */
  readonly grid: string;
  /** Axis tick labels. */
  readonly axis: string;
  /** Every other text: legend, data labels, tooltip, mark-line labels. */
  readonly label: string;
  /** The threshold lines' colours by tone. */
  readonly tones: {
    readonly positive: string;
    readonly warning: string;
    readonly negative: string;
  };
  /** The surface behind the plot: the gaps between pie slices, hollow markers. */
  readonly surface: string;
  /** The tooltip's fill. */
  readonly tooltipBg: string;
  /** The tooltip's border. */
  readonly tooltipBorder: string;
  /** The decals' pattern colour. */
  readonly decal: string;
  /** The UI font family list. */
  readonly fontFamily: string;
  /** The text size, px. */
  readonly fontSize: number;
}

/**
 * Translated templates the canvas strings are built from, each with its
 * arguments replaced by the private-use slots below, so the engine splits
 * them into runs and base-direction words.
 */
export interface ɵTmChartTemplates {
  /** A value as a share: `{share}` and `{value}`. */
  readonly share: string;
  /** A labelled value: a tooltip line, a slice's label; `{label}` and `{value}`. */
  readonly point: string;
}

/** Stands in for a template's `{share}` argument. */
export const ɵTM_CHART_SHARE_SLOT = '\uE000';
/** Stands in for a template's `{value}` argument. */
export const ɵTM_CHART_VALUE_SLOT = '\uE001';
/** Stands in for a template's `{label}` argument. */
export const ɵTM_CHART_LABEL_SLOT = '\uE002';

/** What the facade asks the adapter to draw: the spec and the page's settings. */
export interface ɵTmChartRenderRequest {
  /** The spec, of a type other than `card`, with at least one point. */
  readonly spec: TmChartSpec;
  /** The formatting locale. */
  readonly locale: string;
  /** The page's direction, which canvas text starts from. */
  readonly direction: 'ltr' | 'rtl';
  /** Whether decals apply to every chart (forced colours or more contrast). */
  readonly forceDecals: boolean;
  /** Whether points drill: the pointer cursor over marks. */
  readonly activatable: boolean;
  /** The translated templates. */
  readonly templates: ɵTmChartTemplates;
}

/** Everything the option builder needs: the request, and what the adapter measured and read. */
export interface ɵTmChartPlotInput extends ɵTmChartRenderRequest {
  /** The resolved tokens. */
  readonly theme: ɵTmChartTheme;
  /** The plot's inline size, px. */
  readonly width: number;
  /** The plot's block size, px. */
  readonly height: number;
  /** The advance of a string in the chart's font, px; lays out the legend. */
  readonly measureText: (text: string) => number;
}

/** An ECharts option, as plain data and formatter callbacks. */
export type ɵTmChartOption = { readonly [key: string]: unknown };

/** What the adapter drew: the option, and how its indexes map back to members. */
export interface ɵTmChartPlot {
  /** The option handed to the engine. */
  readonly option: ɵTmChartOption;
  /** Each series' member in the second dimension, by series index; `null` for none. */
  readonly series: readonly (number | null)[];
  /** Each data index's member in the first dimension. */
  readonly categories: readonly number[];
}

/** The modifiers of a pointer activation, as the adapter saw them. */
export interface ɵTmChartPointerModifiers {
  /** Ctrl was held. */
  readonly ctrlKey: boolean;
  /** ⌘ (or the Windows key) was held. */
  readonly metaKey: boolean;
  /** The middle button made the activation. */
  readonly middle: boolean;
}

/** A drawn engine instance, owned by one chart until it is destroyed. */
export interface ɵTmChartEngine {
  /**
   * Draws a spec at the element's current size with the tokens' current
   * values, replacing whatever was drawn; an element without a size keeps
   * its drawing until it has one again. A failure throws; one in a draw the
   * instance makes later on its own, once the fonts have loaded, goes to
   * the factory's `fail` callback instead.
   */
  render(request: ɵTmChartRenderRequest): void;
  /** Releases the instance and empties its element. */
  dispose(): void;
}

/** Called when a point is clicked: its members, and the modifiers held. */
export type ɵTmChartActivate = (
  members: readonly [number | null, number | null],
  modifiers: ɵTmChartPointerModifiers,
) => void;

/** Called with the error of a draw the instance made on its own, after a render call returned. */
export type ɵTmChartFail = (error: unknown) => void;

/** The engine chunk's one export: creates an instance in an element. */
export type ɵTmChartEngineFactory = (
  element: HTMLElement,
  activate: ɵTmChartActivate,
  fail: ɵTmChartFail,
) => ɵTmChartEngine;
