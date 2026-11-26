// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import type { TmPageKind } from '../context/tm-page-kind';

// The icon a recently visited page shows when no launcher entry names it
// (spec 0035 §9.2, which spec 0027 §9.3 reuses): one per page family, so the
// launcher's recents band and the home page's recents list draw the same
// glyph for the same page.

/** The icon of each page family a recent page can be; the rest show a file. */
const ICONS: Readonly<Partial<Record<TmPageKind, string>>> = {
  search: 'list',
  report: 'chart-column',
  dashboard: 'layout-dashboard',
  settings: 'settings',
};

/**
 * The icon a recently visited page shows when no launcher entry gives it
 * one: `list` for a search page, `chart-column` for a report,
 * `layout-dashboard` for a dashboard, `settings` for a settings page, and
 * `file` for any other page. Library wiring with no compatibility promise.
 *
 * @param kind - The page's family.
 * @returns The icon's registered name.
 *
 * @example
 * ```ts
 * const icon = page.icon ?? tmRecentPageIcon(page.kind);
 * ```
 */
export function tmRecentPageIcon(kind: TmPageKind): string {
  return ICONS[kind] ?? 'file';
}
