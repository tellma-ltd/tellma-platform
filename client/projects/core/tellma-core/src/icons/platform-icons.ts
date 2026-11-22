// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import type { TmIconSource } from '@tellma/core-ui/icon';
import {
  ArrowLeft,
  ArrowRight,
  ChevronFirst,
  ChevronLast,
  ChevronLeft,
  ChevronRight,
  LogIn,
  LogOut,
  PanelLeftClose,
  PanelLeftOpen,
  Redo,
  Undo,
} from 'lucide';

// The platform's icon names (spec 0024 §2.2): the one place the platform
// registers names, covering every name a platform declaration, shell or page
// uses. The directional glyphs (spec 0023 §3.1) take the object form with
// `mirrorInRtl`, so they point the reading direction's way in Arabic. A
// platform surface that uses a new name adds it here; the icon's development
// error for an unknown name is the check.

/** The platform's icons by name. */
export const TM_PLATFORM_ICONS: Readonly<Record<string, TmIconSource>> = {
  'arrow-left': { node: ArrowLeft, mirrorInRtl: true },
  'arrow-right': { node: ArrowRight, mirrorInRtl: true },
  'chevron-first': { node: ChevronFirst, mirrorInRtl: true },
  'chevron-last': { node: ChevronLast, mirrorInRtl: true },
  'chevron-left': { node: ChevronLeft, mirrorInRtl: true },
  'chevron-right': { node: ChevronRight, mirrorInRtl: true },
  'log-in': { node: LogIn, mirrorInRtl: true },
  'log-out': { node: LogOut, mirrorInRtl: true },
  'panel-left-close': { node: PanelLeftClose, mirrorInRtl: true },
  'panel-left-open': { node: PanelLeftOpen, mirrorInRtl: true },
  redo: { node: Redo, mirrorInRtl: true },
  undo: { node: Undo, mirrorInRtl: true },
};
