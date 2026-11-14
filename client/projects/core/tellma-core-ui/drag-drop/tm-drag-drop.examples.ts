// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

/**
 * Usage examples: the docs extractor reads each exported
 * const's `template` into components.json (→ llms.txt → the MCP `example`
 * tool), titled by the export name. Keep every template copy-pasteable.
 */

/** A field chip dragged onto a column well; a text field takes the same drag's text at its caret. */
export const SourceAndTarget = {
  template: `
    <span [tmDragSource]="{ kind: 'tm.path', data: field, text: 'Code', label: 'Code' }">Code</span>
    <div tmDropTarget [accepts]="['tm.path']" (dropped)="addColumn($event.payload)">Drop a field here</div>
  `,
};
