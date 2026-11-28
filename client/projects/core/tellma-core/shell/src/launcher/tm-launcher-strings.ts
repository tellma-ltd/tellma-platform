// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

// The launcher's English strings, under `tm.launcher` (spec 0027 §3.2, §3.3,
// §9): the panel's headings, rows, footer and announcements, and the search
// synonyms of the platform's own entries. Only the launcher's chunk reads
// them, so they load with it rather than with the first frame; a language's
// table that holds a key wins over them.

/** The scope of the launcher's strings under `tm`. */
export const TM_LAUNCHER_SCOPE = 'launcher';

/** The launcher's English strings, under `tm.launcher`. */
export const TM_LAUNCHER_STRINGS_EN = {
  /** The listbox's name. */
  results: 'Pages and results',
  /** The hidden instructions that describe the input. */
  hint: 'Type to search. Use the arrow keys to move between options, Enter to open one, and Escape to close.',
  /** Close, beside the field below md. */
  close: 'Close',
  /** The bands' and the typed rows' headings and descriptions. */
  pinned: 'Pinned',
  recent: 'Recently visited',
  pages: 'Pages',
  /** A pinned entry's accessible name. */
  pinnedEntry: '{label}, pinned',
  /** A create row's label: the entity's singular label, then the action. */
  create: '{label} · Create',
  /** The Pages heading's line when no page matches. */
  noMatch: 'Nothing matched “{query}”.',
  /** The footer, from md up. */
  footer: 'Type to search · ↑↓ to move · Enter to open · Esc to close',
  /** The polite announcements, half a second after the last keystroke, and per provider answer. */
  count: '{count, plural, one {# page} other {# pages}}',
  nothing: 'Nothing matched',
  bucketCount: '{bucket}: {count, plural, one {# result} other {# results}}',
  /** The synonyms of the platform's search-only entries, comma-separated. */
  synonyms: {
    meProfile: 'Photo, Signature, Contact',
    meRegion: 'Time zone, Calendar, Digits, Formats',
    meDisplay: 'Theme, Pinned bar',
  },
} as const;
