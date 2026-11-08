// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import { Component, signal } from '@angular/core';
import { Check } from 'lucide';

import { TmButton } from '@tellma/core-ui/button';
import { TmIcon } from '@tellma/core-ui/icon';

import { SHOWCASE_ICONS } from './showcase-icons';

/**
 * tm-icon demo host: every registered name, the size ladder, mirroring
 * (with a dir="ltr" island), the stroke token, a labelled icon, an unknown
 * name's empty box, direct data, and icons inserted on demand for the
 * first-frame check.
 */
@Component({
  imports: [TmButton, TmIcon],
  template: `
    <h2>Icon</h2>

    <!-- Every registry name the showcase registers, captioned. -->
    <section>
      <h3>Registry names</h3>
      <ul class="gallery" data-testid="gallery">
        @for (name of names; track name) {
          <li class="gallery__cell">
            <tm-icon [name]="name" />
            <code>{{ name }}</code>
          </li>
        }
      </ul>
    </section>

    <!-- The ladder, against a container-sized default. -->
    <section>
      <h3>Sizes</h3>
      <div class="row" data-testid="sizes">
        <tm-icon name="hand-coins" size="xs" data-testid="size-xs" />
        <tm-icon name="hand-coins" size="sm" data-testid="size-sm" />
        <tm-icon name="hand-coins" size="md" data-testid="size-md" />
        <tm-icon name="hand-coins" size="lg" data-testid="size-lg" />
        <tm-icon name="hand-coins" size="xl" data-testid="size-xl" />
        <span class="container-sized" data-testid="container-sized">
          <tm-icon name="hand-coins" /> a container at 28px
        </span>
      </div>
    </section>

    <!-- Directional glyphs flip in RTL; the island keeps its own direction. -->
    <section>
      <h3>Mirroring</h3>
      <div class="row" data-testid="mirror-row">
        <tm-icon name="chevron-right" data-testid="mirrored" />
        <tm-icon name="arrow-right" />
        <tm-icon name="undo" />
        <tm-icon name="log-out" />
        <tm-icon name="x" data-testid="unmirrored" />
        <tm-icon name="search" />
        <span dir="ltr" class="island">
          dir="ltr" island: <tm-icon name="chevron-right" data-testid="island" />
        </span>
      </div>
    </section>

    <!-- The stroke token, and a host that raises it. -->
    <section>
      <h3>Stroke</h3>
      <div class="row">
        <tm-icon name="check" size="xl" data-testid="stroke-default" />
        <tm-icon name="check" size="xl" class="heavy" data-testid="stroke-heavy" />
      </div>
    </section>

    <!-- Meaningful on its own: role="img" with the label. -->
    <section>
      <h3>Labelled</h3>
      <p class="row">
        Posting <tm-icon name="circle-alert" label="Has errors" data-testid="labelled" />
      </p>
    </section>

    <!-- An unknown name keeps the box (outlined here to show it). -->
    <section>
      <h3>Unknown name</h3>
      <div class="row">
        <tm-icon name="not-registered" class="outlined" data-testid="unknown" />
        <tm-icon name="plus" class="outlined" data-testid="known" />
      </div>
    </section>

    <!-- Library code passes Lucide data directly. -->
    <section>
      <h3>Direct data</h3>
      <div class="row">
        <tm-icon [svg]="checkIcon" data-testid="direct" />
      </div>
    </section>

    <!-- Controls created on demand draw their glyph in their first frame. -->
    <section>
      <h3>Inserted on demand</h3>
      <button
        tmButton
        variant="secondary"
        data-testid="insert-icons"
        (click)="inserted.set(!inserted())"
      >
        {{ inserted() ? 'Remove' : 'Insert' }} rows
      </button>
      @if (inserted()) {
        <ul class="rows" data-testid="inserted">
          @for (row of rows; track row) {
            <li class="row"><tm-icon name="chevron-right" /> Row {{ row }}</li>
          }
        </ul>
      }
    </section>
  `,
  styles: `
    .gallery {
      display: grid;
      grid-template-columns: repeat(auto-fill, minmax(132px, 1fr));
      gap: 8px;
      padding: 0;
      list-style: none;
    }
    .gallery__cell {
      display: flex;
      align-items: center;
      gap: 8px;
      font-size: 12px;
    }
    .row {
      display: flex;
      align-items: center;
      gap: 12px;
      flex-wrap: wrap;
    }
    .rows {
      padding: 0;
      list-style: none;
    }
    .container-sized {
      --icon-size: 28px;
    }
    .island {
      display: inline-flex;
      align-items: center;
      gap: 6px;
    }
    .heavy {
      --icon-stroke-width: 2.25;
    }
    .outlined {
      outline: 1px dashed var(--border-strong);
    }
  `,
})
export class IconStory {
  protected readonly names = Object.keys(SHOWCASE_ICONS);
  protected readonly checkIcon = Check;
  protected readonly rows = Array.from({ length: 20 }, (_, index) => index + 1);
  protected readonly inserted = signal(false);
}
