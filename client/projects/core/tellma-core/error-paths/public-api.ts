/**
 * Public API Surface of @tellma/core/error-paths — the server's error paths
 * onto a form: the splitter that turns a validation item's path back into
 * its member and index segments, and the walk that follows them through a
 * Signal Forms tree, guided by the entity the form holds. An entity action's
 * dialog and the details page's save land their errors through it. It loads
 * with whichever code imports it, never with the app's first frame.
 *
 * @packageDocumentation
 */

// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

export {
  tmSplitErrorPath,
  tmWalkErrorPath,
  type TmErrorPathSegment,
  type TmErrorPathTarget,
  type TmErrorPathWalkOptions,
} from './src/tm-error-paths';
