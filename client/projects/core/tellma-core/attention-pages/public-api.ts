/**
 * Public API Surface of @tellma/core/attention-pages — the attention pages'
 * bodies: the Notifications section of My settings
 * (`tm-notification-settings`), which edits the signed-in user's or another
 * user's arrival pop-ups and notification channels and saves every change
 * itself, and its page (`tm-notification-settings-page`), which the hub
 * renders and which publishes the section's page context. It loads with
 * the pages that show it, never with the app's first frame.
 *
 * @packageDocumentation
 */

// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

export { TmNotificationSettings } from './src/tm-notification-settings';
export { TmNotificationSettingsPage } from './src/tm-notification-settings-page';
export {
  type TmNotificationPreference,
  type TmNotificationSettingsUser,
  type TmTestNotificationResult,
} from './src/tm-notification-settings-model';
