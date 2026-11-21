// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import {
  afterNextRender,
  Component,
  computed,
  contentChildren,
  DestroyRef,
  Directive,
  ElementRef,
  forwardRef,
  inject,
  input,
  isDevMode,
  model,
  signal,
  viewChild,
} from '@angular/core';

import { TM_UI_TRANSLATE } from '@tellma/core-ui';

import {
  ɵtmSplitterBounds,
  ɵtmSplitterClamp,
  type ɵTmSplitterLimits,
} from './internal/tm-splitter-clamp';

/** How a {@link TmSplitter} divides its container: `horizontal` puts the panes side by side. */
export type TmSplitterOrientation = 'horizontal' | 'vertical';

/** One of a {@link TmSplitter}'s two panes: the one before the bar, or the one after it. */
export type TmSplitterSide = 'start' | 'end';

/** The arrow keys' multiplier while Shift is held. */
const SHIFT_MULTIPLIER = 4;

let nextPaneId = 0;

/** The state of a drag in progress, captured when the pointer went down. */
interface DragState {
  /** The pointer that holds the capture. */
  readonly pointerId: number;
  /** The pointer's coordinate along the split when it went down. */
  readonly origin: number;
  /** The primary pane's rendered size when the pointer went down. */
  readonly size: number;
  /** The container's size along the split when the pointer went down. */
  readonly container: number;
  /** The separator's size along the split. */
  readonly separator: number;
  /** Whether a horizontal split runs right to left, so a rightward move shrinks the start pane. */
  readonly rtl: boolean;
}

/**
 * Divides its container between two panes with a bar the user drags or
 * moves by keyboard. The consumer sizes one pane, the `primary`, in pixels
 * through `size`; the other pane takes the rest.
 *
 * The rendered size is clamped: with `W` the container's size along the
 * split, it is `max(min, min(size ?? defaultSize, maxFraction × W,
 * W − otherMin − separator))`, re-applied whenever the container resizes.
 * `size` keeps the user's intent, not the clamped value: in a 1,200 px
 * container with `maxFraction` 0.66, a `size` of 800 renders at 792 px and
 * stays 800, so the pane grows back when the container does.
 *
 * Exactly two children carry `tmSplitterPane`, one `"start"` and one
 * `"end"`, written as static attributes.
 *
 * ```html
 * <tm-splitter [(size)]="panel" [defaultSize]="500" [min]="420" [maxFraction]="0.66" [otherMin]="340">
 *   <section tmSplitterPane="start">…</section>
 *   <section tmSplitterPane="end">…</section>
 * </tm-splitter>
 * ```
 *
 * @tmGroup layout
 * @tmA11yNotes The bar is a focusable `role="separator"` (one tab stop)
 *   named "Resize panels" by default, with `aria-orientation`
 *   perpendicular to the split, `aria-valuenow` as the primary pane's share
 *   (0–100), `aria-valuemin`/`aria-valuemax` from the clamp, and
 *   `aria-controls` pointing at the primary pane. Arrows move the bar by
 *   `step` px (Shift: ×4) — Left/Right on a horizontal splitter, moving
 *   the bar the way the arrow points in either direction; Up/Down on a
 *   vertical one. Home and End jump to the ends of the clamp. Dragging
 *   works with any pointer, touch included.
 */
