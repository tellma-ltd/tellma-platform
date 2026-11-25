/**
 * Public API Surface of @tellma/core/entity-editing — the edit side of the
 * entity pages, which a page loads when it edits and which a read-only page
 * never reaches: the value editor a value descriptor chooses, a form's
 * uploads, the entity picker binding with the entity fields built on it and
 * the page a picker opens, and the grid columns of a form's grid that edits
 * its cells. It builds on `@tellma/core/entity`'s view side (the select
 * plans, the formats, the blob checks and the picker pages' contract).
 *
 * @packageDocumentation
 */

// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

// Typed values in edit mode: the editor, a labelled one, and a form's uploads.
export { TmValueEditor } from './src/tm-value-editor';
export { ɵTmValueFormField } from './src/tm-value-form-field';
export { TmUploads } from './src/tm-uploads';

// Entity pickers: the binding, the fields and the page they open.
export { TmEntityPickers, type TmEntityPickerBinding, type TmEntityPickerOptions } from './src/tm-entity-pickers';
export { TmEntityField } from './src/tm-entity-field';
export { TmEntityMultiField } from './src/tm-entity-multi-field';
export { TmLazyPickerPage } from './src/tm-lazy-picker-page';

// The grid columns of a form's grid that edits its cells.
export { tmEditableGridColumnFor, type TmEditableGridColumnOptions } from './src/tm-editable-grid-column';
