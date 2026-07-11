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
    <svg viewBox="0 0 16 16" fill="none">
      <circle cx="8" cy="8" r="6.5" stroke="currentColor" stroke-opacity="0.25" />
      <path d="M8 1.5 A 6.5 6.5 0 0 1 14.5 8" stroke="currentColor" stroke-linecap="round" />
    </svg>
  `,
  styleUrl: './tm-spinner.css',
  host: { 'aria-hidden': 'true' },
})
export class TmSpinner {}