@Component({
  selector: 'tm-splitter',
  // The start pane, the bar, the end pane, in reading order.
  template: `
    <ng-content select="[tmSplitterPane=start]" />
    <div
      #separator
      class="tm-splitter__separator"
      role="separator"
      tabindex="0"
      [attr.aria-orientation]="orientation() === 'horizontal' ? 'vertical' : 'horizontal'"
      [attr.aria-valuenow]="aria()?.now"
      [attr.aria-valuemin]="aria()?.min"
      [attr.aria-valuemax]="aria()?.max"
      [attr.aria-controls]="primaryPaneId()"
      [attr.aria-label]="label()"
      (keydown)="onKeydown($event)"
      (pointerdown)="onPointerDown($event)"
      (pointermove)="onPointerMove($event)"
      (pointerup)="endDrag($event)"
      (pointercancel)="endDrag($event)"
      (lostpointercapture)="endDrag($event)"
    >
      <span class="tm-splitter__grip"></span>
    </div>
    <ng-content select="[tmSplitterPane=end]" />
  `,
  styleUrl: './tm-splitter.css',
  host: {
    class: 'tm-splitter',
    '[attr.data-orientation]': 'orientation()',
    '[class.tm-splitter--dragging]': 'dragging()',
  },
})
export class TmSplitter {
  private readonly host = inject<ElementRef<HTMLElement>>(ElementRef).nativeElement;
  private readonly translate = inject(TM_UI_TRANSLATE);

  /** `horizontal` (default): panes side by side; `vertical`: one above the other. */
  readonly orientation = input<TmSplitterOrientation>('horizontal');

  /** The pane whose size is set: `start` (default) or `end`. */
  readonly primary = input<TmSplitterSide>('start');

  /**
   * The primary pane's size in px, as the user last set it; `null`
   * (default) renders `defaultSize`. The bar writes it on every move, and
   * it keeps the user's intent: the clamp never writes back.
   */
  readonly size = model<number | null>(null);

  /** The primary pane's size in px while `size` is `null`. */
  readonly defaultSize = input.required<number>();

  /** The primary pane's minimum in px. Default 0. */
  readonly min = input(0);

  /** The other pane's minimum in px. Default 0. */
  readonly otherMin = input(0);

  /** The primary pane's largest share of the container, 0 to 1. Default 1. */
  readonly maxFraction = input(1);

  /** How far one arrow key moves the bar, in px. Default 16; Shift moves four times as far. */
  readonly step = input(16);

  /** The bar's accessible name. Default "Resize panels". */
  readonly ariaLabel = input<string | undefined>(undefined);

  /** The two panes, as projected. */
  private readonly panes = contentChildren<TmSplitterPane>(forwardRef(() => TmSplitterPane));

  /** The bar. */
  private readonly separator = viewChild.required<ElementRef<HTMLElement>>('separator');

  /** The container's size along the split, and the bar's, as last observed; null before the first observation. */
  private readonly measured = signal<{
    readonly container: number;
    readonly separator: number;
  } | null>(null);

  /** The drag in progress, if any. */
  private readonly drag = signal<DragState | null>(null);

  /** Whether a drag is in progress: the bar keeps its active look under the pointer. */
  protected readonly dragging = computed(() => this.drag() !== null);

  /** The bar's accessible name. */
  protected readonly label = computed(() => this.ariaLabel() ?? this.translate('splitter.label')());

  /** The primary pane's id, the bar's `aria-controls`. */
  protected readonly primaryPaneId = computed(
    () => this.panes().find((pane) => pane.tmSplitterPane() === this.primary())?.ɵid ?? null,
  );

  /**
   * The bar's value and range as shares of the container (0–100): where
   * the primary pane is, and how small and large the clamp lets it be.
   * Null until the container has been measured.
   */
  protected readonly aria = computed(() => {
    const measured = this.measured();
    if (measured === null || measured.container <= 0) {
      return null;
    }
    const limits = this.limits(measured.separator);
    const share = (px: number): number => Math.round((px / measured.container) * 100);
    const { lower, upper } = ɵtmSplitterBounds(measured.container, limits);
    return {
      now: share(ɵtmSplitterClamp(this.intent(), measured.container, limits)),
      min: share(lower),
      max: share(upper),
    };
  });

  /**
   * The primary pane's flex value: a fixed basis that applies the clamp in
   * CSS, so the size is right in the first frame and on every container
   * resize without script. Percentages resolve against the container.
   */
  readonly ɵprimaryFlex = computed(() => {
    const upper = `min(${this.intent()}px, ${this.maxFraction() * 100}%, calc(100% - ${this.otherMin()}px - var(--splitter-size)))`;
    return `0 0 max(${this.min()}px, ${upper})`;
  });

