import { Component } from '@angular/core';
import { RouterLink } from '@angular/router';

import { SANDBOX_STORIES } from './stories';

/** Index page listing every registered story. */
@Component({
  imports: [RouterLink],
  template: `
    <main class="index-main">
      <h1>Tellma UI sandbox</h1>
      <ul>
        @for (story of stories; track story.id) {
          <li><a [routerLink]="['/story', story.id]">{{ story.title }}</a></li>
        }
      </ul>
    </main>
  `,
  styles: `
    .index-main {
      padding: 24px;
    }
  `,
})
export class StoryIndex {
  protected readonly stories = SANDBOX_STORIES;
}
