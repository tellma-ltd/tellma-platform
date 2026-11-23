/**
 * Public API Surface of @tellma/core/recents — the pages the user last
 * visited, which the launcher and the home page list. The app loads it after
 * its first render, when visits start to count.
 *
 * @packageDocumentation
 */

// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

export { TmRecents, type TmRecentPage } from './src/tm-recents';

// The routed page that recents read until the page context lands; not for app code.
export {
  TM_ROUTED_PAGE as ɵTM_ROUTED_PAGE,
  type TmRoutedPage as ɵTmRoutedPage,
} from './src/tm-recents';