  constructor() {
    const destroyRef = inject(DestroyRef);
    // Observe the container along the split for the ARIA values; the
    // layout itself is clamped in CSS.
    afterNextRender(() => {
      const observer = new ResizeObserver(() => this.measure());
      observer.observe(this.host);
      destroyRef.onDestroy(() => observer.disconnect());
      this.measure();
      if (isDevMode()) {
        this.checkPanes();
      }
    });
  }

  /** The size the user asked for: `size`, else `defaultSize`. */
  private intent(): number {
    return this.size() ?? this.defaultSize();
  }

  /** The clamp's limits, with the bar's measured size. */
  private limits(separator: number): ɵTmSplitterLimits {
    return {
      min: this.min(),
      otherMin: this.otherMin(),
      maxFraction: this.maxFraction(),
      separator,
    };
  }

  /** Reads the container's and the bar's sizes along the split. */
  private measure(): void {
    const horizontal = this.orientation() === 'horizontal';
    const separator = this.separator().nativeElement;
    this.measured.set({
      container: horizontal ? this.host.clientWidth : this.host.clientHeight,
      separator: horizontal ? separator.offsetWidth : separator.offsetHeight,
    });
  }

  /** Sets the primary pane's size, clamped to the range the container allows now. */
  private moveTo(next: number, container: number, separator: number): void {
    this.size.set(Math.round(ɵtmSplitterClamp(next, container, this.limits(separator))));
  }

  /** Arrows move the bar by `step` (Shift ×4); Home and End jump to the clamp's ends. */
  protected onKeydown(event: KeyboardEvent): void {
    if (event.altKey || event.ctrlKey || event.metaKey) {
      return;
    }
    const horizontal = this.orientation() === 'horizontal';
    const jump = event.key === 'Home' || event.key === 'End';
    const screenDelta = jump ? 0 : this.arrowDelta(event.key, horizontal, event.shiftKey);
    if (screenDelta === null) {
      return;
    }
    this.measure();
    const measured = this.measured();
    if (measured === null || measured.container <= 0) {
      return;
    }
    event.preventDefault();
    const limits = this.limits(measured.separator);
    if (jump) {
      const { lower, upper } = ɵtmSplitterBounds(measured.container, limits);
      this.size.set(Math.round(event.key === 'Home' ? lower : upper));
      return;
    }
    // Move from where the pane is drawn, not from an intent the container
    // cannot hold.
    const current = ɵtmSplitterClamp(this.intent(), measured.container, limits);
    const delta = this.primaryDelta(screenDelta, horizontal && this.isRtl());
    this.moveTo(current + delta, measured.container, measured.separator);
  }

  /**
   * How far an arrow key moves the bar along the screen axis (right or
   * down positive), or null for a key the orientation does not answer:
   * Left and Right on a horizontal bar, Up and Down on a vertical one.
   */
  private arrowDelta(key: string, horizontal: boolean, shift: boolean): number | null {
    const step = this.step() * (shift ? SHIFT_MULTIPLIER : 1);
    switch (key) {
      case 'ArrowLeft':
        return horizontal ? -step : null;
      case 'ArrowRight':
        return horizontal ? step : null;
      case 'ArrowUp':
        return horizontal ? null : -step;
      case 'ArrowDown':
        return horizontal ? null : step;
      default:
        return null;
    }
  }

  /** A press on the bar starts a drag that holds the pointer until it lifts. */
  protected onPointerDown(event: PointerEvent): void {
    if (event.button !== 0 || this.drag() !== null) {
      return;
    }
    this.measure();
    const measured = this.measured();
    const primary = this.panes().find((pane) => pane.tmSplitterPane() === this.primary());
    if (measured === null || primary === undefined) {
      return;
    }
    const horizontal = this.orientation() === 'horizontal';
    const rect = primary.ɵelement.getBoundingClientRect();
    this.separator().nativeElement.setPointerCapture(event.pointerId);
    this.drag.set({
      pointerId: event.pointerId,
      origin: horizontal ? event.clientX : event.clientY,
      size: horizontal ? rect.width : rect.height,
      container: measured.container,
      separator: measured.separator,
      rtl: horizontal && this.isRtl(),
    });
  }

