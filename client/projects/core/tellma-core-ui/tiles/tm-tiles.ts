// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import { LiveAnnouncer } from '@angular/cdk/a11y';
import { NgComponentOutlet, NgTemplateOutlet } from '@angular/common';
import {
  afterEveryRender,
  afterNextRender,
  booleanAttribute,
  ChangeDetectorRef,
  Component,
  computed,
  contentChild,
  DestroyRef,
  effect,
  ElementRef,
  inject,
  input,
  model,
  output,
  PendingTasks,
  signal,
  type Signal,
  untracked,
  viewChild,
} from '@angular/core';
import { Check, Image } from 'lucide';

import { TM_BREAKPOINTS, TM_UI_TRANSLATE } from '@tellma/core-ui';
import { TmBadge, type TmBadgeTone } from '@tellma/core-ui/badge';
import type { TmRowId } from '@tellma/core-ui/contracts';
import type { TmGridDriverTarget, TmGridRowActivation } from '@tellma/core-ui/grid';
import { TmIcon } from '@tellma/core-ui/icon';
import { TmImage, TmImagePlaceholder } from '@tellma/core-ui/image';
import { tmIsApplePlatform } from '@tellma/core-ui/kbd';
import { TmSkeleton } from '@tellma/core-ui/skeleton';

import {
  ɵtmTilesAssignSlots,
  ɵtmTilesColumns,
  ɵtmTilesLineCount,
  ɵtmTilesMove,
  ɵtmTilesRenderedLines,
  ɵtmTilesSpan,
  type ɵTmTilesMove,
} from './internal/tm-tiles-lattice';
import type { ɵTmTilesRow, ɵTmTilesTree, ɵTmTilesTreePlace } from './internal/tm-tiles-tree-seam';
import { TmTileContent, TmTilesEmpty, TmTilesLoading } from './tm-tiles-templates';
import type { TmTileImage, TmTileLayout, TmTileText } from './tm-tiles-types';

/** A second activation of the same row within this window (ms) is ignored. */
const ACTIVATION_GUARD_MS = 400;
/** A press that travels this far (px) before its click is a drag or a text selection, not a tap. */
const CLICK_SLOP_PX = 4;
/** The image box (px): `tm-image` sizes its rendition from numbers. Pinned to `--tiles-image-size` by a test. */
const IMAGE_SIZE = 40;
/** Geometry fallbacks (px) while the tokens are unresolvable. */
const DEFAULT_HEIGHT = 60;
const DEFAULT_GAP = 10;
const DEFAULT_MIN_WIDTH = 220;
/** Flash length fallback (ms) while `--tiles-flash-duration` is unresolvable. */
const DEFAULT_FLASH_MS = 1200;
/** Scrolling counts as settled this long (ms) after its last scroll event. */
const SCROLL_SETTLE_MS = 100;

/** Numbers each instance's element ids. */
let nextTilesId = 0;

/** One corner's text, normalized from {@link TmTileText}. */
interface Corner {
  /** Where it sits: the suffix of its `tm-tiles__corner--` class. */
  readonly place: 'top-end' | 'bottom-start' | 'bottom-end';
  readonly text: string;
  readonly numeric: boolean;
  readonly badge: TmBadgeTone | null;
}

/** What a row shows, read once per row through the layout. */
interface TileContent {
  readonly title: string;
  readonly titleParts: readonly string[] | null;
  readonly href: string | null;
  /** `undefined`: the layout has no image column. */
  readonly image: TmTileImage | undefined;
  /** The corners that have text, in reading order. */
  readonly corners: readonly Corner[];
  readonly rowClass: string | null;
  /** The checkbox's accessible name ("Select …"); empty when not selectable. */
  readonly checkLabel: string;
}

/** One rendered tile. */
interface TileView<T> {
  readonly row: T;
  readonly id: TmRowId;
  /** The position in the visible sequence. */
  readonly index: number;
  readonly elementId: string;
  readonly tabStop: boolean;
  readonly selected: boolean;
  readonly classes: string;
  readonly content: TileContent;
  /**
   * The image source to load now, as at most one entry keyed by itself, so
   * a tile that moves to another row's image gets a fresh `tm-image`; empty
   * shows the placeholder (no source, or held while scrolling).
   */
  readonly images: readonly string[];
}

/** One rendered lattice line. */
interface LineView<T> {
  readonly index: number;
  /** What the line's element is tracked by: a slot the line keeps while rendered, then hands on. */
  readonly slot: number;
  readonly top: number;
  readonly tiles: readonly TileView<T>[];
  /** Tree mode's place of the line's one tile: its row attributes and chrome; `null` when flat. */
  readonly tree: ɵTmTilesTreePlace | null;
}

/** Normalizes one corner into the list of those with text. */
function corner(
  corners: Corner[],
  place: Corner['place'],
  value: TmTileText | null | undefined,
): void {
  // No corner for null, undefined or empty text.
  if (!value) {
    return;
  }
  const { text, numeric, badge } = typeof value === 'string' ? { text: value } : value;
  corners.push({ place, text, numeric: numeric === true, badge: badge ?? null });
}

/** Reads a px length token, or the fallback while it is unresolvable. */
function pxToken(style: CSSStyleDeclaration, name: string, fallback: number): number {
  const value = Number.parseFloat(style.getPropertyValue(name));
  return Number.isFinite(value) && value >= 0 ? value : fallback;
}

