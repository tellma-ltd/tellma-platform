// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import type { TmJson } from '../../json/tm-json';

// The realtime vocabulary the tab bus carries between the leader tab and its
// followers (spec 0025 §7.1): the hub connection's state and one server
// event, as the hub delivers it.

/**
 * The state of a tenant's realtime connection, as its leader tab holds it:
 * `disabled` when realtime is off on this deployment.
 */
export type TmHubState = 'connecting' | 'connected' | 'reconnecting' | 'offline' | 'disabled';

/** One event the server pushed to a tenant's clients. */
export interface TmClientEvent {
  /** The event's name, such as `cache.changed`. */
  readonly name: string;
  /** The tenant the event belongs to. */
  readonly tenantId: number;
  /** The event's payload, or `null`. */
  readonly payload: TmJson | null;
}
