// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import { BarChart, LineChart, PieChart } from 'echarts/charts';
import {
  AriaComponent,
  GridComponent,
  LegendComponent,
  MarkLineComponent,
  TooltipComponent,
} from 'echarts/components';
import { init, use, type ECElementEvent, type EChartsType } from 'echarts/core';
import { CanvasRenderer } from 'echarts/renderers';
import type {
  ɵTmChartActivate,
  ɵTmChartEngine,
  ɵTmChartEngineFactory,
  ɵTmChartFail,
  ɵTmChartPlot,
  ɵTmChartRenderRequest,
} from './chart-plot';
import { ɵtmBuildChartOption } from './chart-option';
import { ɵtmReadChartTheme } from './chart-theme';

// The adapter (spec 0034 §7.3): the one module that imports ECharts, loaded
// with the engine on the first spec the chart draws. It registers exactly
// the modules the chart uses, draws on canvas only, and resolves clicks by
// series and data index, never by name.

use([
  BarChart,
  LineChart,
  PieChart,
  GridComponent,
  TooltipComponent,
  LegendComponent,
  MarkLineComponent,
  AriaComponent,
  CanvasRenderer,
]);

/** A point under the pointer: its series, and its data index where the mark has one. */
interface Hovered {
  /** The series under the pointer. */
  readonly seriesIndex: number;
  /** The point's index in its series, `undefined` over a line or an area away from a marker. */
  readonly dataIndex: number | undefined;
}

/** The context the adapter measures legend text with. */
let measuring: CanvasRenderingContext2D | null | undefined;

/** The advance of a string in a CSS font, px. */
function measure(font: string, text: string): number {
  if (measuring === undefined) {
    measuring = document.createElement('canvas').getContext('2d');
  }
  if (measuring === null) {
    return text.length * 7;
  }
  measuring.font = font;
  return measuring.measureText(text).width;
}

/**
 * Keeps the renderer's one HTML assignment within Trusted Types. zrender
 * empties its root with `innerHTML = ''` when it starts and when it is
 * disposed, which a page enforcing Trusted Types refuses without a policy.
 * The root gets its own accessor that empties it through the DOM for the
 * empty string and hands any other value to the native setter, which the
 * page's policy still governs. The returned function removes the accessor.
 */
function emptyThroughDom(element: HTMLElement): () => void {
  const native = Object.getOwnPropertyDescriptor(Element.prototype, 'innerHTML');
  if (native?.set === undefined || native.get === undefined) {
    return () => undefined;
  }
  const { get, set } = native;
  Object.defineProperty(element, 'innerHTML', {
    configurable: true,
    get(this: Element) {
      return get.call(this);
    },
    set(this: Element, value: unknown) {
      if (value === '') {
        this.replaceChildren();
      } else {
        set.call(this, value);
      }
    },
  });
  return () => {
    delete (element as { innerHTML?: string }).innerHTML;
  };
}

/** The text of a spec's labels, which the fonts must cover before anything is measured. */
function sampleText(request: ɵTmChartRenderRequest): string {
  const { spec } = request;
  const labels = spec.dimensions.flatMap((dimension) => [
    dimension.label,
    ...dimension.members.map((member) => member.label),
  ]);
  return `${spec.measure.label}${labels.join('')}0123456789`;
}

/**
 * Creates the engine instance of one chart in its plot element. ECharts
 * starts on the first render at a size, so a hidden chart costs nothing
 * until it shows; the fonts the chart's text needs load before the first
 * measure, since the renderer caches text widths per font.
 *
 * @param element - The plot element the canvas fills; it is the chart's alone.
 * @param activate - Called with a clicked point's members and the modifiers held.
 * @param fail - Called with the error of a draw made once the fonts loaded.
 * @returns The instance.
 */