/**
 * Rows as a virtualized lattice of fixed-size tiles — an image and four
 * text corners each — for browsing records as cards rather than a table.
 * Columns follow the container width (tiles at least 220px wide); below the
 * small breakpoint the lattice becomes a divided list. Bound with
 * `[parentId]` through `TmTilesTree` (from `@tellma/core-ui/tiles-tree`),
 * it is a single-column tree whose children load lazily in batches, so a
 * flat list carries none of the tree's code. Its members mirror the data
 * grid's, so a page can swap grid and tiles over one state, and
 * `<input [tmGridDriver]="tiles">` moves the active tile from a search
 * field.
 *
 * ```html
 * <tm-tiles
 *   ariaLabel="Customers"
 *   [data]="customers()"
 *   [rowId]="customerId"
 *   [layout]="customerTile"
 *   selectable
 *   [(selectedIds)]="selected"
 *   (rowActivate)="open($event)"
 * />
 * ```
 *
 * @tmGroup collection
 * @tmA11yNotes Flat, the list is an APG layout grid: `role="grid"` with
 *   `aria-rowcount` and `aria-colcount` over every line, each lattice line
 *   a `row` and each tile a `gridcell` with `aria-colindex` (and
 *   `aria-selected` when selectable). In tree mode (`TmTilesTree`) it is a
 *   `treegrid` of one cell per row, the rows carrying `aria-level`, `aria-expanded`,
 *   `aria-posinset` and `aria-setsize`; a row whose children load is
 *   `aria-busy`. One tab stop: the arrows move between tiles in the live
 *   direction (Up/Down keep the column), Home/End go to the line's ends,
 *   Ctrl+Home/End to the first and last tile, PageUp/PageDown by visible
 *   lines, Enter opens the tile (Mod+Enter in a new tab), Space toggles its
 *   selection and Ctrl+Shift+Space toggles every loaded tile; in tree mode
 *   Right expands or enters and Left collapses or leaves. The title link is
 *   out of the tab order (`tabindex="-1"`); the checkbox is a named
 *   pointer target whose state the cell's `aria-selected` also carries.
 *   Each selection change announces "{count} of {total} selected", and a
 *   failed children load announces once. The active tile is always
 *   rendered, so `aria-activedescendant` never dangles. Screen-reader
 *   verification beyond the ARIA mechanics remains a manual pass.
 */
@Component({
  selector: 'tm-tiles',
  imports: [
    NgComponentOutlet,
    NgTemplateOutlet,
    TmBadge,
    TmIcon,
    TmImage,
    TmImagePlaceholder,
    TmSkeleton,
  ],
  template: `
    <!-- The scroll container is the grid: lines are its rows, tiles its
         cells. Pointer and keyboard handling is delegated here. -->
    <div
      #scroller
      class="tm-tiles__scroller"
      tabindex="-1"
      [id]="controlsId()"
      [attr.role]="tree() ? 'treegrid' : 'grid'"
      [attr.aria-label]="ariaLabel()"
      [attr.aria-rowcount]="lineCount()"
      [attr.aria-colcount]="columns()"
      [attr.aria-multiselectable]="selectable() ? 'true' : null"
      [attr.aria-busy]="loading() ? 'true' : null"
      (scroll)="onScroll()"
      (keydown)="onKeydown($event)"
      (pointerdown)="onPointerDown($event)"
      (pointercancel)="onPointerCancel()"
      (click)="onClick($event)"
      (auxclick)="onAuxClick($event)"
      (focusin)="onFocusIn($event)"
      (focusout)="onFocusOut()"
    >
      <div class="tm-tiles__canvas" [style.block-size.px]="extent()">
        <!-- Skeleton tiles hold the final geometry while the first data loads. -->
        @if (showSkeleton()) {
          @for (line of skeletonLines(); track line) {
            <div
              class="tm-tiles__line tm-tiles__line--skeleton"
              aria-hidden="true"
              [style.transform]="'translateY(' + line * pitch() + 'px)'"
            >
              @for (column of skeletonColumns(); track column) {
                <div class="tm-tiles__item tm-tiles__item--skeleton">
                  @if (layout().image) {
                    <tm-skeleton
                      class="tm-tiles__image"
                      shape="block"
                      width="var(--tiles-image-size)"
                      height="var(--tiles-image-size)"
                    />
                  }
                  <tm-skeleton class="tm-tiles__skeleton-text" [lines]="2" />
                </div>
              }
            </div>
          }
        }

        <!-- The windowed lines, plus the active tile's line wherever it is.
             Lines are tracked by slot and built-in tiles by position, so a
             scroll re-binds the elements of the lines that leave to the
             lines that enter instead of rebuilding them; the DOM stays in
             line order for the reading order. A custom body's tiles are
             tracked by row: its views are never re-bound to another row. -->
        @for (line of lines(); track line.slot) {
          <div
            class="tm-tiles__line"
            role="row"
            [attr.aria-rowindex]="line.index + 1"
            [attr.aria-level]="line.tree?.level ?? null"
            [attr.aria-expanded]="line.tree?.expanded ?? null"
            [attr.aria-posinset]="line.tree?.posInSet ?? null"
            [attr.aria-setsize]="line.tree?.setSize ?? null"
            [attr.aria-busy]="line.tree?.loading ? 'true' : null"
            [style.transform]="'translateY(' + line.top + 'px)'"
          >
            @for (tile of line.tiles; track contentDef() ? tile.id : $index) {
              <div
                role="gridcell"
                class="tm-tiles__item"
                [class]="tile.classes"
                [id]="tile.elementId"
                [attr.data-index]="tile.index"
                [attr.aria-colindex]="$index + 1"
                [attr.aria-selected]="selectable() ? tile.selected : null"
                [attr.tabindex]="tile.tabStop ? 0 : -1"
                [style.--tm-tiles-level]="line.tree?.level ?? 1"
              >
                <!-- Tree chrome, the tree's own: the twisty and the reserved
                     spinner slot (the indent is the tile's padding). -->
                @if (line.tree; as place) {
                  <ng-container
                    [ngComponentOutlet]="treeMode()!.affordance"
                    [ngComponentOutletInputs]="{ place }"
                  />
                }
                <!-- The reserved checkbox: a pointer target that toggles selection only. -->
                @if (selectable()) {
                  <span
                    class="tm-tiles__check"
                    data-tm-check
                    role="checkbox"
                    [attr.aria-checked]="tile.selected"
                    [attr.aria-label]="tile.content.checkLabel"
                  >
                    <!-- The check is drawn only when checked: the box keeps its size either way. -->
                    <span class="tm-tiles__box">
                      @if (tile.selected) {
                        <tm-icon [svg]="checkGlyph" />
                      }
                    </span>
                  </span>
                }
                @if (contentDef(); as custom) {
                  <div class="tm-tiles__body">
                    <ng-container
                      [ngTemplateOutlet]="custom.template"
                      [ngTemplateOutletContext]="{ $implicit: tile.row, rowId: tile.id }"
                    />
                  </div>
                } @else {
                  <!-- The built-in body: the image column, then the four corners. -->
                  <!-- Inline markup, no shared templates: a reused tile
                       re-binds its title and corners in place. -->
                  @let content = tile.content;
                  @if (content.image; as image) {
                    <span class="tm-tiles__image">
                      <!-- A tile that arrives mid-scroll holds its source until
                           scrolling settles, so a fling fetches and decodes
                           nothing; the window already bounds the instances,
                           so tm-image's own viewport deferral is off. The
                           image is keyed by its source: a re-bound tile never
                           shows its previous row's image. -->
                      @for (src of tile.images; track src) {
                        <tm-image
                          [src]="src"
                          [etag]="image.etag ?? null"
                          [alt]="image.alt"
                          [width]="imageSize"
                          [height]="imageSize"
                          [defer]="false"
                          placeholderOnError
                        >
                          <span *tmImagePlaceholder class="tm-tiles__placeholder">
                            <tm-icon [svg]="placeholderGlyph" />
                          </span>
                        </tm-image>
                      } @empty {
                        <span class="tm-tiles__placeholder">
                          <tm-icon [svg]="placeholderGlyph" />
                        </span>
                      }
                    </span>
                  }
                  <span class="tm-tiles__text">
                    <!-- The title, a link when the row has one (without an
                         href the <a> is no link and takes no focus): plain,
                         or as parts each isolated in its own <bdi>. -->
                    <a
                      class="tm-tiles__title"
                      [attr.href]="content.href"
                      [attr.tabindex]="content.href === null ? null : -1"
                    >
                      @if (content.titleParts; as parts) {
                        @for (part of parts; track $index) {
                          @if (!$first) {
                            {{ ' - ' }}
                          }
                          <bdi>{{ part }}</bdi>
                        }
                      } @else {
                        {{ content.title }}
                      }
                    </a>
                    <!-- The corners: text, numeric text (LTR, tabular,
                         physically right), or a badge. They hold no state of
                         their own, so they re-bind in place by position. -->
                    @for (value of content.corners; track $index) {
                      <span
                        class="tm-tiles__corner"
                        [class]="'tm-tiles__corner--' + value.place"
                        [class.tm-tiles__corner--numeric]="value.numeric"
                        [attr.dir]="value.numeric ? 'ltr' : null"
                      >
                        @if (value.badge; as tone) {
                          <tm-badge [tone]="tone">{{ value.text }}</tm-badge>
                        } @else {
                          {{ value.text }}
                        }
                      </span>
                    }
                  </span>
                }
              </div>
            }
          </div>
        }
      </div>
    </div>

    <!-- Loading and empty layers: siblings of the scroller, so they cover
         the viewport whatever the scroll position. -->
    @if (loading() && data().length === 0) {
      @if (loadingDef(); as custom) {
        <div class="tm-tiles__overlay" data-tm-loading>
          <ng-container [ngTemplateOutlet]="custom.template" />
        </div>
      }
    } @else if (data().length === 0) {
      <div class="tm-tiles__overlay" data-tm-empty>
        @if (emptyDef(); as custom) {
          <ng-container [ngTemplateOutlet]="custom.template" />
        } @else {
          <span class="tm-tiles__empty">{{ emptyText() }}</span>
        }
      </div>
    }
  `,
  styleUrl: './tm-tiles.css',
  host: {
    class: 'tm-tiles',
    '[attr.data-layout]': 'tree() ? "tree" : stacked() ? "list" : "lattice"',
    '[style.--tm-tiles-columns]': 'columns()',
  },
})
export class TmTiles<T> implements TmGridDriverTarget {
  private readonly host = inject<ElementRef<HTMLElement>>(ElementRef).nativeElement;
  private readonly translate = inject(TM_UI_TRANSLATE);
  private readonly announcer = inject(LiveAnnouncer);
  private readonly changeDetector = inject(ChangeDetectorRef);

