import { Component } from '@angular/core';

import { ProbeSelect } from './probe-select';

/**
 * Story host for the §3.4 risk spike:
 * - "clipped": the trigger sits inside a short overflow:hidden ancestor — the
 *   usePopover:'inline' top-layer panel must escape the clip.
 * - "flip": the trigger is pinned near the viewport bottom — the panel must
 *   flip above the trigger (requires the updatePosition()-on-attach fix).
 */
@Component({
  imports: [ProbeSelect],
  template: `
    <div class="clipbox" data-testid="clipbox">
      <sandbox-probe-select testid="clipped" />
    </div>

    <div class="flip-anchor">
      <sandbox-probe-select testid="flip" />
    </div>
  `,
  styles: `
    .clipbox {
      block-size: 60px;
      inline-size: 320px;
      overflow: hidden;
      border: 1px dashed #a8b7bc;
    }
    .flip-anchor {
      position: fixed;
      inset-block-end: 8px;
      inset-inline-start: 24px;
    }
  `,
})
export class OverlayProbeStory {}
