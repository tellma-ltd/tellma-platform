// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import { Component } from '@angular/core';
import { RouterLink } from '@angular/router';

import { SHOWCASE_STORIES } from './stories';

/** Index page listing every registered story. */
@Component({
  imports: [RouterLink],
  template: `
    <main class="index-main">
      <h1>Tellma UI showcase</h1>
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
  protected readonly stories = SHOWCASE_STORIES;
}
