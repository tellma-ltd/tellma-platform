// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import {
  booleanAttribute,
  DestroyRef,
  Directive,
  effect,
  ElementRef,
  inject,
  input,
  untracked,
  ViewContainerRef,
} from '@angular/core';
import { AriaDescriber } from '@angular/cdk/a11y';

import { ɵTmTooltipController } from '@tellma/core-ui/tooltip';

/**
 * A tooltip that exists only while `active`: an icon-only segment shows its
 * label as a tooltip, and a segment showing its label carries none (the
 * text is on screen already). The behaviour is the shared tooltip
 * controller's, created and torn down with the state, so a labelled
 * segment pays for no listeners.
 *
 * @internal
 */
@Directive({
  selector: '[tmSegmentTooltip]',
})
export class ɵTmSegmentTooltip {
  /** The tooltip text. */
  readonly text = input.required<string>({ alias: 'tmSegmentTooltip' });
  /** Whether the tooltip exists. */
  readonly active = input(false, { alias: 'tmSegmentTooltipActive', transform: booleanAttribute });

  private readonly host = inject<ElementRef<HTMLElement>>(ElementRef).nativeElement;
  private readonly viewContainer = inject(ViewContainerRef);
  private readonly describer = inject(AriaDescriber);
  /** The live controller while active. */
  private controller: ɵTmTooltipController | null = null;

  constructor() {
    effect(() => {
      const text = this.text();
      const active = this.active();
      untracked(() => {
        if (!active) {
          this.controller?.destroy();
          this.controller = null;
          return;
        }
        this.controller ??= new ɵTmTooltipController(
          this.host,
          () => this.viewContainer,
          this.describer,
        );
        this.controller.setText(text);
      });
    });
    inject(DestroyRef).onDestroy(() => this.controller?.destroy());
  }
}
