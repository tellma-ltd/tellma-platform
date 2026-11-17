// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

/** A text direction. */
type Direction = 'ltr' | 'rtl';

/** The bidi marks the chart writes into canvas strings. */
const MARKS = /[\u200E\u200F]/g;

/** One run a label is expected to draw: its text, and the direction it reads in. */
export interface ProbeRun {
  /** The run's text. */
  readonly text: string;
  /** The direction the run reads in; the base direction for text between runs. */
  readonly direction: Direction;
}

/**
 * The runs a label is expected to draw, in logical order, from a source
 * that does not read the drawn string's marks; `null` for a label it does
 * not know.
 */
export type ProbeExpectation = (text: string) => readonly ProbeRun[] | null;

/** One label the engine drew, compared with its expected runs drawn apart. */
export interface ProbedLabel {
  /** The label without its marks. */
  readonly text: string;
  /** How many runs the label was expected to draw apart; 0 when the expectation does not know it. */
  readonly runs: number;
  /** How many bidi marks the engine's string carries. */
  readonly marks: number;
  /** How many canvas pixels differ between the engine's drawing and the runs drawn apart. */
  readonly differing: number;
  /** How many canvas pixels the label itself paints. */
  readonly painted: number;
  /** How many canvas pixels elements painted after the label cover, left out of both counts. */
  readonly covered: number;
}

/** The renderer's view of a drawn text span, as far as the probe reads and redraws it. */
interface Span {
  /** The element's kind; text spans are `'tspan'`. */
  readonly type: string;
  /** The span's transform to the canvas, before the device pixel ratio. */
  readonly transform: readonly number[] | null;
  /** The clipping paths the span is drawn through, if any. */
  readonly __clipPaths?: readonly unknown[];
  /** The span's string, anchor and paint. */
  readonly style: {
    readonly text?: string;
    readonly x?: number;
    readonly y?: number;
    readonly font?: string;
    readonly textAlign?: CanvasTextAlign;
    readonly textBaseline?: CanvasTextBaseline;
    readonly fill?: string;
    stroke?: string;
    readonly lineWidth?: number;
    readonly lineJoin?: CanvasLineJoin;
    opacity?: number;
    readonly strokeFirst?: boolean;
    readonly shadowBlur?: number;
    readonly shadowColor?: string;
  };
  /** Marks the span for the next repaint. */
  markRedraw(): void;
  /**
   * Whether the renderer paints the span: not ignored (itself or an
   * ancestor, as an axis hides a label that overlaps another), visible,
   * opaque at all and inside the view.
   */
  shouldBePainted(
    viewWidth: number,
    viewHeight: number,
    considerClipPath: boolean,
    considerAncestors: boolean,
  ): boolean;
}

/** The parts of an ECharts instance the probe uses. */
interface Engine {
  /** The renderer. */
  getZr(): {
    /** The scene, in paint order. */
    readonly storage: { getDisplayList(update?: boolean): readonly unknown[] };
    /** Repaints now. */
    refreshImmediately(): void;
  };
  /** The canvas' pixels per CSS pixel. */
  getDevicePixelRatio(): number;
}

/**
 * The segments expected runs draw apart in: text in the base direction next
 * to more of it reads as one, as one string's text between runs does.
 */
function segmentsOf(runs: readonly ProbeRun[], base: Direction): ProbeRun[] {
  const segments: ProbeRun[] = [];
  for (const run of runs) {
    const last = segments[segments.length - 1];
    if (run.text === '') {
      continue;
    }
    if (last !== undefined && last.direction === base && run.direction === base) {
      segments[segments.length - 1] = { text: last.text + run.text, direction: base };
    } else {
      segments.push(run);
    }
  }
  return segments;
}

/**
 * A copy of a canvas' pixels on a fresh canvas, for drawing over. Reading a
 * canvas back can move it off the GPU, which antialiases text a little
 * differently; a fresh copy draws as the engine's canvas does.
 */
function copyOf(canvas: HTMLCanvasElement): CanvasRenderingContext2D {
  const copy = document.createElement('canvas');
  copy.width = canvas.width;
  copy.height = canvas.height;
  const context = copy.getContext('2d')!;
  context.drawImage(canvas, 0, 0);
  return context;
}

/** A canvas' pixels. */
function pixelsOf(context: CanvasRenderingContext2D): Uint8ClampedArray {
  return context.getImageData(0, 0, context.canvas.width, context.canvas.height).data;
}

