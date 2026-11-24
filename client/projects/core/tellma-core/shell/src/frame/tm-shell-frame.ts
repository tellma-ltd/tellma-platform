// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import {
  ChangeDetectionStrategy,
  Component,
  computed,
  DestroyRef,
  DOCUMENT,
  effect,
  type ElementRef,
  inject,
  Injectable,
  input,
  type Signal,
  signal,
  viewChild,
} from '@angular/core';
import { TM_SHELL_LAYOUT, TmBanners, TmLocalization, TmShell } from '@tellma/core';
import { TmBannerHost } from '@tellma/core/attention';

// The shell frame (spec 0027 §5), which both shells render: the skip link,
// the `header` holding the app bar, the banner row and the pinned bar, the
// document's one `main`, which alone scrolls, and the empty `aside` reserved
// for the help drawer. The grid itself and the root row variables live in
// @tellma/core/styles.css (tm-frame.css), because the toast host and the
// pages read `--tm-shell-top` before any shell renders; the frame writes the
// row heights its shell shows. Nothing in it animates (spec 0027 §5.4).

/** The scope of the frame's strings under `tm`. */
const SCOPE = 'shell';

/** The frame's English strings, under `tm.shell`. */
const STRINGS_EN = {
  /** The skip link, which moves focus past the chrome to the page. */
  skip: 'Skip to content',
  /** The tenant picker's document title, which the router sets before the picker's chunk loads. */
  tenants: 'Tenants',
} as const;

/** The heights a frame gives its rows, in CSS pixels. */
export interface TmShellRows {
  /** The app bar's row. */
  readonly '--tm-app-bar-height': number;
  /** The banner row. */
  readonly '--tm-banner-height': number;
  /** The pinned bar's row. */
  readonly '--tm-pinned-bar-height': number;
}

/** The row variables a frame writes on the root element. */
const ROWS = ['--tm-app-bar-height', '--tm-banner-height', '--tm-pinned-bar-height'] as const;

/**
 * Writes the newest live frame's row heights on the root element, where the
 * frame stylesheet's grid, `--tm-shell-top` and the toast offset read them.
 * A frame that replaces another (a navigation from the picker into a tenant)
 * may live beside it for a moment, so the newest one wins and the older
 * one's end leaves the rows alone; the last one's end restores the
 * stylesheet's defaults.
 */
@Injectable({ providedIn: 'root' })
export class TmShellRowsWriter {
  readonly #frames = signal<readonly Signal<TmShellRows>[]>([]);

