// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

/**
 * Usage examples: the docs extractor reads each exported
 * const's `template` into components.json (→ llms.txt → the MCP `example`
 * tool), titled by the export name. Keep every template copy-pasteable.
 */

/** Initials on a tint the record key picks; the same key always gives the same tint. */
export const Initials = {
  template: `
    <tm-avatar name="Farah Nasser" colorKey="core.User:17" />
    <tm-avatar name="فرح ناصر" colorKey="core.User:18" />
  `,
};

/** A photo, with the initials shown while it loads and if it fails. */
export const Photo = {
  template: `<tm-avatar name="Farah Nasser" src="/api/users/17/image" colorKey="core.User:17" size="lg" />`,
};

/** Beside a printed name, the avatar is decorative so the name is read once. */
export const BesideAName = {
  template: `
    <span>
      <tm-avatar name="Farah Nasser" colorKey="core.User:17" size="xs" decorative />
      Farah Nasser
    </span>
  `,
};
