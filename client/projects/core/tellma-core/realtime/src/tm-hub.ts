// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import { effect, untracked, type EffectRef, type Injector, type Signal } from '@angular/core';
import type { TmClientEvent, TmHubConnection, TmHubSink, TmHubState, TmJson, TmTelemetry } from '@tellma/core';

// The leader tab's realtime connection and its reconnect loop (spec 0025
// §8.2-§8.5). Each attempt builds a fresh connection; the loop, not the
// library, decides when the next one starts: at once for the first, then
// after 2 s, 10 s and 30 s, then doubling to 5 min, each but the first with
// ±20 % jitter. A connection that lasted a minute resets the count. A wait is
// cut short by a nudge (a follower's probe, the server becoming reachable),
// and while the server is unreachable the loop waits in `offline`. It never
// gives up on a transient failure; a negotiate refused with 401, 403 or 404,
// a user check that fails before a request leaves, and the server's
// `session.ended` stop it, and the leader decides what follows. This file
// holds the loop over an attempt seam; tm-hub-signalr.ts makes the attempts.

/**
 * Why the server refused a connection for good: a negotiate's 401, 403 or
 * 404, or `'user'` when the tab's session check failed before a request left.
 */
export type TmHubRefusal = 401 | 403 | 404 | 'user';

/** What one attempt's connection reports to the loop. */
export interface TmHubAttemptHandlers {
  /**
   * A message of the hub's `event` method arrived.
   *
   * @param envelope - The message's one argument, as the server sent it.
   */
  event(envelope: unknown): void;
  /**
   * The connection closed after it had connected.
   *
   * @param error - Why, or `undefined` when the server closed it cleanly.
   */
  closed(error: Error | undefined): void;
}

/** One attempt to connect, and the connection it makes. */
export interface TmHubAttempt {
  /**
   * Connects.
   *
   * @returns A promise that resolves once the connection is open, and rejects when the attempt fails.
   */
  start(): Promise<void>;
  /**
   * Ends the attempt, or closes its connection: aborts its requests in flight.
   *
   * @returns A promise that settles once the connection is closed.
   */
  stop(): Promise<void>;
  /**
   * Why the server refused the attempt for good, once it did.
   *
   * @returns The refusal, or `null` while there is none.
   */
  refusal(): TmHubRefusal | null;
}

/**
 * Makes one attempt's connection, which reports to the handlers.
 *
 * @param handlers - Where the connection reports.
 * @returns The attempt, not yet started.
 */
export type TmHubConnect = (handlers: TmHubAttemptHandlers) => TmHubAttempt;

/** What the loop runs on. */
export interface TmHubOptions {
  /** Makes each attempt's connection. */
  readonly connect: TmHubConnect;
  /** Whether the server is reachable, as the HTTP layer knows it. */
  readonly online: Signal<boolean>;
  /** Where the loop reports its telemetry. */
  readonly telemetry: TmTelemetry;
  /** The app's clock, in epoch milliseconds. */
  readonly now: () => number;
  /** A random number in [0, 1), for the jitter. */
  readonly random: () => number;
  /** The injector the loop's effect runs in. */
  readonly injector: Injector;
}

/** The longest wait between two attempts, before its jitter. */
const MAX_DELAY_MS = 300_000;

/** How far the jitter moves a wait, either way, as a fraction of it. */
const JITTER = 0.2;

/** How long a connection must last for the attempt count to start over. */
const STABLE_MS = 60_000;

/**
 * How long one attempt may take to connect. The library's own HTTP timeout
 * ends a negotiate that hangs, but nothing ends a transport that never
 * opens, so an attempt that has not connected by then counts as failed.
 */
const START_TIMEOUT_MS = 100_000;

/** How long a stop waits for the connection to close before it reports it closed. */
const STOP_GRACE_MS = 5_000;

/** The event through which the server ends the session or the user's access. */
const SESSION_ENDED = 'session.ended';

/** The reasons the server ends a connection for. */
type TmEndReason = Parameters<TmHubSink['ended']>[0];

/** The `session.ended` reasons this build knows. */
const END_REASONS: readonly string[] = ['session-terminated', 'access-revoked', 'tenant-unavailable'] satisfies TmEndReason[];

/**
 * The wait before an attempt: none for the first, then 2 s, 10 s and 30 s,
 * then the previous wait doubled, at most 5 minutes; every wait but the
 * first moves by up to 20 % either way.
 *
 * @param attempt - The attempt's number since the count last started over, from 1.
 * @param random - A random number in [0, 1).
 * @returns The wait, in milliseconds.
 */
