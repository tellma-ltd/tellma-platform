// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import { Directionality } from '@angular/cdk/bidi';
import {
  afterNextRender,
  Component,
  ElementRef,
  inject,
  signal,
  viewChildren,
} from '@angular/core';

import { TM_ACTIVE_LOCALE } from '@tellma/core-ui';
import { TmButton } from '@tellma/core-ui/button';
import {
  TmChart,
  ɵtmCanvasText,
  type TmChartMember,
  type TmChartSpec,
  type ɵTmCanvasPiece,
} from '@tellma/core-ui/chart';
import { tmFormatNumber } from '@tellma/core-ui/l10n';

import {
  onTheLine,
  probeChartLabels,
  type ProbedLabel,
  type ProbeExpectation,
  type ProbeRun,
} from './chart-bidi-probe';

/** A text direction. */
type Direction = 'ltr' | 'rtl';

/** One canvas string: its pieces, drawn from a base direction. */
interface BidiCase {
  /** The case's id, in its canvases' test ids. */
  readonly id: string;
  /** What the case shows. */
  readonly title: string;
  /** The string's pieces, in logical order. */
  readonly pieces: readonly ɵTmCanvasPiece[];
}

/** The canvas size, px. */
const WIDTH = 360;
/** The canvas height, px. */
const HEIGHT = 36;
/** The inset of the text from the canvas' start edge, px. */
const INSET = 8;
/** The font size, px. */
const FONT_SIZE = 16;

/** The strings the chart draws on canvas: display names by parts, numbers, a tooltip line. */
const CASES: readonly BidiCase[] = [
  { id: 'code-name', title: 'A code and a Latin name', pieces: ['1100', ' - ', 'Cash'] },
  { id: 'name-code', title: 'A Latin name and a code', pieces: ['Cash', ' ', '1100'] },
  { id: 'code-arabic', title: 'A code and an Arabic name', pieces: ['1100', ' ', 'النقدية'] },
  {
    id: 'arabic-latin',
    title: 'An Arabic and a Latin part',
    pieces: ['النقدية', ' ', 'Cash (USD)'],
  },
  {
    id: 'negative-arabic',
    title: 'A negative value in Arabic-Indic digits',
    pieces: [tmFormatNumber(-1234.5, 'ar-EG', { maxDecimals: 1 })],
  },
  {
    id: 'percent-arabic',
    title: 'A negative share in Arabic-Indic digits',
    pieces: [tmFormatNumber(-0.25, 'ar-EG', { percent: true })],
  },
  {
    id: 'tooltip-line',
    title: 'A tooltip line: a label, the template’s words, a value',
    pieces: ['B2C', { base: ': ' }, tmFormatNumber(-35000, 'ar-EG')],
  },
];

/**
 * Mixed labels through the chart itself: categories by parts (a code and an
 * Arabic name, a Latin name and a code), a Latin label with brackets, series
 * in Arabic and in Latin with Arabic, a negative value labelled on its mark,
 * and a threshold labelled with its value.
 */
const COLUMN: TmChartSpec = {
  type: 'column',
  title: 'المبيعات',
  dimensions: [
    {
      label: 'الحساب',
      members: [
        { label: '1100 - النقدية', parts: ['1100', 'النقدية'] },
        { label: 'Cash (USD)' },
        { label: 'Cash - 1100', parts: ['Cash', '1100'] },
      ],
    },
    {
      label: 'نوع العميل',
      members: [{ label: 'شركات' }, { label: 'B2C - أفراد', parts: ['B2C', 'أفراد'] }],
    },
  ],
  measure: { label: 'المبيعات', format: { maxDecimals: 0 } },
  rows: [
    [0, 0, 42000],
    [0, 1, -35000],
    [1, 0, 18000],
    [1, 1, 27000],
    [2, 0, -12500],
    [2, 1, 9000],
  ],
  options: { labels: true, legend: 'top', thresholds: [{ value: 30000, tone: 'positive' }] },
};

