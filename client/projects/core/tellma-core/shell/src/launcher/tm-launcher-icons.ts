// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

/// <reference types="@tellma/core-tools/lucide-icons" />

import type { TmIconRegistry, TmIconSource } from '@tellma/core-ui/icon';
import Bell from 'lucide/dist/esm/icons/bell.mjs';
import ChartColumn from 'lucide/dist/esm/icons/chart-column.mjs';
import File from 'lucide/dist/esm/icons/file.mjs';
import FileText from 'lucide/dist/esm/icons/file-text.mjs';
import Globe from 'lucide/dist/esm/icons/globe.mjs';
import LayoutDashboard from 'lucide/dist/esm/icons/layout-dashboard.mjs';
import Link from 'lucide/dist/esm/icons/link.mjs';
import List from 'lucide/dist/esm/icons/list.mjs';
import Pin from 'lucide/dist/esm/icons/pin.mjs';
import Plus from 'lucide/dist/esm/icons/plus.mjs';
import Settings from 'lucide/dist/esm/icons/settings.mjs';
import Settings2 from 'lucide/dist/esm/icons/settings-2.mjs';
import SquareDashed from 'lucide/dist/esm/icons/square-dashed.mjs';
import SunMoon from 'lucide/dist/esm/icons/sun-moon.mjs';
import User from 'lucide/dist/esm/icons/user.mjs';
import UserCog from 'lucide/dist/esm/icons/user-cog.mjs';

// The icon names the launcher draws (spec 0027 §3.2, §9.3, §9.4): the
// default icon of each entry kind, the core sections' and the core entries'
// icons, a recent page's icon by family, the pinned glyph, the create
// row's plus and the Manage pins command's gear. The Manage pins dialog
// draws its rows' icons from the same names. No surface of the first frame
// draws them, so they load in a chunk of their own beside the launcher's
// panel rather than with the platform's first-frame names. A name the app
// already registered keeps its own glyph: a distribution may draw
// `settings` its own way.

/** The launcher's icons, by name. */
const ICONS: Readonly<Record<string, TmIconSource>> = {
  bell: Bell,
  'chart-column': ChartColumn,
  file: File,
  'file-text': FileText,
  globe: Globe,
  'layout-dashboard': LayoutDashboard,
  link: Link,
  list: List,
  pin: Pin,
  plus: Plus,
  settings: Settings,
  'settings-2': Settings2,
  'square-dashed': SquareDashed,
  'sun-moon': SunMoon,
  user: User,
  'user-cog': UserCog,
};

/**
 * Registers the launcher's icon names that the app has not registered.
 *
 * @param registry - The app's icon registry.
 */
export function tmRegisterLauncherIcons(registry: TmIconRegistry): void {
  registry.register(Object.fromEntries(Object.entries(ICONS).filter(([name]) => registry.get(name) === undefined)));
}
