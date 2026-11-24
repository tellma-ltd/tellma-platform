/**
 * Public API Surface of @tellma/core/attention — the attention surfaces the
 * shells render on every page: the banner host, which shows the row of
 * `TmBanners` and announces each banner as it is queued; the attention
 * model behind the bell and the tray, with its badges, panels and verbs;
 * the arrival engine, which toasts what arrives in the visible, focused
 * tab; the outcome toasts of background work; and the rendering of
 * notification texts, job labels and assignment captions. It loads with the
 * app, preloaded beside the primary entry point, and imports only that
 * entry point.
 *
 * @packageDocumentation
 */

// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

export { TmBannerHost } from './src/tm-banner-host';
export { TmAttention, type TmBellBadge, type TmPanel, type TmTrayBadge } from './src/tm-attention';
export { TmArrivals, tmNotificationToastsKey } from './src/tm-arrivals';
export {
  TmAttentionText,
  type TmAssignmentCaption,
  type TmJobLabelSource,
  type TmNotificationText,
  type TmTextSegment,
} from './src/tm-attention-text';
export { TmOutcomeToasts } from './src/tm-outcome-toasts';
