// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import { Injectable } from '@angular/core';
import type { TmClientEvent, TmHubConnection, TmHubSink, TmJson } from '@tellma/core';
import type { ɵTmHubFactory } from '@tellma/core/leader';

// The realtime connection in a test: no SignalR client and no server. The
// leader tab's connection is one of these, which records what the leader
// asks of it and lets the test report what a real one would: that it
// connected or dropped, the server's events, a refusal and the server's end.

/** The reasons for which the server ends a connection. */
type TmFakeHubEndReason = Parameters<TmHubSink['ended']>[0];

/**
 * One realtime connection in a test, which the test drives through the
 * sink the leader gave it.
 *
 * @example
 * ```ts
 * // Once the tab leads and the leader opened its connection:
 * const connection = TestBed.inject(TmFakeHub).connections[0]!;
 * connection.connect();
 * connection.emit('acme.jobs.changed', { id: 4 });
 * ```
 */
export class TmFakeHubConnection implements TmHubConnection {
  /** How many times the leader started it. */
  starts = 0;
  /** How many times the leader stopped it. */
  stops = 0;
  /** How many times the leader cut its wait short. */
  nudges = 0;
  /** The tenant it connects to. */
  readonly tenantId: number;
  /** Where it reports: the sink the leader gave it. */
  readonly sink: TmHubSink;

  /**
   * Creates the connection.
   *
   * @param tenantId - The tenant it connects to.
   * @param sink - Where it reports.
   */
  constructor(tenantId: number, sink: TmHubSink) {
    this.tenantId = tenantId;
    this.sink = sink;
  }

  /** Counts a start. */
  start(): void {
    this.starts++;
  }

  /**
   * Counts a stop.
   *
   * @returns A promise that settles at once.
   */
  stop(): Promise<void> {
    this.stops++;
    return Promise.resolve();
  }

  /** Counts a nudge. */
  nudge(): void {
    this.nudges++;
  }

  /** Reports that the connection opened, as after a negotiate and a handshake. */
  connect(): void {
    this.sink.state('connected');
    this.sink.connected();
  }

  /** Reports that the connection dropped, and that it reconnects. */
  drop(): void {
    this.sink.state('reconnecting');
  }

  /**
   * Reports an event of the server's.
   *
   * @param name - The event's name.
   * @param payload - Its payload.
   * @param tenantId - Its tenant; the connection's own by default.
   */
  emit(name: string, payload: TmJson | null = null, tenantId = this.tenantId): void {
    const event: TmClientEvent = { name, tenantId, payload };
    this.sink.event(event);
  }

  /**
   * Reports a negotiate the server refused for good.
   *
   * @param status - The refusal's status.
   */
  refuse(status: 401 | 403 | 404): void {
    this.sink.refused(status);
  }

  /**
   * Reports that the server ended the session or the user's access: the
   * `session.ended` event, then the end, as a real connection does.
   *
   * @param reason - Why the server ended it.
   */
  end(reason: TmFakeHubEndReason): void {
    this.emit('session.ended', { reason });
    this.sink.ended(reason);
  }
}

/**
 * The realtime connections of a test app, in place of the SignalR client:
 * each connection the leader tab opens is a {@link TmFakeHubConnection},
 * oldest first. `provideTellmaStateTesting()` provides it.
 */
@Injectable()
export class TmFakeHub {
  readonly #connections: TmFakeHubConnection[] = [];

  /**
   * Opens a connection for the leader: the factory the leader election
   * calls in place of the realtime entry point's. Internal.
   */
  readonly factory: ɵTmHubFactory = (tenantId, sink) => {
    const connection = new TmFakeHubConnection(tenantId, sink);
    this.#connections.push(connection);
    return Promise.resolve(connection);
  };

  /** Every connection the leader opened, oldest first. */
  get connections(): readonly TmFakeHubConnection[] {
    return this.#connections;
  }
}
