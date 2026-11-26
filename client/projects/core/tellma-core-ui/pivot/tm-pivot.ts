// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

/// <reference types="@tellma/core-tools/lucide-icons" />

import { AriaDescriber, LiveAnnouncer } from '@angular/cdk/a11y';
import {
  afterNextRender,
  afterRenderEffect,
  booleanAttribute,
  Component,
  computed,
  DestroyRef,
  effect,
  ElementRef,
  inject,
  Injector,
  input,
  isDevMode,
  model,
  output,
  signal,
  untracked,
  viewChild,
  ViewContainerRef,
} from '@angular/core';
import ChevronDown from 'lucide/dist/esm/icons/chevron-down.mjs';
import ChevronRight from 'lucide/dist/esm/icons/chevron-right.mjs';

import { TM_FORM_FIELD_DEFAULTS, TM_UI_TRANSLATE, ɵtmRegisterUiStrings, type TmControlSize } from '@tellma/core-ui';
import { tmComputeAxisWindow, type TmAxisWindow } from '@tellma/core-ui/grid-engine';
import { TmIcon } from '@tellma/core-ui/icon';
import { tmIsApplePlatform, tmKeysMatch } from '@tellma/core-ui/keys';
import { ɵTmTooltipController } from '@tellma/core-ui/tooltip';

import {
  ɵtmPivotAxis,
  ɵtmPivotColumns,
  ɵtmPivotExpandableIds,
  ɵtmPivotFlattenRows,
  ɵtmPivotStronger,
  ɵtmPivotVisibleUnder,
  type ɵTmPivotHeaderCell,
  type ɵTmPivotRow,
  type ɵTmPivotStyle,
} from './internal/pivot-layout';
import {
  ɵtmPivotClamp,
  ɵtmPivotCover,
  ɵtmPivotMoveFrom,
  type ɵTmPivotCover,
  type ɵTmPivotGrid,
  type ɵTmPivotMove,
  type ɵTmPivotPosition,
} from './internal/pivot-nav';
import {
  ɵtmPivotResizeLimits,
  ɵtmPivotWidthOf,
  ɵtmPivotWidthPlan,
  ɵtmPivotWithWidth,
  type ɵTmPivotResizeTarget,
  type ɵTmPivotWidthPlan,
} from './internal/pivot-widths';
import { ɵtmPivotHeaderPieces, ɵtmPivotSegments } from './internal/pivot-window';
import { TM_PIVOT_STRINGS_EN } from './tm-pivot-strings';
import type {
  TmPivotAxis,
  TmPivotCell,
  TmPivotModel,
  TmPivotWidths,
} from './tm-pivot-types';

/** Data columns past which the columns window as the rows do. */
const COLUMN_WINDOW_THRESHOLD = 40;
/** Body rows rendered beyond the visible ones on each side. */
const OVERSCAN_ROWS = 4;
/** Data columns, at least, rendered beyond the visible ones on each side once the columns window. */
const OVERSCAN_COLUMNS = 4;
/** The row height (px) while the row-height token is unresolvable. */
const DEFAULT_ROW_HEIGHT = 30;
/** Stands in for a label inside a formatted message, so the message can be split around it. */
const LABEL_MARK = '';
/** The expansion sets' shared empty default. */
const NO_IDS: ReadonlySet<number> = new Set();
/** The widths' empty default: every column at its default width. */
const NO_WIDTHS: TmPivotWidths = {};

/** A label as rendered: text around an isolated label, and a phantom's suffix. */
interface LabelView {
  /** The message text before the label. */
  readonly before: string;
  /** The label itself, rendered in its own isolate. */
  readonly label: string;
  /** The message text after the label. */
  readonly after: string;
  /** The " (direct)" suffix of a phantom; `null` otherwise. */
  readonly direct: string | null;
}

/** One rendered column header (or corner) cell, or a spacer over unrendered columns. */
interface HeadView {
  /** The cell's track key. */
  readonly key: string;
  /** Whether it is a spacer standing in for unrendered columns. */
  readonly spacer: boolean;
  /** The cell's classes: look, stickiness and kind. */
  readonly classes: string;
  /** The logical row of the cell's first row. */
  readonly row: number;
  /** The logical column of the cell's first column. */
  readonly column: number;
  /** The table column span over the rendered columns; `null` for one. */
  readonly colSpan: number | null;
  /** The table row span; `null` for one. */
  readonly rowSpan: number | null;
  /** `aria-colindex` over the logical grid; `null` on a spacer. */
  readonly ariaColIndex: number | null;
  /** `aria-colspan` over the logical grid; `null` for one. */
  readonly ariaColSpan: number | null;
  /** `aria-rowspan`; `null` for one. */
  readonly ariaRowSpan: number | null;
  /** Whether the member is expanded; `null` when the cell does not expand. */
  readonly expanded: boolean | null;
  /** The cell's label; `null` on a spacer. */
  readonly label: LabelView | null;
  /** A level's label, at the corner's inline end. */
  readonly level: string | null;
  /** The sticky block offset, px. */
  readonly top: number;
  /** The sticky inline offset, px; `null` when the cell does not stick sideways. */
  readonly start: number | null;
  /** The resize handle's target, encoded; `null` without a handle. */
  readonly resize: string | null;
}

/** One rendered cell of a body row after its row header: an attribute, a value, or a spacer. */
interface CellView {
  /** The cell's track key. */
  readonly key: string;
  /** What the cell holds. */
  readonly kind: 'attribute' | 'value' | 'spacer';
  /** The cell's classes: look, stickiness, rules and drill state. */
  readonly classes: string;
  /** The logical column. */
  readonly column: number;
  /** The formatted text; `null` for a cell without facts, or a spacer. */
  readonly text: string | null;
  /** The sticky inline offset, px; `null` when the cell does not stick. */
  readonly start: number | null;
}

/** One rendered body row. */
interface RowView {
  /** Tells a row from a gap. */
  readonly kind: 'row';
  /** The row's track key. */
  readonly key: string;
  /** The logical row. */
  readonly row: number;
  /** `aria-level`: the member's depth, from 1. */
  readonly level: number;
  /** `aria-posinset` among its siblings. */
  readonly posInSet: number;
  /** `aria-setsize`: its sibling count. */
  readonly setSize: number;
  /** Whether the member is expanded; `null` for a leaf. */
  readonly expanded: boolean | null;
  /** The row header's classes. */
  readonly headClasses: string;
  /** The row header's sticky inline offset, px; `null` when it does not stick. */
  readonly headStart: number | null;
  /** The row header's label. */
  readonly label: LabelView;
  /** The attribute and value cells after the row header. */
  readonly cells: readonly CellView[];
}

/** A run of unrendered body rows. */
interface GapView {
  /** Tells a gap from a row. */
  readonly kind: 'gap';
  /** The gap's track key. */
  readonly key: string;
  /** The rows' full height, px. */
  readonly height: number;
  /** The table columns the gap row spans. */
  readonly colSpan: number;
}

/** One `<col>`: the leading columns, then a data column or a gap per segment. */
interface ColView {
  /** The column's track key. */
  readonly key: string;
  /** Its width, px. */
  readonly width: number;
}

/** The active cell, kept by identity so it survives expansion and new results. */
interface ActiveRef {
  /** A header row by index, or a body row by its member's id. */
  readonly row: { readonly header: number } | { readonly member: number };
  /** A leading column by index, or a data column by its group and measure. */
  readonly column:
    | { readonly lead: number }
    | { readonly member: number; readonly subtotal: boolean; readonly measure: number };
  /** The logical position it last resolved to: the fallback when its identity is gone. */
  readonly last: ɵTmPivotPosition;
}

/** A resize in progress. */
interface Drag {
  /** The column being resized. */
  readonly target: ɵTmPivotResizeTarget;
  /** Its width under the pointer, px. */
  readonly width: number;
  /** Each measure's width at the press. */
  readonly frozen: readonly number[];
}

/** The tooltip currently attached to one cell. */
interface CellTooltip {
  /** The cell it is attached to. */
  readonly element: HTMLElement;
  /** The text it shows. */
  readonly text: string;
  /** Its controller. */
  readonly controller: ɵTmTooltipController;
}

/** Two axis windows render the same items. */
function sameWindow(a: TmAxisWindow, b: TmAxisWindow): boolean {
  return a.start === b.start && a.end === b.end && a.totalSize === b.totalSize;
}

