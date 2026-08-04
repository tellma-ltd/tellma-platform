// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import {
  afterNextRender,
  booleanAttribute,
  createComponent,
  Directive,
  effect,
  ElementRef,
  EnvironmentInjector,
  inject,
  input,
  isDevMode,
  untracked,
} from '@angular/core';

import { TmSpinner } from '@tellma/core-ui/spinner';

/** The brand-themed visual variants of {@link TmButton}. */
export type TmButtonVariant = 'primary' | 'secondary' | 'ghost' | 'danger';

/** The height scale of {@link TmButton} — aligned with the form-field heights. */
export type TmButtonSize = 'sm' | 'md' | 'lg';

/**
 * Brand-themed button — a directive on the native `<button>`, which stays
 * the control (semantics, focus, activation, and forms participation for
 * free). Heights map to the form-field height tokens so buttons align with
 * fields in toolbars.
 *
 * A native button inside a `<form>` defaults to `type="submit"` — a
 * recurring footgun on data-entry screens — so the directive defaults an
 * unauthored `type` to `"button"`; submit buttons opt in explicitly with
 * `type="submit"`.
 *
 * The `pending` state (async actions — Save, Post) suppresses activation
 * without disabling: focus is retained, the state change is announced via
 * `aria-busy`, the label is hidden with the box keeping its exact size, and
 * a centered spinner overlays the content.
 *
 * Icons are leading/trailing inline SVGs projected as content, marked
 * `aria-hidden`. An icon-only button must carry an accessible name
 * (`aria-label`/`aria-labelledby`); a dev-mode warning fires otherwise.
 *
 * @tmGroup control
 * @tmA11yNotes Native button semantics; `pending` sets `aria-busy` and
 *   swallows activation while keeping focus (never `disabled`, so the
 *   state change is announced); icon-only buttons need an `aria-label`.
 */
@Directive({
  selector: 'button[tmButton]',
  host: {
    class: 'tm-button',
    '[class.tm-button--primary]': 'variant() === "primary"',
    '[class.tm-button--secondary]': 'variant() === "secondary"',
    '[class.tm-button--ghost]': 'variant() === "ghost"',
    '[class.tm-button--danger]': 'variant() === "danger"',
    '[class.tm-button--sm]': 'size() === "sm"',
    '[class.tm-button--lg]': 'size() === "lg"',
    '[class.tm-button--pending]': 'pending()',
    '[attr.aria-busy]': 'pending() ? "true" : null',
  },
})
export class TmButton {
  private readonly element = inject<ElementRef<HTMLButtonElement>>(ElementRef).nativeElement;
  private readonly environmentInjector = inject(EnvironmentInjector);

  /** The visual variant. Default `secondary` — the workhorse toolbar button. */
  readonly variant = input<TmButtonVariant>('secondary');
  /** The height scale, mapped to the form-field height tokens. Default `md`. */
  readonly size = input<TmButtonSize>('md');
  /**
   * Async-action state: announces `aria-busy`, swallows click/Enter/Space
   * activation, hides the label (box size kept) and overlays a centered
   * spinner. The button is NOT disabled — focus is retained so the state
   * change is announced where the user is.
   */
  readonly pending = input(false, { transform: booleanAttribute });

  /**
   * Capture-phase activation guard: registered in the constructor, so it
   * runs before any consumer `(click)` handler on the button or its
   * projected content — Enter/Space arrive here too (a native button
   * synthesizes a click), and `preventDefault` also stops an explicit
   * `type="submit"` from submitting while pending. The listener lives and
   * dies with the element.
   */
  private readonly suppressWhilePending = (event: Event): void => {
    if (untracked(this.pending)) {
      event.preventDefault();
      event.stopImmediatePropagation();
    }
  };

  constructor() {
    // Host-bind the authored `type` when present, else default to "button":
    // an attribute binding would race an authored static type, so the
    // default is applied imperatively, exactly once, only when unauthored.
    if (!this.element.hasAttribute('type')) {
      this.element.setAttribute('type', 'button');
    }

    this.element.addEventListener('click', this.suppressWhilePending, { capture: true });

    // The pending spinner overlays lazily: nothing is created (or shipped
    // into the render tree) until the first pending=true.
    effect((onCleanup) => {
      if (!this.pending()) {
        return;
      }
      const spinner = createComponent(TmSpinner, {
        environmentInjector: this.environmentInjector,
      });
      const spinnerElement = spinner.location.nativeElement as HTMLElement;
      spinnerElement.classList.add('tm-button__spinner');
      this.element.appendChild(spinnerElement);
      // The spinner is static (no bindings) — one detection pass renders it;
      // it is never attached to the application ref.
      spinner.changeDetectorRef.detectChanges();
      onCleanup(() => {
        spinner.destroy();
        spinnerElement.remove();
      });
    });

    if (isDevMode()) {
      afterNextRender(() => {
        const hasText = (this.element.textContent ?? '').trim() !== '';
        const hasName =
          this.element.hasAttribute('aria-label') || this.element.hasAttribute('aria-labelledby');
        if (!hasText && !hasName) {
          console.warn(
            'tmButton: icon-only button without an accessible name — add aria-label or aria-labelledby.',
            this.element,
          );
        }
      });
    }
  }
}
