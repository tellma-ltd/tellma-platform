// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

/**
 * The pivot's built-in English strings, under the `pivot` key of the
 * library's strings (`tmUi.pivot.subtotal` in a translation table). They
 * ship with the pivot, not in the core table, and a locale pack or the
 * app's own table translates or overrides them as it does the core's.
 */
export const TM_PIVOT_STRINGS_EN = {
  // A column member's subtotal header; the label is the member's.
  subtotal: '{label} total',
  // Follows a phantom member's label: the parent's own facts, apart from its children's.
  direct: '(direct)',
  // A value cell that does not drill: its tooltip, its description, and what Enter announces.
  notActivatable: 'Only the finest cells open their rows in this report.',
  // Announced when a row member expands, with the rows it reveals.
  rowExpanded: '{label} expanded, {count, plural, one {# row} other {# rows}}',
  // Announced when a column member expands, with the columns it reveals.
  columnExpanded: '{label} expanded, {count, plural, one {# column} other {# columns}}',
  // Announced when a member collapses.
  collapsed: '{label} collapsed',
} as const;
