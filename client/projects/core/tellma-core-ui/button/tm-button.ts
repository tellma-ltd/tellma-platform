// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import {
  afterNextRender,
  afterRenderEffect,
  booleanAttribute,
  computed,
  createComponent,
  DestroyRef,
  Directive,
  effect,
  ElementRef,
  EnvironmentInjector,
  inject,
  Injector,
  input,
  isDevMode,
  untracked,
  ViewContainerRef,
} from '@angular/core';
import { AriaDescriber } from '@angular/cdk/a11y';

import { ɵtmControlSize, type TmControlSize } from '@tellma/core-ui';
import { TmSpinner } from '@tellma/core-ui/spinner';
import { TmTooltip, ɵTmTooltipController } from '@tellma/core-ui/tooltip';

/** The brand-themed visual variants of {@link TmButton}. */
export type TmButtonVariant = 'primary' | 'secondary' | 'ghost' | 'danger';

/** The size scale of {@link TmButton} — the shared control size ladder. */
export type TmButtonSize = TmControlSize;

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
 * A `disabledReason` disables the button for a reason the user can act on
 * (a missing permission): the button keeps its focus, renders
 * `aria-disabled="true"` with the disabled styling, swallows activation as
 * `pending` does, and shows the reason as its tooltip on hover, on keyboard
 * focus and on a tap — the reason is also the button's accessible
 * description. Native `disabled` stays for transient states (loading, busy,
 * locks), with no tooltip; setting both is a dev-mode warning.
 *
 * On an `<a>` the directive applies the variant and the size and nothing
 * else, so the link keeps its native behaviour (Mod+click, the middle
 * button, the browser's link menu). A link cannot be disabled: render an
 * action that has a reason as a `button[tmButton]` instead — a reason on an
 * anchor is a dev-mode error.
 *
 * Icons are leading/trailing `tm-icon` elements (or inline SVGs) projected
 * as content: the button sizes them and hides them while pending. An
 * icon-only button must carry an accessible name
 * (`aria-label`/`aria-labelledby`); a dev-mode warning fires otherwise.
 *
 * @tmGroup control
 * @tmA11yNotes Native button (or link) semantics; `pending` sets
 *   `aria-busy` and `disabledReason` sets `aria-disabled`, both swallowing
 *   activation while keeping focus (never `disabled`, so the state is
 *   announced and the reason's tooltip can show); the reason is the
 *   button's accessible description; icon-only buttons need an
 *   `aria-label`.
 */
@Directive({
  selector: 'button[tmButton], a[tmButton]',
  host: {
    class: 'tm-button',
    '[class.tm-button--primary]': 'variant() === "primary"',
    '[class.tm-button--secondary]': 'variant() === "secondary"',
    '[class.tm-button--ghost]': 'variant() === "ghost"',
    '[class.tm-button--danger]': 'variant() === "danger"',
    '[class.tm-button--sm]': 'resolvedSize() === "sm"',
    '[class.tm-button--lg]': 'resolvedSize() === "lg"',
    '[class.tm-button--pending]': 'isPending()',
    '[attr.aria-busy]': 'isPending() ? "true" : null',
    '[attr.aria-disabled]': 'reason() !== null ? "true" : null',
  },
})
export class TmButton {
  private readonly element = inject<ElementRef<HTMLElement>>(ElementRef).nativeElement;
  private readonly environmentInjector = inject(EnvironmentInjector);
  /** Whether the host is a link, which takes the look and nothing else. */
  private readonly isAnchor = this.element.tagName === 'A';

  /** The visual variant. Default `secondary` — the workhorse toolbar button. */
  readonly variant = input<TmButtonVariant>('secondary');
  /**
   * The size step, mapped to the shared control size tokens. Unset, the
   * button takes the workspace default from `TM_FORM_FIELD_DEFAULTS` — so a
   * button in a toolbar lines up with the fields beside it without being
   * told twice — and the `lg` touch height below the `sm` breakpoint.
   */
  readonly size = input<TmButtonSize | undefined>(undefined);
  /** The size step the button renders at: `size`, else the responsive default. */
  protected readonly resolvedSize = ɵtmControlSize(this.size);
  /**
   * Async-action state: announces `aria-busy`, swallows click/Enter/Space
   * activation, hides the label (box size kept) and overlays a centered
   * spinner. The button is NOT disabled — focus is retained so the state
   * change is announced where the user is. Ignored on a link.
   */
  readonly pending = input(false, { transform: booleanAttribute });
  /**
   * Why the button is disabled, for a fact the user can act on (a missing
   * permission). Non-empty, the button is disabled with this reason: it
   * renders `aria-disabled="true"`, keeps focus, swallows activation, and
   * shows the reason as its tooltip and accessible description. Empty,
   * `null` or unset, the button is unaffected. Not for links.
   */
  readonly disabledReason = input<string | null | undefined>(undefined);

  /** Whether the pending state applies (never on a link). */
  protected readonly isPending = computed(() => !this.isAnchor && this.pending());
  /** The reason in force: a non-blank reason on a button, else null. */
  protected readonly reason = computed(() => {
    const reason = this.disabledReason();
    return !this.isAnchor && typeof reason === 'string' && reason.trim() !== '' ? reason : null;
  });

  /**
   * Capture-phase activation guard: registered in the constructor, so it
   * runs before any consumer `(click)` handler on the button or its
   * projected content — Enter/Space arrive here too (a native button
   * synthesizes a click), and `preventDefault` also stops an explicit
   * `type="submit"` from submitting while pending or disabled with a
   * reason. The listener lives and dies with the element.
   */
  private readonly suppressActivation = (event: Event): void => {
    if (untracked(this.isPending) || untracked(this.reason) !== null) {
      event.preventDefault();
      event.stopImmediatePropagation();
    }
  };

  constructor() {
    // Host-bind the authored `type` when present, else default to "button":
    // an attribute binding would race an authored static type, so the
    // default is applied imperatively, exactly once, only when unauthored.
    // A link has no type.
    if (!this.isAnchor && !this.element.hasAttribute('type')) {
      this.element.setAttribute('type', 'button');
    }

    this.element.addEventListener('click', this.suppressActivation, { capture: true });

    // The pending spinner overlays lazily: nothing is created (or shipped
    // into the render tree) until the first pending=true.
    effect((onCleanup) => {
      if (!this.isPending()) {
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

    this.showReasonAsTooltip();

    if (isDevMode()) {
      this.checkInDevMode();
    }
  }

  /**
   * The reason's tooltip. A `tmTooltip` on the same button shows the
   * reason in place of its own text while one is set; otherwise the button
   * runs a tooltip of its own, created on the first reason, so a button
   * that never has one pays for nothing. Either way a tap shows it, since
   * the tap does nothing else.
   */
  private showReasonAsTooltip(): void {
    const tooltip = inject(TmTooltip, { self: true, optional: true });
    if (tooltip !== null) {
      effect(() => tooltip.ɵreason.set(this.reason()));
      return;
    }
    const injector = inject(Injector);
    const describer = inject(AriaDescriber);
    let controller: ɵTmTooltipController | null = null;
    effect(() => {
      const reason = this.reason();
      untracked(() => {
        if (reason === null) {
          controller?.destroy();
          controller = null;
          this.element.classList.remove('tm-tooltip-host');
          return;
        }
        if (controller === null) {
          // The view container is resolved only when the surface first
          // shows, so a button never shown a reason carries no container.
          controller = new ɵTmTooltipController(
            this.element,
            () => injector.get(ViewContainerRef),
            describer,
          );
          controller.showOnTap = true;
          // The tooltip harnesses and styles key on this class.
          this.element.classList.add('tm-tooltip-host');
        }
        controller.setText(reason);
      });
    });
    inject(DestroyRef).onDestroy(() => controller?.destroy());
  }

  /** The development-mode checks: an icon-only button's name, and misused reasons. */
  private checkInDevMode(): void {
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
    // After the render, so a [disabled] binding set beside the reason has
    // reached the element.
    afterRenderEffect(() => {
      const reason = this.disabledReason();
      if (typeof reason !== 'string' || reason.trim() === '') {
        return;
      }
      if (this.isAnchor) {
        console.error(
          'tmButton: a link cannot be disabled, so its disabledReason is ignored — render the ' +
            'action as a button[tmButton] while it has a reason.',
          this.element,
        );
      } else if ((this.element as HTMLButtonElement).disabled) {
        console.warn(
          'tmButton: both disabled and disabledReason are set — a natively disabled button takes ' +
            'no focus and shows no tooltip. Use disabled for transient states and disabledReason ' +
            'for a reason the user can act on.',
          this.element,
        );
      }
    });
  }
}