/** Mixed slice labels: each slice's member and share. */
const PIE: TmChartSpec = {
  type: 'pie',
  title: 'الحصص',
  dimensions: [
    {
      label: 'نوع العميل',
      members: [
        { label: 'شركات' },
        { label: 'B2C - أفراد', parts: ['B2C', 'أفراد'] },
        { label: 'Cash (USD)' },
      ],
    },
  ],
  measure: { label: 'المبيعات', format: { maxDecimals: 0 } },
  rows: [
    [0, null, 42000],
    [1, null, 27000],
    [2, null, 18000],
  ],
  options: { labels: true, legend: 'none' },
};

/** The facade's charts in one locale: a labelled column chart and a labelled pie. */
const FACADE_TEMPLATE = `
  <div class="demo-facade">
    <tm-chart class="demo-facade-chart" [attr.data-testid]="'facade-column-' + id" [spec]="column" />
    <tm-chart class="demo-facade-chart" [attr.data-testid]="'facade-pie-' + id" [spec]="pie" />
  </div>
`;

/** The facade's charts' layout. */
const FACADE_STYLES = `
  .demo-facade {
    display: flex;
    flex-wrap: wrap;
    gap: 16px;
  }

  .demo-facade-chart {
    inline-size: 420px;
    block-size: 260px;
  }
`;

/** The facade's charts in Arabic, whose digits Chromium writes in Latin. */
@Component({
  selector: 'app-demo-bidi-ar',
  imports: [TmChart],
  providers: [{ provide: TM_ACTIVE_LOCALE, useValue: signal('ar') }],
  template: FACADE_TEMPLATE,
  styles: FACADE_STYLES,
})
class FacadeAr {
  /** The locale's id in the charts' test ids. */
  readonly id = 'ar';
  /** The column chart. */
  readonly column = COLUMN;
  /** The pie. */
  readonly pie = PIE;
}

/** The facade's charts in Egyptian Arabic, whose digits are Arabic-Indic. */
@Component({
  selector: 'app-demo-bidi-ar-eg',
  imports: [TmChart],
  providers: [{ provide: TM_ACTIVE_LOCALE, useValue: signal('ar-EG') }],
  template: FACADE_TEMPLATE,
  styles: FACADE_STYLES,
})
class FacadeArEg {
  /** The locale's id in the charts' test ids. */
  readonly id = 'ar-eg';
  /** The column chart. */
  readonly column = COLUMN;
  /** The pie. */
  readonly pie = PIE;
}

/** A run's direction, as the canvas text reads it: its first letter's, else left to right. */
function runDirection(text: string): Direction {
  const letter = /\p{L}/u.exec(text)?.[0];
  return letter !== undefined && /[\p{Script=Arabic}\p{Script=Hebrew}]/u.test(letter)
    ? 'rtl'
    : 'ltr';
}

/** A number as the chart formats one: a sign, digits and their separators, perhaps a percent. */
const NUMBER = /^[-+]?[0-9\u0660-\u0669][0-9\u0660-\u0669,.\u066B\u066C]*[%\u066A]?$/;
/** A compact number: a number, then a word, such as a thousand in Arabic. */
const COMPACT = /^[-+]?[0-9\u0660-\u0669][0-9\u0660-\u0669,.\u066B\u066C]*\s\p{L}+$/u;
/** A label's text before a number: the template's words after a member's label. */
const WORDS_THEN_NUMBER = /^(.*?)([-+]?[0-9\u0660-\u0669].*)$/;

/** A member's pieces: its parts as runs, joined by the display-name separator, or its label as one run. */
function memberPieces(member: TmChartMember): ɵTmCanvasPiece[] {
  return member.parts === undefined
    ? [member.label]
    : member.parts.flatMap((part, index): ɵTmCanvasPiece[] =>
        index === 0 ? [part] : [{ base: ' - ' }, part],
      );
}