  // ---- inputs, models, output ----

  /** The rows, in display order (tree mode: any order; children follow their parents). */
  readonly data = input<readonly T[]>([]);
  /** Reads a row's stable id: tiles are tracked, selected and activated by it. */
  readonly rowId = input.required<(row: T) => TmRowId>();
  /** How a row maps onto a tile's image and corners. */
  readonly layout = input.required<TmTileLayout<T>>();
  /** The list's accessible name. */
  readonly ariaLabel = input.required<string>();
  /**
   * Whether data is loading: without data, skeleton tiles hold the final
   * geometry (or the `tmTilesLoading` template shows); with data, the list
   * is `aria-busy`.
   */
  readonly loading = input(false, { transform: booleanAttribute });
  /** Whether tiles reserve a checkbox at inline-start, and Space toggles selection. */
  readonly selectable = input(false, { transform: booleanAttribute });
  /**
   * The selected row ids, two-way: a fresh set on every change the tiles
   * make. Ids of rows that are not loaded are kept.
   */
  readonly selectedIds = model<ReadonlySet<TmRowId>>(new Set());
  /**
   * The active tile's row id, two-way: the arrows move it, and a write moves
   * it and scrolls it into view (nearest) without moving focus — a search
   * field can drive it. An id with no row is ignored and written back.
   */
  readonly activeRowId = model<TmRowId | null>(null);
  /**
   * Extra classes for a row's tile. Built in: `tm-tiles__item--context`
   * (drawn muted around the rows that matter) and `tm-tiles__item--marked`
   * (drawn emphasized).
   */
  readonly rowClass = input<((row: T) => string | null) | undefined>(undefined);
  /**
   * Emits when a tile opens: a click or tap anywhere but the checkbox and
   * the twisty, Enter, or `activateRow`. `newTab` is set for a Mod-click, a
   * middle click and Mod+Enter. A second activation of the same row within
   * 400ms is ignored.
   */
  readonly rowActivate = output<TmGridRowActivation<T>>();

  // ---- templates ----

  /** The `tmTileContent` template, replacing every tile's body. */
  protected readonly contentDef = contentChild(TmTileContent);
  /** The `tmTilesEmpty` template. */
  protected readonly emptyDef = contentChild(TmTilesEmpty);
  /** The `tmTilesLoading` template. */
  protected readonly loadingDef = contentChild(TmTilesLoading);
  /** The scroll container. */
  private readonly scroller = viewChild.required<ElementRef<HTMLElement>>('scroller');

  // ---- tree mode ----

  /**
   * The tree applied to these tiles, which `TmTilesTree` sets on itself
   * when it is created on the same element; `null` for a flat list.
   *
   * @internal
   */
  readonly ɵtree = signal<ɵTmTilesTree<T> | null>(null);

