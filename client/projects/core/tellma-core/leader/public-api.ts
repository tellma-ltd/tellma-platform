/**
 * Public API Surface of @tellma/core/leader — the leader election, which
 * decides the one tab per browser and tenant that holds the realtime
 * connection, refreshes the leader documents and polls when realtime is
 * down; and the followers' checks on it. The app loads it after its first
 * render; the rest of the app reads what it decides through `TmLeader`.
 * Platform-internal: nothing here is for app code.
 *
 * @packageDocumentation
 */

// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

// The election, which the startup creates; the browser's locks, which a test
// replaces; and the realtime connection's factory, which the realtime entry
// point provides. Not for app code.
export { TM_LEADER_LOCKS as ɵTM_LEADER_LOCKS, TmLeaderElection as ɵTmLeaderElection } from './src/tm-leader-election';
export { TM_HUB_FACTORY as ɵTM_HUB_FACTORY, type TmHubFactory as ɵTmHubFactory } from './src/tm-leadership';