/** Reads a px length custom property, or the fallback. */
function cssPx(style: CSSStyleDeclaration, name: string, fallback: number): number {
  const value = Number.parseFloat(style.getPropertyValue(name));
  return Number.isFinite(value) ? value : fallback;
}

/** Encodes a resize target for a handle's data attribute. */
function encodeTarget(target: ɵTmPivotResizeTarget): string {
  return target.kind === 'rowHeader' ? 'r' : `${target.kind === 'attribute' ? 'a' : 'm'}${target.index}`;
}

/** Decodes a handle's resize target. */
function decodeTarget(code: string): ɵTmPivotResizeTarget | null {
  if (code === 'r') {
    return { kind: 'rowHeader' };
  }
  const index = Number(code.slice(1));
  if (!Number.isInteger(index) || index < 0) {
    return null;
  }
  return code.startsWith('a') ? { kind: 'attribute', index } : code.startsWith('m') ? { kind: 'measure', index } : null;
}

/**
 * An Excel-style pivot grid in compact form over two member trees. Each
 * row member's row carries its subtotal and is followed by its children;
 * each expanded column member's children are followed by its subtotal
 * column; the totals come last. Every column group holds one data column
 * per measure, and the cells take their text from the model's `text`
 * callback, which the pivot calls only for the cells it renders: rows
 * window past the viewport and columns window past 40, so a cube of any
 * size costs only what shows.
 *
 * Widths are fixed before the first paint: the row header takes 40% of the
 * pivot's width within 120–280 px, an attribute 96 px, and each measure's
 * columns fit its widest value (measured once per result in a hidden
 * probe) at 96 px or more, sharing any slack. A later result only widens
 * a measure. Every column resizes by its header's handle (a measure's
 * width applies to all of its columns), a double-click fits it, and a
 * width the user set is never changed. From a 300 px width, the row header
 * and the attribute columns that still fit in 40% of the width with it
 * stick to the inline start; the headers always stick to the top.
 *
 * The caller owns the expansion sets and the widths, which it keeps in page
 * state. Give the pivot a block size: its body scrolls inside it.
 *
 * @tmGroup collection
 * @tmA11yNotes A `treegrid` with one tab stop (a roving tabindex),
 *   `aria-readonly`, `aria-rowcount` and `aria-colcount`; header cells are
 *   `columnheader`s with `aria-colindex`, `aria-colspan` and
 *   `aria-expanded`; body rows carry `aria-level`, `aria-posinset`,
 *   `aria-setsize` and `aria-expanded` and start with a `rowheader`.
 *   Arrows move (left and right follow the reading direction), headers
 *   included; Home and End to the row's ends, Ctrl+Home and Ctrl+End (⌘ on
 *   Apple) to the corners, PageUp and PageDown by a page. Enter on a value
 *   cell activates it (with Ctrl or ⌘: a new tab); Enter or Space on an
 *   expandable header toggles it; Alt+ArrowRight and Alt+ArrowLeft expand
 *   and collapse a row from any of its cells, Alt+ArrowDown and
 *   Alt+ArrowUp a column from its header. Ctrl+C (⌘C) copies the focused
 *   cell's full text. Toggles are announced politely with the rows or
 *   columns they reveal. A cell cut short shows its full text in a
 *   tooltip; a cell that does not drill says so in its tooltip and
 *   description, and in an announcement on Enter.
 */
@Component({
  selector: 'tm-pivot',
  imports: [TmIcon],
  template: `
    <!-- The scroller: the body scrolls under the sticky headers and leading columns. -->
    <div class="tm-pivot__scroller" #scroller (scroll)="onScroll()">
      <table
        class="tm-pivot__table"
        role="treegrid"
        aria-readonly="true"
        [attr.aria-label]="ariaLabel()"
        [attr.aria-rowcount]="ariaRowCount()"
        [attr.aria-colcount]="ariaColCount()"
        [style.inline-size.px]="plan().total"
        (keydown)="onKeydown($event)"
        (click)="onClick($event)"
        (focusin)="onFocusIn($event)"
        (pointerover)="onPointerOver($event)"
        (copy)="onCopy($event)"
      >
        <colgroup>
          @for (col of cols(); track col.key) {
            <col [style.inline-size.px]="col.width" />
          }
        </colgroup>

        <!-- The header rows: the corner, then the column headers over the rendered columns. -->
        <thead role="rowgroup">
          @for (cells of headerRows(); track $index; let headerRow = $index) {
            <tr role="row" class="tm-pivot__header-row" [attr.aria-rowindex]="headerRow + 1">
              @for (cell of cells; track cell.key) {
                @if (cell.spacer) {
                  <th
                    role="presentation"
                    aria-hidden="true"
                    [class]="cell.classes"
                    [attr.colspan]="cell.colSpan"
                    [style.inset-block-start.px]="cell.top"
                  ></th>
                } @else {
                  <th
                    role="columnheader"
                    [class]="cell.classes"
                    [attr.colspan]="cell.colSpan"
                    [attr.rowspan]="cell.rowSpan"
                    [attr.aria-colindex]="cell.ariaColIndex"
                    [attr.aria-colspan]="cell.ariaColSpan"
                    [attr.aria-rowspan]="cell.ariaRowSpan"
                    [attr.aria-expanded]="cell.expanded"
                    [attr.data-r]="cell.row"
                    [attr.data-c]="cell.column"
                    [attr.tabindex]="tabIndexAt(cell.row, cell.column)"
                    [style.inset-block-start.px]="cell.top"
                    [style.inset-inline-start.px]="cell.start"
                  >
                    <span class="tm-pivot__content">
                      @if (cell.expanded !== null) {
                        <span
                          class="tm-pivot__twisty"
                          data-tm-toggle
                          aria-hidden="true"
                          [class.tm-pivot__twisty--open]="cell.expanded"
                        >
                          <tm-icon [svg]="cell.expanded ? expandedGlyph : collapsedGlyph" />
                        </span>
                      }
                      @if (cell.label; as label) {
                        <span class="tm-pivot__label" [attr.data-tm-toggle]="cell.expanded === null ? null : ''"
                          >{{ label.before }}<bdi>{{ label.label }}</bdi>{{ label.after
                          }}@if (label.direct) {<span class="tm-pivot__direct">{{ label.direct }}</span>}</span
                        >
                      }
                      @if (cell.level) {
                        <span class="tm-pivot__level">{{ ' ' }}<bdi>{{ cell.level }}</bdi></span>
                      }
                    </span>
                    @if (cell.resize) {
                      <span
                        class="tm-pivot__resize"
                        aria-hidden="true"
                        [attr.data-tm-resize]="cell.resize"
                        (pointerdown)="onPointerDown($event)"
                        (dblclick)="onDoubleClick($event)"
                      ></span>
                    }
                  </th>
                }
              }
            </tr>
          }
        </thead>

        <!-- The body rows: the window, the active row, and gaps for the rest. -->
        <tbody role="rowgroup">
          @for (row of bodyRows(); track row.key) {
            @if (row.kind === 'gap') {
              <tr role="presentation" class="tm-pivot__gap" [style.block-size.px]="row.height">
                <td role="presentation" [attr.colspan]="row.colSpan"></td>
              </tr>
            } @else {
              <tr
                role="row"
                class="tm-pivot__row"
                [attr.aria-rowindex]="row.row + 1"
                [attr.aria-level]="row.level"
                [attr.aria-posinset]="row.posInSet"
                [attr.aria-setsize]="row.setSize"
                [attr.aria-expanded]="row.expanded"
              >
                <!-- The row header: the indented label behind its twisty. -->
                <th
                  role="rowheader"
                  aria-colindex="1"
                  [class]="row.headClasses"
                  [attr.data-r]="row.row"
                  data-c="0"
                  [attr.tabindex]="tabIndexAt(row.row, 0)"
                  [style.inset-inline-start.px]="row.headStart"
                  [style.--tm-pivot-level]="row.level - 1"
                >
                  <span class="tm-pivot__content">
                    <span
                      class="tm-pivot__twisty"
                      aria-hidden="true"
                      [attr.data-tm-toggle]="row.expanded === null ? null : ''"
                      [class.tm-pivot__twisty--open]="row.expanded === true"
                    >
                      @if (row.expanded !== null) {
                        <tm-icon [svg]="row.expanded ? expandedGlyph : collapsedGlyph" />
                      }
                    </span>
                    <span class="tm-pivot__label" [attr.data-tm-toggle]="row.expanded === null ? null : ''"
                      ><bdi>{{ row.label.label }}</bdi>@if (row.label.direct) {<span class="tm-pivot__direct">{{
                          row.label.direct
                        }}</span>}</span
                    >
                  </span>
                </th>
                <!-- The attribute and value cells, with spacers over unrendered columns. -->
                @for (cell of row.cells; track cell.key) {
                  @if (cell.kind === 'spacer') {
                    <td role="presentation" aria-hidden="true" [class]="cell.classes"></td>
                  } @else {
                    <td
                      role="gridcell"
                      [class]="cell.classes"
                      [attr.aria-colindex]="cell.column + 1"
                      [attr.data-r]="row.row"
                      [attr.data-c]="cell.column"
                      [attr.tabindex]="tabIndexAt(row.row, cell.column)"
                      [style.inset-inline-start.px]="cell.start"
                    >
                      @if (cell.kind === 'value') {
                        @if (cell.text) {
                          <span class="tm-pivot__text" dir="ltr">{{ cell.text }}</span>
                        }
                      } @else {
                        <span class="tm-pivot__label"><bdi>{{ cell.text }}</bdi></span>
                      }
                    </td>
                  }
                }
              </tr>
            }
          }
        </tbody>
      </table>
    </div>
    <!-- The probe: styled like a value cell, it measures each measure's widest value. -->
    <span class="tm-pivot__probe" #probe aria-hidden="true"></span>
  `,
  styleUrl: './tm-pivot.css',
  host: {
    class: 'tm-pivot',
    '[class.tm-pivot--sm]': 'resolvedSize() === "sm"',
    '[class.tm-pivot--lg]': 'resolvedSize() === "lg"',
  },
})
export class TmPivot {
  // ---- inputs and outputs ----

