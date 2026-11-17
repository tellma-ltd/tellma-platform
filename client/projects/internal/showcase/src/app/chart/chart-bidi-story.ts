// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import { afterNextRender, Component, ElementRef, signal, viewChildren } from '@angular/core';

import { ɵtmCanvasText, type ɵTmCanvasPiece } from '@tellma/core-ui/chart';
import { tmFormatNumber } from '@tellma/core-ui/l10n';

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
  { id: 'arabic-latin', title: 'An Arabic and a Latin part', pieces: ['النقدية', ' ', 'Cash (USD)'] },
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

/** A run's direction, as the canvas text reads it: its first letter's, else left to right. */
function runDirection(text: string): Direction {
  const letter = /\p{L}/u.exec(text)?.[0];
  return letter !== undefined && /[\p{Script=Arabic}\p{Script=Hebrew}]/u.test(letter) ? 'rtl' : 'ltr';
}

/**
 * The canvas-text battery's page: each string drawn three ways, from
 * each base direction. "Together" is the string the chart hands the
 * engine, drawn whole on a canvas that takes its direction from CSS;
 * "apart" draws each run on its own, in its own direction, placed in the
 * base direction's order (the base pieces in the base direction); "plain"
 * draws the pieces joined without marks. Together must equal apart
 * pixel for pixel in every engine.
 */
@Component({
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

    <!-- Set once every canvas has drawn with the fonts loaded. -->
    <output data-testid="bidi-drawn">{{ drawn() ? 'drawn' : 'drawing' }}</output>
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

  /** The canvases. */
  private readonly canvases = viewChildren<ElementRef<HTMLCanvasElement>>('canvas');

  /** Draws every canvas once the page's fonts have loaded. */
  constructor() {
    afterNextRender(() => {
      void document.fonts.ready.then(async () => {
        const family = getComputedStyle(this.canvases()[0]!.nativeElement).fontFamily;
        const font = `${FONT_SIZE}px ${family}`;
        await document.fonts.load(font, CASES.map((item) => this.plain(item.pieces)).join(''));
        for (const canvas of this.canvases()) {
          this.draw(canvas.nativeElement, font);
        }
        this.drawn.set(true);
      });
    });
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
      const text = canvas.dataset['mode'] === 'together' ? ɵtmCanvasText(item.pieces, base) : this.plain(item.pieces);
      context.textAlign = base === 'rtl' ? 'right' : 'left';
      context.fillText(text, base === 'rtl' ? WIDTH - INSET : INSET, y);
      return;
    }
    // Section: apart: each piece alone, in its own direction, placed in base order.
    let x = base === 'rtl' ? WIDTH - INSET : INSET;
    for (const piece of item.pieces) {
      const text = typeof piece === 'string' ? piece : piece.base;
      context.direction = typeof piece === 'string' ? runDirection(text) : base;
      context.textAlign = base === 'rtl' ? 'right' : 'left';
      context.fillText(text, x, y);
      const advance = context.measureText(text).width;
      x += base === 'rtl' ? -advance : advance;
    }
  }
}
