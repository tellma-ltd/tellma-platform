// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import { NgTemplateOutlet } from '@angular/common';
import {
  booleanAttribute,
  Component,
  computed,
  DestroyRef,
  ElementRef,
  inject,
  input,
  output,
  type OutputRef,
  signal,
  untracked,
} from '@angular/core';
import { outputFromObservable } from '@angular/core/rxjs-interop';
import { X } from 'lucide';
import { Observable, Subject } from 'rxjs';

import { TM_UI_TRANSLATE, type TmControlSize, ɵtmControlSize } from '@tellma/core-ui';
import type { TmBadgeTone } from '@tellma/core-ui/badge';
import { TmIcon } from '@tellma/core-ui/icon';

import { TM_CHIP_SET } from './internal/tm-chip-set-ref';

/**
 * One value, filter, link or command in a pill: "Service", "Posted after
 * 1 May", "SAL-0042". The label renders in a `<bdi>`, so a value in another
 * script keeps its order; projected content replaces the visible label
 * (for a sentence with isolated arguments), while `label` still names the
 * chip and its remove button.
 *
 * The label renders as a link with `href`, as a button when `(activate)` is
 * bound, and as plain text otherwise. `removable` adds a remove button
 * named "Remove {label}"; Delete or Backspace on the chip removes it too.
 * Removing is the host's job: `remove` only asks.
 *
 * In a `tm-chip-set` the chip is a list item and its buttons join the
 * set's single tab stop.
 *
 * @tmGroup indicator
 * @tmA11yNotes A plain chip is text; a linked or activatable chip is a
 *   native link or button named by `label`. The remove button is named
 *   "Remove {label}". Delete or Backspace on any of the chip's controls
 *   emits `remove`. In a `tm-chip-set` the chip is a `listitem` and the
 *   arrow keys move between its controls (see `tm-chip-set`).
 */
@Component({
  selector: 'tm-chip',
  imports: [NgTemplateOutlet, TmIcon],
  // The icon and the label are one template, stamped into exactly one of
  // the three label elements (link, button, text) so the projected
  // content has one home. The remove button follows the label.
  template: `
    <ng-template #content>
      @if (icon(); as name) {
        <tm-icon class="tm-chip__icon" [name]="name" />
      }
      <bdi class="tm-chip__text"><ng-content>{{ label() }}</ng-content></bdi>
    </ng-template>
    @switch (kind()) {
      @case ('link') {
        <a class="tm-chip__label" [attr.href]="href()" [attr.aria-label]="label()">
          <ng-container [ngTemplateOutlet]="content" />
        </a>
      }
      @case ('button') {
        <button
          type="button"
          class="tm-chip__label"
          [attr.aria-label]="label()"
          (click)="activated.next()"
        >
          <ng-container [ngTemplateOutlet]="content" />
        </button>
      }
      @default {
        <span class="tm-chip__label"><ng-container [ngTemplateOutlet]="content" /></span>
      }
    }
    @if (removable()) {
      <button
        type="button"
        class="tm-chip__remove"
        [attr.aria-label]="removeName()"
        (click)="remove.emit()"
      >
        <tm-icon [svg]="removeGlyph" />
      </button>
    }
  `,
  styleUrl: './tm-chip.css',
  host: {
    class: 'tm-chip',
    '[attr.data-tone]': 'tone()',
    '[attr.data-size]': 'resolvedSize()',
    '[attr.role]': 'set === null ? null : "listitem"',
    '[class.tm-chip--removable]': 'removable()',
    '(keydown)': 'onKeydown($event)',
  },
})
export class TmChip {
  /** The chip's own element. */
  private readonly host = inject<ElementRef<HTMLElement>>(ElementRef).nativeElement;

  /** The chip set this chip sits in, if any. */
  protected readonly set = inject(TM_CHIP_SET, { optional: true });

  private readonly translate = inject(TM_UI_TRANSLATE);

  /** The text, and the name of the chip and its remove button. */
  readonly label = input.required<string>();

  /** The colour tone, as for `tm-badge`. Default `neutral`. */
  readonly tone = input<TmBadgeTone>('neutral');

