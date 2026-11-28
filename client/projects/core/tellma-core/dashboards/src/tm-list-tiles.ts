// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import { computed, type Signal } from '@angular/core';
import type { TmEntities, TmUrls } from '@tellma/core';
import type { TmAttention } from '@tellma/core/attention';

// The built-in list types' headers (spec 0035 §8.4, §9.2). A list type
// shows its count beside the title and its "View all" link where another
// type shows its menu, and its description only in edit mode. Both come
// from the type itself, not from its body, so the header is whole from the
// tile's first frame, before the body's chunk loads, and never changes with
// the body's state. The count follows the stored assignments document live,
// like the list under it.

/** Which list a home row or "View all" belongs to, as the home's telemetry names it. */
export type TmHomeList = 'to-me' | 'by-me' | 'recents';

/** A list type's header: its count and its "View all" link. */
export interface TmTileList {
  /** The list, as the home's telemetry names it. */
  readonly list: TmHomeList;
  /** The count beside the title; `null` shows none, as a count of 0 does. */
  readonly count: Signal<number | null>;
  /** Where "View all" leads; `null` when the list has none. */
  readonly viewAll: string | null;
}

/** The built-in list types, by key, and the list each is. */
const LISTS: Readonly<Record<string, TmHomeList>> = {
  'core.assignments-to-me': 'to-me',
  'core.assignments-by-me': 'by-me',
  'core.recents': 'recents',
};

/**
 * A built-in list type's header, or `null` for any other type.
 *
 * @param type - The widget type's key.
 * @param attention - The attention model, whose assignments document gives the counts.
 * @param urls - The app's URLs, for "View all".
 * @param entities - The entity registry, which says whether the server routes assignments.
 * @returns The header, or `null`.
 */
export function tmTileList(type: string, attention: TmAttention, urls: TmUrls, entities: TmEntities): TmTileList | null {
  const list = LISTS[type];
  if (list === undefined) {
    return null;
  }
  if (list === 'recents') {
    return { list, count: computed(() => null), viewAll: null };
  }

  // --- The assignment lists: the open count, and the Assignments page on the right tab ---
  const count = computed(() => {
    const summary = attention.assignments();
    const value = list === 'to-me' ? summary?.open : summary?.openByMe;
    return value ? value : null;
  });
  // While the server routes no assignments there is no page to view them all on.
  const routed = !!entities.summary('core.Assignment')?.segment;
  return { list, count, viewAll: routed ? urls.search('core.Assignment', list === 'by-me' ? { tab: 'by-me' } : {}) : null };
}