  // ---- glyphs and constants the template reads ----

  /** The checkbox's check. */
  protected readonly checkGlyph = Check;
  /** The image placeholder's glyph. */
  protected readonly placeholderGlyph = Image;
  /** The image box, in px. */
  protected readonly imageSize = IMAGE_SIZE;
  /** The built-in empty text. */
  protected readonly emptyText = this.translate('tiles.empty');

  // ---- measured state ----

  /** The element ids' prefix. */
  private readonly idPrefix = `tm-tiles-${nextTilesId++}`;
  /** The scroller's content width; `NaN` until measured. */
  private readonly width = signal(Number.NaN);
  /** The scroller's visible height. */
  private readonly viewportHeight = signal(0);
  /** The scroller's scroll offset. */
  private readonly scrollTop = signal(0);
  /** Whether the scroller is scrolling: set by each scroll event, cleared once they stop. */
  private readonly scrolling = signal(false);
  /** Clears `scrolling` once the scroll events stop. */
  private scrollTimer: ReturnType<typeof setTimeout> | undefined;
  /** Rows whose tile showed its image while scrolling was settled: they keep it through a scroll. */
  private readonly shownImages = new WeakSet<object>();
  /** The token geometry, read on every resize. */
  private readonly geometry = signal({
    height: DEFAULT_HEIGHT,
    gap: DEFAULT_GAP,
    minWidth: DEFAULT_MIN_WIDTH,
  });

  // ---- interaction state ----

  /** The active row's id: the source of `activeRowId`, current as soon as a move returns. */
  private readonly activeId = signal<TmRowId | null>(null);
  /** The active tile's last known index, where the active tile falls back when its row goes. */
  private lastActiveIndex = 0;
  /** The column vertical moves keep through short-line clamps. */
  private moveColumn: number | null = null;
  /** The tile to scroll into view after the next render. */
  private pendingReveal: { readonly id: TmRowId; readonly center: boolean } | null = null;
  /** Whether the next render moves focus to the active tile (a keyboard move). */
  private pendingFocus = false;
  /** Whether focus is inside the scroller, so a re-render that drops it gets it back. */
  private focusWithin = false;
  /** The row whose tile last took focus: a tile re-bound to another row under focus hands it on. */
  private focusedId: TmRowId | undefined;
  /** The press a click is measured against: the tap slop, and the row pressed. */
  private press: {
    readonly x: number;
    readonly y: number;
    readonly id: TmRowId | undefined;
  } | null = null;
  /** The last activation, for the repeat guard. */
  private lastActivation: { readonly id: TmRowId; readonly at: number } | null = null;
  /** The flashing tile and its keyframes (`1` or `2`, alternating so a re-flash restarts). */
  private readonly flash = signal<{ readonly id: TmRowId; readonly keyframes: 1 | 2 } | null>(null);
  /** Clears the flash once it has run. */
  private flashTimer: ReturnType<typeof setTimeout> | undefined;

  // ---- derived state ----

  /** The tree's rows on show while one is applied with a parent reader bound; `null` when flat. */
  private readonly treeRows = computed(() => this.ɵtree()?.rows() ?? null);
  /** The tree the tiles are, or `null` when flat. */
  protected readonly treeMode = computed(() => (this.treeRows() === null ? null : this.ɵtree()));
  /** Whether the tiles are a tree. */
  protected readonly tree = computed(() => this.treeMode() !== null);
  /** Whether the tiles stack one per line: narrow, or a tree. */
  protected readonly stacked = computed(() => this.tree() || !(this.width() >= TM_BREAKPOINTS.sm));
  /** Tiles per line. Recomputed on resize only: the width changes nowhere else. */
  protected readonly columns = computed(() => {
    const geometry = this.geometry();
    return ɵtmTilesColumns(this.width(), {
      tree: this.tree(),
      minWidth: geometry.minWidth,
      gap: geometry.gap,
      breakpoint: TM_BREAKPOINTS.sm,
    });
  });
  /** The distance from one line's top to the next: the tile plus the gap, or the tile alone when stacked. */
  protected readonly pitch = computed(() => {
    const geometry = this.geometry();
    return geometry.height + (this.stacked() ? 0 : geometry.gap);
  });

  /** The rows on show, in display order: every row when flat, the tree's expanded sequence. */
  private readonly visible = computed((): readonly ɵTmTilesRow<T>[] => {
    const rows = this.treeRows();
    if (rows !== null) {
      return rows;
    }
    const rowIdOf = this.rowId();
    return this.data().map((row) => ({ row, id: rowIdOf(row) }));
  });

  /** Each visible row's index, by id. */
  private readonly indexById = computed(() => {
    const index = new Map<TmRowId, number>();
    const rows = this.visible();
    for (let i = 0; i < rows.length; i++) {
      if (!index.has(rows[i].id)) {
        index.set(rows[i].id, i);
      }
    }
    return index;
  });

  /** The active tile's index in the visible sequence, or `null`. */
  private readonly activeIndex = computed(() => {
    const id = this.activeId();
    return id === null ? null : (this.indexById().get(id) ?? null);
  });

  /** The lattice's line count. */
  protected readonly lineCount = computed(() =>
    ɵtmTilesLineCount({ columns: this.columns(), count: this.visible().length }),
  );

  /**
   * The offset a scroll started at, while its own frame holds the overscan
   * back: set by the scroll and cleared in the next task, so a jump's frame
   * re-binds only lines it already has and the overscan follows in a task
   * of its own.
   */
  private readonly sliceFrom = signal<number | null>(null);
  /** Keeps the application unstable until the held-back overscan renders. */
  private readonly pendingTasks = inject(PendingTasks);

  /** The windowed lines over the scroll position. */
  private readonly lineWindow = computed(
    () =>
      ɵtmTilesSpan(
        this.scrollTop(),
        this.sliceFrom(),
        this.pitch(),
        this.lineCount(),
        this.viewportHeight(),
      ),
    // A scroll within the same lines renders nothing new.
    { equal: (a, b) => a.start === b.start && a.end === b.end },
  );

  /**
   * The slot each line had in the last render, by line in rendering order:
   * the lines a render keeps hold their slots, and the lines it brings in
   * take the slots of those it drops.
   */
  private lineSlots = new Map<number, number>();

