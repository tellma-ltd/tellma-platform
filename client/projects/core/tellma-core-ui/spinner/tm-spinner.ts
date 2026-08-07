// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import { Component } from '@angular/core';

/**
 * The shared pending/progress spinner: a decorative, `currentColor` ring
 * sized by the spacing tokens, spinning on the host so consumers (and
 * tests) can observe or restyle the animation there. Hidden from assistive
 * technology — the pending SEMANTIC belongs to the busy control
 * (`aria-busy`), never to this glyph.
 *
 * @tmGroup indicator
 * @tmA11yNotes Purely decorative (aria-hidden); the busy control carries
 *   aria-busy. The animation collapses under prefers-reduced-motion.
 */
@Component({
  selector: 'tm-spinner',
  template: `
    <!-- A full track plus the quarter arc that turns on it. Both colors come
         from CSS, not presentation attributes, so a host can re-point either
         one — a spinner on a filled button needs a track that reads against
         the fill, not against the page. -->
    <svg viewBox="0 0 16 16" fill="none" stroke-width="2">
      <circle class="tm-spinner__track" cx="8" cy="8" r="7" />
      <path class="tm-spinner__arc" d="M8 1 A 7 7 0 0 1 15 8" stroke-linecap="round" />
    </svg>
  `,
  styleUrl: './tm-spinner.css',
  host: { 'aria-hidden': 'true' },
})
export class TmSpinner {}
