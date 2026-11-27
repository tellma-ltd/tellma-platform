// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

/// <reference types="@tellma/core-tools/lucide-icons" />

import {
  afterNextRender,
  afterRenderEffect,
  ChangeDetectionStrategy,
  ChangeDetectorRef,
  Component,
  computed,
  DestroyRef,
  DOCUMENT,
  effect,
  ElementRef,
  inject,
  InjectionToken,
  input,
  signal,
  untracked,
  viewChild,
} from '@angular/core';
import { Router, RouterLink, type UrlTree } from '@angular/router';
import { TmLaunchCache, TmLocalization, TmTelemetry, TmUrls } from '@tellma/core';
import { TmBreakpoints } from '@tellma/core-ui';
import { TmButton } from '@tellma/core-ui/button';
import { TmIcon } from '@tellma/core-ui/icon';
import { tmAriaKeyShortcuts, tmKeysMatch } from '@tellma/core-ui/keys';
import { TmSkeleton } from '@tellma/core-ui/skeleton';
import { TmTooltip } from '@tellma/core-ui/tooltip';
import ChevronDown from 'lucide/dist/esm/icons/chevron-down.mjs';
import Ellipsis from 'lucide/dist/esm/icons/ellipsis.mjs';
import Lock from 'lucide/dist/esm/icons/lock.mjs';
import Pin from 'lucide/dist/esm/icons/pin.mjs';
import Settings2 from 'lucide/dist/esm/icons/settings-2.mjs';

import { type TmPinFolderView, TmPins, tmPinsVia, type TmPinView } from './tm-pins';
import { type TmPinnedBarPort, TmPinsUi } from './tm-pins-ui';

// The pinned bar (spec 0027 §8.1, §8.6, §8.7, §8.12): the 32 px row under
// the app bar with the user's pins and folders as chips, then the pin
// control and Manage pins. It draws its own glyphs, so it registers no icon
// name.
//
// The fit. A hidden, out-of-flow row renders every item as the bar shows
// it, at the current chip's weight, and its widths are read in one
// afterRenderEffect's read phase whenever the items, the breakpoint, the
// language, the direction or the loaded fonts change; Angular renders the
// fitted count in the same tick. A ResizeObserver on the track refits from
// those widths and renders at once, so a resize never paints the old fit.
// Each shown chip takes its measured width as its box, so becoming current
// never widens it and nothing moves on hover, focus or, from md up,
// navigation. Below md the current pill leads, so there the fit follows
// navigation too. The chips stay hidden until the first fit, which waits for
// the fonts.
//
// Before the launch documents land, a cold launch's pending sync shows three
// chip skeletons in the row, and a failed one leaves the row empty (spec
// 0027 §7.5).
//
// The bar's menus, its popover and its drag code are the overlays' deferred
// chunk (TmPinsUi); the chips' buttons only ask for them.

/** The slack a fit allows for sub-pixel rounding, as tm-chip-set's does. */
const FIT_TOLERANCE = 0.5;

/** The measuring row's key for More, which no pin or folder id can take. */
const MORE = 'more';

/**
 * The fonts the bar waits for before its first fit, and whose late faces
 * re-read its widths: the document's. Internal: a test provides a fake.
 */
export const TM_PINNED_BAR_FONTS = new InjectionToken<FontFaceSet | null>('TM_PINNED_BAR_FONTS', {
  providedIn: 'root',
  factory: () => inject(DOCUMENT).fonts ?? null,
});

/** One item of the bar: a pin, a revoked or unavailable pin, or a folder. */
interface TmPinnedBarRow {
  /** The pin's or folder's id. */
  readonly id: string;
  /** `pin` is a link; `revoked` and `folder` are menu buttons. */
  readonly kind: 'pin' | 'revoked' | 'folder';
  /** The chip's text. */
  readonly label: string;
  /** The chip's accessible name and tooltip. */
  readonly name: string;
  /** Whether a separator precedes it: a folder after a pin. */
  readonly separated: boolean;
  /** Where a pin's link goes; `null` for a button. */
  readonly link: UrlTree | null;
  /** The pin or folder. */
  readonly item: TmPinView | TmPinFolderView;
}

