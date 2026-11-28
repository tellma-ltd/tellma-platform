/**
 * Public API Surface of @tellma/core/memory — the tab's memory of its pages:
 * page state and tree expansion, which the page hosts read and write and
 * which load with them, not with the app. A tab's drafts are
 * `@tellma/core/drafts`'.
 *
 * @packageDocumentation
 */

// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

export { TmPageState, tmPageStateKey, type TmPageStateKey, type TmRememberOptions } from './src/tm-page-state';
export { TmTreeExpansion } from './src/tm-tree-expansion';