  /** The members, measures and cell callbacks to render. */
  readonly model = input.required<TmPivotModel>();

  /** The ids of the expanded row members; written on every toggle. */
  readonly expandedRows = model<ReadonlySet<number>>(NO_IDS);

  /** The ids of the expanded column members; written on every toggle. */
  readonly expandedColumns = model<ReadonlySet<number>>(NO_IDS);

  /** The user's column widths; written when a resize or a fit ends. */
  readonly widths = model<TmPivotWidths>(NO_WIDTHS);

  /**
   * Whether value cells are drill targets that emit `cellActivate`.
   * Defaults to `true`; the model's `activatable` may still refuse cells.
   */
  readonly activatable = input(true, { transform: booleanAttribute });

  /** The pivot's accessible name. */
  readonly ariaLabel = input.required<string>();

  /** Row density. Defaults to the workspace default from `TM_FORM_FIELD_DEFAULTS`. */
  readonly size = input<TmControlSize | undefined>(undefined);

  /** Emits when a value cell that drills is activated by Enter or a click. */
  readonly cellActivate = output<TmPivotCell>();

  // ---- services and elements ----

  /** The host element. */
  private readonly host: HTMLElement = inject(ElementRef<HTMLElement>).nativeElement;
  /** Runs the after-render focus hooks in this component's context. */
  private readonly injector = inject(Injector);
  /** The library's strings in the active language. */
  private readonly translate = inject(TM_UI_TRANSLATE);
  /** Speaks toggles and refusals politely. */
  private readonly announcer = inject(LiveAnnouncer);
  /** Registers a refused cell's description. */
  private readonly describer = inject(AriaDescriber);
  /** Hosts the cell tooltips' panels. */
  private readonly viewContainer = inject(ViewContainerRef);
  /** The workspace's default control size. */
  private readonly sizeDefaults = inject(TM_FORM_FIELD_DEFAULTS);

  /** The scroll container around the table. */
  private readonly scroller = viewChild.required<ElementRef<HTMLElement>>('scroller');
  /** The hidden span styled like a value cell, which measures the widest values. */
  private readonly probe = viewChild.required<ElementRef<HTMLElement>>('probe');

  // ---- glyphs ----

  /** A collapsed member's twisty: it points along the reading direction. */
  protected readonly collapsedGlyph = { node: ChevronRight, mirrorInRtl: true };
  /** An expanded member's twisty. */
  protected readonly expandedGlyph = ChevronDown;

  // ---- measured state ----

  /** The pivot's inline size (W), from its ResizeObserver. */
  private readonly hostWidth = signal(0);
  /** The scroller's client box. */
  private readonly viewport = signal({ width: 0, height: 0 });
  /** The scroller's offsets; `left` is the logical inline offset, never negative. */
  private readonly scroll = signal({ top: 0, left: 0 });
  /** The row height in force, from the density's row-height token. */
  private readonly rowHeight = signal(DEFAULT_ROW_HEIGHT);
  /**
   * Each measure's fit as measured so far, by measure index as the user
   * widths are; it only widens while the measure count holds.
   */
  private readonly fitState = signal<readonly number[]>([]);
  /** The resize in progress, if any. */
  private readonly drag = signal<Drag | null>(null);

  /** The rendered cells' texts, keyed by row member, column member and measure, for the current model. */
  private textCache = new Map<number, string | null>();
  /** The model the text cache holds texts of. */
  private textCacheModel: TmPivotModel | null = null;
  /** Each measure's widest value, asked of the model once per model. */
  private widestCache: { readonly model: TmPivotModel; readonly values: readonly (string | null)[] } | null = null;
  /** The tooltip attached to a cell, if any. */
  private tooltip: CellTooltip | null = null;
  /** Set by Mod+C until the native copy event takes it. */
  private copyPending = false;
  /** Set while the fits could not be measured because the pivot was not rendered (inside `display: none`). */
  private unmeasured = false;

  // ---- structure ----

  /** The row density in force. */
  protected readonly resolvedSize = computed(() => this.size() ?? this.sizeDefaults.size);

  /** The row axis, indexed once per model. */
  private readonly rowAxis = computed(() => ɵtmPivotAxis(this.model().rows));
  /** The column axis, indexed once per model. */
  private readonly columnAxis = computed(() => ɵtmPivotAxis(this.model().columns));
  /** The measure count, at least one. */
  private readonly measureCount = computed(() => Math.max(1, this.model().measures.length));
  /** The attribute column count. */
  private readonly attributeCount = computed(() => this.model().attributeLabels.length);

  /** The visible body rows, in compact form. */
  private readonly rows = computed(() => ɵtmPivotFlattenRows(this.rowAxis(), this.expandedRows()));

  /** Each row member's visible position, or -1. */
  private readonly rowPosition = computed(() => {
    const position = new Int32Array(this.rowAxis().members.length).fill(-1);
    this.rows().forEach((row, index) => (position[row.index] = index));
    return position;
  });

  /** The column groups, data columns and header cells. */
  private readonly columns = computed(() =>
    ɵtmPivotColumns(
      this.columnAxis(),
      this.expandedColumns(),
      this.measureCount(),
      this.model().columnLevelLabels.length,
    ),
  );

  /** The logical grid the keyboard moves in. */
  private readonly grid = computed<ɵTmPivotGrid>(() => ({
    columns: this.columns(),
    attributeCount: this.attributeCount(),
    bodyRows: this.rows().length,
  }));

  /** The columns before the data columns: the row header and the attributes. */
  private readonly lead = computed(() => 1 + this.attributeCount());

  /** `aria-rowcount`: every header row and every visible body row. */
  protected readonly ariaRowCount = computed(() => this.columns().headerRows + this.rows().length);
  /** `aria-colcount`: the row header, the attributes and every data column. */
  protected readonly ariaColCount = computed(() => this.lead() + this.columns().dataCount);

  // ---- widths ----

  /** Each measure's fit for the current measure count (0 until measured). */
  private readonly fits = computed(() => {
    const state = this.fitState();
    const count = this.measureCount();
    return Array.from({ length: count }, (_, index) => (state.length === count ? (state[index] ?? 0) : 0));
  });

  /** The user's widths with a drag in progress applied. */
  private readonly effectiveWidths = computed(() => {
    const drag = this.drag();
    return drag === null ? this.widths() : ɵtmPivotWithWidth(this.widths(), drag.target, drag.width);
  });

  /** Every column's resolved width. */
  protected readonly plan = computed<ɵTmPivotWidthPlan>(() =>
    ɵtmPivotWidthPlan({
      hostWidth: this.hostWidth(),
      available: this.viewport().width,
      attributeCount: this.attributeCount(),
      slotCount: this.columns().slots.length,
      fits: this.fits(),
      user: this.effectiveWidths(),
      frozen: this.drag()?.frozen ?? null,
    }),
  );

