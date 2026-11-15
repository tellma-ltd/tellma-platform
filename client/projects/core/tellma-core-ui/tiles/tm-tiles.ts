// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import { LiveAnnouncer } from '@angular/cdk/a11y';
import { NgTemplateOutlet } from '@angular/common';
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
  signal,
  untracked,
  viewChild,
} from '@angular/core';
import { Check, ChevronDown, ChevronRight, Image } from 'lucide';

import { TM_ACTIVE_LOCALE, TM_BREAKPOINTS, TM_UI_TRANSLATE } from '@tellma/core-ui';
import { TmBadge, type TmBadgeTone } from '@tellma/core-ui/badge';
import type { TmRowId } from '@tellma/core-ui/contracts';
import type { TmGridDriverTarget, TmGridRowActivation } from '@tellma/core-ui/grid';
import {
  tmComputeAxisWindow,
  ɵtmFlattenVisible,
  ɵtmResolveTree,
  type ɵTmTreeStructure,
} from '@tellma/core-ui/grid-engine';
import { TmIcon } from '@tellma/core-ui/icon';
import { TmImage, TmImagePlaceholder } from '@tellma/core-ui/image';
import { tmIsApplePlatform } from '@tellma/core-ui/kbd';
import { TmSkeleton } from '@tellma/core-ui/skeleton';
import { TmSpinner } from '@tellma/core-ui/spinner';

import {
  ɵtmTilesColumns,
  ɵtmTilesLineCount,
  ɵtmTilesMove,
  ɵtmTilesRenderedLines,
  type ɵTmTilesMove,
} from './internal/tm-tiles-lattice';
import { TmTileContent, TmTilesEmpty, TmTilesLoading } from './tm-tiles-templates';
import type { TmTileImage, TmTileLayout, TmTileText } from './tm-tiles-types';

/** Lattice lines rendered beyond the visible ones on each side. */
const OVERSCAN_LINES = 3;
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

/** Numbers each instance's element ids. */
let nextTilesId = 0;

/** One corner's text, normalized from {@link TmTileText}. */
interface Corner {
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
  readonly topEnd: Corner | null;
  readonly bottomStart: Corner | null;
  readonly bottomEnd: Corner | null;
  readonly rowClass: string | null;
}

/** One visible row, in display order: every row when flat, the expanded sequence in tree mode. */
interface VisibleRow<T> {
  readonly row: T;
  readonly id: TmRowId;
}

/** Tree placement of the rows, for tree mode. */
interface TreeInfo<T> {
  readonly structure: ɵTmTreeStructure<T>;
  /** Each row's 1-based position among its siblings. */
  readonly posInSet: ReadonlyMap<TmRowId, number>;
}

/** One rendered tile. */
interface TileView<T> {
  readonly row: T;
  readonly id: TmRowId;
  /** The position in the visible sequence. */
  readonly index: number;
  readonly column: number;
  readonly elementId: string;
  readonly tabStop: boolean;
  readonly selected: boolean;
  /** The checkbox's accessible name ("Select …"); empty when not selectable. */
  readonly checkLabel: string;
  readonly classes: string;
  readonly content: TileContent;
  /** Tree mode: the 1-based depth, and whether the row expands, is expanded and is loading. */
  readonly level: number;
  readonly expandable: boolean;
  readonly expanded: boolean;
  readonly loading: boolean;
}

/** One rendered lattice line. */
interface LineView<T> {
  readonly index: number;
  readonly top: number;
  readonly tiles: readonly TileView<T>[];
  /** Tree mode's row attributes, from the line's one tile; `null` when flat. */
  readonly tree: {
    readonly level: number;
    readonly expanded: boolean | null;
    readonly posInSet: number;
    readonly setSize: number;
    readonly busy: boolean;
  } | null;
}