  /** The canvas height: every line at the pitch. */
  protected readonly extent = computed(() =>
    this.showSkeleton() ? 0 : this.lineCount() * this.pitch(),
  );

  /**
   * What each row shows, one computed per row while the data, the layout and
   * `rowClass` stay: a row re-reads only when a signal its readers read
   * changes (a translated label), and a window move reads only new rows.
   */
  private readonly contentCache = computed(() => {
    this.data();
    this.layout();
    this.rowClass();
    return new Map<T, Signal<TileContent>>();
  });

  /** Whether skeleton tiles stand in for the first data. */
  protected readonly showSkeleton = computed(
    () => this.loading() && this.data().length === 0 && this.loadingDef() === undefined,
  );
  /** The skeleton's lines: enough to fill the viewport. */
  protected readonly skeletonLines = computed(() =>
    indexes(Math.max(1, Math.ceil(this.viewportHeight() / this.pitch()))),
  );
  /** The skeleton's tiles per line. */
  protected readonly skeletonColumns = computed(() => indexes(this.columns()));

  /** The rendered lines and their tiles. */
  protected readonly lines = computed((): readonly LineView<T>[] => {
    const rows = this.visible();
    const columns = this.columns();
    const pitch = this.pitch();
    const window = this.lineWindow();
    const active = this.activeIndex();
    const selected = this.selectable() ? this.selectedIds() : null;
    const flash = this.flash();
    const tree = this.treeMode();
    const cache = this.contentCache();
    const scrolling = this.scrolling();
    const shownImages = this.shownImages;
    // One tab stop: the active tile, else the first rendered one.
    const tabStop = active ?? Math.min(window.start * columns, rows.length - 1);
    // The windowed lines, plus the active tile's line wherever it is, each
    // with its slot. Slots follow from the last render's, and the same lines
    // keep the same slots, so re-running this for another reason (a
    // selection) re-binds nothing.
    this.lineSlots = ɵtmTilesAssignSlots(
      ɵtmTilesRenderedLines(
        window.start,
        window.end,
        active === null ? null : Math.floor(active / columns),
      ),
      this.lineSlots,
    );
    return Array.from(this.lineSlots, ([line, slot]): LineView<T> => {
      const tiles: TileView<T>[] = [];
      const end = Math.min(rows.length, (line + 1) * columns);
      for (let index = line * columns; index < end; index++) {
        const { row, id } = rows[index];
        const content = this.contentOf(row, cache);
        const isSelected = selected?.has(id) === true;
        // The tile's state classes, after the consumer's.
        let classes = content.rowClass ?? '';
        if (index === active) {
          classes += ' tm-tiles__item--active';
        }
        if (isSelected) {
          classes += ' tm-tiles__item--selected';
        }
        if (flash !== null && flash.id === id) {
          classes += flash.keyframes === 1 ? ' tm-tiles__item--flash' : ' tm-tiles__item--reflash';
        }
        const src = this.imageSrcOf(row, content, scrolling, shownImages);
        tiles.push({
          row,
          id,
          images: src === null ? [] : [src],
          index,
          elementId: `${this.idPrefix}-t${index}`,
          tabStop: index === tabStop,
          selected: isSelected,
          classes,
          content,
        });
      }
      // A tree is one tile per line: the line carries its row's place.
      const first = tiles[0];
      return {
        index: line,
        slot,
        top: line * pitch,
        tiles,
        tree: tree === null || first === undefined ? null : tree.place(first.id),
      };
    });
  });

  // ---- driver target ----

  /** The scroll container's id, which a driving input's `aria-controls` names. */
  readonly controlsId = signal(`${this.idPrefix}-grid`).asReadonly();
  /** The active tile's element id, which a driving input's `aria-activedescendant` names. */
  readonly activeDescendantId = computed(() => {
    const index = this.activeIndex();
    return index === null ? null : `${this.idPrefix}-t${index}`;
  });
  /** How many lattice lines a PageDown moves: the lines the viewport shows. */
  readonly pageStep = computed(() => Math.max(1, Math.floor(this.viewportHeight() / this.pitch())));

  constructor() {
    const destroyRef = inject(DestroyRef);

    // Consumer writes to activeRowId: a known row becomes active (its tree
    // ancestors expand) and scrolls into view without taking focus; an
    // unknown id is written back.
    effect(() => {
      const written = this.activeRowId();
      untracked(() => {
        if (written === this.activeId()) {
          return;
        }
        if (written !== null && this.reveal(written)) {
          this.setActive(written, { reveal: true, focus: false });
        } else {
          this.activeRowId.set(this.activeId());
        }
      });
    });

    // The active row left the visible rows: a collapsed ancestor takes it,
    // else the tile now at its old index (clamped), else nothing.
    effect(() => {
      const id = this.activeId();
      const index = this.indexById();
      untracked(() => {
        if (id === null) {
          return;
        }
        const at = index.get(id);
        if (at !== undefined) {
          this.lastActiveIndex = at;
          return;
        }
        const ancestor = this.treeMode()?.shownAncestor(id, index) ?? null;
        const rows = this.visible();
        const next =
          ancestor ??
          (rows.length === 0 ? null : rows[Math.min(this.lastActiveIndex, rows.length - 1)].id);
        this.setActive(next, { reveal: false, focus: this.focusWithin });
      });
    });

    // Measure once the scroller exists, then on every resize: the width
    // decides the columns, the height the window and the page step.
    afterNextRender({
      read: () => {
        const scroller = this.scroller().nativeElement;
        this.measure(scroller);
        const observer = new ResizeObserver(() => this.measure(scroller));
        observer.observe(scroller);
        destroyRef.onDestroy(() => observer.disconnect());
      },
    });

    // After each render: scroll a requested tile into view, move focus with
    // a keyboard move, and take back focus a re-render dropped onto <body>.
    afterEveryRender({ write: () => this.afterRender() });

    destroyRef.onDestroy(() => {
      clearTimeout(this.flashTimer);
      clearTimeout(this.scrollTimer);
    });
  }

  // ---- public methods ----

  /**
   * Moves the active tile by lattice lines, keeping the column (clamped,
   * never wrapping), or to the first or last tile. From no active tile it
   * starts at the first. Scrolls the tile into view; focus stays where it is.
   */
  moveActiveRow(by: number | 'first' | 'last'): void {
    this.move(typeof by === 'number' ? { kind: 'lines', by } : { kind: by }, false);
  }