  /** Each data column's inline offset within its group, by measure. */
  private readonly measureOffsets = computed(() => {
    const offsets: number[] = [];
    let offset = 0;
    for (const width of this.plan().measures) {
      offsets.push(offset);
      offset += width;
    }
    return offsets;
  });

  /** Each leading column's inline offset from the table's start. */
  private readonly leadOffsets = computed(() => {
    const plan = this.plan();
    const offsets = [0];
    let offset = plan.rowHeader;
    for (const width of plan.attributes) {
      offsets.push(offset);
      offset += width;
    }
    return offsets;
  });

  // ---- the active cell ----

  /** The active cell by identity; `null` until the user moves, which means the first body row's header. */
  private readonly activeRef = signal<ActiveRef | null>(null);

  /** The active cell's logical position in the current layout. */
  protected readonly active = computed<ɵTmPivotPosition>(() => this.resolve(this.activeRef()));

  /** The cell covering the active position: the one element that takes the tab stop. */
  protected readonly activeCover = computed<ɵTmPivotCover>(() => ɵtmPivotCover(this.grid(), this.active()));

  // ---- windows ----

  /** The body rows the viewport shows, with overscan. */
  private readonly rowWindow = computed(
    () => {
      const height = this.rowHeight();
      return tmComputeAxisWindow({
        scrollOffset: this.scroll().top,
        viewportSize: Math.max(0, this.viewport().height - this.columns().headerRows * height),
        itemSize: height,
        itemCount: this.rows().length,
        overscan: OVERSCAN_ROWS,
      });
    },
    { equal: sameWindow },
  );

  /** The data columns the viewport shows (every one up to the threshold), as a window over data columns. */
  private readonly columnWindow = computed(
    () => {
      const columns = this.columns();
      if (columns.dataCount <= COLUMN_WINDOW_THRESHOLD) {
        return { start: 0, end: columns.dataCount, leadOffset: 0, totalSize: columns.dataCount };
      }
      // Window whole column groups: a group's width is the same everywhere.
      const plan = this.plan();
      const measures = columns.measureCount;
      const left = this.scroll().left;
      const from = Math.max(0, left + plan.stickyWidth - plan.corner);
      const to = left + this.viewport().width - plan.corner;
      const groups = tmComputeAxisWindow({
        scrollOffset: from,
        viewportSize: Math.max(0, to - from),
        itemSize: Math.max(1, plan.slotWidth),
        itemCount: columns.slots.length,
        overscan: Math.max(1, Math.ceil(OVERSCAN_COLUMNS / measures)),
      });
      return {
        start: groups.start * measures,
        end: groups.end * measures,
        leadOffset: 0,
        totalSize: columns.dataCount,
      };
    },
    { equal: sameWindow },
  );

  /** The rendered body rows: the window, the active row kept rendered, and gaps. */
  private readonly rowSegments = computed(() => {
    const window = this.rowWindow();
    const active = this.active().row - this.columns().headerRows;
    return ɵtmPivotSegments(window.start, window.end, this.rows().length, active >= 0 ? active : null);
  });

  /** The rendered data columns: the window, the active column kept rendered, and gaps. */
  private readonly columnSegments = computed(() => {
    const window = this.columnWindow();
    const active = this.active().column - this.lead();
    return ɵtmPivotSegments(window.start, window.end, this.columns().dataCount, active >= 0 ? active : null);
  });

  // ---- rendering ----

  /** The subtotal header's text around its label, for the active language. */
  private readonly subtotalParts = computed(() => {
    const text = this.translate('pivot.subtotal', { label: LABEL_MARK })();
    const at = text.indexOf(LABEL_MARK);
    return at === -1 ? { before: `${text} `, after: '' } : { before: text.slice(0, at), after: text.slice(at + 1) };
  });

  /** A phantom's suffix, for the active language. */
  private readonly directText = computed(() => this.translate('pivot.direct')());

  /** The `<col>` elements: the leading columns, then one per rendered data segment. */
  protected readonly cols = computed<readonly ColView[]>(() => {
    const plan = this.plan();
    const cols: ColView[] = [{ key: 'r', width: plan.rowHeader }];
    plan.attributes.forEach((width, index) => cols.push({ key: `a${index}`, width }));
    for (const segment of this.columnSegments()) {
      cols.push(
        segment.kind === 'item'
          ? { key: `d${segment.index}`, width: plan.measures[segment.index % plan.measures.length] }
          : { key: `g${segment.from}`, width: this.dataX(segment.to) - this.dataX(segment.from) },
      );
    }
    return cols;
  });

  /** The header rows, corner included. */
  protected readonly headerRows = computed<readonly (readonly HeadView[])[]>(() => {
    const model = this.model();
    const columns = this.columns();
    const plan = this.plan();
    const segments = this.columnSegments();
    const height = this.rowHeight();
    const lead = this.lead();
    const pieces = ɵtmPivotHeaderPieces(columns, segments);
    const leadOffsets = this.leadOffsets();
    const rows: HeadView[][] = [];
    for (let row = 0; row < columns.headerRows; row++) {
      const top = row * height;
      const cells: HeadView[] = [];
      // Section: the corner. The row-header column holds the row
      // dimensions' names on the last header row and each column level's
      // name at its end; the attribute names span every header row.
      const bottom = row === columns.headerRows - 1;
      const level = row < columns.levelRows ? (model.columnLevelLabels[row] ?? '') : '';
      cells.push({
        ...this.headBase(`c${row}`, row, 0, top),
        classes:
          this.leadClasses('tm-pivot__corner tm-pivot__corner--row', 0, plan) +
          (bottom ? ' tm-pivot__corner--bottom' : ''),
        ariaColIndex: 1,
        label: bottom ? { before: '', label: model.rowLabel, after: '', direct: null } : null,
        level: level === '' ? null : level,
        start: 0 < plan.sticky ? leadOffsets[0] : null,
        resize: bottom ? encodeTarget({ kind: 'rowHeader' }) : null,
      });
      if (row === 0) {
        model.attributeLabels.forEach((label, index) =>
          cells.push({
            ...this.headBase(`a${index}`, 0, index + 1, top),
            classes: this.leadClasses('tm-pivot__corner tm-pivot__corner--attribute', index + 1, plan),
            rowSpan: columns.headerRows > 1 ? columns.headerRows : null,
            ariaColIndex: index + 2,
            ariaRowSpan: columns.headerRows > 1 ? columns.headerRows : null,
            label: { before: '', label, after: '', direct: null },
            start: index + 1 < plan.sticky ? leadOffsets[index + 1] : null,
            resize: encodeTarget({ kind: 'attribute', index }),
          }),
        );
      }
      // Section: the column headers over the rendered data columns.
      for (const piece of pieces[row]) {
        if (piece.kind === 'spacer') {
          cells.push({
            ...this.headBase(piece.key, row, -1, top),
            spacer: true,
            classes: 'tm-pivot__spacer',
            colSpan: piece.placement.span > 1 ? piece.placement.span : null,
          });
          continue;
        }
        const cell = piece.cell;
        cells.push({
          ...this.headBase(cell.key, cell.row, lead + cell.start, top),
          classes: this.headerClasses(cell),
          colSpan: piece.placement.span > 1 ? piece.placement.span : null,
          rowSpan: cell.rowSpan > 1 ? cell.rowSpan : null,
          ariaColIndex: lead + cell.start + 1,
          ariaColSpan: cell.end - cell.start > 1 ? cell.end - cell.start : null,
          ariaRowSpan: cell.rowSpan > 1 ? cell.rowSpan : null,
          expanded: cell.expandable ? cell.expanded : null,
          label: this.headerLabel(cell),
          resize: cell.resizes === -1 ? null : encodeTarget({ kind: 'measure', index: cell.resizes }),
        });
      }
      rows.push(cells);
    }
    return rows;
  });

