// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import { NgComponentOutlet } from '@angular/common';
import { Component, computed, inject } from '@angular/core';
import { toSignal } from '@angular/core/rxjs-interop';
import { ActivatedRoute } from '@angular/router';

import { SHOWCASE_STORIES } from './stories';

/**
 * Renders the story named by the :id route param. The ?dir/?theme query
 * params that make every story addressable in all four combinations are
 * applied to the document root by the app shell.
 */
@Component({
  imports: [NgComponentOutlet],
  template: `
    @let s = story();
    @if (s) {
      <main class="story-main" [class.story-main--wide]="s.wide" [attr.data-story]="s.id">
        <ng-container [ngComponentOutlet]="s.component" />
      </main>
    } @else {
      <p role="alert">Unknown story: {{ storyId() }}</p>
    }
  `,
  styles: `
    .story-main {
      padding: 24px;
      max-inline-size: 720px;
    }
    .story-main--wide {
      max-inline-size: none;
    }
  `,
})
export class StoryPage {
  private readonly route = inject(ActivatedRoute);

  private readonly params = toSignal(this.route.paramMap);

  protected readonly storyId = computed(() => this.params()?.get('id') ?? '');
  protected readonly story = computed(() =>
    SHOWCASE_STORIES.find((s) => s.id === this.storyId()),
  );
}
