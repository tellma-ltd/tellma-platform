// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import { TmLocalization } from '@tellma/core';

// The English strings of the attention rows, under `tm.attentionRows`
// (spec 0028 §7.3, §11.3): an assignment's caption title and the assignment
// list's lines. They ship with this entry point, which loads with the first
// surface that renders a row, never with the first frame; each service and
// component registers them when it is created. A language's table that
// holds a key wins over them.

/** The scope of the rows' strings under `tm`. */
const SCOPE = 'attentionRows';

/** The attention rows' English strings, under `tm.attentionRows`. */
export const TM_ATTENTION_ROWS_STRINGS_EN = {
  /** An assignment's caption: the entity's singular label and the target's code. */
  assignment: { title: '{entity} {code}' },
  /** The assignment list's second lines, its mark and its loading state. */
  list: {
    comment: '{assigner}: {comment}',
    returned: 'Returned by {assigner}: {comment}',
    assignedBy: 'Assigned by {assigner}',
    automatic: 'Assigned automatically',
    with: 'With {assignee}',
    // A home row's comment alone, kept a message so a language can quote it.
    quote: '{comment}',
    // What joins a caption's detail and amount on one line.
    joiner: ' · ',
    new: 'New',
    loading: 'Loading assignments',
  },
  /** What joins the parts of a row's accessible name. */
  separator: ', ',
} as const;

/**
 * Registers the attention rows' strings, once per app; a later call does nothing.
 *
 * @param l10n - The app's localization.
 */
export function tmRegisterAttentionRowsStrings(l10n: TmLocalization): void {
  l10n.ɵregisterStrings(SCOPE, TM_ATTENTION_ROWS_STRINGS_EN);
}
