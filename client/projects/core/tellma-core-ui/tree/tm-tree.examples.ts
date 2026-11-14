// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

/**
 * Usage examples: the docs extractor reads each exported
 * const's `template` into components.json (→ llms.txt → the MCP `example`
 * tool), titled by the export name. Keep every template copy-pasteable.
 */

/** Fields as a tree: navigations are branches, opened on demand; Enter or a double-click picks a field. */
export const Basic = {
  template: `
    <tm-tree
      [nodes]="fields"
      [nodeKey]="fieldKey"
      [label]="fieldLabel"
      [children]="fieldChildren"
      [(expanded)]="openFields"
      ariaLabel="Fields"
      (activate)="pickField($event)"
    />
  `,
};

/** Checked fields, with a custom row that shows each navigation's target. */
export const MultipleSelection = {
  template: `
    <tm-tree
      [nodes]="fields"
      [nodeKey]="fieldKey"
      [label]="fieldLabel"
      [children]="fieldChildren"
      selection="multiple"
      [(selected)]="shownFields"
      ariaLabel="Available columns"
      size="md"
    >
      <ng-template tmTreeNode [tmTreeNodeOf]="fields" let-node>
        {{ node.label }}
        @if (node.target) {
          <tm-badge>{{ node.target }}</tm-badge>
        }
      </ng-template>
    </tm-tree>
  `,
};

/** Rows that drag: a field's path drops into a text field at its caret, or onto a drop target. */
export const Draggable = {
  template: `
    <tm-tree
      [nodes]="fields"
      [nodeKey]="fieldKey"
      [label]="fieldLabel"
      [children]="fieldChildren"
      [dragData]="fieldDrag"
      ariaLabel="Fields"
    />
  `,
};
