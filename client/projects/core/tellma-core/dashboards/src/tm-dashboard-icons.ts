// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

/// <reference types="@tellma/core-tools/lucide-icons" />

import type { TmIconRegistry, TmIconSource } from '@tellma/core-ui/icon';
import Bell from 'lucide/dist/esm/icons/bell.mjs';
import ChartColumn from 'lucide/dist/esm/icons/chart-column.mjs';
import CircleAlert from 'lucide/dist/esm/icons/circle-alert.mjs';
import Ellipsis from 'lucide/dist/esm/icons/ellipsis.mjs';
import File from 'lucide/dist/esm/icons/file.mjs';
import FileText from 'lucide/dist/esm/icons/file-text.mjs';
import Globe from 'lucide/dist/esm/icons/globe.mjs';
import Inbox from 'lucide/dist/esm/icons/inbox.mjs';
import LayoutDashboard from 'lucide/dist/esm/icons/layout-dashboard.mjs';
import Link from 'lucide/dist/esm/icons/link.mjs';
import List from 'lucide/dist/esm/icons/list.mjs';
import Lock from 'lucide/dist/esm/icons/lock.mjs';
import Puzzle from 'lucide/dist/esm/icons/puzzle.mjs';
import RefreshCw from 'lucide/dist/esm/icons/refresh-cw.mjs';
import RotateCcwClock from 'lucide/dist/esm/icons/rotate-ccw-clock.mjs';
import Send from 'lucide/dist/esm/icons/send.mjs';
import Settings from 'lucide/dist/esm/icons/settings.mjs';
import SquareDashed from 'lucide/dist/esm/icons/square-dashed.mjs';
import SunMoon from 'lucide/dist/esm/icons/sun-moon.mjs';
import User from 'lucide/dist/esm/icons/user.mjs';
import UserCog from 'lucide/dist/esm/icons/user-cog.mjs';

// The icon names the home page and the grid draw (spec 0035 §8.6, §9.2): a
// recent page's icon, which is its launcher entry's (the default icon of
// each entry kind and the core entries' icons) or else its family's, the
// recents' empty state, the assignment lists' empty states, the tiles'
// not-available and no-access states, the options menu with its Refresh and
// the status line's stale marker.
// No surface of the first frame draws them, so they load with this entry
// point rather than with the platform's first-frame names, as the launcher's
// do with its panel. A name the app already registered keeps its own glyph:
// a distribution may draw `settings` its own way.

/** The home page's and the grid's icons, by name. */
const ICONS: Readonly<Record<string, TmIconSource>> = {
  bell: Bell,
  'chart-column': ChartColumn,
  'circle-alert': CircleAlert,
  ellipsis: Ellipsis,
  file: File,
  'file-text': FileText,
  globe: Globe,
  inbox: Inbox,
  history: RotateCcwClock,
  'layout-dashboard': LayoutDashboard,
  link: Link,
  list: List,
  lock: Lock,
  puzzle: Puzzle,
  'refresh-cw': RefreshCw,
  send: { node: Send, mirrorInRtl: true },
  settings: Settings,
  'square-dashed': SquareDashed,
  'sun-moon': SunMoon,
  user: User,
  'user-cog': UserCog,
};

/**
 * Registers the home page's and the grid's icon names that the app has not
 * registered.
 *
 * @param registry - The app's icon registry.
 */
export function tmRegisterDashboardIcons(registry: TmIconRegistry): void {
  registry.register(Object.fromEntries(Object.entries(ICONS).filter(([name]) => registry.get(name) === undefined)));
}
