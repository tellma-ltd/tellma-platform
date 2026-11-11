// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import type { TemplateRef } from '@angular/core';

import type { TmIconSource } from '@tellma/core-ui/icon';
import type { TmKeys } from '@tellma/core-ui/kbd';

/**
 * The leading icon of a menu row, in one of three forms:
 *
 * - a string names a glyph in the icon registry (`'log-out'`);
 * - icon data draws that glyph directly, bypassing the registry;
 * - a template renders as given, inside the row's icon box.
 *
 * Every form is decorative (`aria-hidden`); the row's label names it.
 */
export type TmMenuIcon = TemplateRef<void> | string | TmIconSource;

/** One actionable menu item. */
export interface TmMenuItem {
  /** Stable identity of the item. */
  readonly id: string;
  /** Literal label. Provide exactly one of `label`/`labelKey`. */
  readonly label?: string;
  /** Label resolved through the UI translation seam. */
  readonly labelKey?: string;
  /**
   * Leading icon. A checkable item (`role`) draws its check mark in the
   * icon box instead, so its `icon` is not shown.
   */
  readonly icon?: TmMenuIcon;
  /**
   * Whether the item is disabled for a passing state (kept navigable,
   * never activates). To tell the user why, set `disabledReason` instead;
   * setting both logs a development warning.
   */
  readonly disabled?: boolean;
  /**
   * Keyboard-shortcut hint shown trailing the label as key caps labelled
   * for the platform (`'Mod+C'` shows Ctrl and C on Windows and Linux, ⌘
   * and C on Apple platforms). Display only: it never binds the shortcut,
   * and assistive technology does not read it.
   */
  readonly shortcut?: TmKeys;
  /** A muted second line under the label, read as the row's description. */
  readonly description?: string;
  /**
   * A trailing value, such as the current choice of a submenu (`'English'`)
   * or a version (`'v2.14.0'`). It is part of the row's accessible name
   * ("Language, English") but not of what typeahead matches.
   */
  readonly detail?: string;
  /**
   * Makes the item checkable: `'checkbox'` renders `menuitemcheckbox`,
   * `'radio'` renders `menuitemradio`. Radio items belong inside a
   * {@link TmMenuGroup}, which is what scopes "one of these".
   */
  readonly role?: 'checkbox' | 'radio';
  /** Whether a checkable item is checked. Ignored without `role`. */
  readonly checked?: boolean;
  /**
   * Leaves the menu open after activation, for toggles a user flips
   * several of in one visit. `action()` and `itemSelected` still run.
   */
  readonly keepOpen?: boolean;
  /**
   * The label's language as a BCP 47 tag, set as `lang` on the row: for
   * labels written in a language other than the page's, such as the native
   * names in a language switcher.
   */
  readonly lang?: string;
  /**
   * Disables the item and says why. The row stays focusable and in
   * typeahead, shows the reason as its second line (in place of
   * `description`), exposes it as the row's accessible description, and
   * does nothing when activated: the menu stays open.
   */
  readonly disabledReason?: string;
  /** Runs on activation (click, Enter, Space), before the menu closes. */
  action(): void;
}

/**
 * A menu row that is a real link: it renders an `<a>`, so the browser's
 * own link behaviors (open in a new tab, copy the address, middle-click)
 * all work on it.
 */
export interface TmMenuLink {
  /** Stable identity of the link. */
  readonly id: string;
  /** Literal label. Provide exactly one of `label`/`labelKey`. */
  readonly label?: string;
  /** Label resolved through the UI translation seam. */
  readonly labelKey?: string;
  /** Leading icon. */
  readonly icon?: TmMenuIcon;
  /** Keyboard-shortcut hint, as on {@link TmMenuItem.shortcut}. */
  readonly shortcut?: TmKeys;
  /** A trailing value, as on {@link TmMenuItem.detail}. */
  readonly detail?: string;
  /** Whether the link is disabled for a passing state. */
  readonly disabled?: boolean;
  /** Where the link goes. */
  readonly href: string;
  /**
   * `'_blank'` opens the link in a new tab; the row then shows an external
   * glyph and tells assistive technology that it opens in a new tab.
   */
  readonly target?: '_blank';
  /**
   * When set, a plain activation (click, Enter, Space) runs this instead of
   * navigating: how an application routes in place. Activations the
   * browser handles itself (Ctrl, ⌘, Shift or middle-click, and the
   * link's context menu) still use `href`.
   */
  action?(): void;
  /** Disables the link and says why, as on {@link TmMenuItem.disabledReason}. */
  readonly disabledReason?: string;
}

/**
 * A row that opens a submenu at its inline end. Submenus go one level
 * deep: a submenu's own entries cannot hold another.
 */
export interface TmMenuSubmenu {
  /** Stable identity of the row. */
  readonly id: string;
  /** Literal label. Provide exactly one of `label`/`labelKey`. */
  readonly label?: string;
  /** Label resolved through the UI translation seam. */
  readonly labelKey?: string;
  /** Leading icon. */
  readonly icon?: TmMenuIcon;
  /** Keyboard-shortcut hint, as on {@link TmMenuItem.shortcut}. */
  readonly shortcut?: TmKeys;
  /** A trailing value, such as the submenu's current choice. */
  readonly detail?: string;
  /** Whether the row is disabled for a passing state; the submenu does not open. */
  readonly disabled?: boolean;
  /** The submenu's entries, in display order. */
  readonly items: readonly (TmMenuItem | TmMenuLink | TmMenuGroup | TmMenuSeparator)[];
  /**
   * Disables the row and says why, as on {@link TmMenuItem.disabledReason};
   * the submenu does not open.
   */
  readonly disabledReason?: string;
}

/**
 * A run of rows that belong together, with an optional heading. A divider
 * is drawn above a group whenever rows precede it.
 */
export interface TmMenuGroup {
  /** Discriminates a group entry. */
  readonly group: true;
  /** Literal heading. Provide at most one of `label`/`labelKey`. */
  readonly label?: string;
  /** Heading resolved through the UI translation seam. */
  readonly labelKey?: string;
  /** The group's rows, in display order. */
  readonly items: readonly (TmMenuItem | TmMenuLink | TmMenuSubmenu)[];
}

/** A visual separator between runs of rows. */
export interface TmMenuSeparator {
  /** Discriminates a separator entry. */
  readonly separator: true;
}

/** One entry of a menu: an item, a link, a submenu, a group, or a separator. */
export type TmMenuEntry = TmMenuItem | TmMenuLink | TmMenuSubmenu | TmMenuGroup | TmMenuSeparator;

/** Where a menu opens: an element, a rectangle, or a point (right-click). */
export type TmMenuAnchor = Element | DOMRect | { readonly x: number; readonly y: number };

/** Options of `TmMenu.open`. */
export interface TmMenuOpenOptions {
  /** The element focus returns to when the menu closes via keyboard. */
  readonly restoreFocus?: HTMLElement;
  /** Which row takes focus as the menu opens. Default `'first'`. */
  readonly initialFocus?: 'first' | 'last';
}
