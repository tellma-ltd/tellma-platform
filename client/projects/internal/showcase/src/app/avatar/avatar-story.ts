// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import { Component } from '@angular/core';

import { TmAvatar, type TmAvatarSize } from '@tellma/core-ui/avatar';

/**
 * tm-avatar at every size, in every keyed tone and the accent tone, with
 * the initials of Latin, Arabic, Ethiopic and CJK names, the glyph for an
 * empty name, a photo, a failing photo, and a decorative avatar beside a
 * printed name.
 */
@Component({
  imports: [TmAvatar],
  template: `
    <h2>Avatars</h2>

    <!-- Every size, accent tone. -->
    <section class="row" data-testid="sizes">
      @for (size of sizes; track size) {
        <tm-avatar name="Farah Nasser" [size]="size" [attr.data-testid]="'size-' + size" />
      }
    </section>

    <!-- One record key per categorical tone, 1 to 8. -->
    <section class="row" data-testid="tones">
      @for (key of toneKeys; track key; let index = $index) {
        <tm-avatar
          name="Farah Nasser"
          [colorKey]="key"
          size="md"
          [attr.data-testid]="'tone-' + (index + 1)"
        />
      }
    </section>

    <!-- Scripts: two initials when cased, one when not; the glyph when empty. -->
    <section class="row" data-testid="scripts">
      <tm-avatar name="Farah Nasser" colorKey="core.User:17" size="lg" data-testid="latin" />
      <tm-avatar name="فرح ناصر" colorKey="core.User:18" size="lg" data-testid="arabic" />
      <tm-avatar name="ተስፋዬ ገብረ" colorKey="core.User:2" size="lg" data-testid="ethiopic" />
      <tm-avatar name="王小明" colorKey="core.User:3" size="lg" data-testid="cjk" />
      <tm-avatar name="" size="lg" data-testid="empty-name" />
    </section>

    <!-- A photo, and a photo that fails and falls back to the initials. -->
    <section class="row" data-testid="photos">
      <tm-avatar
        name="Lina Hakim"
        src="/test-image.png"
        colorKey="core.User:5"
        size="xl"
        data-testid="photo"
      />
      <tm-avatar
        name="Lina Hakim"
        src="/missing-avatar-story.png"
        colorKey="core.User:5"
        size="xl"
        data-testid="photo-failed"
      />
    </section>

    <!-- Beside a printed name the avatar is decorative. -->
    <section class="row">
      <span class="person" data-testid="decorative-row">
        <tm-avatar name="Farah Nasser" colorKey="core.User:17" size="xs" decorative />
        Farah Nasser
      </span>
    </section>
  `,
  styles: `
    .row {
      display: flex;
      flex-wrap: wrap;
      align-items: center;
      gap: 16px;
      margin-block-end: 16px;
    }
    .person {
      display: inline-flex;
      align-items: center;
      gap: 8px;
    }
  `,
})
export class AvatarStory {
  /** Every avatar size, smallest first. */
  protected readonly sizes: readonly TmAvatarSize[] = ['xs', 'sm', 'md', 'lg', 'xl'];

  /** A record key for each tone: the n-th key hashes to tone n. */
  protected readonly toneKeys: readonly string[] = [
    'core.User:3',
    'core.User:8',
    'core.User:5',
    'core.User:2',
    'core.User:7',
    'core.User:4',
    'core.User:1',
    'core.User:6',
  ];
}
