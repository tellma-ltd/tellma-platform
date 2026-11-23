// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import { ChangeDetectionStrategy, Component, DestroyRef, effect, inject, signal, untracked } from '@angular/core';
import { TmBanners } from '@tellma/core';
import { TmBannerBar } from '@tellma/core-ui/banner';

// The banner host (spec 0028 §9.4), which both shells place in the banner
// row. It renders `tm-banner-bar` from what `TmBanners` says the row shows
// after the idle gate, and wires the bar's buttons back to the service. It
// never moves focus to a banner. It owns the one visually hidden live region
// the banners speak through: assertive for a level-1 banner, polite for the
// rest. The region speaks when a banner becomes the row's banner, before the
// gate opens the row, so keyboard and screen-reader users hear "Connection
// lost" at once even while the row waits for the user to stop typing. The
// bar itself is not a live region.

/** The pause between emptying the live region and filling it, so a repeated message is announced again. */
const ANNOUNCE_DELAY_MS = 100;

/**
 * The shell's banner row: the banner `TmBanners` shows, with its action,
 * pager and dismiss button, and the live region that announces each banner
 * the moment it is queued. While the row is open and emptied, it holds an
 * empty 32 px box until the idle gate closes it.
 *
 * @example
 * ```html
 * <header>
 *   <tm-app-bar />
 *   <tm-banner-host />
 * </header>
 * ```
 */
@Component({
  selector: 'tm-banner-host',
  imports: [TmBannerBar],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <!-- The row: the shown banner, or an empty box while an emptied row waits to close. -->
    @if (banners.visible()) {
      @if (banners.current(); as view) {
        <tm-banner-bar
          [tone]="view.options.tone"
          [message]="view.options.message()"
          [icon]="view.options.icon"
          [actionLabel]="view.options.action?.label() ?? null"
          [dismissible]="view.options.dismissible"
          [position]="view"
          (action)="view.options.action?.run()"
          (dismiss)="dismiss(view.options.id)"
          (previous)="page(-1)"
          (next)="page(1)"
        />
      } @else {
        <div class="tm-banner-host__empty"></div>
      }
    }
    <!-- The one live region, read but never drawn. -->
    <div class="tm-banner-host__live" [attr.aria-live]="politeness()" aria-atomic="true">{{ announcement() }}</div>
  `,
  styles: `
    :host {
      display: block;
    }

    .tm-banner-host__empty {
      block-size: var(--banner-height);
    }

    .tm-banner-host__live {
      position: absolute;
      inline-size: 1px;
      block-size: 1px;
      overflow: hidden;
      clip-path: inset(50%);
      white-space: nowrap;
    }
  `,
})
export class TmBannerHost {
  /** The banners the row shows. */
  protected readonly banners = inject(TmBanners);

  /** The live region's politeness: assertive for a level-1 banner. */
  protected readonly politeness = signal<'assertive' | 'polite'>('polite');

  /** The live region's text. */
  protected readonly announcement = signal('');

  /** Creates the host, which announces each banner that becomes the row's. */
  constructor() {
    let announced: string | null = null;
    let timer: ReturnType<typeof setTimeout> | undefined;
    inject(DestroyRef).onDestroy(() => clearTimeout(timer));

    // --- The announcement: once per banner that becomes the row's, before the gate ---
    effect(() => {
      const head = this.banners.ɵhead();
      untracked(() => {
        const id = head?.options.id ?? null;
        if (id === announced) {
          return;
        }
        announced = id;
        if (head !== null) {
          // The politeness changes with the region empty, and the text follows
          // a moment later, so the screen reader reads it at the new politeness.
          clearTimeout(timer);
          this.politeness.set(head.options.level === 1 ? 'assertive' : 'polite');
          this.announcement.set('');
          timer = setTimeout(() => this.announcement.set(head.options.message()), ANNOUNCE_DELAY_MS);
        }
      });
    });
  }

  /**
   * The dismiss button: the user's own dismiss, which applies at once.
   *
   * @param id - The shown banner's id.
   */
  protected dismiss(id: string): void {
    this.banners.ɵdismiss(id);
  }

  /**
   * The pager's buttons.
   *
   * @param step - `1` for the next banner, `-1` for the previous.
   */
  protected page(step: 1 | -1): void {
    this.banners.ɵpage(step);
  }
}
