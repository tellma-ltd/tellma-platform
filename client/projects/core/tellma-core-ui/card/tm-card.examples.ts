// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

/**
 * Usage examples: the docs extractor reads each exported
 * const's `template` into components.json (→ llms.txt → the MCP `example`
 * tool), titled by the export name. Keep every template copy-pasteable.
 */

/** A titled panel with a count, a subtitle and a header action. */
export const WithHeader = {
  template: `
    <tm-card heading="Lines" [count]="12">
      <span tmCardSubtitle>Posted entries only</span>
      <button tmButton tmCardActions variant="ghost">Add line</button>
      <p>The lines of the document.</p>
    </tm-card>
  `,
};

/** A collapsible section that keeps its fields while collapsed and flags their errors. */
export const CollapsibleWithErrors = {
  template: `
    <tm-card heading="Address" [collapsible]="true" [(expanded)]="addressOpen" [preserveContent]="true" [invalid]="addressInvalid">
      <p>The address fields.</p>
    </tm-card>
  `,
};

/** A grid filling the card edge to edge, with a footer. */
export const Flush = {
  template: `
    <tm-card heading="Recent documents" [flush]="true">
      <p>A grid goes here.</p>
      <p tmCardFooter>Showing the last 30 days.</p>
    </tm-card>
  `,
};