/** Counts the pixels two pixel arrays disagree on, leaving out those `skip` marks. */
function differing(a: Uint8ClampedArray, b: Uint8ClampedArray, skip?: Uint8Array): number {
  let count = 0;
  for (let offset = 0; offset < a.length; offset += 4) {
    if (skip?.[offset / 4] === 1) {
      continue;
    }
    if (
      a[offset] !== b[offset] ||
      a[offset + 1] !== b[offset + 1] ||
      a[offset + 2] !== b[offset + 2] ||
      a[offset + 3] !== b[offset + 3]
    ) {
      count++;
    }
  }
  return count;
}

/**
 * Draws a label's expected segments apart, each in its own direction, placed
 * in the base direction's order from where the span's alignment puts its
 * edge, with the paint the renderer gives the span. Every segment's first
 * pass (stroke or fill, in the span's order) comes before any second pass,
 * as one string's would.
 */
function drawApart(
  context: CanvasRenderingContext2D,
  span: Span,
  segments: readonly ProbeRun[],
  base: Direction,
  dpr: number,
): void {
  const { style } = span;
  const m = span.transform ?? [1, 0, 0, 1, 0, 0];
  context.setTransform(
    dpr * m[0]!,
    dpr * m[1]!,
    dpr * m[2]!,
    dpr * m[3]!,
    dpr * m[4]!,
    dpr * m[5]!,
  );
  context.font = style.font ?? '12px sans-serif';
  context.textBaseline = style.textBaseline ?? 'alphabetic';
  context.textAlign = 'left';
  context.globalAlpha = style.opacity ?? 1;
  context.shadowBlur = dpr * (style.shadowBlur ?? 0);
  context.shadowColor = style.shadowColor ?? 'transparent';
  context.lineJoin = style.lineJoin ?? 'miter';
  context.lineCap = 'butt';
  context.miterLimit = 10;
  // Section: each segment's advance, and the string's left edge.
  const widths = segments.map((segment) => {
    context.direction = segment.direction;
    return context.measureText(segment.text).width;
  });
  const total = widths.reduce((sum, width) => sum + width, 0);
  // The string's edge is found as the canvas aligns it: from the drawn
  // string's own advance, which its marks do not change (they are of zero
  // width), so the runs start where the engine's string did to the canvas'
  // precision. Only the width is read from the drawn string, never its
  // runs or their order.
  context.direction = base;
  const whole = context.measureText(style.text ?? '').width;
  const x = style.x ?? 0;
  const align = style.textAlign ?? 'left';
  const left =
    align === 'right' || align === 'end' ? x - whole : align === 'center' ? x - whole / 2 : x;
  // Section: the segments' left edges, in visual order.
  const lefts: number[] = [];
  let edge = base === 'rtl' ? left + total : left;
  segments.forEach((_, index) => {
    if (base === 'rtl') {
      edge -= widths[index]!;
      lefts.push(edge);
    } else {
      lefts.push(edge);
      edge += widths[index]!;
    }
  });
  // Section: the passes, in the renderer's order.
  const fill = style.fill !== undefined && style.fill !== 'none' ? style.fill : null;
  const stroke =
    style.stroke !== undefined && style.stroke !== 'none' && (style.lineWidth ?? 0) > 0
      ? style.stroke
      : null;
  const passes = style.strokeFirst ? (['stroke', 'fill'] as const) : (['fill', 'stroke'] as const);
  for (const pass of passes) {
    segments.forEach((segment, index) => {
      context.direction = segment.direction;
      if (pass === 'fill' && fill !== null) {
        context.fillStyle = fill;
        context.fillText(segment.text, lefts[index]!, style.y ?? 0);
      }
      if (pass === 'stroke' && stroke !== null) {
        context.strokeStyle = stroke;
        context.lineWidth = style.lineWidth!;
        context.strokeText(segment.text, lefts[index]!, style.y ?? 0);
      }
    });
  }
}

/**
 * Compares every label a chart's engine drew with its expected runs drawn
 * apart. The runs come from `expected`, which knows the chart's data, not
 * from the drawn string: a label that lost its marks draws its runs in
 * another order and compares unequal. For each text span the renderer
 * paints (an axis label hidden for overlapping another is not), the scene
 * is repainted without it, its runs are drawn apart over that, and the
 * result is compared with the engine's own drawing pixel for pixel, except where elements painted after the span
 * cover it. A span drawn through a clipping path is skipped: the
 * comparison would have to clip alike.
 *
 * Text borders (the halo a data label draws under its glyphs) are left off
 * every span while the probe draws, and put back after: a border stroked
 * across runs the canvas shapes apart overlaps itself where runs meet and
 * composites there by however the canvas groups the glyphs, which says
 * nothing about where the glyphs go. The glyphs' fill is compared exactly.
 *
 * @param plot - The chart's plot element, which the engine draws in.
 * @param base - The page's direction, which the chart drew from.
 * @param expected - The runs each label is expected to draw.
 * @returns Each compared label.
 */
