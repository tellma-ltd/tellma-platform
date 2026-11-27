// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

/// <reference types="@tellma/core-tools/lucide-icons" />

import ArrowLeft from 'lucide/dist/esm/icons/arrow-left.mjs';
import ArrowRight from 'lucide/dist/esm/icons/arrow-right.mjs';
import Check from 'lucide/dist/esm/icons/check.mjs';
import ChevronFirst from 'lucide/dist/esm/icons/chevron-first.mjs';
import ChevronLast from 'lucide/dist/esm/icons/chevron-last.mjs';
import ChevronLeft from 'lucide/dist/esm/icons/chevron-left.mjs';
import ChevronRight from 'lucide/dist/esm/icons/chevron-right.mjs';
import CircleAlert from 'lucide/dist/esm/icons/circle-alert.mjs';
import CircleX from 'lucide/dist/esm/icons/circle-x.mjs';
import HandCoins from 'lucide/dist/esm/icons/hand-coins.mjs';
import Inbox from 'lucide/dist/esm/icons/inbox.mjs';
import LayoutGrid from 'lucide/dist/esm/icons/layout-grid.mjs';
import List from 'lucide/dist/esm/icons/list.mjs';
import LogIn from 'lucide/dist/esm/icons/log-in.mjs';
import LogOut from 'lucide/dist/esm/icons/log-out.mjs';
import PanelLeftClose from 'lucide/dist/esm/icons/panel-left-close.mjs';
import PanelLeftOpen from 'lucide/dist/esm/icons/panel-left-open.mjs';
import Plus from 'lucide/dist/esm/icons/plus.mjs';
import Redo from 'lucide/dist/esm/icons/redo.mjs';
import Search from 'lucide/dist/esm/icons/search.mjs';
import SearchX from 'lucide/dist/esm/icons/search-x.mjs';
import Table2 from 'lucide/dist/esm/icons/table-2.mjs';
import Undo from 'lucide/dist/esm/icons/undo.mjs';
import X from 'lucide/dist/esm/icons/x.mjs';

import type { TmIconSource } from '@tellma/core-ui/icon';

/**
 * The registry names the showcase draws, registered once in app.config.ts
 * the way a distribution's feature registers its own: Lucide data imported
 * one icon at a time, directional glyphs in the object form with
 * `mirrorInRtl`.
 */
export const SHOWCASE_ICONS: Readonly<Record<string, TmIconSource>> = {
  // Glyphs with no reading direction.
  plus: Plus,
  x: X,
  check: Check,
  search: Search,
  'circle-alert': CircleAlert,
  'circle-x': CircleX,
  inbox: Inbox,
  'search-x': SearchX,
  'hand-coins': HandCoins,
  // The segmented story's view toggle.
  'table-2': Table2,
  'layout-grid': LayoutGrid,
  list: List,
  // Directional glyphs: they point along the reading direction, so they flip
  // in right-to-left text.
  'chevron-left': { node: ChevronLeft, mirrorInRtl: true },
  'chevron-right': { node: ChevronRight, mirrorInRtl: true },
  'chevron-first': { node: ChevronFirst, mirrorInRtl: true },
  'chevron-last': { node: ChevronLast, mirrorInRtl: true },
  'arrow-left': { node: ArrowLeft, mirrorInRtl: true },
  'arrow-right': { node: ArrowRight, mirrorInRtl: true },
  'log-in': { node: LogIn, mirrorInRtl: true },
  'log-out': { node: LogOut, mirrorInRtl: true },
  undo: { node: Undo, mirrorInRtl: true },
  redo: { node: Redo, mirrorInRtl: true },
  'panel-left-open': { node: PanelLeftOpen, mirrorInRtl: true },
  'panel-left-close': { node: PanelLeftClose, mirrorInRtl: true },
};
