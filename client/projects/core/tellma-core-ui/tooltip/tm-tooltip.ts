// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import {
  booleanAttribute,
  Directive,
  effect,
  ElementRef,
  inject,
  input,
  type OnDestroy,
  signal,
  untracked,
  ViewContainerRef,
} from '@angular/core';
import { AriaDescriber } from '@angular/cdk/a11y';

import { ɵTmTooltipController } from './internal/tm-tooltip-controller';

/**
 * A plain-text tooltip on any element. Interactive or rich content belongs
 * in `tm-popover` — a tooltip must never contain focusables.
 *
 * Shows on pointer hover (after the `--tooltip-delay` token delay) and on
 * `:focus-visible` (immediately); hides on pointer leave, blur, and Escape
 * (WCAG 1.4.13 dismissable). The surface itself is hoverable — moving the
 * pointer onto it does not dismiss (1.4.13 hoverable/persistent). On touch,
 * a long-press shows it until the next tap. At most one tooltip is visible
 * at a time. The text is registered as the host's accessible description
 * (CDK `AriaDescriber`), so assistive technology reads it whether or not
 * the tooltip has ever opened — tooltips stay a progressive enhancement,
 * and content required for operation must not live only in a tooltip.
 *
 * Known limitation: natively `disabled` controls fire no pointer events in
 * most engines, so a tooltip on a disabled button never shows on hover.
 * Give a `tmButton` a `disabledReason` instead: it stays focusable,
 * swallows activation, and shows the reason as its tooltip.
 *
 * @tmGroup overlay
 * @tmA11yNotes The panel is `role="tooltip"` and `aria-hidden` (the CDK
 *   AriaDescriber description carries the text to AT); never
 *   focus-stealing; Escape dismisses without side effects.
 */
@Directive({
  selector: '[tmTooltip]',
  host: {
    // A bound [tmTooltip] input leaves no DOM attribute — the class is the
    // stable hook harnesses and styles can rely on.
    class: 'tm-tooltip-host',
  },
})
export class TmTooltip implements OnDestroy {
  /** The tooltip text (plain text only). */
  readonly text = input.required<string>({ alias: 'tmTooltip' });

  /**
   * A touch tap on the host shows the tooltip, and the next tap elsewhere
   * hides it. For a host whose tap does nothing else; a tooltip otherwise
   * opens on touch only by long-press.
   * @internal
   */
  readonly ɵshowOnTap = input(false, { transform: booleanAttribute });

  /**
   * A disabled reason a `tmButton` on the same host shows here while set:
   * the surface shows the tooltip text and then the reason on its own line
   * (an icon-only button keeps its only visible label), the reason joins
   * the host's description, and a tap also opens the tooltip.
   * @internal
   */
  readonly ɵreason = signal<string | null>(null);

  /** The tooltip behaviour this directive drives from its inputs. */
  private readonly controller = new ɵTmTooltipController(
    inject<ElementRef<HTMLElement>>(ElementRef).nativeElement,
    () => this.viewContainer,
    inject(AriaDescriber),
  );
  /** Where the surface is created, next to the host. */
  private readonly viewContainer = inject(ViewContainerRef);

  constructor() {
    // The description is available to AT from the start — no opening
    // needed. Re-registered whenever the shown text changes.
    effect(() => {
      const text = this.text();
      const reason = this.ɵreason();
      untracked(() =>
        reason === null
          ? this.controller.setText(text)
          : this.controller.setText(`${text}\n${reason}`, [text, reason]),
      );
    });
    effect(() => {
      this.controller.showOnTap = this.ɵshowOnTap() || this.ɵreason() !== null;
    });
  }

  /** Tears down the surface, timers, listeners, and the ARIA description. */
  ngOnDestroy(): void {
    this.controller.destroy();
  }
}
