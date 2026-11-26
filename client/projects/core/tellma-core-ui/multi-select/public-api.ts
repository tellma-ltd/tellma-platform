/**
 * Public API Surface of @tellma/core-ui/multi-select — tm-multi-select
 * (several choices), a dropdown over the shell `@tellma/core-ui/select`
 * shares, reading the same tm-option and tm-option-group definitions.
 *
 * It is an entry point of its own so a page that shows only single selects
 * (a pager's page size) never loads it: an app's bundler places the code a
 * lazily loaded form uses from a file in the chunk of every page that
 * reaches that file.
 *
 * @packageDocumentation
 */

// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.
export { TmMultiSelect } from './tm-multi-select';
