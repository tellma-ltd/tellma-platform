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
 * `TmMenuEntry[]` in the component class and binds it:
 *
 * - an item: `{ id, label | labelKey, action, icon?, shortcut?, detail?,
 *   description?, role?, checked?, keepOpen?, lang?, disabled?,
 *   disabledReason? }`;
 * - a link: `{ id, label | labelKey, href, target?, action? }`;
 * - a submenu: `{ id, label | labelKey, items }`;
 * - a group: `{ group: true, label?, items }`;
 * - a separator: `{ separator: true }`.
 */

/**
 * A menu button: the button opens the menu anchored at itself, with the
 * menu-button keys and ARIA, and takes focus back when the menu closes.
 */
export const MenuButton = {
  template: `
    <button type="button" [tmMenuTriggerFor]="menu">Options</button>
    <tm-menu #menu [items]="[]" aria-label="Options" />
  `,
};

/**
 * A header block above the rows, read as the menu's description when it
 * opens. Text and images only: focus never enters it.
 */
export const Header = {
  template: `
    <button type="button" [tmMenuTriggerFor]="menu">Account</button>
    <tm-menu #menu [items]="[]" aria-label="Account">
      <ng-template tmMenuHeader>Signed in as Rana Haddad</ng-template>
    </tm-menu>
  `,
};

/**
 * Opening from code, at any element: `restoreFocus` returns focus to the
 * element when the menu closes via keyboard.
 */
export const OpenFromCode = {
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