  /**
   * Opens a row as a keyboard activation: emits `rowActivate` for it (the
   * active row when no id is given). Returns `false` when there is no such
   * row; a repeat within the guard window is ignored but returns `true`.
   */
  activateRow(rowId?: TmRowId, options: { newTab?: boolean } = {}): boolean {
    const id = rowId ?? this.activeId();
    return id !== null && this.activate(id, 'keyboard', options.newTab === true);
  }

  /**
   * Scrolls a row into view — in tree mode, expanding its ancestors first —
   * centered by default, and by default makes it the active tile. Never
   * moves focus. `flash` tints the tile briefly. Returns `false` when no row
   * has the id.
   */
  scrollToRow(
    rowId: TmRowId,
    options: { flash?: boolean; activate?: boolean; align?: 'nearest' | 'center' } = {},
  ): boolean {
    if (!this.reveal(rowId)) {
      return false;
    }
    if (options.activate !== false) {
      this.setActive(rowId, { reveal: false, focus: false });
    }
    this.pendingReveal = { id: rowId, center: options.align !== 'nearest' };
    if (options.flash === true) {
      this.startFlash(rowId);
    }
    this.changeDetector.markForCheck();
    return true;
  }

  /** The scroll offset, in px, for a page to remember. */
  scrollOffset(): number {
    return this.scroller().nativeElement.scrollTop;
  }

  /** Scrolls to a remembered offset, in px. */
  scrollTo(offset: number): void {
    const scroller = this.scroller().nativeElement;
    scroller.scrollTop = offset;
    this.scrollTop.set(scroller.scrollTop);
  }

  /** Focuses the active tile, else the tab stop, else the list itself. */
  focus(): void {
    const scroller = this.scroller().nativeElement;
    const tile = scroller.querySelector<HTMLElement>('.tm-tiles__item[tabindex="0"]');
    (tile ?? scroller).focus();
  }

  // ---- event handlers ----

  /** Tracks the scroll offset for the line window. */
  protected onScroll(): void {
    // The scroll's own frame re-binds the lines it has; the overscan
    // renders in the next task, so a jump splits into two shorter tasks.
    if (this.sliceFrom() === null) {
      this.sliceFrom.set(this.scrollTop());
      const done = this.pendingTasks.add();
      setTimeout(() => {
        this.sliceFrom.set(null);
        done();
      });
    }
    this.scrollTop.set(this.scroller().nativeElement.scrollTop);
    this.scrolling.set(true);
    clearTimeout(this.scrollTimer);
    this.scrollTimer = setTimeout(() => this.scrolling.set(false), SCROLL_SETTLE_MS);
  }

  /** The keyboard model (one tab stop, arrows in the live direction). */
  protected onKeydown(event: KeyboardEvent): void {
    // IME composition owns its keys; 229 is the composition sentinel.
    const imeKeyCode = (event as { readonly keyCode: number }).keyCode;
    if (event.defaultPrevented || event.isComposing || imeKeyCode === 229) {
      return;
    }
    // Keys from inside a tile (a custom body's control) stay there.
    const target = event.target as Element;
    if (target !== this.scroller().nativeElement && !target.classList.contains('tm-tiles__item')) {
      return;
    }
    const apple = tmIsApplePlatform();
    const mod = apple ? event.metaKey : event.ctrlKey;
    const plain = !event.altKey && !event.ctrlKey && !event.metaKey && !event.shiftKey;
    // Mapped keys read the live direction, so a language switch needs no reload.
    const rtl = getComputedStyle(this.host).direction === 'rtl';
    switch (event.key) {
      case 'ArrowLeft':
      case 'ArrowRight': {
        if (!plain) {
          return;
        }
        const forward = (event.key === 'ArrowRight') !== rtl;
        const tree = this.treeMode();
        if (tree === null) {
          this.move({ kind: 'step', by: forward ? 1 : -1 }, true);
          break;
        }
        // The tree's Right and Left: expand or enter, collapse or leave.
        const to = tree.arrow(this.activeId(), forward);
        if (to === 'first') {
          this.move({ kind: 'first' }, true);
        } else if (to !== null) {
          this.setActive(to, { reveal: true, focus: true });
        }
        break;
      }
      case 'ArrowUp':
      case 'ArrowDown':
        if (!plain) {
          return;
        }
        this.move({ kind: 'lines', by: event.key === 'ArrowDown' ? 1 : -1 }, true);
        break;
      case 'Home':
      case 'End': {
        // Ctrl goes to the first and last tile; so does a single column,
        // where a line's ends are the tile itself.
        const all = event.ctrlKey || event.metaKey || this.columns() === 1;
        const toEnd = event.key === 'End';
        this.move(
          all ? { kind: toEnd ? 'last' : 'first' } : { kind: toEnd ? 'lineEnd' : 'lineStart' },
          true,
        );
        break;
      }
      case 'PageUp':
      case 'PageDown':
        this.move(
          { kind: 'lines', by: (event.key === 'PageDown' ? 1 : -1) * this.pageStep() },
          true,
        );
        break;
      case 'Enter': {
        const id = this.activeId();
        if (!event.repeat && id !== null) {
          this.activate(id, 'keyboard', mod);
        }
        break;
      }
      case ' ':
        if (event.ctrlKey && event.shiftKey && !event.altKey && !event.metaKey) {
          this.toggleAll();
        } else if (plain) {
          const id = this.activeId();
          if (id !== null) {
            this.toggleSelected(id);
          }
        }
        break;
      default:
        return;
    }
    event.preventDefault();
  }

  /** Records a press and its row for the click; keeps a middle press from starting autoscroll. */
  protected onPointerDown(event: PointerEvent): void {
    const row = this.rowAt(event.target);
    this.press = { x: event.clientX, y: event.clientY, id: row?.id };
    if (
      event.button === 1 &&
      event.pointerType !== 'touch' &&
      row &&
      (event.target as Element).closest('a[href]') === null
    ) {
      event.preventDefault();
    }
  }

  /** A cancelled press (a scroll took the touch) is no tap to measure a click against. */
  protected onPointerCancel(): void {
    this.press = null;
  }

