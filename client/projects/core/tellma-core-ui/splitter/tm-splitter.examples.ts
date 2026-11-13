// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

/**
 * Usage examples: the docs extractor reads each exported
 * const's `template` into components.json (→ llms.txt → the MCP `example`
 * tool), titled by the export name. Keep every template copy-pasteable.
 */

/** A list beside its details: the list keeps 420 px or more and at most 66 %. */
export const MasterDetail = {
  template: `
    <tm-splitter [(size)]="listWidth" [defaultSize]="500" [min]="420" [maxFraction]="0.66" [otherMin]="340">
      <section tmSplitterPane="start">The list.</section>
      <section tmSplitterPane="end">The details.</section>
    </tm-splitter>
  `,
};

/** A preview panel under the editor, sized from the bottom. */
export const Vertical = {
  template: `
    <tm-splitter orientation="vertical" primary="end" [defaultSize]="240" [min]="120" ariaLabel="Resize the preview">
      <section tmSplitterPane="start">The editor.</section>
      <section tmSplitterPane="end">The preview.</section>
    </tm-splitter>
  `,
};
