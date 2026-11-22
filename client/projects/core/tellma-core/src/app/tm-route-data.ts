// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import type { TmPageKind } from '../context/tm-page-kind';
import type { TmLazyComponent, TmSecurableRef } from '../declarations/tm-declaration-types';

// The data of page routes (spec 0024 §4.4). Each page route's data names its
// page family and skeleton, and the route host draws the skeleton in the
// click's frame, before the page loads.

/**
 * The skeleton a route host draws in the click's frame, before the page
 * loads: the final geometry of the page family that replaces it. `me` is the
 * My settings hub, and `blank` is a page with no family of its own.
 */
export type TmRouteSkeleton = 'search' | 'details' | 'report' | 'dashboard' | 'home' | 'me' | 'blank';

/**
 * The data of a page route, whose component is the route host.
 *
 * @example
 * ```ts
 * const data: TmRouteData = { kind: 'settings', skeleton: 'details', titleKey: 'tm.admin.settings.title', load };
 * ```
 */
export interface TmRouteData {
  /** The page's family. */
  readonly kind: TmPageKind;
  /** The skeleton the host draws before the page loads. */
  readonly skeleton: TmRouteSkeleton;
  /** The page's component, for a static page; an entity route's host resolves its own. */
  readonly load?: TmLazyComponent;
  /** The string key of the page's title. */
  readonly titleKey?: string;
  /** The permission the page requires; without it the host renders the `forbidden` view in place. */
  readonly securable?: TmSecurableRef;
}
