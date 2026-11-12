// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import {
  Component,
  DestroyRef,
  type ElementRef,
  inject,
  input,
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

  /** Releases the stack's place on the shared Escape stack while it holds focus. */
  private releaseEscape: (() => void) | null = null;

  constructor() {
    inject(DestroyRef).onDestroy(() => this.releaseEscape?.());
  }

  /**
   * Focuses the newest toast's first button; `false` when no toast shows.
   * The caller renders the host first, so a toast shown in the same task
   * is there to focus.
   */
  focusNewest(): boolean {
    const button = this.region().nativeElement.querySelector<HTMLElement>('.tm-toast button');
    if (button === null) {
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
    if (this.origin?.isConnected === true) {
      this.origin.focus({ preventScroll: true });
      return;
    }
    const other = Array.from(region.querySelectorAll<HTMLElement>('.tm-toast')).find(
      (element) => element !== card,
    );
    const button = other?.querySelector<HTMLElement>('button');
    if (button !== undefined && button !== null) {
      button.focus();
    } else {
      active.blur();
    }
  }

  /**
   * Focus entering the stack from outside: timers pause, the origin is
   * remembered, and Escape closes the focused toast.
   */
  protected onFocusin(event: FocusEvent): void {
    const region = this.region().nativeElement;
    const from = event.relatedTarget;
    if (from instanceof Node && region.contains(from)) {
      return;
    }
    this.origin = from instanceof HTMLElement ? from : null;
    this.controller().setPaused('focus', true);
    this.releaseEscape ??= tmPushEscapeDismissal(() => this.dismissFocused());
  }

  /** Focus leaving the stack: timers resume and Escape is released. */
  protected onFocusout(event: FocusEvent): void {
    const to = event.relatedTarget;
    if (to instanceof Node && this.region().nativeElement.contains(to)) {
      return;
    }
    this.origin = null;
    this.controller().setPaused('focus', false);
    this.releaseEscape?.();
    this.releaseEscape = null;
  }

  /** Escape: closes the toast that holds focus, which hands focus back. */
  private dismissFocused(): void {
    const card = (document.activeElement as HTMLElement | null)?.closest<HTMLElement>(
      '[data-toast-id]',
    );
    if (card !== null && card !== undefined) {
      this.controller().dismiss(Number(card.dataset['toastId']));
    }
  }
}
