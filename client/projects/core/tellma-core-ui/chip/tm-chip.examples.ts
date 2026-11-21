// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

/**
 * Usage examples: the docs extractor reads each exported
 * const's `template` into components.json (→ llms.txt → the MCP `example`
 * tool), titled by the export name. Keep every template copy-pasteable.
 */

/** A plain value, a link to a document and a status-toned chip. */
export const Chips = {
  template: `
    <tm-chip label="Service" />
    <tm-chip label="SAL-0042" href="/documents/sales/42" tone="accent" />
    <tm-chip label="Overdue" tone="warning" icon="circle-alert" />
  `,
};

/** A removable chip: the host removes the value when asked. */
export const Removable = {
  template: `
    <tm-chip label="Riyadh" [removable]="true" (remove)="onRemove('Riyadh')" />
  `,
};

/** An activatable chip: binding (activate) renders its label as a button. */
export const Activatable = {
  template: `
    <tm-chip label="Posted after 1 May" (activate)="onActivate('date')">
      Posted after <bdi>1 May</bdi>
    </tm-chip>
  `,
};
