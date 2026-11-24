// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import type { TmStringTree } from '@tellma/core';

// The expression editor's English strings, under `tm.queryex`. They register
// with `TmLocalization` when the first editor is created, so they ship with
// the editor rather than in the primary's English table; a locale pack's
// table holds their translations under the same keys. The problems
// themselves are the server's `Queryex_*` and `Query_*` messages.

/**
 * The expression editor's English strings, under `tm.queryex`: the status
 * line, the problems list, the parameter chips and the Queryex types'
 * names. Internal: the editor registers them.
 */
export const ɵTM_QUERYEX_STRINGS_EN = {
  noProblems: 'No problems',
  checking: 'Checking…',
  offline: "Can't check while offline",
  failed: "Couldn't check the expression",
  retry: 'Retry',
  problemAt: 'Line {line}: {message}',
  more: '+{count} more',
  problems: '{count, plural, one {# problem} other {# problems}}',
  parameters: 'Parameters',
  untyped: 'No type',
  conflicting: 'Conflicting types',
  type: {
    Bool: 'Yes or no',
    Numeric: 'Number',
    String: 'Text',
    Guid: 'Identifier',
    Date: 'Date',
    DateTime: 'Date and time',
    DateTimeOffset: 'Moment',
    HierarchyId: 'Hierarchy',
    Geography: 'Location',
  },
} as const satisfies TmStringTree;
