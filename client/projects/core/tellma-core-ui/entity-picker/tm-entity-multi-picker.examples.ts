// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

/**
 * Usage examples: the docs extractor reads each exported
 * const's `template` into components.json (→ llms.txt → the MCP `example`
 * tool), titled by the export name. Keep every template copy-pasteable —
 * the function-valued bindings (`searchAgents`, `agentId`, …) are host
 * members a consumer component defines.
 */

export const InField = {
  template: `
    <tm-form-field label="Suppliers" style="max-width: 360px; display: block;">
      <tm-entity-multi-picker
        [search]="searchAgents"
        [itemId]="agentId"
        [itemLabel]="agentName"
        [displayWith]="agentDisplay"
        placeholder="Search suppliers"
      />
    </tm-form-field>
  `,
};

/**
 * A capped selection whose advanced-search page edits the whole set: the
 * page opens with `multiple`, the chosen `ids` and `maxItems`, and closes
 * with a `TmEntityMultiPick` that adds and removes in one visit.
 */
export const WithAdvancedSearch = {
  template: `
    <tm-form-field label="Suppliers" style="max-width: 360px; display: block;">
      <tm-entity-multi-picker
        [search]="searchAgents"
        [itemId]="agentId"
        [itemLabel]="agentName"
        [displayWith]="agentDisplay"
        [advancedSearch]="agentPage"
        [create]="agentPage"
        [maxItems]="10"
      />
    </tm-form-field>
  `,
};

export const Standalone = {
  template: `
    <tm-entity-multi-picker
      [search]="searchAgents"
      [itemId]="agentId"
      [itemLabel]="agentName"
      aria-label="Suppliers"
      style="max-width: 360px;"
    />
  `,
};