/** The fit: how many leading items show, and the measured width of each, More's included. */
interface TmPinnedBarLayout {
  /** The number of leading items that show. */
  readonly count: number;
  /** Each item's measured width, by id. */
  readonly widths: ReadonlyMap<string, number>;
}

/**
 * How many leading items fit the room: all of them when they fit together,
 * else as many as fit beside More.
 */
function fitCount(widths: readonly number[], gap: number, more: number, room: number): number {
  const all = widths.reduce((sum, width) => sum + width, 0) + gap * Math.max(widths.length - 1, 0);
  if (all <= room + FIT_TOLERANCE) {
    return widths.length;
  }
  let used = more;
  let count = 0;
  while (count < widths.length && used + gap + widths[count] <= room + FIT_TOLERANCE) {
    used += gap + widths[count];
    count++;
  }
  return count;
}

/** Whether two fits show the same chips at the same widths. */
function sameLayout(a: TmPinnedBarLayout | null, b: TmPinnedBarLayout): boolean {
  return a !== null && a.count === b.count && a.widths.size === b.widths.size && [...b.widths].every(([id, width]) => a.widths.get(id) === width);
}

/**
 * The pinned bar: the user's pins and folders as chips, as many as fit
 * with the rest in More, then the pin control and Manage pins. Rendered by
 * the tenant shell, in its frame's pinned row.
 */
