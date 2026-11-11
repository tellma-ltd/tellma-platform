// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

/**
 * Usage examples: the docs extractor reads each exported
 * const's `template` into components.json (→ llms.txt → the MCP `example`
 * tool), titled by the export name. Keep every template copy-pasteable.
 */

/** A record's status: the text carries the meaning, the tone reinforces it. */
export const Status = {
  template: `
    <tm-badge tone="success">Active</tm-badge>
    <tm-badge tone="warning">Held</tm-badge>
    <tm-badge tone="error">Overdue</tm-badge>
    <tm-badge>Draft</tm-badge>
  `,
};

/** The solid accent badge, for something new. */
export const Accent = {
  template: `<tm-badge tone="accent">New</tm-badge>`,
};

/** A leading dot, or a leading icon by registry name. */
export const DotAndIcon = {
  template: `
    <tm-badge tone="info" dot>Syncing</tm-badge>
    <tm-badge tone="error" icon="circle-alert">2 problems</tm-badge>
  `,
};