/**
 * The runs the story expects each label of its charts to draw, from the
 * charts' data and the kinds of label a chart draws, never from the drawn
 * string or its marks: a member's parts (or its label) as runs; a number,
 * alone or compact with a word, as one run; and a member's label, a
 * template's words and a number (a slice's label) as those pieces. A run
 * reads in its first letter's direction, a number left to right; words
 * between runs read in the base direction.
 */
function expectation(base: Direction): ProbeExpectation {
  const members = [COLUMN, PIE].flatMap((spec) =>
    spec.dimensions.flatMap((dimension) => dimension.members),
  );
  const runs = (pieces: readonly ɵTmCanvasPiece[]): ProbeRun[] =>
    pieces.map((piece) =>
      typeof piece === 'string'
        ? { text: piece, direction: runDirection(piece) }
        : { text: piece.base, direction: base },
    );
  return (text) => {
    if (NUMBER.test(text) || COMPACT.test(text)) {
      return runs([text]);
    }
    for (const member of members) {
      if (text === member.label) {
        return runs(memberPieces(member));
      }
      // A slice's label: the member, the template's words, its share.
      const rest = text.startsWith(member.label)
        ? WORDS_THEN_NUMBER.exec(text.slice(member.label.length))
        : null;
      if (rest !== null && NUMBER.test(rest[2]!)) {
        return runs([...memberPieces(member), { base: rest[1]! }, rest[2]!]);
      }
    }
    return null;
  };
}

/**
 * The canvas-text battery's page. First, each string drawn three ways, from
 * each base direction. "Together" is the string the chart hands the
 * engine, drawn whole on a canvas that takes its direction from CSS;
 * "apart" draws each run on its own, in its own direction, placed in the
 * base direction's order (the base pieces in the base direction), each on
 * the line as {@link onTheLine} draws it; "plain"
 * draws the pieces joined without marks. Together must equal apart
 * pixel for pixel in every engine.
 *
 * Then the same through the chart: charts in Arabic and in Egyptian Arabic,
 * drawn by the engine in the page's direction. "Probe" compares every label
 * the engine painted (categories, legend entries, data labels, the
 * threshold's label, ticks, slice labels) with the runs the story expects
 * of it, from the charts' data, drawn apart where the engine placed it, and
 * lists the result.
 */
@Component({
  imports: [FacadeAr, FacadeArEg, TmButton],
  template: `
    <h2>Chart canvas text</h2>

    @for (item of cases; track item.id) {
      <h3>{{ item.title }}</h3>
      @for (direction of directions; track direction) {
        <div class="demo-row" [attr.dir]="direction">
          @for (mode of modes; track mode) {
            <canvas
              #canvas
              [attr.data-testid]="'bidi-' + item.id + '-' + direction + '-' + mode"
              [attr.data-case]="item.id"
              [attr.data-direction]="direction"
              [attr.data-mode]="mode"
              [attr.width]="width"
              [attr.height]="height"
            ></canvas>
          }
        </div>
      }
    }

    <!-- Set once every canvas has drawn, with every face it needs settled. -->
    <output data-testid="bidi-drawn">{{ drawn() ? 'drawn' : 'drawing' }}</output>

    <!-- The same through the chart's engine, in both locales. -->
    <h3>Through the chart</h3>
    <app-demo-bidi-ar />
    <app-demo-bidi-ar-eg />
    <div class="demo-row">
      <button tmButton variant="secondary" data-testid="bidi-probe" (click)="probe()">Probe</button>
    </div>
    <!-- Each chart's labels, as JSON once probed. -->
    <output data-testid="bidi-facade">{{ probed() }}</output>
  `,
  styles: `
    .demo-row {
      display: flex;
      gap: 8px;
      margin-block-end: 8px;
    }

    canvas {
      inline-size: 360px;
      block-size: 36px;
      border: 1px solid var(--divider);
      font-family: var(--font-ui);
    }
  `,
})
export class ChartBidiStory {
  /** The cases. */
  readonly cases = CASES;
  /** The base directions each case is drawn from. */
  readonly directions: readonly Direction[] = ['ltr', 'rtl'];
  /** The three ways each is drawn. */
  readonly modes = ['together', 'apart', 'plain'] as const;
  /** The canvas width. */
  readonly width = WIDTH;
  /** The canvas height. */
  readonly height = HEIGHT;
  /** Whether every canvas has drawn. */
  readonly drawn = signal(false);