  /** A leading icon, by registry name; unset, none. */
  readonly icon = input<string | undefined>(undefined);

  /** A URL: the label renders as a link to it. */
  readonly href = input<string | null>(null);

  /** Adds a remove button named "Remove {label}" and lets Delete or Backspace remove. */
  readonly removable = input(false, { transform: booleanAttribute });

  /** The size step: 20, 24 or 28 px. Unset, the workspace default (`lg` on a phone). */
  readonly size = input<TmControlSize | undefined>(undefined);

  /**
   * Emits when the label button is clicked, or pressed with Enter or Space.
   * Binding it (without `href`) is what renders the label as a button.
   */
  // An output whose listeners are counted, so the label turns into a
  // button exactly when someone listens. The count changes while a parent
  // template sets up its listeners, so it is written outside any reactive
  // context. (The subscriber function runs on subscription, after every
  // field below exists.)
  readonly activate: OutputRef<void> = outputFromObservable(
    new Observable<void>((subscriber) => {
      untracked(() => this.activateListeners.update((count) => count + 1));
      const subscription = this.activated.subscribe(subscriber);
      return () => {
        subscription.unsubscribe();
        untracked(() => this.activateListeners.update((count) => count - 1));
      };
    }),
  );

  /** Emits when the remove button is clicked, or Delete or Backspace is pressed on the chip. */
  readonly remove = output<void>();

  /** The glyph of the remove button (the library's own data). */
  protected readonly removeGlyph = X;

  /** The size step the chip renders at. */
  protected readonly resolvedSize = ɵtmControlSize(this.size);

  /** The remove button's name: "Remove {label}". */
  protected readonly removeName = computed(() =>
    this.translate('chip.remove', { label: this.label() })(),
  );

  /** Feeds `activate`; the label button pushes into it. */
  protected readonly activated = new Subject<void>();

  /** How many listeners `activate` has: any at all makes the label a button. */
  private readonly activateListeners = signal(0);

  /**
   * What the label renders as: a link with `href`, a button while
   * `activate` has a listener, else text.
   * @internal
   */
  readonly ɵkind = computed<'link' | 'button' | 'text'>(() =>
    this.href() !== null ? 'link' : this.activateListeners() > 0 ? 'button' : 'text',
  );

  /** The template's view of {@link ɵkind}. */
  protected readonly kind = this.ɵkind;

  constructor() {
    // A chip removed while it holds focus lets its set move focus on.
    inject(DestroyRef).onDestroy(() => this.set?.chipDestroyed(this));
  }

  /**
   * Focuses the label button, else the remove button, else the link. A
   * plain chip that is not removable has nothing to focus.
   */
  focus(): void {
    this.ɵfocusTarget()?.focus();
  }

  /**
   * The element {@link focus} moves to, if any.
   * @internal
   */
  ɵfocusTarget(): HTMLElement | null {
    return (
      this.host.querySelector<HTMLElement>('button.tm-chip__label') ??
      this.host.querySelector<HTMLElement>('.tm-chip__remove') ??
      this.host.querySelector<HTMLElement>('a.tm-chip__label')
    );
  }

  /**
   * The chip's own controls in order: the label link or button, then the
   * remove button. Projected content is not among them.
   * @internal
   */
  ɵcontrols(): HTMLElement[] {
    return Array.from(
      this.host.querySelectorAll<HTMLElement>(
        ':scope > a.tm-chip__label, :scope > button.tm-chip__label, :scope > .tm-chip__remove',
      ),
    );
  }

  /**
   * The chip's element.
   * @internal
   */
  get ɵelement(): HTMLElement {
    return this.host;
  }

  /** Delete or Backspace on one of the chip's own controls asks for removal. */
  protected onKeydown(event: KeyboardEvent): void {
    if (
      !this.removable() ||
      (event.key !== 'Delete' && event.key !== 'Backspace') ||
      event.ctrlKey ||
      event.metaKey ||
      event.altKey ||
      event.shiftKey ||
      !this.ɵcontrols().includes(event.target as HTMLElement)
    ) {
      return;
    }
    event.preventDefault();
    this.remove.emit();
  }
}
