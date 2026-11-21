// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

/**
 * Usage examples: the docs extractor reads each exported
 * const's `template` into components.json (→ llms.txt → the MCP `example`
 * tool), titled by the export name. Keep every template copy-pasteable.
 */

/** A shortcut hint: Mod is ⌘ on Apple platforms and Ctrl elsewhere. */
export const Shortcut = {
  template: `<tm-kbd keys="Mod+K" />`,
};

/** Inside a sentence; the caps keep keyboard order in right-to-left text too. */
export const InASentence = {
  template: `<p>Press <tm-kbd keys="Alt+Shift+P" /> to pin this page.</p>`,
};

/** A single key, and a symbol that is matched with or without Shift. */
export const SingleKeys = {
  template: `
    <tm-kbd keys="F2" />
    <tm-kbd keys="?" />
    <tm-kbd keys="Escape" />
  `,
};

/** Forced platform labels, e.g. for documentation that shows both. */
export const BothPlatforms = {
  template: `
    <tm-kbd keys="Mod+Alt+Plus" [apple]="false" />
    <tm-kbd keys="Mod+Alt+Plus" [apple]="true" />
  `,
};
