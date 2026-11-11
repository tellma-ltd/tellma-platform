// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

/**
 * Usage examples: the docs extractor reads each exported
 * const's `template` into components.json (→ llms.txt → the MCP `example`
 * tool), titled by the export name. Keep every template copy-pasteable.
 *
 * The `items` arrays are empty because entries carry an `action()`
 * FUNCTION, which a template literal cannot express — real usage builds
 * `TmMenuEntry[]` in the component class and binds it.
 */

/**
 * A menu button. A click toggles the menu; Enter, Space and ArrowDown open
 * it on its first row, ArrowUp on its last.
 */
export const MenuButton = {
  template: `
    <button type="button" [tmMenuTriggerFor]="menu">Options</button>
    <tm-menu #menu [items]="[]" aria-label="Options" />
  `,
};

/** On a styled button: the trigger composes with `tmButton`. */
export const StyledButton = {
  template: `
    <button tmButton variant="secondary" [tmMenuTriggerFor]="menu">Options</button>
    <tm-menu #menu [items]="[]" aria-label="Options" />
  `,
};