  /** The facade charts' probe results, as JSON, once probed. */
  readonly probed = signal('');

  /** The canvases. */
  private readonly canvases = viewChildren<ElementRef<HTMLCanvasElement>>('canvas');
  /** The page's direction, which the charts draw from. */
  private readonly direction = inject(Directionality);
  /** This story's element. */
  private readonly host = inject<ElementRef<HTMLElement>>(ElementRef);

  /** Draws every canvas once the page's fonts have loaded. */
  constructor() {
    afterNextRender(() => {
      void document.fonts.ready.then(async () => {
        const family = getComputedStyle(this.canvases()[0]!.nativeElement).fontFamily;
        const font = `${FONT_SIZE}px ${family}`;
        // A face that cannot load rejects the whole load at once: a metric
        // fallback whose local() font the system lacks (Arial or Tahoma on
        // Linux) does, while the self-hosted faces may still be loading.
        // The drawing then waits until every face loading has settled, so
        // the canvases always draw in the self-hosted faces.
        await document.fonts
          .load(font, CASES.map((item) => this.plain(item.pieces)).join(''))
          .catch(() => document.fonts.ready);
        for (const canvas of this.canvases()) {
          this.draw(canvas.nativeElement, font);
        }
        this.drawn.set(true);
      });
    });
  }

  /** Compares every label each facade chart drew with its runs drawn apart, and lists the results. */
  async probe(): Promise<void> {
    const base: Direction = this.direction.value === 'rtl' ? 'rtl' : 'ltr';
    const results: Record<string, ProbedLabel[]> = {};
    for (const chart of this.host.nativeElement.querySelectorAll<HTMLElement>(
      '[data-testid^="facade-"]',
    )) {
      const plot = chart.querySelector<HTMLElement>('.tm-chart__plot');
      results[chart.dataset['testid']!] =
        plot === null ? [] : await probeChartLabels(plot, base, expectation(base));
    }
    this.probed.set(JSON.stringify(results));
  }

  /** The pieces joined, without marks. */
  private plain(pieces: readonly ɵTmCanvasPiece[]): string {
    return pieces.map((piece) => (typeof piece === 'string' ? piece : piece.base)).join('');
  }

  /** Draws one canvas in its mode. */
  private draw(canvas: HTMLCanvasElement, font: string): void {
    const item = CASES.find((candidate) => candidate.id === canvas.dataset['case'])!;
    const base = canvas.dataset['direction'] as Direction;
    const context = canvas.getContext('2d')!;
    context.font = font;
    context.textBaseline = 'middle';
    context.fillStyle = getComputedStyle(document.body).color;
    const y = HEIGHT / 2;
    // Section: whole strings take the canvas' direction from CSS, as the engine's do.
    if (canvas.dataset['mode'] !== 'apart') {
      const text =
        canvas.dataset['mode'] === 'together'
          ? ɵtmCanvasText(item.pieces, base)
          : this.plain(item.pieces);
      context.textAlign = base === 'rtl' ? 'right' : 'left';
      context.fillText(text, base === 'rtl' ? WIDTH - INSET : INSET, y);
      return;
    }
    // Section: apart: each piece alone, in its own direction, placed in base
    // order, and on the line (its Arabic words closed by an ALM).
    let x = base === 'rtl' ? WIDTH - INSET : INSET;
    for (const piece of item.pieces) {
      const text = onTheLine(typeof piece === 'string' ? piece : piece.base);
      context.direction = typeof piece === 'string' ? runDirection(text) : base;
      context.textAlign = base === 'rtl' ? 'right' : 'left';
      context.fillText(text, x, y);
      const advance = context.measureText(text).width;
      x += base === 'rtl' ? -advance : advance;
    }
  }
}
