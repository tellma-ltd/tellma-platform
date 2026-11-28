// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

/**
 * Usage examples: the docs extractor reads each exported
 * const's `template` into components.json (→ llms.txt → the MCP `example`
 * tool), titled by the export name. Keep every template copy-pasteable.
 */

/** An update notice with an action. */
export const UpdateAvailable = {
  template: `
    <tm-banner-bar
      message="A new version is available."
      actionLabel="Reload"
      (action)="onReload()"
    />
  `,
};

/** The second of three messages, dismissible, with the pager. */
export const Paged = {
  template: `
    <tm-banner-bar
      tone="warning"
      message="The server restarts for maintenance at 22:00."
      [dismissible]="true"
      [position]="{ index: 2, count: 3 }"
      (previous)="onPrevious()"
      (next)="onNext()"
      (dismiss)="onDismiss()"
    />
  `,
};
