/**
 * Public API Surface of @tellma/core/realtime — the leader tab's realtime
 * connection to its tenant's hub, over the SignalR client, with its own
 * reconnect loop. The leader election loads it once a tab leads, so no other
 * tab downloads the client. Platform-internal: nothing here is for app code.
 *
 * @packageDocumentation
 */

// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

// The connection's factory, which the leader election calls. Not for app code.
export { tmCreateHub } from './src/tm-create-hub';