/** Normalizes one corner. */
function corner(value: TmTileText | null | undefined): Corner | null {
  if (value === null || value === undefined) {
    return null;
  }
  if (typeof value === 'string') {
    return value === '' ? null : { text: value, numeric: false, badge: null };
  }
  return { text: value.text, numeric: value.numeric === true, badge: value.badge ?? null };
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
 * small breakpoint the lattice becomes a divided list, and with `parentId`
 * set it is a single-column tree whose children load lazily in batches.
 * Its members mirror the data grid's, so a page can swap grid and tiles
 * over one state, and `<input [tmGridDriver]="tiles">` moves the active
 * tile from a search field.
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
 *   `aria-selected` when selectable). In tree mode it is a `treegrid` of
 *   one cell per row, the rows carrying `aria-level`, `aria-expanded`,
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
  imports: [NgTemplateOutlet, TmBadge, TmIcon, TmImage, TmImagePlaceholder, TmSkeleton, TmSpinner],
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
      (click)="onClick($event)"
      (auxclick)="onAuxClick($event)"
      (focusin)="onFocusIn($event)"
      (focusout)="onFocusOut($event)"
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

        <!-- The windowed lines, plus the active tile's line wherever it is. -->
        @for (line of lines(); track line.index) {
          <div
            class="tm-tiles__line"
            role="row"
            [attr.aria-rowindex]="line.index + 1"
            [attr.aria-level]="line.tree?.level ?? null"
            [attr.aria-expanded]="line.tree?.expanded ?? null"
            [attr.aria-posinset]="line.tree?.posInSet ?? null"
            [attr.aria-setsize]="line.tree?.setSize ?? null"
            [attr.aria-busy]="line.tree?.busy ? 'true' : null"
            [style.transform]="'translateY(' + line.top + 'px)'"
          >
            @for (tile of line.tiles; track tile.id) {
              <div
                role="gridcell"
                class="tm-tiles__item"
                [class]="tile.classes"
                [id]="tile.elementId"
                [attr.data-index]="tile.index"
                [attr.aria-colindex]="tile.column + 1"
                [attr.aria-selected]="selectable() ? tile.selected : null"
                [attr.tabindex]="tile.tabStop ? 0 : -1"
                [style.--tm-tiles-level]="tile.level"
              >
                <!-- Tree chrome: the indent, the twisty, and the reserved spinner slot. -->
                @if (tree()) {
                  <span
                    class="tm-tiles__twisty"
                    data-tm-twisty
                    aria-hidden="true"
                    [class.tm-tiles__twisty--open]="tile.expanded"
                  >
                    @if (tile.expandable) {
                      <tm-icon [svg]="tile.expanded ? expandedGlyph : collapsedGlyph" />
                    }
                  </span>
                  @if (loadChildren()) {
                    <span class="tm-tiles__spinner-slot">
                      @if (tile.loading) {
                        <tm-spinner />
                      }
                    </span>
                  }
                }
                <!-- The reserved checkbox: a pointer target that toggles selection only. -->
                @if (selectable()) {
                  <span
                    class="tm-tiles__check"
                    data-tm-check
                    role="checkbox"
                    [attr.aria-checked]="tile.selected"
                    [attr.aria-label]="tile.checkLabel"
                  >
                    <span class="tm-tiles__box"><tm-icon [svg]="checkGlyph" /></span>
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
                  @if (tile.content.image; as image) {
                    <span class="tm-tiles__image">
                      @if (image.src; as src) {
                        <tm-image
                          [src]="src"
                          [etag]="image.etag ?? null"
                          [alt]="image.alt"
                          [width]="imageSize"
                          [height]="imageSize"
                          placeholderOnError
                        >
                          <ng-template tmImagePlaceholder>
                            <ng-container [ngTemplateOutlet]="placeholder" />
                          </ng-template>
                        </tm-image>
                      } @else {
                        <ng-container [ngTemplateOutlet]="placeholder" />
                      }
                    </span>
                  }
                  <span class="tm-tiles__text">
                    <span class="tm-tiles__title">
                      @if (tile.content.href; as href) {
                        <a class="tm-tiles__link" tabindex="-1" [href]="href">
                          <ng-container
                            [ngTemplateOutlet]="titleText"
                            [ngTemplateOutletContext]="{ $implicit: tile.content }"
                          />
                        </a>
                      } @else {
                        <ng-container
                          [ngTemplateOutlet]="titleText"
                          [ngTemplateOutletContext]="{ $implicit: tile.content }"
                        />
                      }
                    </span>
                    <ng-container
                      [ngTemplateOutlet]="cornerText"
                      [ngTemplateOutletContext]="{
                        $implicit: tile.content.topEnd,
                        place: 'top-end',
                      }"
                    />
                    <ng-container
                      [ngTemplateOutlet]="cornerText"
                      [ngTemplateOutletContext]="{
                        $implicit: tile.content.bottomStart,
                        place: 'bottom-start',
                      }"
                    />
                    <ng-container
                      [ngTemplateOutlet]="cornerText"
                      [ngTemplateOutletContext]="{
                        $implicit: tile.content.bottomEnd,
                        place: 'bottom-end',
                      }"
                    />
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

    <!-- The title: plain, or as parts each isolated in its own <bdi>. -->
    <ng-template #titleText let-content>
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
    </ng-template>

    <!-- One corner: text, numeric text (LTR, tabular, physically right), or a badge. -->
    <ng-template #cornerText let-value let-place="place">
      @if (value) {
        <span
          class="tm-tiles__corner"
          [class]="'tm-tiles__corner--' + place"
          [class.tm-tiles__corner--numeric]="value.numeric"
          [attr.dir]="value.numeric ? 'ltr' : null"
        >
          @if (value.badge) {
            <tm-badge [tone]="value.badge">{{ value.text }}</tm-badge>
          } @else {
            {{ value.text }}
          }
        </span>
      }
    </ng-template>

    <!-- The image placeholder: a glyph in the image box. -->
    <ng-template #placeholder>
      <span class="tm-tiles__placeholder"><tm-icon [svg]="placeholderGlyph" /></span>
    </ng-template>
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
  private readonly locale = inject(TM_ACTIVE_LOCALE);
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
   * Reads a row's parent id (`null` for a root). Set, the tiles are a
   * single-column tree over the flat rows; a parent id that resolves to no
   * row, or that closes a cycle, renders the row as a root.
   */
  readonly parentId = input<((row: T) => TmRowId | null) | undefined>(undefined);
  /** Marks rows whose children may not be loaded yet, so they show a twisty before any child exists. */
  readonly hasChildren = input<((row: T) => boolean) | undefined>(undefined);
  /**
   * The expanded row ids, two-way: the tree's only expansion state. Ids of
   * rows that are not present are kept and expand when their rows arrive.
   */
  readonly expandedIds = model<ReadonlySet<TmRowId>>(new Set());
  /**
   * Loads the children of a batch of expanded rows: append the fetched rows
   * to `data` and resolve. Every expanded `hasChildren` row with no loaded
   * children that one moment collects arrives in one call, in data order —
   * the first bind, a written `expandedIds`, the rows a resolved batch
   * brings (so a deep expansion loads a level per batch), or the expands a
   * user makes within one task. A row that loads no children becomes a
   * leaf. A rejection collapses the batch's rows and announces once.
   */
  readonly loadChildren = input<((rows: readonly T[]) => Promise<void>) | undefined>(undefined);
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

  // ---- glyphs and constants the template reads ----

  /** A collapsed row's twisty, mirrored in RTL. */
  protected readonly collapsedGlyph = { node: ChevronRight, mirrorInRtl: true };
  /** An expanded row's twisty. */
  protected readonly expandedGlyph = ChevronDown;
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
  /** The press a click is measured against for the tap slop. */
  private press: { readonly x: number; readonly y: number } | null = null;
  /** The last activation, for the repeat guard. */
  private lastActivation: { readonly id: TmRowId; readonly at: number } | null = null;
  /** The flashing tile and its keyframes (`1` or `2`, alternating so a re-flash restarts). */
  private readonly flash = signal<{ readonly id: TmRowId; readonly keyframes: 1 | 2 } | null>(null);
  /** Clears the flash once it has run. */
  private flashTimer: ReturnType<typeof setTimeout> | undefined;

  // ---- tree loading state ----

  /** Rows whose children batch is in flight. */
  private readonly inFlight = signal<ReadonlySet<TmRowId>>(new Set());
  /** Rows whose children loaded: one that brought none is a leaf from then on. */
  private readonly settled = signal<ReadonlySet<TmRowId>>(new Set());
  /** Whether a batch flush is queued for the end of the task. */
  private flushQueued = false;
  /** Set on destroy: loads still in flight settle silently. */
  private destroyed = false;

  // ---- derived state ----

  /** Whether the tiles are a tree. */
  protected readonly tree = computed(() => this.parentId() !== undefined);
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

  /** The tree's resolved structure and sibling positions; `null` when flat. */
  private readonly treeInfo = computed((): TreeInfo<T> | null => {
    const parentIdOf = this.parentId();
    if (parentIdOf === undefined) {
      return null;
    }
    const hasChildrenOf = this.hasChildren();
    const settled = this.settled();
    const rowIdOf = this.rowId();
    // A row whose children loaded and brought none is a leaf: no twisty, no
    // second load.
    const structure = ɵtmResolveTree(
      this.data(),
      rowIdOf,
      parentIdOf,
      hasChildrenOf === undefined
        ? null
        : (row: T) => hasChildrenOf(row) && !settled.has(rowIdOf(row)),
    );
    const posInSet = new Map<TmRowId, number>();
    const number = (ids: readonly TmRowId[]): void => {
      for (let i = 0; i < ids.length; i++) {
        posInSet.set(ids[i], i + 1);
      }
    };
    number(structure.roots);
    for (const node of structure.nodes.values()) {
      number(node.children);
    }
    return { structure, posInSet };
  });

  /** The rows on show, in display order. */
  private readonly visible = computed((): readonly VisibleRow<T>[] => {
    const info = this.treeInfo();
    if (info === null) {
      const rowIdOf = this.rowId();
      return this.data().map((row) => ({ row, id: rowIdOf(row) }));
    }
    const nodes = info.structure.nodes;
    return ɵtmFlattenVisible(info.structure, this.expandedIds()).map((id) => ({
      row: nodes.get(id)!.row,
      id,
    }));
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

  /** The windowed lines over the scroll position. */
  private readonly lineWindow = computed(
    () =>
      tmComputeAxisWindow({
        scrollOffset: this.scrollTop(),
        viewportSize: this.viewportHeight(),
        itemSize: this.pitch(),
        itemCount: this.lineCount(),
        overscan: OVERSCAN_LINES,
      }),
    // A scroll within the same lines renders nothing new.
    { equal: (a, b) => a.start === b.start && a.end === b.end && a.totalSize === b.totalSize },
  );

  /** The canvas height: every line at the pitch. */
  protected readonly extent = computed(() =>
    this.showSkeleton() ? 0 : this.lineWindow().totalSize,
  );

  /** What each row shows, read once per row while the data, the layout and `rowClass` stay. */
  private readonly contentCache = computed(() => {
    this.data();
    this.layout();
    this.rowClass();
    return new Map<T, TileContent>();
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

  /** Rows waiting on their children: expanded, on show, marked, with none loaded and a loader bound. */
  private readonly loadingIds = computed((): ReadonlySet<TmRowId> => {
    const info = this.treeInfo();
    const hasChildrenOf = this.hasChildren();
    if (info === null || hasChildrenOf === undefined || this.loadChildren() === undefined) {
      return new Set();
    }
    const expanded = this.expandedIds();
    const waiting = new Set<TmRowId>();
    for (const { id } of this.visible()) {
      const node = info.structure.nodes.get(id)!;
      if (node.expandable && node.children.length === 0 && expanded.has(id)) {
        waiting.add(id);
      }
    }
    return waiting;
  });

  /** The rendered lines and their tiles. */
  protected readonly lines = computed((): readonly LineView<T>[] => {
    const rows = this.visible();
    const columns = this.columns();
    const pitch = this.pitch();
    const window = this.lineWindow();
    const active = this.activeIndex();
    const selected = this.selectable() ? this.selectedIds() : null;
    const expanded = this.expandedIds();
    const flash = this.flash();
    const info = this.treeInfo();
    const loadingIds = this.loadingIds();
    const cache = this.contentCache();
    // One tab stop: the active tile, else the first rendered one.
    const tabStop = active ?? Math.min(window.start * columns, rows.length - 1);
    const lineIndexes = ɵtmTilesRenderedLines(
      window.start,
      window.end,
      active === null ? null : Math.floor(active / columns),
    );
    return lineIndexes.map((line): LineView<T> => {
      const tiles: TileView<T>[] = [];
      const end = Math.min(rows.length, (line + 1) * columns);
      for (let index = line * columns; index < end; index++) {
        const { row, id } = rows[index];
        const content = this.contentOf(row, cache);
        const node = info?.structure.nodes.get(id);
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
        tiles.push({
          row,
          id,
          index,
          column: index - line * columns,
          elementId: `${this.idPrefix}-t${index}`,
          tabStop: index === tabStop,
          selected: isSelected,
          checkLabel:
            selected === null ? '' : this.translate('tiles.select', { label: content.title })(),
          classes,
          content,
          level: node === undefined ? 1 : node.level + 1,
          expandable: node?.expandable === true,
          expanded: node?.expandable === true && expanded.has(id),
          loading: loadingIds.has(id),
        });
      }
      const first = tiles[0];
      const parent = parentOf(info, first?.id);
      return {
        index: line,
        top: line * pitch,
        tiles,
        tree:
          info === null || first === undefined
            ? null
            : {
                level: first.level,
                expanded: first.expandable ? first.expanded : null,
                posInSet: info.posInSet.get(first.id) ?? 1,
                setSize:
                  parent === null
                    ? info.structure.roots.length
                    : (info.structure.nodes.get(parent)?.children.length ?? 1),
                busy: first.loading,
              },
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
        const nodes = this.treeInfo()?.structure.nodes;
        let ancestor = nodes?.get(id)?.parentId ?? null;
        while (ancestor !== null && !index.has(ancestor)) {
          ancestor = nodes?.get(ancestor)?.parentId ?? null;
        }
        const rows = this.visible();
        const next =
          ancestor ??
          (rows.length === 0 ? null : rows[Math.min(this.lastActiveIndex, rows.length - 1)].id);
        this.setActive(next, { reveal: false, focus: this.focusWithin });
      });
    });

    // Lazy children: rows waiting on a load are batched at the end of the
    // task, so a bind, a written expansion or a burst of expands is one call.
    effect(() => {
      const waiting = this.loadingIds();
      const flying = this.inFlight();
      if (this.flushQueued || ![...waiting].some((id) => !flying.has(id))) {
        return;
      }
      this.flushQueued = true;
      queueMicrotask(() => this.flushLoads());
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
      this.destroyed = true;
      clearTimeout(this.flashTimer);
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
    this.scrollTop.set(this.scroller().nativeElement.scrollTop);
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
        if (this.tree()) {
          this.treeArrow(forward);
        } else {
          this.move({ kind: 'step', by: forward ? 1 : -1 }, true);
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

  /** Records a press for the tap slop; keeps a middle press from starting autoscroll. */
  protected onPointerDown(event: PointerEvent): void {
    this.press = { x: event.clientX, y: event.clientY };
    if (
      event.button === 1 &&
      event.pointerType !== 'touch' &&
      event.target instanceof Element &&
      event.target.closest('a[href]') === null &&
      this.tileElement(event.target) !== null
    ) {
      event.preventDefault();
    }
  }

  /** A click or tap: the checkbox toggles selection, the twisty expansion, anywhere else opens the tile. */
  protected onClick(event: MouseEvent): void {
    const target = event.target;
    const tile = target instanceof Element ? this.tileElement(target) : null;
    if (tile === null) {
      return;
    }
    const row = this.visible()[Number(tile.dataset['index'])];
    if (row === undefined) {
      return;
    }
    const element = target as Element;
    if (element.closest('[data-tm-check]') !== null) {
      this.toggleSelected(row.id);
      return;
    }
    if (element.closest('[data-tm-twisty]') !== null) {
      if (this.treeInfo()?.structure.nodes.get(row.id)?.expandable === true) {
        this.setExpanded(row.id, !this.expandedIds().has(row.id));
      }
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
    // A press that travelled is a drag or a text selection, not a tap.
    const press = this.press;
    if (
      press !== null &&
      Math.hypot(event.clientX - press.x, event.clientY - press.y) >= CLICK_SLOP_PX
    ) {
      return;
    }
    this.activate(row.id, 'pointer', mod);
  }

  /** A middle click outside a link opens the tile in a new tab; on a link it is the browser's. */
  protected onAuxClick(event: MouseEvent): void {
    if (event.button !== 1 || !(event.target instanceof Element)) {
      return;
    }
    const tile = this.tileElement(event.target);
    if (tile === null || event.target.closest('a[href]') !== null) {
      return;
    }
    const row = this.visible()[Number(tile.dataset['index'])];
    if (row !== undefined) {
      this.activate(row.id, 'pointer', true);
    }
  }

  /** Focus entering a tile makes it active; focus on its title link moves to the tile. */
  protected onFocusIn(event: FocusEvent): void {
    this.focusWithin = true;
    const target = event.target;
    const tile = target instanceof Element ? this.tileElement(target) : null;
    if (tile === null) {
      return;
    }
    if (target !== tile && (target as Element).classList.contains('tm-tiles__link')) {
      tile.focus({ preventScroll: true });
    }
    const row = this.visible()[Number(tile.dataset['index'])];
    if (row !== undefined && row.id !== this.activeId()) {
      this.setActive(row.id, { reveal: false, focus: false });
    }
  }

  /** Focus leaving the list ends the focus reclaim. */
  protected onFocusOut(event: FocusEvent): void {
    const next = event.relatedTarget;
    if (!(next instanceof Node) || !this.scroller().nativeElement.contains(next)) {
      this.focusWithin = false;
    }
  }

  // ---- internals ----

  /** Reads what a row shows through the layout, once per row per cache. */
  private contentOf(row: T, cache: Map<T, TileContent>): TileContent {
    let content = cache.get(row);
    if (content === undefined) {
      const layout = this.layout();
      const parts = layout.titleParts?.(row) ?? null;
      content = {
        title: layout.title(row),
        titleParts: parts !== null && parts.length > 0 ? parts : null,
        href: layout.href?.(row) ?? null,
        image: layout.image?.(row),
        topEnd: corner(layout.topEnd?.(row)),
        bottomStart: corner(layout.bottomStart?.(row)),
        bottomEnd: corner(layout.bottomEnd?.(row)),
        rowClass: this.rowClass()?.(row) ?? null,
      };
      cache.set(row, content);
    }
    return content;
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

  /** Tree mode's Right and Left (mapped): expand or enter, collapse or leave. */
  private treeArrow(forward: boolean): void {
    const id = this.activeId();
    const node = id === null ? undefined : this.treeInfo()?.structure.nodes.get(id);
    if (id === null || node === undefined) {
      this.move({ kind: 'first' }, true);
      return;
    }
    const expanded = this.expandedIds().has(id);
    if (forward) {
      if (node.expandable && !expanded) {
        this.setExpanded(id, true);
      } else if (expanded && node.children.length > 0) {
        this.setActive(node.children[0], { reveal: true, focus: true });
      }
    } else if (node.expandable && expanded) {
      this.setExpanded(id, false);
    } else if (node.parentId !== null) {
      this.setActive(node.parentId, { reveal: true, focus: true });
    }
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
   * mode, expands its collapsed ancestors.
   */
  private reveal(id: TmRowId): boolean {
    const info = this.treeInfo();
    if (info === null) {
      return this.indexById().has(id);
    }
    const nodes = info.structure.nodes;
    if (!nodes.has(id)) {
      return false;
    }
    const expanded = this.expandedIds();
    const missing: TmRowId[] = [];
    for (
      let parent = nodes.get(id)!.parentId;
      parent !== null;
      parent = nodes.get(parent)!.parentId
    ) {
      if (!expanded.has(parent)) {
        missing.push(parent);
      }
    }
    if (missing.length > 0) {
      this.expandedIds.set(new Set([...expanded, ...missing]));
    }
    return true;
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

  /** Expands or collapses one row through the two-way expansion. */
  private setExpanded(id: TmRowId, open: boolean): void {
    const next = new Set(this.expandedIds());
    if (open) {
      next.add(id);
    } else {
      next.delete(id);
    }
    this.expandedIds.set(next);
  }

  /**
   * Calls `loadChildren` once with every waiting row that is not in flight,
   * in data order. A resolved batch settles its rows (a row that brought no
   * children becomes a leaf); a rejected one collapses them and announces
   * once.
   */
  private flushLoads(): void {
    this.flushQueued = false;
    const load = this.loadChildren();
    const info = this.treeInfo();
    if (this.destroyed || load === undefined || info === null) {
      return;
    }
    const flying = this.inFlight();
    const batch = [...this.loadingIds()]
      .filter((id) => !flying.has(id))
      .map((id) => info.structure.nodes.get(id)!)
      .sort((a, b) => a.modelIndex - b.modelIndex);
    if (batch.length === 0) {
      return;
    }
    const ids = batch.map((node) => node.id);
    this.inFlight.set(new Set([...flying, ...ids]));
    const settle = (resolved: boolean): void => {
      if (this.destroyed) {
        return;
      }
      const remaining = new Set(this.inFlight());
      for (const id of ids) {
        remaining.delete(id);
      }
      this.inFlight.set(remaining);
      if (resolved) {
        this.settled.set(new Set([...this.settled(), ...ids]));
        return;
      }
      // The failed rows collapse and leave the expansion; the next expand
      // tries again.
      const expanded = new Set(this.expandedIds());
      for (const id of ids) {
        expanded.delete(id);
      }
      this.expandedIds.set(expanded);
      const titles = batch.map((node) => this.layout().title(node.row));
      const label = new Intl.ListFormat(this.locale(), { type: 'conjunction' }).format(titles);
      void this.announcer.announce(this.translate('tiles.loadFailed', { label })(), 'polite');
    };
    // A loader that throws synchronously rejects the batch like one that rejects.
    Promise.resolve()
      .then(() => load(batch.map((node) => node.row)))
      .then(
        () => settle(true),
        () => settle(false),
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
    if (reveal !== null) {
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
    const focusActive =
      this.pendingFocus || (this.focusWithin && document.activeElement === document.body);
    this.pendingFocus = false;
    if (focusActive) {
      const id = this.activeDescendantId();
      const tile = id === null ? null : scroller.querySelector<HTMLElement>(`#${id}`);
      if (tile !== null && document.activeElement !== tile) {
        tile.focus({ preventScroll: true });
      }
    }
  }
}

/** The indexes `0 … count - 1`. */
function indexes(count: number): readonly number[] {
  return Array.from({ length: count }, (_, i) => i);
}

/** A row's effective parent id in tree mode, or `null`. */
function parentOf<T>(info: TreeInfo<T> | null, id: TmRowId | undefined): TmRowId | null {
  return info === null || id === undefined
    ? null
    : (info.structure.nodes.get(id)?.parentId ?? null);
}
