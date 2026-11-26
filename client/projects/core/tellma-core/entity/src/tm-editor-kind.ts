// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import type { TmValueDescriptor } from '@tellma/core';

// Which control a typed value takes in edit mode (spec 0029 §12.2's control
// column), decided by its type facts alone. The decision lives on the view
// side, apart from the controls, so the entity actions' front can tell
// whether a generated dialog edits every argument without loading the
// editors; the value editor renders the control it names.

/**
 * The control a value takes. Internal: the value editor renders it.
 */
export type ɵTmEditorKind =
  | 'readonly'
  | 'text'
  | 'textarea'
  | 'plain'
  | 'integer'
  | 'exact'
  | 'bool'
  | 'date'
  | 'enum'
  | 'enums'
  | 'image'
  | 'file'
  | 'entity'
  | 'entities';

/** The longest text a single-line input takes; longer, or unbounded, text takes a textarea. */
const SINGLE_LINE_MAX = 255;

/**
 * The control a writable value takes by its type facts: a reference takes
 * the entity field, and each type its control. Internal: the value editor
 * picks its control by it.
 *
 * @param of - The value's type facts.
 * @returns The control; `readonly` for a type with none.
 */
export function ɵtmEditorKind(of: TmValueDescriptor): ɵTmEditorKind {
  if (of.target !== undefined) {
    return of.array ? 'entities' : 'entity';
  }
  switch (of.type) {
    case 'string':
      return of.maxLength !== undefined && of.maxLength <= SINGLE_LINE_MAX ? 'text' : 'textarea';
    case 'guid':
    case 'time':
      return 'plain';
    case 'integer':
      return of.long ? 'exact' : 'integer';
    case 'decimal':
      return 'exact';
    case 'bool':
      return 'bool';
    case 'date':
      return 'date';
    case 'enum':
      return of.array ? 'enums' : 'enum';
    default:
      // Instants and date-times are read-only here; floats, JSON and binary have no control.
      return 'readonly';
  }
}

/**
 * Whether the value editor edits a value of these type facts, rather than
 * showing it as read-only text. Internal: an action's generated dialog
 * needs a control for each argument.
 *
 * @param of - The value's type facts.
 * @returns Whether the value takes a control.
 */
export function ɵtmEditable(of: TmValueDescriptor): boolean {
  return ɵtmEditorKind(of) !== 'readonly';
}