export function tmHubDelay(attempt: number, random: number): number {
  if (attempt <= 1) {
    return 0;
  }
  const base = attempt === 2 ? 2_000 : attempt === 3 ? 10_000 : Math.min(30_000 * 2 ** (attempt - 4), MAX_DELAY_MS);
  return Math.round(base * (1 - JITTER + 2 * JITTER * random));
}

/**
 * The event a hub message carries: its name, its tenant and its payload, or
 * `null` when the message is not an event's envelope.
 *
 * @param envelope - The message's argument.
 * @returns The event, or `null`.
 */
export function tmHubEvent(envelope: unknown): TmClientEvent | null {
  if (typeof envelope !== 'object' || envelope === null) {
    return null;
  }
  const { name, tenantId, payload } = envelope as Readonly<Record<string, unknown>>;
  return typeof name === 'string' && Number.isSafeInteger(tenantId)
    ? { name, tenantId: tenantId as number, payload: (payload ?? null) as TmJson | null }
    : null;
}

/**
 * The leader tab's realtime connection to one tenant's hub: a loop of
 * attempts that keeps it connected until it is stopped or refused.
 * Internal: the realtime entry point creates it.
 */
export class TmHub implements TmHubConnection {
  readonly #sink: TmHubSink;
  readonly #options: TmHubOptions;

  /** Attempts made since the count last started over; the next one waits by its number. */
  #attempts = 0;
  /** Whether a connection of this hub has connected, after which a new attempt reconnects. */
  #connectedOnce = false;
  /** Counts the loop's runs; a stop starts a new one, so the reports of the old one's attempt are ignored. */
  #run = 0;
  #running = false;
  /** The attempt in flight or connected, or `null`. */
  #attempt: TmHubAttempt | null = null;
  /** When the current connection opened, or `null` while none is open. */
  #connectedAt: number | null = null;
  /** The wait before the next attempt, or the attempt's deadline. */
  #timer: ReturnType<typeof setTimeout> | undefined;
  /** Whether the loop waits, so that a nudge may cut the wait short. */
  #waiting = false;
  /** The state last reported to the sink. */
  #reported: TmHubState | null = null;
  /** Nudges the loop when the server becomes reachable, while it runs. */
  #online: EffectRef | null = null;
  /** Settles once the last stopped connection closed. */
  #stopping: Promise<void> = Promise.resolve();

  /**
   * Creates the connection, which does nothing until started.
   *
   * @param sink - Where it reports.
   * @param options - What it runs on.
   */
  constructor(sink: TmHubSink, options: TmHubOptions) {
    this.#sink = sink;
    this.#options = options;
  }

  /** Starts the loop, unless it runs; after a stop or a refusal it goes on counting attempts. */
  start(): void {
    if (this.#running) {
      return;
    }
    this.#running = true;
    this.#reported = null;
    const run = ++this.#run;
    // The server becoming reachable cuts the wait short.
    const online = this.#options.online;
    let was = untracked(online);
    this.#online = effect(
      () => {
        const now = online();
        untracked(() => {
          if (now && !was) {
            this.nudge();
          }
          was = now;
        });
      },
      { injector: this.#options.injector, manualCleanup: true },
    );
    this.#next(run);
  }

