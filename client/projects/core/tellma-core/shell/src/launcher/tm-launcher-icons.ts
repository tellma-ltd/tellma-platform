// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import type { TmIconRegistry, TmIconSource } from '@tellma/core-ui/icon';
import {
  Bell,
  ChartColumn,
  File,
  FileText,
  Globe,
  LayoutDashboard,
  Link,
  List,
  Pin,
  Plus,
  Settings,
  SquareDashed,
  SunMoon,
  User,
  UserCog,
} from 'lucide';

// The icon names the launcher draws (spec 0027 §3.2, §9.3, §9.4): the
// default icon of each entry kind, the core sections' and the core entries'
// icons, a recent page's icon by family, the pinned glyph and the create
// row's plus. No surface of the first frame draws them, so they load in a
// chunk of their own beside the launcher's panel rather than with the
// platform's first-frame names. A name the app already registered keeps its
// own glyph: a distribution may draw `settings` its own way.

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
