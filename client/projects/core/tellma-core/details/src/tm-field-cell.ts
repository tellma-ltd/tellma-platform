// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import type { Signal } from '@angular/core';
import type { TmNavigationDescriptor, TmPathResolution, TmPropertyDescriptor, TmValueDescriptor } from '@tellma/core';
import type { ɵTmFieldEditorEditing } from '@tellma/core/entity-editing';

// One cell of a tm-field, as the field and its lazily loaded editor share
// it (spec 0031 §6.2): a scalar, a twin, one slot of a group, a navigation,
// a blob or a dotted path. Types only, so the editor, which loads with the
// edit side, and the field agree on it without the field's code reaching
// the editor's.

/** One labelled value a `tm-field` renders: a group anchor renders one per admitted slot. */
export interface ɵTmFieldCell {
  /** The path the cell registers: the property's PascalCase name (`Name2`, `CurrencyId`), or a dotted path. */
  readonly key: string;
  /** The label's key. */
  readonly labelKey: string;
  /** The label, in the active language. */
  readonly label: Signal<string>;
  /** The member a control writes, such as `currencyId`; `null` for a dotted path, which never edits. */
  readonly json: string | null;
  /** What `tm-value` reads the value by in view mode. */
  readonly view: TmValueDescriptor | TmPathResolution;
  /** What the editor edits: the property, or the navigation whose key the entity field writes; `null` when nothing edits it. */
  readonly edits: TmPropertyDescriptor | TmNavigationDescriptor | null;
  /** The ownership of what the cell writes. */
  readonly ownership: TmPropertyDescriptor['ownership'];
  /** Whether the value takes several lines: unbounded text, or text longer than 255 characters. */
  readonly multiline: boolean;
  /** Whether the cell takes two grid columns. */
  readonly wide: boolean;
}

/** What a cell's editor renders with, beside the cell. */
export type ɵTmFieldEditing = ɵTmFieldEditorEditing;
