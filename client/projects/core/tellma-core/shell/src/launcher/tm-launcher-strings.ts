// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

// The launcher's English strings, under `tm.launcher`: the search synonyms
// of the platform's own entries (spec 0027 §3.2, §3.3). Only the launcher's
// search reads them, so they load with it rather than with the first frame;
// a language's table that holds a key wins over them.

/** The scope of the launcher's strings under `tm`. */
export const TM_LAUNCHER_SCOPE = 'launcher';

/** The launcher's English strings, under `tm.launcher`. */
export const TM_LAUNCHER_STRINGS_EN = {
  /** The synonyms of the platform's search-only entries, comma-separated. */
  synonyms: {
    meProfile: 'Photo, Signature, Contact',
    meRegion: 'Time zone, Calendar, Digits, Formats',
    meDisplay: 'Theme, Pinned bar',
  },
} as const;
