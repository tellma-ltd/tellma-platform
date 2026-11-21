// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import { NgTemplateOutlet } from '@angular/common';
import { booleanAttribute, Component, computed, inject, input } from '@angular/core';
import { User } from 'lucide';

import { TmL10n } from '@tellma/core-ui';
import { TmIcon } from '@tellma/core-ui/icon';
import { TmImage, TmImagePlaceholder } from '@tellma/core-ui/image';

import { tmAvatarTone, tmInitials } from './tm-initials';

/** The avatar diameters: 20, 24, 28, 40 and 72 px. */
export type TmAvatarSize = 'xs' | 'sm' | 'md' | 'lg' | 'xl';

/**
 * Each size's diameter in px — the `--avatar-size-*` token values, which
 * size the circle in CSS. `tm-image` needs the box as numbers to request
 * the right rendition; a unit test pins the two together.
 */
const DIAMETER_PX: Readonly<Record<TmAvatarSize, number>> = {
  xs: 20,
  sm: 24,
  md: 28,
  lg: 40,
  xl: 72,
};

/**
 * A person as a round photo, or as their initials on a tinted circle when
 * there is no photo, while it loads, and when it fails. A stable
 * `colorKey` picks one of eight tints, so two people with the same
 * initials usually differ; without one, the circle is the accent teal.
 *
 * Initials come from `tmInitials` in the active locale: two letters for a
 * name in a cased script ("FN"), one for an uncased script ("ف"), and a
 * person glyph for an empty name. Pass a record key such as
 * `core.User:17` as the `colorKey`, never an email address.
 *
 * @tmGroup media
 * @tmA11yNotes `role="img"` named by `name`; set `decorative` when the
 *   name is printed beside the avatar, which hides it from assistive
 *   technology. The photo itself is unnamed, so nothing is read twice.
 *   Under forced colours the circle keeps a border in the system text
 *   colour and the initials take the system text colour.
 */
@Component({
  selector: 'tm-avatar',
  imports: [NgTemplateOutlet, TmIcon, TmImage, TmImagePlaceholder],
  // With a photo, the face (initials or the glyph) is tm-image's placeholder,
  // shown while it loads and when it fails; without one, it is the avatar.
  template: `
    @if (src(); as url) {
      <tm-image
        class="tm-avatar__image"
        [src]="url"
        [etag]="etag()"
        alt=""
        shape="circle"
        [width]="diameter()"
        [height]="diameter()"
        placeholderOnError
      >
        <ng-template tmImagePlaceholder>
          <ng-container [ngTemplateOutlet]="face" />
        </ng-template>
      </tm-image>
    } @else {
      <ng-container [ngTemplateOutlet]="face" />
    }
    <ng-template #face>
      <span class="tm-avatar__face">
        @if (initials(); as text) {
          {{ text }}
        } @else {
          <tm-icon [svg]="personGlyph" />
        }
      </span>
    </ng-template>
  `,
  styleUrl: './tm-avatar.css',
  host: {
    class: 'tm-avatar',
    '[attr.data-size]': 'size()',
    '[attr.data-tone]': 'tone()',
    '[attr.role]': 'named() ? "img" : null',
    '[attr.aria-label]': 'named() ? name() : null',
    '[attr.aria-hidden]': 'named() ? null : "true"',
  },
})
export class TmAvatar {
  private readonly l10n = inject(TmL10n);

  /** The person's name: the initials' source and the accessible name. */
  readonly name = input.required<string>();

  /** The photo's `tm-image` URL; `null` (or empty) shows the initials. */
  readonly src = input<string | null>(null);

  /** The photo's entity tag, passed to `tm-image` with `src`. */
  readonly etag = input<string | null>(null);

  /**
   * An opaque, stable key that picks the tint, such as the record key
   * `core.User:17`; `null` gives the accent tone.
   */
  readonly colorKey = input<string | null>(null);

  /** The diameter. Default `sm` (24 px). */
  readonly size = input<TmAvatarSize>('sm');

  /**
   * Set when the name is printed beside the avatar: the avatar is then
   * hidden from assistive technology, so the name is not read twice.
   */
  readonly decorative = input(false, { transform: booleanAttribute });

  /** The person glyph an empty name shows (the library's own data). */
  protected readonly personGlyph = User;

  /** The initials in the active locale; `''` shows the person glyph. */
  protected readonly initials = computed(() => tmInitials(this.name(), this.l10n.locale()));

  /** The categorical slot the key picks, or `null` for the accent tone. */
  protected readonly tone = computed(() => {
    const key = this.colorKey();
    return key === null || key === '' ? null : tmAvatarTone(key);
  });

  /** The diameter in px, for the photo's rendition. */
  protected readonly diameter = computed(() => DIAMETER_PX[this.size()]);

  /**
   * Whether the avatar is an image named by the person's name: not when it
   * is decorative, and not for a blank name, which has nothing to say.
   */
  protected readonly named = computed(() => !this.decorative() && this.name().trim() !== '');
}