@Component({
  selector: 'tm-pinned-bar',
  imports: [RouterLink, TmButton, TmIcon, TmSkeleton, TmTooltip],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <nav class="tm-pinned-bar" [attr.aria-label]="text('bar')" (pointerenter)="ui.preload()" (focusin)="ui.preload()">
      @if (ready()) {
        <div #track class="tm-pinned-bar__track">
          @if (rows().length === 0) {
            <!-- No pins: the empty line, short below sm. -->
            <p class="tm-pinned-bar__empty">
              <span class="tm-pinned-bar__long">{{ text('empty') }}</span><span class="tm-pinned-bar__short">{{ text('emptyShort') }}</span>
            </p>
          } @else {
            <!-- The items that fit, then More: one tab stop, hidden until the first fit. -->
            <ul
              #list
              role="list"
              class="tm-pinned-bar__list"
              [class.tm-pinned-bar__list--fitted]="layout() !== null"
              (keydown)="keydown($event)"
              (focusin)="roving.set($any($event.target).dataset.tmPin ?? null)"
              (focusout)="left($event)"
            >
              @for (row of shown(); track row.id) {
                <li class="tm-pinned-bar__item" [class.tm-pinned-bar__item--separated]="row.separated" [style.inline-size.px]="layout()?.widths?.get(row.id)">
                  @if (row.link !== null) {
                    <a
                      class="tm-pinned-bar__chip"
                      [class.tm-pinned-bar__chip--current]="row.id === currentRow()"
                      [routerLink]="row.link"
                      [attr.aria-current]="row.id === currentRow() ? 'page' : null"
                      [attr.aria-label]="row.name"
                      [attr.data-tm-pin]="row.id"
                      [tabIndex]="tabStop() === row.id ? 0 : -1"
                      [tmTooltip]="row.name"
                      (click)="followed($event)"
                      ><span class="tm-pinned-bar__label" dir="auto">{{ row.label }}</span></a
                    >
                  } @else {
                    <button
                      #chip
                      type="button"
                      draggable="true"
                      class="tm-pinned-bar__chip"
                      [class.tm-pinned-bar__chip--current]="row.id === currentRow()"
                      [class.tm-pinned-bar__chip--revoked]="row.kind === 'revoked'"
                      aria-haspopup="menu"
                      [attr.aria-expanded]="ui.isOpenFor(chip)"
                      [attr.aria-label]="row.name"
                      [attr.data-tm-pin]="row.id"
                      [tabIndex]="tabStop() === row.id ? 0 : -1"
                      [tmTooltip]="row.name"
                      (click)="ui.toggleMenu(chip, row.kind === 'folder' ? { kind: 'folder', id: row.id } : { kind: 'revoked', id: row.id })"
                    >
                      @if (row.kind === 'revoked') {
                        <tm-icon [svg]="lock" />
                      }
                      <span class="tm-pinned-bar__label" dir="auto">{{ row.label }}</span>
                      @if (row.kind === 'folder') {
                        <tm-icon [svg]="chevron" />
                      }
                    </button>
                  }
                </li>
              }
              @if (hidden().length > 0) {
                <li class="tm-pinned-bar__item" [style.inline-size.px]="layout()?.widths?.get('more')">
                  <button
                    #more
                    type="button"
                    class="tm-pinned-bar__chip"
                    [class.tm-pinned-bar__chip--current]="hidden().includes(currentRow() ?? '')"
                    aria-haspopup="menu"
                    [attr.aria-expanded]="ui.isOpenFor(more)"
                    data-tm-pin="more"
                    [tabIndex]="tabStop() === 'more' ? 0 : -1"
                    (click)="ui.toggleMenu(more, { kind: 'more', items: hiddenItems })"
                  >
                    <tm-icon [svg]="ellipsis" /><span>{{ text('more') }}</span>
                  </button>
                </li>
              }
            </ul>
            <!-- The measuring row: every item as the bar shows it, at the current chip's weight, then More. -->
            <div #measure class="tm-pinned-bar__measure" aria-hidden="true">
              @for (row of rows(); track row.id) {
                <span class="tm-pinned-bar__item" [class.tm-pinned-bar__item--separated]="row.separated" [attr.data-tm-measure]="row.id">
                  <span class="tm-pinned-bar__chip">
                    @if (row.kind === 'revoked') {
                      <tm-icon [svg]="lock" />
                    }
                    <span class="tm-pinned-bar__label" dir="auto">{{ row.label }}</span>
                    @if (row.kind === 'folder') {
                      <tm-icon [svg]="chevron" />
                    }
                  </span>
                </span>
              }
              <span class="tm-pinned-bar__item" data-tm-measure="more">
                <span class="tm-pinned-bar__chip"><tm-icon [svg]="ellipsis" /><span>{{ text('more') }}</span></span>
              </span>
            </div>
          }
        </div>
        <!-- The pin control, then Manage pins, in every state of the bar. -->
        <button
          #control
          tmButton
          variant="ghost"
          class="tm-pinned-bar__control"
          [class.tm-pinned-bar__control--pinned]="state().pinned"
          [attr.aria-label]="controlLabel()"
          [attr.aria-keyshortcuts]="keys"
          [tmTooltip]="controlLabel()"
          [disabledReason]="state().reason"
          [disabled]="state().inert"
          (click)="ui.press('control', control)"
        >
          <tm-icon [svg]="pin" />
        </button>
        <button tmButton variant="ghost" class="tm-pinned-bar__control" [attr.aria-label]="text('manage')" [tmTooltip]="text('manage')" (click)="ui.manage()">
          <tm-icon [svg]="settings" />
        </button>
      } @else if (!failed()) {
        <!-- A cold launch while its sync is pending: three chip skeletons; a failed sync leaves the row empty. -->
        <div class="tm-pinned-bar__track tm-pinned-bar__skeletons">
          <tm-skeleton shape="block" width="64px" height="24px" />
          <tm-skeleton shape="block" width="64px" height="24px" />
          <tm-skeleton shape="block" width="64px" height="24px" />
        </div>
      }
    </nav>
  `,
  styles: `
    :host {
      display: block;
      block-size: 100%;
      /* Spec 0027 §8.1's chip text, which no token holds. */
      --tm-pinned-bar-text: #c6d4d8;
    }

    /* The row: ink, with a hairline above. */
    .tm-pinned-bar {
      display: flex;
      align-items: center;
      gap: var(--space-2);
      box-sizing: border-box;
      block-size: 100%;
      padding-inline: var(--space-2);
      border-block-start: var(--border-width) solid rgb(255 255 255 / 0.08);
      background: var(--ink-800);
      color: var(--tm-pinned-bar-text);
      font: var(--weight-regular) var(--text-xs) / 16px var(--font-ui);
    }

    .tm-pinned-bar__track {
      position: relative;
      display: flex;
      flex: 1 1 0;
      align-items: center;
      min-inline-size: 0;
      block-size: 100%;
      overflow: hidden;
    }

    .tm-pinned-bar__skeletons {
      gap: var(--space-1);
    }

    .tm-pinned-bar__empty {
      margin: 0;
      overflow: hidden;
      white-space: nowrap;
      text-overflow: ellipsis;
    }

    .tm-pinned-bar__short {
      display: none;
    }

    .tm-pinned-bar__list,
    .tm-pinned-bar__measure {
      display: flex;
      align-items: center;
      gap: var(--space-1);
      margin: 0;
      padding: 0;
      white-space: nowrap;
    }

    .tm-pinned-bar__list {
      block-size: 100%;
      list-style: none;
      visibility: hidden;
    }

    .tm-pinned-bar__list--fitted {
      visibility: visible;
    }

    /* Out of flow and unseen: it takes no room and paints nothing. */
    .tm-pinned-bar__measure {
      position: absolute;
      inset-block-start: 0;
      inset-inline-start: 0;
      visibility: hidden;
      pointer-events: none;
    }

    .tm-pinned-bar__item {
      display: flex;
      flex: none;
      align-items: center;
      gap: var(--space-1);
      block-size: 100%;
    }

    /* A folder after a pin: a 1 x 16 px rule before it. */
    .tm-pinned-bar__item--separated::before {
      content: '';
      flex: none;
      inline-size: 1px;
      block-size: 16px;
      background: rgb(255 255 255 / 0.16);
    }

    .tm-pinned-bar__chip {
      display: inline-flex;
      flex: 1 1 auto;
      align-items: center;
      justify-content: center;
      gap: var(--space-1);
      box-sizing: border-box;
      block-size: 24px;
      padding: 0 9px;
      border: 0;
      border-radius: var(--radius-sm);
      background: transparent;
      color: inherit;
      font: inherit;
      text-decoration: none;
      cursor: pointer;
      --icon-size: 12px;
    }

    /* The measuring row's chips: at the current chip's weight. */
    .tm-pinned-bar__measure .tm-pinned-bar__chip {
      font-weight: var(--weight-medium);
    }

    .tm-pinned-bar__chip:hover,
    .tm-pinned-bar__chip[aria-expanded='true'] {
      background: rgb(255 255 255 / 0.08);
    }

    .tm-pinned-bar__chip:focus-visible {
      outline: var(--focus-ring-width) solid var(--text-on-dark);
      outline-offset: -2px;
    }

    .tm-pinned-bar__label {
      max-inline-size: 120px;
      overflow: hidden;
      text-overflow: ellipsis;
    }

    .tm-pinned-bar__chip--revoked {
      opacity: 0.6;
    }

    /* The current page's chip: full height, teal, with a 2 px bar below. */
    .tm-pinned-bar__chip--current {
      block-size: 100%;
      border-radius: 0;
      color: var(--teal-300);
      font-weight: var(--weight-medium);
      box-shadow: inset 0 -2px 0 var(--teal-300);
    }

    /* The 24 px icon buttons at the inline end. */
    .tm-pinned-bar__control {
      flex: none;
      inline-size: 24px;
      --tm-button-block-size: 24px;
      --tm-button-padding-x: 0;
      --icon-size: 16px;
      --button-ghost-text: var(--tm-pinned-bar-text);
      --button-ghost-hover-bg: rgb(255 255 255 / 0.08);
      --button-ghost-active-bg: rgb(255 255 255 / 0.12);
      --button-disabled-bg: transparent;
      --button-disabled-text: rgb(255 255 255 / 0.4);
    }

    .tm-pinned-bar__control--pinned svg {
      fill: currentColor;
    }

    /* Below md: pills, the current one teal-tinted. */
    @media (width < 768px) {
      .tm-pinned-bar__chip {
        border-radius: var(--radius-full);
        background: rgb(255 255 255 / 0.08);
      }

      .tm-pinned-bar__chip--current {
        block-size: 24px;
        border-radius: var(--radius-full);
        background: rgb(116 186 202 / 0.2);
        box-shadow: none;
      }
    }

    @media (width < 576px) {
      .tm-pinned-bar__long {
        display: none;
      }

      .tm-pinned-bar__short {
        display: inline;
      }
    }
  `,
})
export class TmPinnedBar {
  readonly #pins = inject(TmPins);
  readonly #l10n = inject(TmLocalization);
  readonly #router = inject(Router);
  readonly #urls = inject(TmUrls);
  readonly #telemetry = inject(TmTelemetry);
  readonly #cdr = inject(ChangeDetectorRef);
  readonly #md = inject(TmBreakpoints).atLeast('md');
  readonly #fonts = inject(TM_PINNED_BAR_FONTS);
  /** The track the chips fit in. */
  private readonly trackEl = viewChild<ElementRef<HTMLElement>>('track');
  /** The list of chips. */
  private readonly listEl = viewChild<ElementRef<HTMLElement>>('list');
  /** The measuring row. */
  private readonly measureEl = viewChild<ElementRef<HTMLElement>>('measure');
  /** The pin control. */
  private readonly controlEl = viewChild<ElementRef<HTMLElement>>('control');
  // Whether the fonts have loaded, and a count of the faces loaded since.
  readonly #fontsReady = signal(this.#fonts === null || this.#fonts.status === 'loaded');
  readonly #faces = signal(0);
  // The last widths read and the gap between items, which a resize refits from.
  #widths: ReadonlyMap<string, number> | null = null;
  #gap = 0;
  // The chip index focus goes to once a removal from the bar renders.
  #refocus: number | null = null;

  /** The pins' controls: the pin control's state and press, and the overlays. */
  protected readonly ui = inject(TmPinsUi);

  /** The glyphs the bar draws. */
  protected readonly pin = Pin;
  protected readonly settings = Settings2;
  protected readonly lock = Lock;
  protected readonly chevron = ChevronDown;
  protected readonly ellipsis = Ellipsis;

  /** The pin control's `aria-keyshortcuts`. */
  protected readonly keys = tmAriaKeyShortcuts('Alt+Shift+P');

  /** Whether the launch documents are in, so the bar shows the user's pins and the control's state. */
  protected readonly ready = inject(TmLaunchCache).warm;

  /** Whether the cold launch's sync failed, so the row stays empty rather than showing skeletons. */
  readonly failed = input(false);

  /** The chip of the id that holds focus while it is in the bar, else `null`. */
  protected readonly roving = signal<string | null>(null);

  /** The fit, `null` until the first. */
  protected readonly layout = signal<TmPinnedBarLayout | null>(null);

  /** The pin control's state. */
  protected readonly state = this.ui.control;

  /** The pin control's name. */
  protected readonly controlLabel = computed(() => this.text(this.state().pinned ? 'edit' : 'pin'));

  /** The items in stored order, as chips: labels, states and names, never the current flag. */
  protected readonly rows = computed((): readonly TmPinnedBarRow[] =>
    this.#pins.items().map((item, index, items): TmPinnedBarRow => {
      if ('folder' in item) {
        const separated = index > 0 && !('folder' in items[index - 1]);
        return { id: item.id, kind: 'folder', label: item.folder, name: item.folder, separated, link: null, item };
      }
      const label = item.label();
      const state = item.state();
      if (state !== 'ok') {
        const name = this.text(state === 'no-access' ? 'noAccessChip' : 'unavailableChip', { label });
        return { id: item.id, kind: 'revoked', label, name, separated: false, link: null, item };
      }
      return { id: item.id, kind: 'pin', label, name: label, separated: false, link: this.#router.parseUrl(item.href), item };
    }),
  );

  /** The item that holds the routed page's pin: the pin, or its folder. */
  protected readonly currentRow = computed(() => {
    const current = this.#pins.current();
    return current === null ? null : (current.folderId ?? current.id);
  });

  /** The items in the bar's order: stored order, and below md the current one first. */
  readonly #ordered = computed(() => {
    const rows = this.rows();
    const current = this.#md() ? null : this.currentRow();
    const lead = rows.find((row) => row.id === current);
    return lead === undefined ? rows : [lead, ...rows.filter((row) => row !== lead)];
  });

  /** The items that show. */
  protected readonly shown = computed(() => this.#ordered().slice(0, this.layout()?.count ?? Number.POSITIVE_INFINITY));

  /** The ids of the items in More. */
  protected readonly hidden = computed(() =>
    this.#ordered()
      .slice(this.layout()?.count ?? Number.POSITIVE_INFINITY)
      .map((row) => row.id),
  );

  /** The items in More, which its menu lists. */
  protected readonly hiddenItems = computed(() => {
    const hidden = new Set(this.hidden());
    return this.#ordered()
      .filter((row) => hidden.has(row.id))
      .map((row) => row.item);
  });

  /** The chip that takes Tab: the focused one, else the current pin's (More when it is in More), else the first. */
  protected readonly tabStop = computed(() => {
    const ids = [...this.shown().map((row) => row.id), ...(this.hidden().length > 0 ? [MORE] : [])];
    const roving = this.roving();
    const current = this.currentRow();
    if (roving !== null && ids.includes(roving)) {
      return roving;
    }
    if (current !== null && this.hidden().includes(current)) {
      return MORE;
    }
    return current !== null && ids.includes(current) ? current : (ids[0] ?? null);
  });

  /** Creates the bar, which fits its items, follows the fonts and hands itself to the overlays. */
  constructor() {
    const destroyRef = inject(DestroyRef);

    // --- The fonts: the first fit waits for them, and a late face re-reads the widths ---
    const fonts = this.#fonts;
    if (fonts !== null) {
      if (!untracked(this.#fontsReady)) {
        void fonts.ready.then(() => this.#fontsReady.set(true));
      }
      const loaded = (): void => this.#faces.update((count) => count + 1);
      fonts.addEventListener('loadingdone', loaded);
      destroyRef.onDestroy(() => fonts.removeEventListener('loadingdone', loaded));
    }

    // --- The fit: the widths read after each render that changed what they depend on ---
    afterRenderEffect({
      read: () => {
        this.#ordered();
        this.#md();
        this.#l10n.language();
        this.#l10n.dir();
        this.#faces();
        this.layout();
        const pending = this.ui.pending();
        if (!this.#fontsReady() || this.measureEl() === undefined) {
          return;
        }
        untracked(() => {
          this.#read();
          if (!this.#fit()) {
            this.#settled(pending);
          }
        });
      },
    });

    // --- A resize: refit from the widths read last, and render at once ---
    const observer = new ResizeObserver(() => {
      if (this.#widths !== null && this.#fit()) {
        this.#cdr.detectChanges();
      }
    });
    effect(() => {
      const track = this.trackEl()?.nativeElement;
      observer.disconnect();
      if (track !== undefined) {
        observer.observe(track);
      }
    });
    destroyRef.onDestroy(() => observer.disconnect());

    // --- Preloading: after the first render, every pin that opens loads its route's chunks at idle ---
    const preloaded = new Set<string>();
    const rendered = signal(false);
    afterNextRender(() => rendered.set(true));
    effect(() => {
      if (!rendered()) {
        return;
      }
      for (const item of this.#pins.items()) {
        for (const pin of 'folder' in item ? item.items : [item]) {
          if (pin.state() === 'ok' && !preloaded.has(pin.href)) {
            preloaded.add(pin.href);
            untracked(() => this.#urls.preload(pin.href));
          }
        }
      }
    });

    // --- The overlays: the bar's element, its control and its unpin path ---
    const port: TmPinnedBarPort = {
      element: inject<ElementRef<HTMLElement>>(ElementRef).nativeElement,
      control: () => this.controlEl()?.nativeElement ?? null,
      unpin: (id, via, refocus) => {
        const index = this.#chips().findIndex((chip) => chip.dataset['tmPin'] === id);
        tmPinsVia(via, () => this.#pins.remove(id));
        this.#refocus = refocus ? Math.max(index, 0) : null;
      },
    };
    destroyRef.onDestroy(this.ui.attachBar(port));
  }

  /**
   * A string of the pins' scope.
   *
   * @param key - The key under `tm.pins`.
   * @param params - The message's parameters.
   * @returns The text.
   */
  protected text(key: string, params?: Readonly<Record<string, unknown>>): string {
    return this.ui.text(key, params);
  }

  /**
   * Moves focus along the chips: Left and Right by direction, Home and End;
   * Down opens a menu chip's menu.
   *
   * @param event - The keydown.
   */
  protected keydown(event: KeyboardEvent): void {
    const chips = this.#chips();
    const index = chips.indexOf(event.target as HTMLElement);
    if (index < 0) {
      return;
    }
    const forward = this.#l10n.dir() === 'rtl' ? 'ArrowLeft' : 'ArrowRight';
    const back = forward === 'ArrowRight' ? 'ArrowLeft' : 'ArrowRight';
    let next: number;
    if (tmKeysMatch(event, forward)) {
      next = Math.min(index + 1, chips.length - 1);
    } else if (tmKeysMatch(event, back)) {
      next = Math.max(index - 1, 0);
    } else if (tmKeysMatch(event, 'Home')) {
      next = 0;
    } else if (tmKeysMatch(event, 'End')) {
      next = chips.length - 1;
    } else {
      if (tmKeysMatch(event, 'ArrowDown') && chips[index].localName === 'button') {
        event.preventDefault();
        chips[index].click();
      }
      return;
    }
    event.preventDefault();
    this.#focus(chips[next]);
  }

  /**
   * Focus left a chip: when it left the list, Tab enters on the current pin again.
   *
   * @param event - The focusout.
   */
  protected left(event: FocusEvent): void {
    if (!(event.currentTarget as HTMLElement).contains(event.relatedTarget as Node | null)) {
      this.roving.set(null);
    }
  }

  /**
   * A chip's link was followed: a plain click, which the router handles, is reported.
   *
   * @param event - The click.
   */
  protected followed(event: MouseEvent): void {
    if (event.button === 0 && !event.ctrlKey && !event.metaKey && !event.shiftKey && !event.altKey) {
      this.#telemetry.event('tm.shell.navigated', { source: 'pin' }, { sampled: true });
    }
  }

  /** The bar's chips in order, More last. */
  #chips(): HTMLElement[] {
    return [...(this.listEl()?.nativeElement.querySelectorAll<HTMLElement>('[data-tm-pin]') ?? [])];
  }

  /** Focuses a chip, which becomes the tab stop. */
  #focus(chip: HTMLElement): void {
    this.roving.set(chip.dataset['tmPin'] ?? null);
    chip.focus();
  }

  /** Reads every item's width and the gap from the measuring row. */
  #read(): void {
    const measure = this.measureEl()!.nativeElement;
    const widths = new Map<string, number>();
    for (const element of measure.children) {
      widths.set((element as HTMLElement).dataset['tmMeasure']!, element.getBoundingClientRect().width);
    }
    this.#widths = widths;
    this.#gap = parseFloat(getComputedStyle(measure).columnGap) || 0;
  }

  /** Fits the items to the track from the widths read last; whether the fit changed. */
  #fit(): boolean {
    const track = this.trackEl()?.nativeElement;
    const widths = this.#widths;
    if (track === undefined || widths === null) {
      return false;
    }
    const rows = untracked(this.#ordered);
    const count = fitCount(
      rows.map((row) => widths.get(row.id) ?? 0),
      this.#gap,
      widths.get(MORE) ?? 0,
      track.getBoundingClientRect().width,
    );
    const next: TmPinnedBarLayout = { count, widths };
    if (sameLayout(untracked(this.layout), next)) {
      return false;
    }
    this.layout.set(next);
    return true;
  }

  /**
   * The fit has settled: a pin just pinned learns whether its chip shows,
   * and focus moves on from a pin just removed.
   */
  #settled(pending: string | null): void {
    if (pending !== null && untracked(this.rows).some((row) => row.id === pending)) {
      this.ui.reveal(pending, untracked(this.shown).some((row) => row.id === pending));
    }
    const refocus = this.#refocus;
    if (refocus !== null) {
      this.#refocus = null;
      const chips = this.#chips();
      const target = chips[refocus] ?? chips[refocus - 1] ?? this.controlEl()?.nativeElement;
      if (target !== undefined) {
        if (target.dataset['tmPin'] === undefined) {
          target.focus();
        } else {
          this.#focus(target);
        }
      }
    }
  }
}