  /** A click or tap: the checkbox toggles selection, the twisty expansion, anywhere else opens the tile. */
  protected onClick(event: MouseEvent): void {
    // Each press measures one click only.
    const press = this.press;
    this.press = null;
    const row = this.rowAt(event.target);
    // A click on another row than the press's (its tile re-bound in place
    // between press and release, as when rows arrive above) is no click on
    // either row.
    if (row === undefined || this.pressedElsewhere(press, row.id, event)) {
      return;
    }
    const element = event.target as Element;
    if (element.closest('[data-tm-check]') !== null) {
      this.toggleSelected(row.id);
      return;
    }
    if (element.closest('[data-tm-twisty]') !== null) {
      this.treeMode()?.toggle(row.id);
      return;
    }
    const apple = tmIsApplePlatform();
    const mod = apple ? event.metaKey : event.ctrlKey;
    const link = element.closest('a[href]');
    if (link !== null) {
      // A modified click on the title link is the browser's (a new tab, a
      // new window, a download); a plain one opens the tile in place.
      if (mod || event.shiftKey || event.altKey || event.button !== 0) {
        return;
      }
      event.preventDefault();
    } else if (element.closest('button, input, select, textarea') !== null) {
      return; // a custom body's own control
    }
    // A press that travelled is a drag or a text selection, not a tap. A
    // click with no press of its own (`detail` 0: a script's, or an
    // assistive technology's default action) is never one.
    if (
      press !== null &&
      event.detail !== 0 &&
      Math.hypot(event.clientX - press.x, event.clientY - press.y) >= CLICK_SLOP_PX
    ) {
      return;
    }
    this.activate(row.id, 'pointer', mod);
  }

  /** A middle click outside a link opens the tile in a new tab; on a link it is the browser's. */
  protected onAuxClick(event: MouseEvent): void {
    const row = this.rowAt(event.target);
    if (
      event.button === 1 &&
      row &&
      (event.target as Element).closest('a[href]') === null &&
      !this.pressedElsewhere(this.press, row.id, event)
    ) {
      this.activate(row.id, 'pointer', true);
    }
  }

  /** Focus entering a tile makes it active; focus on its title link moves to the tile. */
  protected onFocusIn(event: FocusEvent): void {
    this.focusWithin = true;
    // Focus lands on elements only.
    const target = event.target as Element;
    if (target.classList.contains('tm-tiles__title')) {
      this.tileElement(target)?.focus({ preventScroll: true });
    }
    const row = this.rowAt(target);
    this.focusedId = row?.id;
    if (row !== undefined && row.id !== this.activeId()) {
      this.setActive(row.id, { reveal: false, focus: false });
    }
  }

  /**
   * Focus leaving the list ends the focus reclaim, decided once the focus
   * change settles: a render that moves the focused tile's element to
   * another place in the list blurs it on the way, and gets focus back
   * before the task ends.
   */
  protected onFocusOut(): void {
    queueMicrotask(() => {
      this.focusWithin = this.scroller().nativeElement.contains(document.activeElement);
    });
  }

  // ---- internals ----

  /** What a row shows, through its cached computed: created on the row's first render. */
  private contentOf(row: T, cache: Map<T, Signal<TileContent>>): TileContent {
    let content = cache.get(row);
    if (content === undefined) {
      content = computed(() => this.readContent(row));
      cache.set(row, content);
    }
    return content();
  }

  /** Reads what a row shows through the layout, `rowClass` and the checkbox name. */
  private readContent(row: T): TileContent {
    const layout = this.layout();
    const parts = layout.titleParts?.(row) ?? null;
    const title = layout.title(row);
    const corners: Corner[] = [];
    corner(corners, 'top-end', layout.topEnd?.(row));
    corner(corners, 'bottom-start', layout.bottomStart?.(row));
    corner(corners, 'bottom-end', layout.bottomEnd?.(row));
    return {
      title,
      titleParts: parts !== null && parts.length > 0 ? parts : null,
      href: layout.href?.(row) ?? null,
      image: layout.image?.(row),
      corners,
      rowClass: this.rowClass()?.(row) ?? null,
      checkLabel: this.selectable() ? this.translate('tiles.select', { label: title })() : '',
    };
  }

  /**
   * A tile's image source for this render. While scrolling settles a tile
   * shows its image and the row keeps it from then on; a tile that arrives
   * mid-scroll shows the placeholder until scrolling stops.
   */
  private imageSrcOf(
    row: T,
    content: TileContent,
    scrolling: boolean,
    shownImages: WeakSet<object>,
  ): string | null {
    const src = content.image?.src ?? null;
    if (src === null || typeof row !== 'object' || row === null) {
      return src;
    }
    if (!scrolling) {
      shownImages.add(row);
      return src;
    }
    return shownImages.has(row) ? src : null;
  }

  /** The row the tile holding `target` shows now, if `target` is in a tile. */
  private rowAt(target: EventTarget | null): ɵTmTilesRow<T> | undefined {
    const tile = target instanceof Element ? this.tileElement(target) : null;
    return tile === null ? undefined : this.visible()[Number(tile.dataset['index'])];
  }

  /**
   * Whether a pointer click's press was on another row than `id`; a click
   * with no press of its own (`detail` 0) never is.
   */
  private pressedElsewhere(
    press: { readonly id: TmRowId | undefined } | null,
    id: TmRowId,
    event: MouseEvent,
  ): boolean {
    return press !== null && event.detail !== 0 && press.id !== id;
  }

  /** The tile element a target is in, if it is one of this list's. */
  private tileElement(target: Element): HTMLElement | null {
    const tile = target.closest<HTMLElement>('.tm-tiles__item[data-index]');
    return tile !== null && this.scroller().nativeElement.contains(tile) ? tile : null;
  }

  /** Measures the scroller and re-reads the token geometry. */
  private measure(scroller: HTMLElement): void {
    const style = getComputedStyle(this.host);
    this.geometry.set({
      height: pxToken(style, '--tiles-height', DEFAULT_HEIGHT),
      gap: pxToken(style, '--tiles-gap', DEFAULT_GAP),
      minWidth: pxToken(style, '--tiles-min-width', DEFAULT_MIN_WIDTH),
    });
    this.width.set(scroller.clientWidth);
    this.viewportHeight.set(scroller.clientHeight);
    this.scrollTop.set(scroller.scrollTop);
  }

