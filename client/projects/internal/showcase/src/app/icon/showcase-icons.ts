// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import {
  ArrowLeft,
  ArrowRight,
  Check,
  ChevronFirst,
  ChevronLast,
  ChevronLeft,
  ChevronRight,
  CircleAlert,
  HandCoins,
  LogIn,
  LogOut,
  PanelLeftClose,
  PanelLeftOpen,
  Plus,
  Redo,
  Search,
  Undo,
  X,
} from 'lucide';

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
  'hand-coins': HandCoins,
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
