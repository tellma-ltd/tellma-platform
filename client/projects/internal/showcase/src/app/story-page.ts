import { NgComponentOutlet } from '@angular/common';
import { Component, DOCUMENT, computed, effect, inject } from '@angular/core';
import { toSignal } from '@angular/core/rxjs-interop';
import { ActivatedRoute } from '@angular/router';

import { SHOWCASE_STORIES } from './stories';

/**
 * Renders the story named by the :id route param and applies the ?dir and
 * ?theme query params to the document root, so every story is addressable in
 * all four dir x theme combinations without per-story wiring.
 */
@Component({
  imports: [NgComponentOutlet],
  template: `
    @let s = story();
    @if (s) {
      <main class="story-main" [attr.data-story]="s.id">
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
  `,
})
export class StoryPage {
  private readonly route = inject(ActivatedRoute);
  private readonly document = inject(DOCUMENT);

  private readonly params = toSignal(this.route.paramMap);
  private readonly queryParams = toSignal(this.route.queryParamMap);

  protected readonly storyId = computed(() => this.params()?.get('id') ?? '');
  protected readonly story = computed(() =>
    SHOWCASE_STORIES.find((s) => s.id === this.storyId()),
  );

  constructor() {
    effect(() => {
      const query = this.queryParams();
      const root = this.document.documentElement;
      root.dir = query?.get('dir') === 'rtl' ? 'rtl' : 'ltr';
      const theme = query?.get('theme');
      if (theme === 'dark') {
        root.setAttribute('data-theme', 'dark');
      } else {
        root.removeAttribute('data-theme');
      }
    });
  }
}
