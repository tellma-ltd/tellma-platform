// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import type { TmStringTree } from '@tellma/core';

// The details page's English strings (spec 0031 §3.4, §5.1–§5.3, §10.1),
// merged under `tm.details` when the page first renders. A language pack
// carries the same tree; the Arabic one is @tellma/locale-ar/details-strings.

/** The details page's English strings, merged under `tm.details` when the page is first created; a language pack carries the same tree. */
export const TM_DETAILS_STRINGS_EN = {
  edit: 'Edit',
  create: 'New',
  save: 'Save',
  cancel: 'Cancel',
  refresh: 'Refresh',
  actions: 'Actions',
  active: 'Active',
  inactive: 'Inactive',
  unsaved: 'Unsaved changes',
  loading: 'Loading',
  editing: 'Editing {name}',
  saved: 'Saved',
  discard: {
    title: 'Discard changes?',
    message: 'Your changes to this record will be lost.',
    confirm: 'Discard changes',
    keep: 'Keep editing',
  },
  outsideFilter: "Your “{permission}” permission doesn't cover this record.",
} as const satisfies TmStringTree;
