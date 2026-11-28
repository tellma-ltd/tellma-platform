// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import { Component } from '@angular/core';

import { TmSkeleton } from '@tellma/core-ui/skeleton';

/** tm-skeleton in each shape: lines, a block, a circle and a table. */
@Component({
  imports: [TmSkeleton],
  template: `
    <h2>Skeletons</h2>

    <!-- A paragraph and a lone line. -->
    <section class="stack" aria-busy="true">
      <tm-skeleton [lines]="3" width="320px" data-testid="lines" />
      <tm-skeleton width="160px" data-testid="line" />
    </section>

    <!-- An avatar beside a name. -->
    <section class="person">
      <tm-skeleton shape="circle" width="24px" data-testid="circle" />
      <tm-skeleton width="120px" />
    </section>

    <!-- A chart frame. -->
    <section class="stack">
      <tm-skeleton shape="block" width="320px" height="120px" data-testid="block" />
    </section>

    <!-- A list's place. -->
    <section class="stack">
      <tm-skeleton shape="table" [lines]="5" [columns]="4" data-testid="table" />
    </section>
  `,
  styles: `
    .stack {
      display: grid;
      gap: 16px;
      margin-block-end: 24px;
    }
    .person {
      display: flex;
      align-items: center;
      gap: 8px;
      margin-block-end: 24px;
    }
  `,
})
export class SkeletonStory {}
