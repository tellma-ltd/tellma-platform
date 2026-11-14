// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

/**
 * Usage examples: the docs extractor reads each exported
 * const's `template` into components.json (→ llms.txt → the MCP `example`
 * tool), titled by the export name. Keep every template copy-pasteable.
 */

/** The shown columns, in order: drag a row, or press Space on its handle and use the arrow keys. */
export const Basic = {
  template: `
    <tm-reorder-list
      [(items)]="columns"
      [itemKey]="columnKey"
      [itemLabel]="columnLabel"
      ariaLabel="Shown columns"
    />
  `,
};

/** A list that also takes fields dragged from a tree, with an empty state of its own. */
export const ExternalDrops = {
  template: `
    <tm-reorder-list
      [(items)]="columns"
      [itemKey]="columnKey"
      [itemLabel]="columnLabel"
      [accepts]="['tm.path']"
      ariaLabel="Shown columns"
      (received)="insertColumn($event.payload, $event.index)"
    >
      <ng-template tmReorderItem [tmReorderItemOf]="columns()" let-column let-index="index">
        {{ index + 1 }}. {{ column.label }}
      </ng-template>
      <ng-template tmReorderEmpty>Drag fields here, or check them in the list.</ng-template>
    </tm-reorder-list>
  `,
};