  /** The body: the rendered rows, with gaps standing in for the rest. */
  protected readonly bodyRows = computed<readonly (RowView | GapView)[]>(() => {
    const model = this.model();
    const rowAxis = this.rowAxis();
    const columnAxis = this.columnAxis();
    const columns = this.columns();
    const rows = this.rows();
    const plan = this.plan();
    const height = this.rowHeight();
    const lead = this.lead();
    const headerRows = columns.headerRows;
    const measures = columns.measureCount;
    const segments = this.columnSegments();
    const leadOffsets = this.leadOffsets();
    const drills = this.activatable();
    const direct = this.directText();
    const tableColumns = lead + segments.length;
    // The texts of cells that stay rendered are reused; a new model starts over.
    const cache = this.textCacheModel === model ? this.textCache : new Map<number, string | null>();
    const texts = new Map<number, string | null>();
    const stride = columnAxis.members.length * measures;
    const views: (RowView | GapView)[] = [];
    for (const segment of this.rowSegments()) {
      if (segment.kind === 'gap') {
        views.push({
          kind: 'gap',
          key: `g${segment.from}`,
          height: (segment.to - segment.from) * height,
          colSpan: tableColumns,
        });
        continue;
      }
      const row = rows[segment.index];
      const member = rowAxis.members[row.index];
      const logicalRow = headerRows + segment.index;
      const cells: CellView[] = [];
      // Section: the attribute cells, in the row's look.
      model.attributeLabels.forEach((_, index) =>
        cells.push({
          key: `a${index}`,
          kind: 'attribute',
          classes:
            this.leadClasses(`tm-pivot__attribute ${this.styleClass(row.style)}`, index + 1, plan) +
            (row.style === 'total' ? ' tm-pivot__rule-block' : ''),
          column: index + 1,
          text: member.attributes?.[index] ?? '',
          start: index + 1 < plan.sticky ? leadOffsets[index + 1] : null,
        }),
      );
      // Section: the value cells, each in the stronger look of its row and
      // column, with its text read once while it stays rendered.
      for (const column of segments) {
        if (column.kind === 'gap') {
          // The grand total's rule runs across the spacers too.
          const classes = row.style === 'total' ? 'tm-pivot__spacer tm-pivot__rule-block' : 'tm-pivot__spacer';
          cells.push({ key: `g${column.from}`, kind: 'spacer', classes, column: -1, text: null, start: null });
          continue;
        }
        const slot = columns.slots[Math.floor(column.index / measures)];
        const measure = column.index % measures;
        const columnId = columnAxis.members[slot.index].id;
        const key = row.index * stride + slot.index * measures + measure;
        let text = cache.get(key);
        if (text === undefined) {
          // The model's callbacks run untracked: a new model is what re-reads them.
          text = untracked(() => model.text(member.id, columnId, measure));
        }
        texts.set(key, text);
        const style = ɵtmPivotStronger(row.style, slot.style);
        let classes = `tm-pivot__cell tm-pivot__value ${this.styleClass(style)}`;
        if (drills) {
          classes +=
            model.activatable === undefined || untracked(() => model.activatable!(member.id, columnId))
              ? ' tm-pivot__value--link'
              : ' tm-pivot__value--refused';
        }
        if (row.style === 'total') {
          classes += ' tm-pivot__rule-block';
        }
        if (slot.style === 'total' && measure === 0) {
          classes += ' tm-pivot__rule-inline';
        }
        cells.push({ key: `d${column.index}`, kind: 'value', classes, column: lead + column.index, text, start: null });
      }
      views.push({
        kind: 'row',
        key: `r${member.id}`,
        row: logicalRow,
        level: row.level,
        posInSet: row.posInSet,
        setSize: row.setSize,
        expanded: row.expandable ? row.expanded : null,
        headClasses:
          this.leadClasses(`tm-pivot__row-header ${this.styleClass(row.style)}`, 0, plan) +
          (row.style === 'total' ? ' tm-pivot__rule-block' : ''),
        headStart: 0 < plan.sticky ? 0 : null,
        label: {
          before: '',
          label: member.label,
          after: '',
          direct: member.kind === 'phantom' ? ` ${direct}` : null,
        },
        cells,
      });
    }
    this.textCache = texts;
    this.textCacheModel = model;
    return views;
  });

  /** The header block's height, which the gutter's header band paints. */
  protected readonly headerHeight = computed(() => this.columns().headerRows * this.rowHeight());

  /** Wires the fit measurement, the size observers and the font re-measure. */
  constructor() {
    const destroyRef = inject(DestroyRef);
    // The pivot's strings ship with it, beside the core table.
    ɵtmRegisterUiStrings('pivot', TM_PIVOT_STRINGS_EN);

    // Section: development-mode shape checks, once per model.
    effect(() => {
      const problems = [
        ...this.rowAxis().problems.map((problem) => `rows: ${problem}`),
        ...this.columnAxis().problems.map((problem) => `columns: ${problem}`),
      ];
      if (this.model().measures.length === 0) {
        problems.push('the model has no measures');
      }
      if (isDevMode() && problems.length > 0) {
        untracked(() => console.error(`tm-pivot: the model is malformed — ${problems.join('; ')}.`));
      }
    });

    // Section: the row height from the token, re-read when the density changes.
    afterRenderEffect(() => {
      this.resolvedSize();
      untracked(() =>
        this.rowHeight.set(cssPx(getComputedStyle(this.host), '--tm-pivot-row-height', DEFAULT_ROW_HEIGHT)),
      );
    });

    // Section: the measures' fits, measured in the frame a new model first
    // renders, before it paints. A later model only widens a measure.
    afterRenderEffect({
      earlyRead: () => {
        const model = this.model();
        this.resolvedSize();
        return untracked(() => this.measureWidest(model));
      },
      write: (measured) => this.widen(measured()),
    });

    // Section: the host and viewport sizes, read in the first frame and
    // then on every observed resize.
    afterNextRender({
      earlyRead: () => this.readSizes(),
      write: (sizes) => {
        this.applySizes(sizes);
        const scroller = this.scroller().nativeElement;
        // A pivot first rendered hidden measures its fits once it shows.
        const observer = new ResizeObserver(() => {
          this.applySizes(this.readSizes());
          if (this.unmeasured) {
            this.widen(this.measureWidest(untracked(this.model)));
          }
        });
        observer.observe(this.host);
        observer.observe(scroller);
        destroyRef.onDestroy(() => observer.disconnect());
      },
    });

    // Section: fonts that arrive after the first measure may widen a measure.
    const fonts = typeof document === 'undefined' ? undefined : document.fonts;
    if (fonts !== undefined) {
      const remeasure = (): void => this.widen(this.measureWidest(untracked(this.model)));
      fonts.addEventListener('loadingdone', remeasure);
      destroyRef.onDestroy(() => fonts.removeEventListener('loadingdone', remeasure));
    }

    destroyRef.onDestroy(() => this.dropTooltip());
  }

  // ---- public API ----

  /** Expands every member with children on an axis. */
  expandAll(axis: TmPivotAxis): void {
    if (axis === 'rows') {
      this.expandedRows.set(ɵtmPivotExpandableIds(this.rowAxis()));
    } else {
      this.expandedColumns.set(ɵtmPivotExpandableIds(this.columnAxis()));
    }
  }

  /** Collapses every member on an axis. */
  collapseAll(axis: TmPivotAxis): void {
    if (axis === 'rows') {
      this.expandedRows.set(new Set());
    } else {
      this.expandedColumns.set(new Set());
    }
  }

  /** Moves focus to the active cell, the pivot's one tab stop. */
  focus(): void {
    const element = this.activeElement();
    if (element !== null) {
      element.focus();
    } else {
      afterNextRender(() => this.activeElement()?.focus(), { injector: this.injector });
    }
  }

  // ---- template helpers ----

  /** The roving tabindex: 0 on the cell covering the active position. */
  protected tabIndexAt(row: number, column: number): number {
    const cover = this.activeCover();
    return cover.row === row && cover.column === column ? 0 : -1;
  }

  // ---- events ----

  /** Records the scroll offsets the windows follow. */
  protected onScroll(): void {
    const scroller = this.scroller().nativeElement;
    const top = scroller.scrollTop;
    const left = Math.abs(scroller.scrollLeft);
    const current = this.scroll();
    if (current.top !== top || current.left !== left) {
      this.scroll.set({ top, left });
    }
  }

