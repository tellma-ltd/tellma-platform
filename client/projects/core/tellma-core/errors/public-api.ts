/**
 * Public API Surface of @tellma/core/errors — error presentation: the one
 * table that turns an API error into what the user sees, the inline error
 * state a page renders for a failed read, the shared dialogs a failed write
 * or action shows, the step-up question, and the handling of a tenant that
 * is gone or suspended. The app loads it when it first needs it.
 *
 * @packageDocumentation
 */

// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

export { TmErrorPresenter } from './src/tm-error-presenter';
export { TmErrorState } from './src/tm-error-state';
export type { TmErrorAction, TmErrorContext, TmErrorView } from './src/tm-error-view';
