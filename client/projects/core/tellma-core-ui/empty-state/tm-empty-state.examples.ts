// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

/**
 * Usage examples: the docs extractor reads each exported
 * const's `template` into components.json (→ llms.txt → the MCP `example`
 * tool), titled by the export name. Keep every template copy-pasteable.
 */

/** An empty list, with the action that fills it. */
export const EmptyList = {
  template: `
    <tm-empty-state heading="No invoices yet" icon="inbox">
      Invoices you create appear here.
      <button tmButton variant="primary" tmEmptyStateActions>New invoice</button>
    </tm-empty-state>
  `,
};

/** A search with no results, announced because it replaces the rows. */
export const NoResults = {
  template: `
    <tm-empty-state heading="No matches" icon="search-x" size="panel" live="polite">
      Try fewer or different words.
    </tm-empty-state>
  `,
};

/** A load that failed, with a reference to quote and a retry. */
export const LoadFailed = {
  template: `
    <tm-empty-state heading="Couldn't load the invoices" icon="circle-x" tone="error">
      <tm-reference value="4bf92f3577b34da6a3ce929d0e0e4736" />
      <button tmButton tmEmptyStateActions>Try again</button>
    </tm-empty-state>
  `,
};