  /** The keyboard model: moves, activation, toggles and copy. */
  protected onKeydown(event: KeyboardEvent): void {
    if (event.defaultPrevented || event.isComposing) {
      return;
    }
    const position = this.active();
    const cover = this.activeCover();
    const headerRows = this.columns().headerRows;
    const rtl = getComputedStyle(this.host).direction === 'rtl';
    // Section: expand and collapse from the keyboard.
    if (tmKeysMatch(event, 'Alt+ArrowRight') || tmKeysMatch(event, 'Alt+ArrowLeft')) {
      event.preventDefault();
      const row = this.rows()[position.row - headerRows];
      if (row !== undefined && row.expandable && row.expanded !== (event.key === 'ArrowRight')) {
        this.toggleRow(row);
      }
      return;
    }
    if (tmKeysMatch(event, 'Alt+ArrowDown') || tmKeysMatch(event, 'Alt+ArrowUp')) {
      event.preventDefault();
      const header = this.headerCellAt(cover);
      if (header !== null && header.kind !== 'measure') {
        const expand = event.key === 'ArrowDown';
        const canExpand = header.kind === 'member' && header.expandable;
        const canCollapse = header.kind === 'group' || header.kind === 'subtotal';
        if ((expand && canExpand) || (!expand && canCollapse)) {
          this.toggleColumn(header.member);
        }
      }
      return;
    }
    // Section: moves.
    const moves: readonly (readonly [string, ɵTmPivotMove])[] = [
      ['ArrowLeft', rtl ? 'right' : 'left'],
      ['ArrowRight', rtl ? 'left' : 'right'],
      ['ArrowUp', 'up'],
      ['ArrowDown', 'down'],
      ['Home', 'rowStart'],
      ['End', 'rowEnd'],
      ['Mod+Home', 'first'],
      ['Mod+End', 'last'],
      ['PageUp', 'pageUp'],
      ['PageDown', 'pageDown'],
    ];
    for (const [keys, move] of moves) {
      if (tmKeysMatch(event, keys)) {
        event.preventDefault();
        this.moveTo(ɵtmPivotMoveFrom(this.grid(), position, move, this.pageRows()));
        return;
      }
    }
    // Section: activation and toggles.
    const enter = tmKeysMatch(event, 'Enter');
    const modEnter = tmKeysMatch(event, 'Mod+Enter');
    if (enter || modEnter || tmKeysMatch(event, 'Space')) {
      event.preventDefault();
      if (this.toggleAt(cover)) {
        return;
      }
      if (enter || modEnter) {
        this.activateAt(cover, modEnter);
      }
      return;
    }
    // Section: Mod+C copies through the native copy event, with the
    // clipboard API as the fallback where no copy event follows.
    if (tmKeysMatch(event, 'Mod+C') && !this.hasUserSelection()) {
      this.copyPending = true;
      setTimeout(() => {
        if (this.copyPending) {
          this.copyPending = false;
          const text = this.activeText();
          void navigator.clipboard?.writeText(text).catch(() => undefined);
        }
      });
    }
  }

  /** A click on a twisty or label toggles; a click on a drilling value cell activates it. */
  protected onClick(event: MouseEvent): void {
    const target = event.target;
    if (!(target instanceof Element) || target.closest('[data-tm-resize]') !== null) {
      return;
    }
    const element = target.closest<HTMLElement>('[data-c]');
    if (element === null || event.button !== 0) {
      return;
    }
    const cover = this.coverOf(element);
    if (cover === null) {
      return;
    }
    if (target.closest('[data-tm-toggle]') !== null && this.toggleAt(cover)) {
      return;
    }
    if (element.classList.contains('tm-pivot__value--link')) {
      this.activateAt(cover, tmIsApplePlatform() ? event.metaKey : event.ctrlKey);
    }
  }

  /** Focus landing on a cell makes it the active one, and gives it its tooltip. */
  protected onFocusIn(event: FocusEvent): void {
    const element = event.target instanceof Element ? event.target.closest<HTMLElement>('[data-c]') : null;
    if (element === null) {
      return;
    }
    const cover = this.coverOf(element);
    const current = this.activeCover();
    if (cover !== null && (cover.row !== current.row || cover.column !== current.column)) {
      this.activeRef.set(this.refAt({ row: cover.row, column: cover.column }));
    }
    const tooltip = this.attachTooltip(element, null);
    if (tooltip !== null && element.matches(':focus-visible')) {
      tooltip.show();
    }
  }

  /** A pointer over a cell gives it its tooltip, when it is cut short or refuses to drill. */
  protected onPointerOver(event: PointerEvent): void {
    const element = event.target instanceof Element ? event.target.closest<HTMLElement>('[data-c]') : null;
    if (element !== null) {
      this.attachTooltip(element, event);
    }
  }

  /** The native copy: the active cell's full text, unless the user selected text themselves. */
  protected onCopy(event: ClipboardEvent): void {
    this.copyPending = false;
    if (this.hasUserSelection()) {
      return;
    }
    if (event.clipboardData !== null) {
      event.clipboardData.setData('text/plain', this.activeText());
      event.preventDefault();
    }
  }

  /** Whether the user selected text inside the pivot: a copy of it is left to the browser. */
  private hasUserSelection(): boolean {
    const selection = typeof window === 'undefined' ? null : window.getSelection();
    return selection !== null && !selection.isCollapsed && this.host.contains(selection.anchorNode);
  }

  /** A press on a header's handle starts a resize. */
  protected onPointerDown(event: PointerEvent): void {
    const handle = event.target instanceof Element ? event.target.closest<HTMLElement>('[data-tm-resize]') : null;
    if (handle === null || !event.isPrimary || event.button !== 0) {
      return;
    }
    const target = decodeTarget(handle.dataset['tmResize'] ?? '');
    if (target === null) {
      return;
    }
    event.preventDefault();
    event.stopPropagation();
    const plan = this.plan();
    const startX = event.clientX;
    const startWidth = ɵtmPivotWidthOf(plan, target);
    const limits = ɵtmPivotResizeLimits(plan, target);
    const sign = getComputedStyle(this.host).direction === 'rtl' ? -1 : 1;
    const pointerId = event.pointerId;
    this.drag.set({ target, width: startWidth, frozen: plan.measures });
    try {
      handle.setPointerCapture(pointerId);
    } catch {
      // A synthetic press has no active pointer; the drag still follows the handle.
    }
    const onMove = (move: PointerEvent): void => {
      if (move.pointerId !== pointerId) {
        return;
      }
      const width = Math.min(limits.max, Math.max(limits.min, Math.round(startWidth + (move.clientX - startX) * sign)));
      this.drag.update((drag) => (drag === null || drag.width === width ? drag : { ...drag, width }));
    };
    const onEnd = (end: PointerEvent): void => {
      if (end.pointerId !== pointerId) {
        return;
      }
      handle.removeEventListener('pointermove', onMove);
      handle.removeEventListener('pointerup', onEnd);
      handle.removeEventListener('pointercancel', onEnd);
      const drag = this.drag();
      this.drag.set(null);
      if (drag !== null && drag.width !== startWidth) {
        this.widths.set(ɵtmPivotWithWidth(this.widths(), target, drag.width));
      }
    };
    handle.addEventListener('pointermove', onMove);
    handle.addEventListener('pointerup', onEnd);
    handle.addEventListener('pointercancel', onEnd);
  }

  /** A double-click on a header's handle fits its column to its content. */
  protected onDoubleClick(event: MouseEvent): void {
    const handle = event.target instanceof Element ? event.target.closest<HTMLElement>('[data-tm-resize]') : null;
    const target = handle === null ? null : decodeTarget(handle.dataset['tmResize'] ?? '');
    if (target === null) {
      return;
    }
    event.preventDefault();
    const plan = this.plan();
    const limits = ɵtmPivotResizeLimits(plan, target);
    const fit = Math.min(limits.max, Math.max(limits.min, Math.ceil(this.contentWidth(target))));
    this.widths.set(ɵtmPivotWithWidth(this.widths(), target, fit));
  }

  // ---- toggles and activation ----

  /** Toggles the member a cell stands for, when it is an expandable header; returns whether it did. */
  private toggleAt(cover: ɵTmPivotCover): boolean {
    const headerRows = this.columns().headerRows;
    if (cover.row >= headerRows) {
      const row = this.rows()[cover.row - headerRows];
      if (cover.column === 0 && row !== undefined && row.expandable) {
        this.toggleRow(row);
        return true;
      }
      return false;
    }
    const header = this.headerCellAt(cover);
    if (header !== null && header.expandable) {
      this.toggleColumn(header.member);
      return true;
    }
    return false;
  }

  /** Activates a value cell, or explains why it does not drill. */
  private activateAt(cover: ɵTmPivotCover, newTab: boolean): void {
    const headerRows = this.columns().headerRows;
    const lead = this.lead();
    if (cover.row < headerRows || cover.column < lead || !this.activatable()) {
      return;
    }
    const model = this.model();
    const row = this.rows()[cover.row - headerRows];
    const columns = this.columns();
    const data = cover.column - lead;
    const slot = columns.slots[Math.floor(data / columns.measureCount)];
    if (row === undefined || slot === undefined) {
      return;
    }
    const rowId = this.rowAxis().members[row.index].id;
    const columnId = this.columnAxis().members[slot.index].id;
    if (model.activatable !== undefined && !model.activatable(rowId, columnId)) {
      void this.announcer.announce(untracked(this.translate('pivot.notActivatable')), 'polite');
      return;
    }
    this.cellActivate.emit({ row: rowId, column: columnId, measure: data % columns.measureCount, newTab });
  }

