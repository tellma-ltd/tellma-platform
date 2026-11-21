// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

// The skeletons of page routes (spec 0024 §4.4). Each page route's data names
// one, and the route host draws it in the click's frame, before the page
// loads.

/**
 * The skeleton a route host draws in the click's frame, before the page
 * loads: the final geometry of the page family that replaces it. `me` is the
 * My settings hub, and `blank` is a page with no family of its own.
 */
export type TmRouteSkeleton = 'search' | 'details' | 'report' | 'dashboard' | 'home' | 'me' | 'blank';