  /** Creates the writer, which follows the newest frame's rows; the app's end restores the defaults. */
  constructor() {
    const style = inject(DOCUMENT).documentElement.style;
    const write = (rows: TmShellRows | undefined): void => {
      for (const name of ROWS) {
        if (rows === undefined) {
          style.removeProperty(name);
        } else {
          style.setProperty(name, `${rows[name]}px`);
        }
      }
    };
    effect(() => write(this.#frames().at(-1)?.()));
    inject(DestroyRef).onDestroy(() => write(undefined));
  }

  /**
   * Shows a frame's rows until its `destroyRef` ends.
   *
   * @param rows - The frame's row heights.
   * @param destroyRef - The frame's lifetime.
   */
  hold(rows: Signal<TmShellRows>, destroyRef: DestroyRef): void {
    this.#frames.update((frames) => [...frames, rows]);
    destroyRef.onDestroy(() => this.#frames.update((frames) => frames.filter((frame) => frame !== rows)));
  }
}

/**
 * The frame of both shells: a fixed-height grid in which only `main`
 * scrolls. It renders the skip link, then a `header` with the projected app
 * bar (`[tmShellBar]`), the banner row and the projected pinned bar
 * (`[tmShellPinnedBar]`), then `main#tm-main` with the projected page, then
 * an empty, hidden `aside`. While the frame is immersive, no `header`
 * renders.
 *
 * @example
 * ```html
 * <tm-shell-frame [pinnedBar]="false">
 *   <div tmShellBar>…</div>
 *   <router-outlet />
 * </tm-shell-frame>
 * ```
 */
@Component({
  selector: 'tm-shell-frame',
  imports: [TmBannerHost],
  changeDetection: ChangeDetectionStrategy.OnPush,
  host: { class: 'tm-shell' },
  template: `
    <!-- The skip link, the first focusable element: shown on focus, it moves focus to main. -->
    <a class="tm-shell-frame__skip" href="#tm-main" (click)="skip($event)">{{ skipLabel() }}</a>
    <!-- The chrome, row by row; an immersive frame renders none of it. -->
    @if (!immersive()) {
      <header class="tm-shell-frame__header">
        <div><ng-content select="[tmShellBar]" /></div>
        <tm-banner-host />
        <div><ng-content select="[tmShellPinnedBar]" /></div>
      </header>
    }
    <!-- The one scrolling region, which the skip link and navigation focus fall back to. -->
    <main #main id="tm-main" class="tm-shell-frame__main" tabindex="-1"><ng-content /></main>
    <!-- The help drawer's reserved column: empty, hidden and 0 wide in this release. -->
    <aside class="tm-shell-frame__aside" hidden></aside>
  `,
  styles: `
    :host {
      position: relative;
    }

    /* The skip link: out of sight until it takes focus. */
    .tm-shell-frame__skip {
      position: absolute;
      z-index: 1;
      inset-block-start: var(--space-2);
      inset-inline-start: var(--space-2);
      padding: var(--space-2) var(--space-3);
      border-radius: var(--radius-md);
      background: var(--surface-card);
      color: var(--text-link);
      font: var(--weight-semibold) var(--text-sm) / var(--leading-ui) var(--font-ui);
      box-shadow: var(--shadow-md);
    }

    .tm-shell-frame__skip:not(:focus) {
      clip-path: inset(50%);
      inline-size: 1px;
      block-size: 1px;
      overflow: hidden;
      white-space: nowrap;
    }

    /* The chrome's three rows, in order, at the heights the root variables give them. */
    .tm-shell-frame__header {
      display: grid;
      grid-row: 1 / 4;
      grid-column: 1 / -1;
      grid-template-rows: var(--tm-app-bar-height) var(--tm-banner-height) var(--tm-pinned-bar-height);
      min-block-size: 0;
    }

    .tm-shell-frame__header > * {
      min-block-size: 0;
      overflow: hidden;
    }

    /* Only main scrolls; its gutter is kept, so nothing shifts when it starts. */
    .tm-shell-frame__main {
      grid-row: 4;
      grid-column: 1;
      min-block-size: 0;
      overflow: auto;
      scrollbar-gutter: stable;
    }

    .tm-shell-frame__main:focus {
      outline: none;
    }

    .tm-shell-frame__aside {
      grid-row: 4;
      grid-column: 2;
    }

    /* Print: the page alone, at its full length. */
    @media print {
      :host {
        display: block;
        block-size: auto;
      }

      .tm-shell-frame__skip,
      .tm-shell-frame__header,
      .tm-shell-frame__aside {
        display: none;
      }

      .tm-shell-frame__main {
        overflow: visible;
        block-size: auto;
      }
    }
  `,
})
export class TmShellFrame {
  readonly #banners = inject(TmBanners);
  /** The main landmark, which the skip link focuses. */
  private readonly main = viewChild.required<ElementRef<HTMLElement>>('main');

  /** Whether the frame shows the app bar's row; default `true`. */
  readonly appBar = input(true);

  /** Whether the frame shows the pinned bar's row; default `false`, as outside a tenant. */
  readonly pinnedBar = input(false);

  /** Whether the frame is immersive, so it renders no `header`. */
  protected readonly immersive = inject(TmShell).immersive;

  /** The skip link's text. */
  protected readonly skipLabel: Signal<string>;

  /** Creates the frame, which writes its rows' heights while it lives. */
  constructor() {
    const l10n = inject(TmLocalization);
    l10n.ɵregisterStrings(SCOPE, STRINGS_EN);
    this.skipLabel = l10n.ɵtext('tm.shell.skip');
    // The banner row is open while TmBanners says so; its idle gate times the change.
    const rows = computed<TmShellRows>(() => ({
      '--tm-app-bar-height': this.appBar() ? TM_SHELL_LAYOUT.appBar : 0,
      '--tm-banner-height': this.#banners.visible() ? TM_SHELL_LAYOUT.banner : 0,
      '--tm-pinned-bar-height': this.pinnedBar() ? TM_SHELL_LAYOUT.pinnedBar : 0,
    }));
    inject(TmShellRowsWriter).hold(rows, inject(DestroyRef));
  }

  /**
   * Moves focus to `main` in place of the fragment navigation, which the
   * router would otherwise take for a new URL.
   *
   * @param event - The link's click.
   */
  protected skip(event: MouseEvent): void {
    event.preventDefault();
    this.main().nativeElement.focus();
  }
}
