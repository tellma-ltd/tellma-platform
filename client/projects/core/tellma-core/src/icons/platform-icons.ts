// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

/// <reference types="@tellma/core-tools/lucide-icons" />

import type { TmIconSource } from '@tellma/core-ui/icon';
import ArrowLeft from 'lucide/dist/esm/icons/arrow-left.mjs';
import ArrowRight from 'lucide/dist/esm/icons/arrow-right.mjs';
import ChevronFirst from 'lucide/dist/esm/icons/chevron-first.mjs';
import ChevronLast from 'lucide/dist/esm/icons/chevron-last.mjs';
import ChevronLeft from 'lucide/dist/esm/icons/chevron-left.mjs';
import ChevronRight from 'lucide/dist/esm/icons/chevron-right.mjs';
import LogIn from 'lucide/dist/esm/icons/log-in.mjs';
import LogOut from 'lucide/dist/esm/icons/log-out.mjs';
import PanelLeftClose from 'lucide/dist/esm/icons/panel-left-close.mjs';
import PanelLeftOpen from 'lucide/dist/esm/icons/panel-left-open.mjs';
import Redo from 'lucide/dist/esm/icons/redo.mjs';
import Undo from 'lucide/dist/esm/icons/undo.mjs';
import WifiOff from 'lucide/dist/esm/icons/wifi-off.mjs';

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
  'wifi-off': WifiOff,
};