export const ɵtmCreateChartEngine: ɵTmChartEngineFactory = (
  element: HTMLElement,
  activate: ɵTmChartActivate,
  fail: ɵTmChartFail,
): ɵTmChartEngine => {
  let chart: EChartsType | null = null;
  let restore: (() => void) | null = null;
  let plot: ɵTmChartPlot | null = null;
  let latest: ɵTmChartRenderRequest | null = null;
  let hovered: Hovered | null = null;
  let waiting: string | null = null;
  const settled = new Set<string>();
  let disposed = false;

  // Section: resolving a point. Marks carry their data index; a line or an
  // area reports only its series, so the category is read off the pointer.
  const membersOf = (
    seriesIndex: number,
    dataIndex: number | undefined,
    x: number,
    y: number,
  ): readonly [number | null, number | null] | null => {
    if (plot === null || chart === null) {
      return null;
    }
    let index = dataIndex;
    if (index === undefined) {
      const at = chart.convertFromPixel({ seriesIndex }, [x, y]);
      index = Array.isArray(at) && typeof at[0] === 'number' ? Math.round(at[0]) : undefined;
    }
    const category = index === undefined ? undefined : plot.categories[index];
    if (category === undefined) {
      return null;
    }
    return [category, plot.series[seriesIndex] ?? null];
  };

  // Section: the events: a click drills; the middle button over a point
  // drills to a new tab, and its press does not start auto-scrolling.
  const onClick = (params: ECElementEvent): void => {
    if (params.componentType !== 'series' || typeof params.seriesIndex !== 'number') {
      return;
    }
    const zrEvent = params.event;
    const native = zrEvent?.event as MouseEvent | undefined;
    const dataIndex = typeof params.dataIndex === 'number' ? params.dataIndex : undefined;
    const members = membersOf(params.seriesIndex, dataIndex, zrEvent?.offsetX ?? 0, zrEvent?.offsetY ?? 0);
    if (members !== null) {
      activate(members, {
        ctrlKey: native?.ctrlKey === true,
        metaKey: native?.metaKey === true,
        middle: false,
      });
    }
  };
  const onOver = (params: ECElementEvent): void => {
    hovered =
      params.componentType === 'series' && typeof params.seriesIndex === 'number'
        ? {
            seriesIndex: params.seriesIndex,
            dataIndex: typeof params.dataIndex === 'number' ? params.dataIndex : undefined,
          }
        : null;
  };
  const onOut = (): void => {
    hovered = null;
  };
  const onAuxClick = (event: MouseEvent): void => {
    if (event.button !== 1 || hovered === null) {
      return;
    }
    const box = element.getBoundingClientRect();
    const members = membersOf(
      hovered.seriesIndex,
      hovered.dataIndex,
      event.clientX - box.left,
      event.clientY - box.top,
    );
    if (members !== null) {
      event.preventDefault();
      activate(members, { ctrlKey: event.ctrlKey, metaKey: event.metaKey, middle: true });
    }
  };
  const onMouseDown = (event: MouseEvent): void => {
    if (event.button === 1 && hovered !== null) {
      event.preventDefault();
    }
  };
  element.addEventListener('auxclick', onAuxClick);
  element.addEventListener('mousedown', onMouseDown);

  // Section: drawing: the element's size, the tokens' values, the fonts.
  const draw = (): void => {
    const request = latest;
    const width = element.clientWidth;
    const height = element.clientHeight;
    if (disposed || request === null || width === 0 || height === 0) {
      return;
    }
    const theme = ɵtmReadChartTheme(element);
    const font = `${theme.fontSize}px ${theme.fontFamily}`;
    const sample = sampleText(request);
    const fonts = `${font}
${sample}`;
    if (!settled.has(fonts) && !document.fonts.check(font, sample)) {
      // The renderer caches text widths per font, so nothing is measured
      // until the faces this text needs have loaded, or failed to.
      if (waiting !== fonts) {
        waiting = fonts;
        const done = (): void => {
          settled.add(fonts);
          waiting = null;
          // This draw runs after render() returned, so its caller cannot
          // catch what it throws: the chart hears of it through `fail`.
          try {
            draw();
          } catch (error) {
            fail(error);
          }
        };
        document.fonts.load(font, sample).then(done, done);
      }
      return;
    }
    plot = ɵtmBuildChartOption({
      ...request,
      theme,
      width,
      height,
      measureText: (text) => measure(font, text),
    });
    if (chart === null) {
      restore = emptyThroughDom(element);
      chart = init(element, null, { renderer: 'canvas', width, height });
      chart.on('click', onClick);
      chart.on('mouseover', onOver);
      chart.on('mouseout', onOut);
      chart.on('globalout', onOut);
    } else if (chart.getWidth() !== width || chart.getHeight() !== height) {
      chart.resize({ width, height, animation: { duration: 0 } });
    }
    chart.setOption(plot.option, { notMerge: true });
  };

  return {
    render(request: ɵTmChartRenderRequest): void {
      latest = request;
      draw();
    },
    dispose(): void {
      disposed = true;
      element.removeEventListener('auxclick', onAuxClick);
      element.removeEventListener('mousedown', onMouseDown);
      chart?.dispose();
      chart = null;
      restore?.();
      restore = null;
    },
  };
};
