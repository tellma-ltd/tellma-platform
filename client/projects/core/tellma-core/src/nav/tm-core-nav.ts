// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import type { TmNavEntry, TmNavSection } from './tm-nav-declarations';

// The core feature's navigation (spec 0027 §3.2): the two sections that close
// every launcher, and the entries this spec declares. Other platform pages
// (users, roles, reports, jobs and the rest) add their entries to the core
// feature as their page frameworks land, each with the order §3.2's table
// gives it. A distribution relabels, reorders or hides any of them through
// `overrides` and `hide`.

/** The id of the core entry that opens the tenant's settings. */
export const TM_SETTINGS_ENTRY = 'core.settings';

/** The two platform sections: Analysis and Administration close the launcher, after every distribution section. */
export const TM_CORE_SECTIONS: readonly TmNavSection[] = [
  { id: 'analysis', labelKey: 'tm.nav.analysis', icon: 'chart-column', order: 800 },
  { id: 'administration', labelKey: 'tm.nav.administration', icon: 'settings', order: 900 },
];

/**
 * The core entries: Settings under Administration, and My settings with its
 * four sections, which are search-only and open to every member. Settings
 * declares no `requires`: it shows with `Read` on any settings category,
 * which the navigation registry reads from the client metadata. Three My
 * settings sections take their synonyms from the launcher's own strings,
 * which only the launcher's search reads.
 */
export const TM_CORE_ENTRIES: readonly TmNavEntry[] = [
  { id: TM_SETTINGS_ENTRY, section: 'administration', order: 40, labelKey: 'tm.admin.settings.title', icon: 'settings', url: (t) => `/${t}/settings` },
  { id: 'core.me', labelKey: 'tm.admin.me.title', icon: 'user-cog', url: (t) => `/${t}/me` },
  { id: 'core.me.profile', labelKey: 'tm.nav.me.profile', synonymsKey: 'tm.launcher.synonyms.meProfile', icon: 'user', url: (t) => `/${t}/me/profile` },
  { id: 'core.me.region', labelKey: 'tm.nav.me.region', synonymsKey: 'tm.launcher.synonyms.meRegion', icon: 'globe', url: (t) => `/${t}/me/region` },
  { id: 'core.me.display', labelKey: 'tm.nav.me.display', synonymsKey: 'tm.launcher.synonyms.meDisplay', icon: 'sun-moon', url: (t) => `/${t}/me/display` },
  { id: 'core.me.notifications', labelKey: 'tm.nav.me.notifications', icon: 'bell', url: (t) => `/${t}/me/notifications` },
];
