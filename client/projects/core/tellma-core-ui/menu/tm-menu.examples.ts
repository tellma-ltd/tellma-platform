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
 * `TmMenuEntry[]` in the component class (`{ id, label | labelKey, icon?,
 * disabled?, action }` or `{ separator: true }`) and binds it.
 */

/**
 * A button that opens the menu anchored at itself; `restoreFocus` returns
 * focus to the button when the menu closes via keyboard.
 */
export const ButtonTrigger = {
  template: `
    <button type="button" #btn (click)="menu.open(btn, { restoreFocus: btn })">Options</button>
    <tm-menu #menu [items]="[]" aria-label="Options" />
  `,
};

/**
 * A context menu region: right-click opens at the pointer, the Menu key /
 * Shift+F10 open at the element, and touch long-press opens at the press
 * point. The native browser context menu is suppressed while enabled.
 */
export const ContextMenu = {
  template: `
    <div [tmContextMenuTrigger]="menu" style="padding: 24px; border: 1px dashed currentColor">
      Right-click me
    </div>
    <tm-menu #menu [items]="[]" aria-label="Context actions" />
  `,
};
