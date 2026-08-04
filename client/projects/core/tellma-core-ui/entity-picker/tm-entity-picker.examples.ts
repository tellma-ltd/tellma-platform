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
    <tm-form-field label="Supplier" style="max-width: 320px; display: block;">
      <tm-entity-picker
        [search]="searchAgents"
        [itemId]="agentId"
        [itemLabel]="agentName"
        [displayWith]="agentDisplay"
        placeholder="Search suppliers"
      />
    </tm-form-field>
  `,
};

export const WithModalPages = {
  template: `
    <tm-form-field label="Supplier" style="max-width: 320px; display: block;">
      <tm-entity-picker
        [search]="searchAgents"
        [itemId]="agentId"
        [itemLabel]="agentName"
        [displayWith]="agentDisplay"
        [advancedSearch]="agentPage"
        [create]="agentPage"
        [edit]="agentPage"
        createLabel="Create supplier…"
      />
    </tm-form-field>
  `,
};

export const Standalone = {
  template: `
    <tm-entity-picker
      [search]="searchAgents"
      [itemId]="agentId"
      [itemLabel]="agentName"
      aria-label="Supplier"
      style="max-width: 320px;"
    />
  `,
};
