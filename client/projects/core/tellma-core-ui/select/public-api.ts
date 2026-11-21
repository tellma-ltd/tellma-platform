/**
 * Public API Surface of @tellma/core-ui/select — tm-select (one choice) and
 * tm-multi-select (several), dropdowns over a CDK Overlay panel composed
 * with the `@angular/aria` combobox/listbox directives, reading the same
 * tm-option and tm-option-group definitions, plus the shell both selects
 * share (exported so their inherited API stays visible).
 *
 * @packageDocumentation
 */

// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.
export { TmSelect } from './tm-select';
export { TmMultiSelect } from './tm-multi-select';
export { TmOption } from './tm-option';
export { TmOptionGroup } from './tm-option-group';

// The shell both selects extend; its members are part of their public API.
export { TmSelectBase, type TmSelectEntry } from './tm-select-base';
