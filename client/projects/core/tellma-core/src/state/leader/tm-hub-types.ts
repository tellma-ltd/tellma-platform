// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import type { TmJson } from '../../json/tm-json';

// The realtime vocabulary (spec 0025 §7.1, §8.1): the hub connection's state
// and one server event, which the tab bus carries between the leader tab and
// its followers, and the seam between the leader and the realtime connection
// it loads. Types only: none of it weighs anything in the bundle.

/**
 * The state of a tenant's realtime connection, as its leader tab holds it:
 * `disabled` when realtime is off on this deployment.
 */
export type TmHubState = 'connecting' | 'connected' | 'reconnecting' | 'offline' | 'disabled';

/** The leader tab's realtime connection, as every tab of the tenant sees it. */
export interface TmHubStatus {
  /** The connection's state. */
  readonly state: TmHubState;
  /** When the connection entered the state, in epoch milliseconds. */
  readonly since: number;
  /** Whether the leader tab is visible. */
  readonly leaderVisible: boolean;
}

/** One event the server pushed to a tenant's clients. */
export interface TmClientEvent {
  /** The event's name, such as `cache.changed`. */
  readonly name: string;
  /** The tenant the event belongs to. */
  readonly tenantId: number;
  /** The event's payload, or `null`. */
  readonly payload: TmJson | null;
}

/**
 * What a realtime connection reports to the leader tab that holds it.
 * Internal: the realtime entry point calls it.
 */
export interface TmHubSink {
  /**
   * An event the connection received.
   *
   * @param event - The event.
   */
  event(event: TmClientEvent): void;
  /**
   * The connection entered a state.
   *
   * @param state - The state.
   */
  state(state: Exclude<TmHubState, 'disabled'>): void;
  /** The connection (re)connected, after which the leader catches up on what it missed. */
  connected(): void;
  /**
   * A negotiation the server refused for good; the connection stopped trying.
   *
   * @param status - The refusal's HTTP status.
   */
  refused(status: 401 | 403 | 404): void;
  /**
   * The server ended the session or the user's access to the tenant.
   *
   * @param reason - Why.
   */
  ended(reason: 'session-terminated' | 'access-revoked' | 'tenant-unavailable'): void;
}

/** A realtime connection, as the leader tab drives it. Internal. */
export interface TmHubConnection {
  /** Starts connecting, and keeps reconnecting until stopped or refused; it may start again after either. */
  start(): void;
  /** Stops the connection; resolves once it is closed. */
  stop(): Promise<void>;
  /** Cuts the current wait before a reconnect short. */
  nudge(): void;
}
