/**
 * Public API Surface of @tellma/core/search-normalize — the one text
 * normalization behind the platform's client-side matching: the launcher,
 * the keyboard shortcuts filter, the field tree and the reports list fold
 * both the query and the terms through it, so Arabic and Latin text match
 * however they were typed or pasted. It loads with whichever code matches,
 * never with the app's first frame.
 *
 * @packageDocumentation
 */

// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

export { tmSearchNormalize } from './src/tm-search-normalize';