  /**
   * Stops the loop and closes the connection.
   *
   * @returns A promise that settles once the connection is closed, or 5 s later at most.
   */
  stop(): Promise<void> {
    const attempt = this.#attempt;
    this.#attempt = null;
    this.#settle();
    this.#halt();
    if (attempt !== null) {
      this.#stopping = new Promise<void>((resolve) => {
        const grace = setTimeout(resolve, STOP_GRACE_MS);
        void attempt
          .stop()
          .catch(() => undefined)
          .then(() => {
            clearTimeout(grace);
            resolve();
          });
      });
    }
    return this.#stopping;
  }

  /** Cuts the wait before the next attempt short; while the server is unreachable, the loop keeps waiting. */
  nudge(): void {
    if (this.#running && this.#waiting) {
      clearTimeout(this.#timer);
      this.#ready(this.#run);
    }
  }

  // --- The loop ---

  /** Waits for the next attempt by its number. */
  #next(run: number): void {
    this.#waiting = true;
    this.#timer = setTimeout(() => this.#ready(run), tmHubDelay(this.#attempts + 1, this.#options.random()));
  }

  /** The wait is over: connects, or waits in `offline` until the server is reachable. */
  #ready(run: number): void {
    this.#timer = undefined;
    if (run !== this.#run) {
      return;
    }
    if (!untracked(this.#options.online)) {
      this.#state('offline');
      return;
    }
    this.#waiting = false;
    this.#connect(run);
  }

  /** Makes one attempt; its success, failure or deadline moves the loop on. */
  #connect(run: number): void {
    const number = ++this.#attempts;
    const startedAt = this.#options.now();
    this.#state(this.#connectedOnce ? 'reconnecting' : 'connecting');
    // The attempt reports only while it is the current one of this run.
    let current: TmHubAttempt | null = null;
    const live = (): boolean => current !== null && run === this.#run && current === this.#attempt;
    const attempt = (current = this.#options.connect({
      event: (envelope) => {
        if (live()) {
          this.#event(envelope);
        }
      },
      closed: (error) => {
        if (live()) {
          this.#closed(run, attempt, error);
        }
      },
    }));
    this.#attempt = attempt;
    // An attempt that has not connected in time fails, and its late outcome is ignored.
    this.#timer = setTimeout(() => {
      if (live()) {
        this.#attempt = null;
        void attempt.stop().catch(() => undefined);
        this.#next(run);
      }
    }, START_TIMEOUT_MS);
    attempt.start().then(
      () => {
        if (live()) {
          clearTimeout(this.#timer);
          const now = (this.#connectedAt = this.#options.now());
          this.#connectedOnce = true;
          this.#state('connected');
          this.#options.telemetry.timing('tm.realtime.connected', now - startedAt, {
            attempt: number === 1 ? '1' : number <= 4 ? '2-4' : '5+',
          });
          this.#sink.connected();
        }
      },
      () => {
        if (live()) {
          clearTimeout(this.#timer);
          this.#attempt = null;
          const refusal = attempt.refusal();
          if (refusal === null) {
            this.#next(run);
          } else {
            this.#refuse(refusal);
          }
        }
      },
    );
  }

  /** The open connection closed: the loop tries again, unless the server refused the tab for good. */
  #closed(run: number, attempt: TmHubAttempt, error: Error | undefined): void {
    this.#attempt = null;
    this.#settle();
    this.#options.telemetry.event('tm.realtime.disconnected', {
      reason: !untracked(this.#options.online) ? 'offline' : error === undefined ? 'closed' : 'error',
    });
    this.#state('reconnecting');
    const refusal = attempt.refusal();
    if (refusal === null) {
      this.#next(run);
    } else {
      this.#refuse(refusal);
    }
  }

  /** A terminal refusal: the loop stops, and the leader decides what follows. */
  #refuse(refusal: TmHubRefusal): void {
    this.#halt();
    this.#options.telemetry.event('tm.realtime.refused', { status: refusal === 'user' ? 'user-check' : `${refusal}` });
    this.#sink.refused(refusal === 'user' ? 401 : refusal);
  }

  /** A connection that ends after a minute open starts the attempt count over. */
  #settle(): void {
    if (this.#connectedAt !== null && this.#options.now() - this.#connectedAt >= STABLE_MS) {
      this.#attempts = 0;
    }
    this.#connectedAt = null;
  }

  /** Ends the loop's run: no wait, deadline or nudge outlives it. */
  #halt(): void {
    this.#running = false;
    this.#run++;
    this.#waiting = false;
    clearTimeout(this.#timer);
    this.#timer = undefined;
    this.#online?.destroy();
    this.#online = null;
  }

  /** Reports a state the sink has not heard yet. */
  #state(state: Exclude<TmHubState, 'disabled'>): void {
    if (state !== this.#reported) {
      this.#reported = state;
      this.#sink.state(state);
    }
  }

  // --- Events ---

  /**
   * Hands every well-formed event to the sink. `session.ended` also ends the
   * connection, which the server closes anyway, and stops the loop.
   */
  #event(envelope: unknown): void {
    const event = tmHubEvent(envelope);
    if (event === null) {
      return;
    }
    this.#sink.event(event);
    const reason = event.name === SESSION_ENDED ? (event.payload as { readonly reason?: unknown } | null)?.reason : undefined;
    if (typeof reason === 'string' && END_REASONS.includes(reason)) {
      this.#options.telemetry.event('tm.realtime.disconnected', { reason: 'ended' });
      this.#state('reconnecting');
      void this.stop();
      this.#sink.ended(reason as TmEndReason);
    }
  }
}
