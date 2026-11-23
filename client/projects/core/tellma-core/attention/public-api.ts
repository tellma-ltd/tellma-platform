/**
 * Public API Surface of @tellma/core/attention — the attention surfaces the
 * shells render on every page: for now the banner host, which shows the
 * row of `TmBanners` and announces each banner as it is queued. It loads
 * with the app, preloaded beside the primary entry point, and imports only
 * that entry point.
 *
 * @packageDocumentation
 */

// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

export { TmBannerHost } from './src/tm-banner-host';
