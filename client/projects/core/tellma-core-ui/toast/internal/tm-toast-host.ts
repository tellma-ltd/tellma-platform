// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import {
  afterRenderEffect,
  Component,
  DestroyRef,
  type ElementRef,
  inject,
  input,
  untracked,
  viewChild,
} from '@angular/core';
import { CircleCheckBig, CircleX, Info, TriangleAlert, X } from 'lucide';

import { TM_UI_TRANSLATE } from '@tellma/core-ui';
import { TmButton } from '@tellma/core-ui/button';
import { TmIcon } from '@tellma/core-ui/icon';
import { tmPushEscapeDismissal } from '@tellma/core-ui/overlay';

import type { TmToastController } from './tm-toast-model';

/** The default glyph of each tone, drawn through tm-icon. */
const TONE_GLYPHS = {
  neutral: Info,
  accent: Info,
  success: CircleCheckBig,
  warning: TriangleAlert,
  error: CircleX,
} as const;

/**
 * The one top-layer host of the toasts: a `popover="manual"` element on
 * `<body>`, shown once and kept open. It draws the stack as a region of
 * cards, owns the two persistent live regions, and handles focus: a focus
 * entering the stack is remembered so Escape (through the shared Escape
 * stack) and a closing toast can hand it back. Created and fed by the
 * toasts service.
 */
@Component({
  selector: 'tm-toast-host',
  imports: [TmButton, TmIcon],
  // The region holds the cards, newest first, then the overflow pill. The
  // two live regions sit outside it and always exist: a region that
  // appears together with its text is not reliably announced.
  template: `
    <div
      #region
      class="tm-toasts"
      role="region"
      [attr.aria-label]="regionName()"
      [hidden]="controller().visible().length === 0"
      (pointerenter)="controller().setPaused('hover', true)"
      (pointerleave)="controller().setPaused('hover', false)"
      (focusin)="onFocusin($event)"
      (focusout)="onFocusout($event)"
      (keydown)="onKeydown($event)"
    >
      @for (entry of controller().visible(); track entry.id) {
        <div
          class="tm-toast"
          role="group"
          [attr.data-toast-id]="entry.id"
          [attr.data-tone]="entry.toast.tone"
          [attr.aria-labelledby]="'tm-toast-' + entry.id + '-title'"
        >
          <tm-icon
            class="tm-toast__icon"
            [name]="entry.toast.icon"
            [svg]="entry.toast.icon === undefined ? glyphs[entry.toast.tone] : undefined"
          />
          <div class="tm-toast__body">
            <div class="tm-toast__title" [id]="'tm-toast-' + entry.id + '-title'">
              {{ entry.toast.title }}
            </div>
            @if (entry.toast.detail; as detail) {
              <div class="tm-toast__detail">{{ detail }}</div>
            }
            @if (entry.toast.action; as action) {
              <button
                tmButton
                type="button"
                size="sm"
                class="tm-toast__action"
                (click)="controller().runAction(entry.id)"
              >
                {{ action.label }}
              </button>
            }
          </div>
          <button
            type="button"
            class="tm-toast__close"
            [attr.aria-label]="closeName()"
            (click)="controller().dismiss(entry.id)"
          >
            <tm-icon [svg]="closeGlyph" />
          </button>
        </div>
      }
      @if (controller().pill(); as pill) {
        @if (pill.run) {
          <button type="button" class="tm-toasts__pill" (click)="controller().runPill()">
            {{ pill.label }}
          </button>
        } @else {
          <span class="tm-toasts__pill">{{ pill.label }}</span>
        }
      }
    </div>
    <div class="tm-toasts__live" role="status">{{ controller().politeText() }}</div>
    <div class="tm-toasts__live" role="alert">{{ controller().assertiveText() }}</div>
  `,
  styleUrl: './tm-toast-host.css',
  host: {
    class: 'tm-toast-host',
    popover: 'manual',
  },
})
export class TmToastHost {
  private readonly translate = inject(TM_UI_TRANSLATE);

  /** The toasts service's state and callbacks. */
  readonly controller = input.required<TmToastController>();

  /** The tone glyphs (the library's own data). */
  protected readonly glyphs = TONE_GLYPHS;

  /** The close button's glyph. */
  protected readonly closeGlyph = X;

  /** "Recent messages". */
  protected readonly regionName = this.translate('toast.region');

  /** "Close". */
  protected readonly closeName = this.translate('toast.close');

  /** The stack's region. */
  private readonly region = viewChild.required<ElementRef<HTMLElement>>('region');

  /** Where focus came from when it entered the stack: Escape returns it there. */
  private origin: HTMLElement | null = null;

  /**
   * Releases the stack's place on the shared Escape stack. Set while the
   * stack holds focus: it is also the sign that the stack holds it.
   */
  private releaseEscape: (() => void) | null = null;

  constructor() {
    inject(DestroyRef).onDestroy(() => this.releaseEscape?.());
    // A render can take the focused control away without a close: a new
    // toast pushes its card out of the window, a replacement drops its
    // action, or the pill goes. Firefox and WebKit then report no focus
    // change at all, so after every render of the stack the host checks
    // whether the focus it holds has fallen out of it.
    afterRenderEffect({
      write: () => {
        this.controller().visible();
        this.controller().pill();
        untracked(() => this.recoverFocus());
      },
    });
  }

