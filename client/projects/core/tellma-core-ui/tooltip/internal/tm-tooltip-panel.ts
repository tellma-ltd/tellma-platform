// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import { afterRenderEffect, Component, signal, untracked, viewChild } from '@angular/core';
import { CdkConnectedOverlay, OverlayModule } from '@angular/cdk/overlay';

import { tmCreateAnchoredOverlay, tmLogicalPositions } from '@tellma/core-ui/private';

/**
 * The floating tooltip surface, created lazily by the `TmTooltip` directive
 * on first show and driven through its writable signals. The panel is
 * `aria-hidden`: assistive technology reads the text through the CDK
 * `AriaDescriber` description instead (always available, open or not), so
 * the visible surface never double-announces.
 *
 * The surface itself stays hoverable (WCAG 1.4.13): pointer transitions
 * onto it are reported back to the directive, which cancels the pending
 * grace-period hide.
 *
 * @internal Created exclusively by `TmTooltip`; never use directly.
 */
@Component({
  selector: 'tm-tooltip-panel',
  imports: [OverlayModule],
  template: `
    <ng-template
      [cdkConnectedOverlay]="anchored.overlayConfig()"
      [cdkConnectedOverlayOpen]="expanded()"
      (attach)="anchored.handleAttach()"
      (detach)="anchored.handleDetach()"
    >
      <div
        class="tm-tooltip__panel"
        role="tooltip"
        aria-hidden="true"
        (pointerenter)="pointerEnter?.()"
        (pointerleave)="pointerLeave?.()"
      >
        {{ text() }}
      </div>
    </ng-template>
  `,
  styleUrl: './tm-tooltip-panel.css',
  host: { class: 'tm-tooltip' },
})
export class ɵTmTooltipPanel {
  /** The tooltip text. */
  readonly text = signal('');
  /** The host element the panel anchors to. */
  readonly origin = signal<Element | null>(null);
  /** Whether the panel is shown. */
  readonly expanded = signal(false);

  /** Set by the directive: pointer entered the surface (cancel the hide). */
  pointerEnter: (() => void) | null = null;
  /** Set by the directive: pointer left the surface (schedule the hide). */
  pointerLeave: (() => void) | null = null;

  private readonly overlay = viewChild(CdkConnectedOverlay);

  /** Above-centered with flip; the visual gap is the panel's own margin. */
  protected readonly anchored = tmCreateAnchoredOverlay({
    overlay: () => this.overlay(),
    origin: () => this.origin(),
    positions: tmLogicalPositions('block-start', 'center'),
    // A tooltip is centered on its host by preference, not by meaning, so
    // near a window edge it slides back into view rather than being
    // clipped — the first words are the ones that matter most.
    keepOnScreen: true,
    viewportMargin: 8,
    remeasure: 'none',
  });

  constructor() {
    // Text swapped while the surface is up: the CDK positioned the pane
    // from its OLD box and writes an exact inline offset, so the centered
    // placement stays pinned to the former center and the longer text
    // spills to one side — off-center, and able to run past the viewport
    // edge. Re-anchor once the new text has actually rendered.
    afterRenderEffect(() => {
      this.text();
      if (!this.expanded()) {
        return;
      }
      untracked(() => this.anchored.reanchor());
    });
  }
}
