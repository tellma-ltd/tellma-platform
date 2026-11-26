// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

/// <reference types="@tellma/core-tools/lucide-icons" />

import { afterRenderEffect, ChangeDetectionStrategy, Component, computed, DestroyRef, inject, input, untracked, viewChild } from '@angular/core';
import ClipboardPaste from 'lucide/dist/esm/icons/clipboard-paste.mjs';
import Copy from 'lucide/dist/esm/icons/copy.mjs';
import CopyPlus from 'lucide/dist/esm/icons/copy-plus.mjs';
import CornerDownRight from 'lucide/dist/esm/icons/corner-down-right.mjs';
import Plus from 'lucide/dist/esm/icons/plus.mjs';
import Scissors from 'lucide/dist/esm/icons/scissors.mjs';
import Trash from 'lucide/dist/esm/icons/trash.mjs';

import { tmKeysText, type TmKeys } from '@tellma/core-ui/kbd';
import { TmMenu, type TmMenuEntry } from '@tellma/core-ui/menu';

import type { ɵTmGridMenuState, ɵTmGridViewCore } from './grid-core';

// The grid's context menu (§8.5), in the grid's deferred chunk: the grid
// view loads it once the browser is idle after the first render, or at once
// when the menu is asked for sooner. It imports nothing of the grid's at run
// time (the core is a type here), so the chunk holds only the menu's own code.

/**
 * Context-menu shortcut hints (display only) as key combinations;
 * the menu labels them for the platform (Ctrl+X, or ⌘X on Apple). The keys
 * mirror the keymap: cut/copy/paste are the native Mod+X/C/V; row ops are
 * the Alt-modified Mod+Alt+±.
 */
const GRID_MENU_SHORTCUTS = {
  cut: 'Mod+X',
  copy: 'Mod+C',
  paste: 'Mod+V',
  insertAbove: 'Mod+Alt+Plus',
  deleteRows: 'Mod+Alt+Minus',
} satisfies Record<string, TmKeys>;

/**
 * The context menu's built-in glyphs: Lucide data the menu draws through
 * `tm-icon`. None is directional, so none mirrors in right-to-left text.
 */
const GRID_MENU_ICONS = {
  cut: Scissors,
  copy: Copy,
  copyWithHeaders: CopyPlus,
  paste: ClipboardPaste,
  insertRows: Plus,
  insertChild: CornerDownRight,
  deleteRows: Trash,
} as const;

/**
 * The context menu's entries: the clipboard ones, then on an editable grid
 * the row ones, then the consumer's extras after a separator.
 *
 * @param core - The grid's core, which runs the built-in actions.
 * @param state - What the entries depend on.
 * @returns The entries.
 */
function tmGridMenuEntries(core: ɵTmGridViewCore, state: ɵTmGridMenuState): readonly TmMenuEntry[] {
  const { translate, editable, onRowEnd, rowCount: count } = state;
  const items: TmMenuEntry[] = [
    {
      id: 'cut',
      label: translate('grid.menu.cut')(),
      icon: GRID_MENU_ICONS.cut,
      shortcut: GRID_MENU_SHORTCUTS.cut,
      disabled: !editable || onRowEnd,
      action: () => core.runMenuAction('cut'),
    },
    {
      id: 'copy',
      label: translate('grid.menu.copy')(),
      icon: GRID_MENU_ICONS.copy,
      shortcut: GRID_MENU_SHORTCUTS.copy,
      disabled: onRowEnd,
      action: () => core.runMenuAction('copy'),
    },
    {
      id: 'copyWithHeaders',
      label: translate('grid.menu.copyWithHeaders')(),
      icon: GRID_MENU_ICONS.copyWithHeaders,
      disabled: onRowEnd,
      action: () => core.runMenuAction('copyWithHeaders'),
    },
    state.canReadClipboard
      ? {
          id: 'paste',
          label: translate('grid.menu.paste')(),
          icon: GRID_MENU_ICONS.paste,
          shortcut: GRID_MENU_SHORTCUTS.paste,
          disabled: !editable || onRowEnd,
          action: () => core.runMenuAction('paste'),
        }
      : {
          id: 'paste',
          // The hint is a sentence, so the combination is written out as
          // text for the platform ("Ctrl+V", "⌘V") rather than as caps.
          label: translate('grid.menu.pasteHint', {
            shortcut: tmKeysText(GRID_MENU_SHORTCUTS.paste),
          })(),
          icon: GRID_MENU_ICONS.paste,
          disabled: true,
          action: () => undefined,
        },
  ];
  if (editable) {
    items.push(
      { separator: true },
      {
        id: 'insertAbove',
        label: translate('grid.menu.insertAbove', { count })(),
        icon: GRID_MENU_ICONS.insertRows,
        shortcut: GRID_MENU_SHORTCUTS.insertAbove,
        disabled: !state.canAddRows,
        action: () => core.runMenuAction('insertAbove'),
      },
      {
        id: 'insertBelow',
        label: translate('grid.menu.insertBelow', { count })(),
        icon: GRID_MENU_ICONS.insertRows,
        disabled: !state.canAddRows,
        action: () => core.runMenuAction('insertBelow'),
      },
    );
    if (state.tree) {
      // Insert child row (§13.4): needs the row factory and an active data row.
      items.push({
        id: 'insertChild',
        label: translate('grid.menu.insertChild')(),
        icon: GRID_MENU_ICONS.insertChild,
        disabled: !state.canAddRows || !state.hasActiveRow,
        action: () => core.runMenuAction('insertChild'),
      });
    }
    items.push({
      id: 'deleteRows',
      label: translate('grid.menu.deleteRows', { count })(),
      icon: GRID_MENU_ICONS.deleteRows,
      shortcut: GRID_MENU_SHORTCUTS.deleteRows,
      action: () => core.runMenuAction('deleteRows'),
    });
  }
  return state.extras.length > 0 ? [...items, { separator: true }, ...state.extras] : items;
}

/**
 * The grid's context menu: a `tm-menu` over the core's menu state, which
 * hands itself to the core once it renders, so a right-click, a long-press
 * or the Menu key that came before its code opens it then.
 */
@Component({
  selector: 'tm-grid-context-menu',
  imports: [TmMenu],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `<tm-menu [items]="entries()" />`,
})
export class ɵTmGridContextMenu {
  /** The grid's core. */
  readonly core = input.required<ɵTmGridViewCore>();

  /** The entries, rebuilt as the state they read changes. */
  protected readonly entries = computed(() => tmGridMenuEntries(this.core(), this.core().menuState()));

  /** The menu, once rendered. */
  private readonly menu = viewChild.required(TmMenu);

  /** Hands the menu to the core once it renders, and takes it back when the menu goes. */
  constructor() {
    afterRenderEffect(() => {
      const core = this.core();
      const menu = this.menu();
      untracked(() => core.attachMenu(menu));
    });
    inject(DestroyRef).onDestroy(() => untracked(this.core).attachMenu(null));
  }
}
