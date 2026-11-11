// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import { booleanAttribute, Component, input } from '@angular/core';

import { TmIcon } from '@tellma/core-ui/icon';

/**
 * The colour tones of a {@link TmBadge}: a quiet `neutral`, the solid brand
 * `accent`, and the four status tones.
 */
export type TmBadgeTone = 'neutral' | 'accent' | 'info' | 'success' | 'warning' | 'error';

/**
 * A status label in a colour tone: "Active", "Held", "New". The projected
 * content is the text; an optional leading icon (a registry name) or a
 * 6px leading dot in the tone's ink sits before it.
 *
 * The status tones tint the pill and keep body ink for the text, so the
 * words stay readable at 4.5:1 in every tone; `accent` is the one solid
 * badge. The tone is colour, so the text must carry the meaning on its
 * own. A long label ellipsizes when its container is narrower.
 *
 * @tmGroup indicator
 * @tmA11yNotes Plain inline text, no role: the label is read where it
 *   sits. The dot and the icon are decorative. Forced colours keep the
 *   pill's border and the dot.
 */
@Component({
  selector: 'tm-badge',
  imports: [TmIcon],
  // The optional, decorative leading marks, then the label. (Notes live
  // here rather than in the markup: the template ships in the bundle.)
  template: `
    @if (dot()) {
      <span class="tm-badge__dot" aria-hidden="true"></span>
    }
    @if (icon(); as name) {
      <tm-icon class="tm-badge__icon" [name]="name" />
    }
    <span class="tm-badge__label"><ng-content /></span>
  `,
  styleUrl: './tm-badge.css',
  host: {
    class: 'tm-badge',
    '[attr.data-tone]': 'tone()',
  },
})
export class TmBadge {
  /** The colour tone. Default `neutral`. */
  readonly tone = input<TmBadgeTone>('neutral');

  /** A leading icon, by registry name (`'circle-check'`); unset, none. */
  readonly icon = input<string | undefined>(undefined);

  /** Draws a 6px leading dot in the tone's ink. */
  readonly dot = input(false, { transform: booleanAttribute });
}