  /** Expands or collapses a row member, and announces it. */
  private toggleRow(row: ɵTmPivotRow): void {
    const axis = this.rowAxis();
    const member = axis.members[row.index];
    const next = new Set(this.expandedRows());
    if (row.expanded) {
      next.delete(member.id);
    } else {
      next.add(member.id);
    }
    this.expandedRows.set(next);
    this.announceToggle(member.label, !row.expanded, 'pivot.rowExpanded', () =>
      ɵtmPivotVisibleUnder(axis, row.index, next),
    );
    this.refocusAfterRender();
  }

  /**
   * Expands or collapses a column member by index, and announces it. When
   * the active cell is one of the member's headers, it follows the member
   * to the header it has now: its group header once expanded, its own
   * header once collapsed.
   */
  private toggleColumn(index: number): void {
    const axis = this.columnAxis();
    const member = axis.members[index];
    const expanded = this.expandedColumns().has(member.id);
    const onHeader = this.headerCellAt(this.activeCover())?.member === index;
    const next = new Set(this.expandedColumns());
    if (expanded) {
      next.delete(member.id);
    } else {
      next.add(member.id);
    }
    this.expandedColumns.set(next);
    if (onHeader) {
      const kind = expanded ? 'member' : 'group';
      const header = this.columns().cells.find((cell) => cell.member === index && cell.kind === kind);
      if (header !== undefined) {
        this.activeRef.set(this.refAt({ row: header.row, column: this.lead() + header.start }));
      }
    }
    this.announceToggle(member.label, !expanded, 'pivot.columnExpanded', () =>
      ɵtmPivotVisibleUnder(axis, index, next),
    );
    this.refocusAfterRender();
  }

  /** Announces a toggle: the member and, on expand, what it reveals. */
  private announceToggle(label: string, expanded: boolean, key: string, count: () => number): void {
    const text = expanded
      ? untracked(this.translate(key, { label, count: count() }))
      : untracked(this.translate('pivot.collapsed', { label }));
    void this.announcer.announce(text, 'polite');
  }

  // ---- the active cell ----

  /** Resolves the active identity to a position, falling back to an ancestor or the last position. */
  private resolve(ref: ActiveRef | null): ɵTmPivotPosition {
    const grid = this.grid();
    const columns = grid.columns;
    const lead = 1 + grid.attributeCount;
    if (ref === null) {
      return ɵtmPivotClamp(grid, { row: grid.bodyRows > 0 ? columns.headerRows : 0, column: 0 });
    }
    // Section: the row — a header row by index, a body row by its member
    // or, once hidden, its nearest visible ancestor.
    let row = ref.last.row;
    if ('header' in ref.row) {
      row = Math.min(ref.row.header, columns.headerRows - 1);
    } else {
      const axis = this.rowAxis();
      let index = axis.indexOf.get(ref.row.member) ?? -1;
      const position = this.rowPosition();
      while (index !== -1 && position[index] === -1) {
        index = axis.parent[index];
      }
      if (index !== -1) {
        row = columns.headerRows + position[index];
      }
    }
    // Section: the column — a leading column by index, a data column by
    // its group's member or, once hidden, its nearest shown ancestor.
    let column = ref.last.column;
    if ('lead' in ref.column) {
      column = Math.min(ref.column.lead, lead - 1);
    } else {
      const axis = this.columnAxis();
      let index = axis.indexOf.get(ref.column.member) ?? -1;
      while (index !== -1 && columns.slotOf[index] === -1) {
        index = axis.parent[index];
      }
      if (index !== -1) {
        column = lead + columns.slotOf[index] * columns.measureCount + Math.min(ref.column.measure, columns.measureCount - 1);
      }
    }
    return ɵtmPivotClamp(grid, { row, column });
  }

  /** The identity of a logical position. */
  private refAt(position: ɵTmPivotPosition): ActiveRef {
    const columns = this.columns();
    const lead = this.lead();
    const row = this.rows()[position.row - columns.headerRows];
    const data = position.column - lead;
    const slot = data >= 0 ? columns.slots[Math.floor(data / columns.measureCount)] : undefined;
    return {
      row:
        row === undefined
          ? { header: Math.max(0, position.row) }
          : { member: this.rowAxis().members[row.index].id },
      column:
        slot === undefined
          ? { lead: Math.max(0, Math.min(position.column, lead - 1)) }
          : {
              member: this.columnAxis().members[slot.index].id,
              subtotal: slot.kind === 'subtotal',
              measure: data % columns.measureCount,
            },
      last: position,
    };
  }

  /** Moves the active cell, scrolls it into view and focuses it once rendered. */
  private moveTo(position: ɵTmPivotPosition): void {
    this.activeRef.set(this.refAt(position));
    this.scrollIntoView(position);
    this.refocusAfterRender(true);
  }

  /** Focuses the active cell after the next render, if focus is (still) in the pivot. */
  private refocusAfterRender(force = false): void {
    const inside = force || this.host.contains(document.activeElement);
    if (!inside) {
      return;
    }
    afterNextRender(
      () => {
        const element = this.activeElement();
        if (element !== null && (force || this.host.contains(document.activeElement) || document.activeElement === document.body)) {
          element.focus({ preventScroll: true });
        }
      },
      { injector: this.injector },
    );
  }

  /** Scrolls a position into the part of the viewport the sticky header and columns leave free. */
  private scrollIntoView(position: ɵTmPivotPosition): void {
    const scroller = this.scroller().nativeElement;
    const columns = this.columns();
    const height = this.rowHeight();
    // Section: rows (the header rows always show).
    const body = position.row - columns.headerRows;
    if (body >= 0) {
      const top = body * height;
      const room = Math.max(height, scroller.clientHeight - columns.headerRows * height);
      if (top < scroller.scrollTop) {
        scroller.scrollTop = top;
      } else if (top + height > scroller.scrollTop + room) {
        scroller.scrollTop = top + height - room;
      }
    }
    // Section: columns (the sticky ones always show).
    const plan = this.plan();
    if (position.column < plan.sticky) {
      return;
    }
    const lead = this.lead();
    const x = position.column < lead ? this.leadOffsets()[position.column] : plan.corner + this.dataX(position.column - lead);
    const width =
      position.column === 0
        ? plan.rowHeader
        : position.column < lead
          ? plan.attributes[position.column - 1]
          : plan.measures[(position.column - lead) % plan.measures.length];
    const rtl = getComputedStyle(this.host).direction === 'rtl';
    const left = Math.abs(scroller.scrollLeft);
    let next = left;
    if (x < left + plan.stickyWidth) {
      next = x - plan.stickyWidth;
    } else if (x + width > left + scroller.clientWidth) {
      next = x + width - scroller.clientWidth;
    }
    if (next !== left) {
      scroller.scrollLeft = rtl ? -Math.max(0, next) : Math.max(0, next);
    }
  }

  /** The body rows one page holds. */
  private pageRows(): number {
    const height = this.rowHeight();
    return Math.max(1, Math.floor((this.viewport().height - this.columns().headerRows * height) / height));
  }

  /** The rendered element of the active cell, if rendered. */
  private activeElement(): HTMLElement | null {
    const cover = untracked(this.activeCover);
    return this.host.querySelector<HTMLElement>(`[data-r="${cover.row}"][data-c="${cover.column}"]`);
  }

  /** The cover of a rendered cell, from its data attributes. */
  private coverOf(element: HTMLElement): ɵTmPivotCover | null {
    const row = Number(element.dataset['r']);
    const column = Number(element.dataset['c']);
    if (!Number.isInteger(row) || !Number.isInteger(column) || column < 0) {
      return null;
    }
    return ɵtmPivotCover(this.grid(), { row, column });
  }

  /** The column header a cover stands for, when it is one. */
  private headerCellAt(cover: ɵTmPivotCover): ɵTmPivotHeaderCell | null {
    const columns = this.columns();
    const lead = this.lead();
    if (cover.row >= columns.headerRows || cover.column < lead) {
      return null;
    }
    return (
      columns.rowCells[cover.row].find((cell) => cell.row === cover.row && lead + cell.start === cover.column) ?? null
    );
  }

  /** The active cell's full text, as rendered (CSS cuts it visually only). */
  private activeText(): string {
    return (this.activeElement()?.textContent ?? '').replace(/\s+/g, ' ').trim();
  }