  /** While dragging, the primary pane follows the pointer within the clamp. */
  protected onPointerMove(event: PointerEvent): void {
    const drag = this.drag();
    if (drag === null || event.pointerId !== drag.pointerId) {
      return;
    }
    const position = this.orientation() === 'horizontal' ? event.clientX : event.clientY;
    this.moveTo(
      drag.size + this.primaryDelta(position - drag.origin, drag.rtl),
      drag.container,
      drag.separator,
    );
  }

  /** The pointer lifted, was cancelled, or lost its capture: the drag ends. */
  protected endDrag(event: PointerEvent): void {
    if (this.drag()?.pointerId === event.pointerId) {
      this.drag.set(null);
    }
  }

  /**
   * Turns a move of the bar along the screen axis (right or down positive)
   * into a change of the primary pane's size: in a right-to-left
   * horizontal split the start pane is on the right, and an `end` primary
   * grows as the bar moves toward the start.
   */
  private primaryDelta(screenDelta: number, rtl: boolean): number {
    const startDelta = rtl ? -screenDelta : screenDelta;
    return this.primary() === 'start' ? startDelta : -startDelta;
  }

  /** The live direction, read at the event. */
  private isRtl(): boolean {
    return getComputedStyle(this.host).direction === 'rtl';
  }

  /** Development check: exactly two panes, one on each side. */
  private checkPanes(): void {
    const sides = this.panes().map((pane) => pane.tmSplitterPane());
    if (sides.length !== 2 || !sides.includes('start') || !sides.includes('end')) {
      console.error(
        '[tellma-ui] tm-splitter needs exactly two children marked tmSplitterPane, one "start" ' +
          'and one "end", written as static attributes.',
        this.host,
      );
    }
  }
}

/**
 * Marks one of a {@link TmSplitter}'s two panes: `tmSplitterPane="start"`
 * before the bar, `"end"` after it. Write the value as a static attribute:
 * the splitter projects each pane by it. The primary pane takes its
 * clamped size and the other pane the rest. Both are border-box, so the
 * size includes a pane's own padding and border, and both may shrink
 * below their content, so a pane that can overflow should scroll its own
 * content.
 *
 * @tmGroup layout
 * @tmA11yNotes The primary pane gets an id when it has none, so the bar's
 *   `aria-controls` can point at it.
 */
@Directive({
  selector: '[tmSplitterPane]',
  host: {
    class: 'tm-splitter-pane',
    '[attr.id]': 'paneId',
    '[style.flex]': 'flex()',
    // The size is the pane's outer size, padding and border included, and
    // neither pane takes a content-based minimum, so the clamp holds.
    style: 'box-sizing: border-box; min-inline-size: 0; min-block-size: 0',
  },
})
export class TmSplitterPane {
  private readonly splitter = inject(TmSplitter);

  /** The pane's host element. @internal */
  readonly ɵelement = inject<ElementRef<HTMLElement>>(ElementRef).nativeElement;

  /** Which side of the bar the pane is on. */
  readonly tmSplitterPane = input.required<TmSplitterSide>();

  /** The pane's id: its own, else a generated one. */
  protected readonly paneId = this.ɵelement.id || `tm-splitter-pane-${nextPaneId++}`;

  /** The pane's id, the bar's `aria-controls` when the pane is primary. @internal */
  get ɵid(): string {
    return this.paneId;
  }

  /** The primary pane's clamped basis; the other pane takes the rest. */
  protected readonly flex = computed(() =>
    this.tmSplitterPane() === this.splitter.primary() ? this.splitter.ɵprimaryFlex() : '1 1 0',
  );
}
