/**
 * Public API Surface of @tellma/core/updates — the app's updates through its
 * service worker: the worker's registration through the `tellma` Trusted
 * Types policy, the update checks, the reload into a new version, and the
 * update banner. The app loads it after its first render.
 *
 * @packageDocumentation
 */

// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

export { TmUpdates, type TmUpdateCheck } from './src/tm-updates';

// The browser behind the updates, which a test replaces; not for app code.
export {
  TM_UPDATES_HOST as ɵTM_UPDATES_HOST,
  type TmSwUpdates as ɵTmSwUpdates,
  type TmUpdatesContainer as ɵTmUpdatesContainer,
  type TmUpdatesHost as ɵTmUpdatesHost,
} from './src/tm-updates';
export type {
  TmScriptUrlPolicy as ɵTmScriptUrlPolicy,
  TmScriptUrlRules as ɵTmScriptUrlRules,
  TmTrustedTypes as ɵTmTrustedTypes,
  TmWorkerContainer as ɵTmWorkerContainer,
} from './src/tm-worker-registration';