  /**
   * Applies a lattice move from the active tile and scrolls the result into
   * view; a keyboard move also takes focus to it.
   */
  private move(move: ɵTmTilesMove, focus: boolean): void {
    const rows = this.visible();
    const result = ɵtmTilesMove(
      { columns: this.columns(), count: rows.length },
      this.activeIndex(),
      move,
      move.kind === 'lines' ? this.moveColumn : null,
    );
    if (result.index === null) {
      return;
    }
    this.setActive(rows[result.index].id, { reveal: true, focus });
    this.moveColumn = result.column;
  }

  /**
   * Makes a row the active tile and writes `activeRowId`. `reveal` scrolls it
   * into view (nearest) after the render; `focus` moves focus to it.
   */
  private setActive(id: TmRowId | null, options: { reveal: boolean; focus: boolean }): void {
    this.moveColumn = null;
    if (options.reveal && id !== null) {
      this.pendingReveal = { id, center: false };
    }
    if (options.focus) {
      this.pendingFocus = true;
    }
    this.activeId.set(id);
    if (untracked(this.activeRowId) !== id) {
      this.activeRowId.set(id);
    }
    this.changeDetector.markForCheck();
  }

  /**
   * Prepares a row to be shown: `false` when no row has the id; in tree
   * mode, the tree expands its collapsed ancestors.
   */
  private reveal(id: TmRowId): boolean {
    const tree = this.treeMode();
    return tree === null ? this.indexById().has(id) : tree.reveal(id);
  }

  /** Opens a row, unless the same row opened within the guard window. */
  private activate(id: TmRowId, via: 'pointer' | 'keyboard', newTab: boolean): boolean {
    const index = this.indexById().get(id);
    if (index === undefined) {
      return false;
    }
    const now = Date.now();
    const last = this.lastActivation;
    if (last !== null && last.id === id && now - last.at < ACTIVATION_GUARD_MS) {
      return true;
    }
    this.lastActivation = { id, at: now };
    this.rowActivate.emit({ row: this.visible()[index].row, rowId: id, via, newTab });
    return true;
  }

  /** Toggles one row's selection and announces the new count. */
  private toggleSelected(id: TmRowId): void {
    if (!this.selectable()) {
      return;
    }
    const next = new Set(this.selectedIds());
    if (!next.delete(id)) {
      next.add(id);
    }
    this.commitSelection(next);
  }

  /** Selects every loaded row, or clears them all when every one is selected already. */
  private toggleAll(): void {
    if (!this.selectable()) {
      return;
    }
    const rowIdOf = this.rowId();
    const ids = this.data().map((row) => rowIdOf(row));
    const current = this.selectedIds();
    const next = new Set(current);
    if (ids.every((id) => current.has(id))) {
      for (const id of ids) {
        next.delete(id);
      }
    } else {
      for (const id of ids) {
        next.add(id);
      }
    }
    this.commitSelection(next);
  }

  /** Writes a fresh selection and announces "{count} of {total} selected" over the loaded rows. */
  private commitSelection(next: ReadonlySet<TmRowId>): void {
    this.selectedIds.set(next);
    const rowIdOf = this.rowId();
    const rows = this.data();
    let count = 0;
    for (const row of rows) {
      if (next.has(rowIdOf(row))) {
        count++;
      }
    }
    void this.announcer.announce(
      this.translate('tiles.selected', { count, total: rows.length })(),
      'polite',
    );
  }

  /** Starts a flash on a tile for `--tiles-flash-duration`; a new flash replaces a running one. */
  private startFlash(id: TmRowId): void {
    clearTimeout(this.flashTimer);
    const raw = getComputedStyle(this.host).getPropertyValue('--tiles-flash-duration').trim();
    const value = Number.parseFloat(raw);
    // The token is a CSS time: seconds or milliseconds.
    let ms = DEFAULT_FLASH_MS;
    if (Number.isFinite(value) && value >= 0) {
      ms = raw.endsWith('ms') ? value : value * 1000;
    }
    const previous = this.flash();
    this.flash.set({ id, keyframes: previous?.keyframes === 1 ? 2 : 1 });
    this.flashTimer = setTimeout(() => this.flash.set(null), ms);
  }

  /** The post-render pass: a pending reveal, a pending focus, and the focus reclaim. */
  private afterRender(): void {
    const scroller = this.scroller().nativeElement;
    const reveal = this.pendingReveal;
    // Before the first measure the lattice is one column at the fallback
    // pitch: a reveal waits for the render the measure causes.
    if (reveal !== null && !Number.isNaN(this.width())) {
      this.pendingReveal = null;
      const index = this.indexById().get(reveal.id);
      if (index !== undefined) {
        // The tile's line box, from the lattice arithmetic: no layout read.
        const top = Math.floor(index / this.columns()) * this.pitch();
        const height = this.geometry().height;
        const viewport = scroller.clientHeight;
        let offset = scroller.scrollTop;
        if (reveal.center) {
          offset = top + height / 2 - viewport / 2;
        } else if (top < offset) {
          offset = top;
        } else if (top + height > offset + viewport) {
          offset = top + height - viewport;
        }
        if (offset !== scroller.scrollTop) {
          scroller.scrollTop = offset;
          this.scrollTop.set(scroller.scrollTop);
        }
      }
    }
    // Focus follows the row, not the element: a re-render that dropped
    // focus onto <body>, or re-bound the focused tile to another row, moves
    // it to the active tile.
    const focused = document.activeElement;
    const shown = this.rowAt(focused);
    const stale = shown !== undefined && shown.id !== this.focusedId;
    const focusActive =
      this.pendingFocus || (this.focusWithin && (focused === document.body || stale));
    this.pendingFocus = false;
    if (focusActive) {
      const id = this.activeDescendantId();
      const tile = id === null ? null : scroller.querySelector<HTMLElement>(`#${id}`);
      // A focused tile re-bound in place to the active row is focused
      // already but has announced nothing: it is left and entered again, so
      // the move fires its focus events and focusin records the row.
      if (tile !== null && (focused !== tile || stale)) {
        tile.blur();
        tile.focus({ preventScroll: true });
      }
    }
  }
}

/** The indexes `0 … count - 1`. */
function indexes(count: number): readonly number[] {
  return Array.from({ length: count }, (_, i) => i);
}
