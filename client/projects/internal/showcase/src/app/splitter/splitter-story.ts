// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import { Component, signal } from '@angular/core';

import { TmSplitter, TmSplitterPane } from '@tellma/core-ui/splitter';

/**
 * tm-splitter as a list beside its details (the list sized, at least
 * 240 px and at most 66 %, the details at least 200 px), and as an editor
 * over a preview sized from the bottom. The sizes the bars write show
 * under each.
 */
@Component({
  imports: [TmSplitter, TmSplitterPane],
  template: `
    <h2>Splitter</h2>

    <!-- Horizontal: the start pane is sized. -->
    <tm-splitter
      class="frame"
      [(size)]="listSize"
      [defaultSize]="420"
      [min]="240"
      [maxFraction]="0.66"
      [otherMin]="200"
      data-testid="splitter-horizontal"
    >
      <section tmSplitterPane="start" class="pane" data-testid="pane-list">The list.</section>
      <section tmSplitterPane="end" class="pane" data-testid="pane-details">The details.</section>
    </tm-splitter>
    <p data-testid="list-size">List: {{ listSize() ?? 'default' }}</p>

    <!-- Vertical: the end pane is sized. -->
    <tm-splitter
      class="frame frame--tall"
      orientation="vertical"
      primary="end"
      [(size)]="previewSize"
      [defaultSize]="120"
      [min]="60"
      ariaLabel="Resize the preview"
      data-testid="splitter-vertical"
    >
      <section tmSplitterPane="start" class="pane">The editor.</section>
      <section tmSplitterPane="end" class="pane" data-testid="pane-preview">The preview.</section>
    </tm-splitter>
    <p data-testid="preview-size">Preview: {{ previewSize() ?? 'default' }}</p>
  `,
  styles: `
    .frame {
      block-size: 240px;
      border: 1px solid var(--border-default);
    }
    .frame--tall {
      block-size: 320px;
    }
    .pane {
      overflow: auto;
      padding: 12px;
    }
  `,
})
export class SplitterStory {
  /** The list's width as the user set it, or null for the default. */
  protected readonly listSize = signal<number | null>(null);

  /** The preview's height as the user set it, or null for the default. */
  protected readonly previewSize = signal<number | null>(null);
}
