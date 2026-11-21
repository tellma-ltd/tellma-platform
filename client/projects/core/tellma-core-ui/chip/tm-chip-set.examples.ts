// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

/**
 * Usage examples: the docs extractor reads each exported
 * const's `template` into components.json (→ llms.txt → the MCP `example`
 * tool), titled by the export name. Keep every template copy-pasteable.
 */

/** Applied filters that wrap, each removable, with one tab stop. */
export const Filters = {
  template: `
    <tm-chip-set ariaLabel="Filters">
      <tm-chip label="Service" [removable]="true" (remove)="onRemove('Service')" />
      <tm-chip label="Sale" [removable]="true" (remove)="onRemove('Sale')" />
      <tm-chip label="Operation" [removable]="true" (remove)="onRemove('Operation')" />
    </tm-chip-set>
  `,
};

/** One line that collapses what does not fit behind a "+N" chip. */
export const Collapse = {
  template: `
    <tm-chip-set ariaLabel="Tags" overflow="collapse">
      <tm-chip label="Service" />
      <tm-chip label="Sale" />
      <tm-chip label="Operation" />
      <tm-chip label="Purchase" />
    </tm-chip-set>
  `,
};

/**
 * A multi-value field's chip row: one scrolling line beside an input that
 * takes focus back when the arrows leave the chips.
 */
export const BesideAnInput = {
  template: `
    <tm-chip-set
      #chips
      ariaLabel="Chosen agents"
      overflow="scroll"
      (exit)="$event === 'end' && agentInput.focus()"
    >
      <tm-chip label="Adam Brown" [removable]="true" (remove)="onRemove('Adam Brown')" />
      <tm-chip label="Alice Green" [removable]="true" (remove)="onRemove('Alice Green')" />
    </tm-chip-set>
    <input #agentInput aria-label="Add an agent" (keydown.backspace)="chips.focusLast()" />
  `,
};