  /**
   * Focuses the newest toast's first button; `false` when no toast shows.
   * The caller renders the host first, so a toast shown in the same task
   * is there to focus.
   */
  focusNewest(): boolean {
    const button = this.controls()[0];
    if (button === undefined) {
      return false;
    }
    button.focus();
    return true;
  }

  /**
   * Called just before a toast closes. If focus is inside it, focus goes
   * back where it came from, else to another toast, else nowhere, so it
   * never falls into a card that is going away.
   */
  releaseFocus(id: number): void {
    const region = this.region().nativeElement;
    const card = region.querySelector(`[data-toast-id="${id}"]`);
    const active = document.activeElement;
    if (card === null || !(active instanceof HTMLElement) || !card.contains(active)) {
      return;
    }
    if (this.returnFocus()) {
      return;
    }
    const other = this.controls().find((control) => !card.contains(control));
    if (other !== undefined) {
      other.focus();
    } else {
      active.blur();
      this.leave();
    }
  }

  /**
   * Focus entering the stack from outside: timers pause, the origin is
   * remembered, and Escape closes the focused toast. Focus the host moves
   * itself, while it already holds focus, keeps the first origin.
   */
  protected onFocusin(event: FocusEvent): void {
    const region = this.region().nativeElement;
    const from = event.relatedTarget;
    if ((from instanceof Node && region.contains(from)) || this.releaseEscape !== null) {
      return;
    }
    this.origin = from instanceof HTMLElement ? from : null;
    this.controller().setPaused('focus', true);
    this.releaseEscape = tmPushEscapeDismissal(() => this.dismissFocused());
  }

  /**
   * Focus leaving a control of the stack. A move within the stack changes
   * nothing, and a move to another element releases the hold. A loss to
   * nowhere waits a microtask: Chromium reports a control removed by a
   * render that way, and the render's own check has by then answered it
   * by focusing another control.
   */
  protected onFocusout(event: FocusEvent): void {
    const region = this.region().nativeElement;
    const to = event.relatedTarget;
    if (to instanceof Node && region.contains(to)) {
      return;
    }
    if (to !== null) {
      this.leave();
      return;
    }
    queueMicrotask(() => {
      if (!region.contains(document.activeElement)) {
        this.leave();
      }
    });
  }

  /**
   * Tab and Shift+Tab stay among the toasts' buttons: past the last (or
   * before the first) focus goes back where it came from, so a modal that
   * sent the user here gets focus back instead of the page behind it.
   * Without an origin to return to, focus wraps around the stack.
   */
  protected onKeydown(event: KeyboardEvent): void {
    if (event.key !== 'Tab' || event.ctrlKey || event.metaKey || event.altKey) {
      return;
    }
    const controls = this.controls();
    const index = controls.indexOf(event.target as HTMLElement);
    const edge = event.shiftKey ? 0 : controls.length - 1;
    if (index === -1 || index !== edge) {
      return;
    }
    event.preventDefault();
    if (!this.returnFocus()) {
      controls[event.shiftKey ? controls.length - 1 : 0]?.focus();
    }
  }

  /** The stack's focusable controls, newest card first, then the pill. */
  private controls(): HTMLElement[] {
    return Array.from(this.region().nativeElement.querySelectorAll<HTMLElement>('button'));
  }

  /** Returns focus where it came from, when that is still in the page. */
  private returnFocus(): boolean {
    const origin = this.origin;
    if (origin?.isConnected !== true) {
      return false;
    }
    origin.focus({ preventScroll: true });
    if (document.activeElement !== origin) {
      return false;
    }
    // Focus left the stack, perhaps from a control the browser has already
    // dropped, which reports no focus change: release the hold here.
    this.leave();
    return true;
  }

  /**
   * After a render: if the stack holds focus but the focused control is
   * gone, focus moves to the newest toast, else back where it came from,
   * else the hold is released, so the timers run and Escape reaches what
   * lies under the stack again.
   */
  private recoverFocus(): void {
    const active = document.activeElement;
    if (this.releaseEscape === null || this.region().nativeElement.contains(active)) {
      return;
    }
    if (active !== null && active !== document.body) {
      // Focus moved somewhere else on purpose; the focusout check releases.
      return;
    }
    if (!this.focusNewest() && !this.returnFocus()) {
      this.leave();
    }
  }

  /** The stack no longer holds focus: timers resume and Escape is released. */
  private leave(): void {
    this.origin = null;
    if (this.releaseEscape !== null) {
      this.releaseEscape();
      this.releaseEscape = null;
      this.controller().setPaused('focus', false);
    }
  }

  /**
   * Escape: closes the toast that holds focus, which hands focus back. On
   * the pill, focus simply goes back.
   */
  private dismissFocused(): void {
    const card = (document.activeElement as HTMLElement | null)?.closest<HTMLElement>(
      '[data-toast-id]',
    );
    if (card !== null && card !== undefined) {
      this.controller().dismiss(Number(card.dataset['toastId']));
    } else if (!this.returnFocus()) {
      (document.activeElement as HTMLElement | null)?.blur();
      this.leave();
    }
  }
}