export async function probeChartLabels(
  plot: HTMLElement,
  base: Direction,
  expected: ProbeExpectation,
): Promise<ProbedLabel[]> {
  const { getInstanceByDom } = await import('echarts/core');
  const engine = getInstanceByDom(plot) as unknown as Engine | undefined;
  const canvas = plot.querySelector('canvas');
  if (engine === undefined || canvas === null) {
    return [];
  }
  const zr = engine.getZr();
  const dpr = engine.getDevicePixelRatio();
  zr.refreshImmediately();
  const width = canvas.width / dpr;
  const height = canvas.height / dpr;
  const scene = zr.storage.getDisplayList(true) as Span[];
  const spans = scene.filter(
    (span) =>
      span.type === 'tspan' &&
      span.shouldBePainted(width, height, true, true) &&
      (span.style.text ?? '') !== '' &&
      (span.__clipPaths === undefined || span.__clipPaths.length === 0),
  );
  // Section: the text borders come off for the probe's drawings.
  const strokes = spans.map((span) => span.style.stroke);
  for (const span of spans) {
    span.style.stroke = undefined;
    span.markRedraw();
  }
  try {
    zr.refreshImmediately();
    return compare(zr, canvas, scene, spans, base, dpr, expected);
  } finally {
    // Section: the text borders go back, and the chart repaints as it was.
    spans.forEach((span, index) => {
      span.style.stroke = strokes[index];
      span.markRedraw();
    });
    zr.refreshImmediately();
  }
}

/**
 * Compares each span the engine drew in the current scene with its runs
 * drawn apart over the scene without it.
 */
function compare(
  zr: ReturnType<Engine['getZr']>,
  canvas: HTMLCanvasElement,
  scene: readonly Span[],
  spans: readonly Span[],
  base: Direction,
  dpr: number,
  expected: ProbeExpectation,
): ProbedLabel[] {
  const drawn = pixelsOf(copyOf(canvas));
  // Repaints the scene with some elements hidden by their own opacity (a
  // text element copies its own visibility onto its spans whenever it
  // updates, so a span's own flag would not hold), and copies the canvas.
  const without = (hidden: readonly Span[]): CanvasRenderingContext2D => {
    const opacities = hidden.map((element) => element.style.opacity);
    for (const element of hidden) {
      element.style.opacity = 0;
      element.markRedraw();
    }
    zr.refreshImmediately();
    const copy = copyOf(canvas);
    hidden.forEach((element, index) => {
      element.style.opacity = opacities[index];
      element.markRedraw();
    });
    zr.refreshImmediately();
    return copy;
  };
  const results: ProbedLabel[] = [];
  for (const span of spans) {
    // Section: the scene without the span, then the span's runs drawn apart over it.
    const bare = without([span]);
    const ground = pixelsOf(bare);
    // A label the expectation does not know is drawn as one base run, and
    // reported with no runs.
    const text = (span.style.text ?? '').replace(MARKS, '');
    const runs = expected(text);
    const apart = copyOf(bare.canvas);
    drawApart(apart, span, segmentsOf(runs ?? [{ text, direction: base }], base), base, dpr);
    const reference = pixelsOf(apart);
    // Section: what the elements painted after the span cover. The runs are
    // drawn over the finished scene, the engine drew the span beneath them,
    // so those pixels say nothing about the span and are left out. The
    // scene is transparent, so the later elements painted alone show where
    // they paint, whatever their colour against the ground.
    const index = scene.indexOf(span);
    const later = pixelsOf(without(scene.slice(0, index + 1)));
    const covered = new Uint8Array(ground.length / 4);
    let coveredCount = 0;
    for (let pixel = 0; pixel < covered.length; pixel++) {
      if (index + 1 < scene.length && later[pixel * 4 + 3] !== 0) {
        covered[pixel] = 1;
        coveredCount++;
      }
    }
    results.push({
      text,
      runs: runs?.length ?? 0,
      marks: (span.style.text ?? '').match(MARKS)?.length ?? 0,
      differing: differing(drawn, reference, covered),
      painted: differing(drawn, ground, covered),
      covered: coveredCount,
    });
  }
  return results;
}