  // ---- tooltips ----

  /**
   * Gives a cell its tooltip when it needs one: the refusal of a value cell
   * that does not drill (also its description), or the full text of one
   * cut short. One cell holds a tooltip at a time; a hover replays the
   * pointer's entry so the tooltip's own delay applies.
   */
  private attachTooltip(element: HTMLElement, pointer: PointerEvent | null): ɵTmTooltipController | null {
    const refused = element.classList.contains('tm-pivot__value--refused');
    const cutElement = element.querySelector<HTMLElement>('.tm-pivot__label') ?? element;
    const cut = cutElement.scrollWidth > cutElement.clientWidth;
    const full = (element.textContent ?? '').replace(/\s+/g, ' ').trim();
    const refusal = refused ? untracked(this.translate('pivot.notActivatable')) : null;
    const text = refusal !== null ? (cut ? `${full}\n${refusal}` : refusal) : cut ? full : null;
    if (this.tooltip !== null && this.tooltip.element === element && this.tooltip.text === text) {
      return this.tooltip.controller;
    }
    this.dropTooltip();
    if (text === null) {
      return null;
    }
    const controller = new ɵTmTooltipController(element, () => this.viewContainer, this.describer);
    controller.setText(text, refusal === null ? [] : [refusal]);
    this.tooltip = { element, text, controller };
    if (pointer !== null && pointer.pointerType !== 'touch') {
      element.dispatchEvent(new PointerEvent('pointerenter', { pointerType: pointer.pointerType }));
    }
    return controller;
  }

  /** Removes the current cell tooltip and its description. */
  private dropTooltip(): void {
    this.tooltip?.controller.destroy();
    this.tooltip = null;
  }

  // ---- measuring ----

  /** The host width and the scroller's client box. */
  private readSizes(): { readonly host: number; readonly width: number; readonly height: number } {
    const scroller = this.scroller().nativeElement;
    return {
      host: this.host.getBoundingClientRect().width,
      width: scroller.clientWidth,
      height: scroller.clientHeight,
    };
  }

  /** Applies measured sizes, keeping identical values so nothing recomputes. */
  private applySizes(sizes: { readonly host: number; readonly width: number; readonly height: number }): void {
    this.hostWidth.set(sizes.host);
    const viewport = untracked(this.viewport);
    if (viewport.width !== sizes.width || viewport.height !== sizes.height) {
      this.viewport.set({ width: sizes.width, height: sizes.height });
    }
  }

  /** Takes measured fits: each measure only widens, unless the measure count changed. Ignores an unmeasured result. */
  private widen(widths: readonly number[] | null): void {
    if (widths !== null) {
      this.fitState.update((previous) =>
        previous.length === widths.length ? widths.map((width, index) => Math.max(width, previous[index] ?? 0)) : widths,
      );
    }
  }

  /**
   * Each measure's widest value measured in the probe: text, padding and
   * rule. The values are asked of the model once per model; a font that
   * arrives later, the pivot showing after a hidden first render, or a
   * double-click fit measures them again. `null` while the probe is not
   * rendered (the pivot sits inside `display: none`), which marks the fits
   * unmeasured.
   */
  private measureWidest(model: TmPivotModel): readonly number[] | null {
    if (this.widestCache?.model !== model) {
      const values = Array.from({ length: Math.max(1, model.measures.length) }, (_, index) => model.widest(index));
      this.widestCache = { model, values };
    }
    const probe = this.probe().nativeElement;
    this.unmeasured = probe.getClientRects().length === 0;
    if (this.unmeasured) {
      return null;
    }
    const widths: number[] = [];
    for (const widest of this.widestCache.values) {
      if (widest === null || widest === '') {
        widths.push(0);
        continue;
      }
      probe.textContent = widest;
      widths.push(Math.ceil(probe.getBoundingClientRect().width));
    }
    probe.textContent = '';
    return widths;
  }

  /** What a column needs to show its content uncut, for a double-click fit. */
  private contentWidth(target: ɵTmPivotResizeTarget): number {
    const model = untracked(this.model);
    if (target.kind === 'measure') {
      return this.measureWidest(model)?.[target.index] ?? 0;
    }
    // Labels need no tabular figures, so a canvas measures them in bulk.
    const probe = this.probe().nativeElement;
    const style = getComputedStyle(probe);
    const hostStyle = getComputedStyle(this.host);
    const context = document.createElement('canvas').getContext('2d');
    if (context === null) {
      return 0;
    }
    const font = (weight: string): string => `${weight} ${style.fontSize} ${style.fontFamily}`;
    const measure = (text: string, weight: string): number => {
      context.font = font(weight);
      return context.measureText(text).width;
    };
    const padding = 2 * cssPx(style, 'padding-inline-start', 0) + 1;
    const regular = hostStyle.getPropertyValue('--pivot-leaf-font-weight').trim() || '400';
    const strong = hostStyle.getPropertyValue('--pivot-total-font-weight').trim() || '600';
    const axis = this.rowAxis();
    const rows = this.rows();
    if (target.kind === 'attribute') {
      let widest = measure(model.attributeLabels[target.index] ?? '', strong);
      for (const row of rows) {
        const value = axis.members[row.index].attributes?.[target.index] ?? '';
        widest = Math.max(widest, measure(value, row.style === 'leaf' || row.style === 'phantom' ? regular : strong));
      }
      return widest + padding;
    }
    const indent = cssPx(hostStyle, '--pivot-indent', 16);
    const twisty = cssPx(hostStyle, '--pivot-twisty-size', 13) + cssPx(hostStyle, '--pivot-twisty-gap', 4);
    const direct = ` ${untracked(this.directText)}`;
    let widest = measure(model.rowLabel, strong);
    for (const row of rows) {
      const member = axis.members[row.index];
      const label = member.kind === 'phantom' ? member.label + direct : member.label;
      const weight = row.style === 'leaf' || row.style === 'phantom' ? regular : strong;
      widest = Math.max(widest, indent * (row.level - 1) + twisty + measure(label, weight));
    }
    return widest + padding;
  }

  /** A data column's inline offset from the first data column. */
  private dataX(column: number): number {
    const plan = this.plan();
    const measures = plan.measures.length;
    return Math.floor(column / measures) * plan.slotWidth + (this.measureOffsets()[column % measures] ?? 0);
  }

  // ---- view-model helpers ----

  /** The fields every header view starts from. */
  private headBase(key: string, row: number, column: number, top: number): HeadView {
    return {
      key,
      spacer: false,
      classes: '',
      row,
      column,
      colSpan: null,
      rowSpan: null,
      ariaColIndex: null,
      ariaColSpan: null,
      ariaRowSpan: null,
      expanded: null,
      label: null,
      level: null,
      top,
      start: null,
      resize: null,
    };
  }

  /** A leading column's classes: its own, and its stickiness. */
  private leadClasses(classes: string, column: number, plan: ɵTmPivotWidthPlan): string {
    let result = `tm-pivot__cell ${classes}`;
    if (column < plan.sticky) {
      result += ' tm-pivot__sticky';
      if (column === plan.sticky - 1) {
        result += ' tm-pivot__sticky--last';
      }
    }
    return result;
  }

  /** A column header's classes. */
  private headerClasses(cell: ɵTmPivotHeaderCell): string {
    let classes = `tm-pivot__cell tm-pivot__header ${this.styleClass(cell.style)}`;
    if (cell.expandable) {
      classes += ' tm-pivot__header--expandable';
    }
    if (cell.kind === 'measure') {
      classes += ' tm-pivot__header--measure';
    }
    if (cell.style === 'total' && (cell.kind !== 'measure' || cell.measure === 0)) {
      classes += ' tm-pivot__rule-inline';
    }
    return classes;
  }

  /** A column header's label. */
  private headerLabel(cell: ɵTmPivotHeaderCell): LabelView {
    if (cell.kind === 'measure') {
      return { before: '', label: this.model().measures[cell.measure] ?? '', after: '', direct: null };
    }
    const member = this.columnAxis().members[cell.member];
    if (cell.kind === 'subtotal') {
      const parts = this.subtotalParts();
      return { before: parts.before, label: member.label, after: parts.after, direct: null };
    }
    return {
      before: '',
      label: member.label,
      after: '',
      direct: member.kind === 'phantom' ? ` ${this.directText()}` : null,
    };
  }

  /** The class of a look. */
  private styleClass(style: ɵTmPivotStyle): string {
    return `tm-pivot__cell--${style}`;
  }
}
