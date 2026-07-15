// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

/**
 * Usage examples: the docs extractor reads each exported
 * const's `template` into components.json (→ llms.txt → the MCP `example`
 * tool), titled by the export name. Keep every template copy-pasteable.
 */

export const InField = {
  template: `
    <tm-form-field label="Email" hint="Your work email">
      <input tmInput placeholder="name@company.com" />
    </tm-form-field>
  `,
};

export const WithAdornments = {
  template: `
    <tm-form-field label="Search">
      <svg tmPrefix width="16" height="16" viewBox="0 0 16 16" fill="none" aria-hidden="true">
        <circle cx="7" cy="7" r="4.5" stroke="currentColor" />
        <path d="m10.5 10.5 3 3" stroke="currentColor" stroke-linecap="round" />
      </svg>
      <input tmInput placeholder="Search records" />
    </tm-form-field>
  `,
};

export const Sizes = {
  template: `
    <tm-form-field label="Small" size="sm"><input tmInput /></tm-form-field>
    <tm-form-field label="Medium" size="md"><input tmInput /></tm-form-field>
    <tm-form-field label="Large" size="lg"><input tmInput /></tm-form-field>
  `,
};

export const Disabled = {
  template: `
    <tm-form-field label="Disabled">
      <input tmInput disabled value="Cannot touch this" />
    </tm-form-field>
  `,
};

export const NonFormError = {
  template: `
    <tm-form-field label="Code" error="That code is not valid">
      <input tmInput value="XYZ" />
    </tm-form-field>
  `,
};

/** The bare directive — what a grid cell mounts, no chrome to strip. */
export const BareInGridCell = {
  template: `<input tmInput placeholder="Bare input" />`,
};
