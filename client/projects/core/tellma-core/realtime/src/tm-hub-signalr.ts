// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import {
  type AbortSignal as TmSignalRAbortSignal,
  DefaultHttpClient,
  HttpError,
  type HttpRequest,
  type HttpResponse,
  HubConnectionBuilder,
  type ILogger,
  LogLevel,
  NullLogger,
} from '@microsoft/signalr';
import type { TmSessionCheck, TmTelemetry } from '@tellma/core';

import type { TmHubAttempt, TmHubAttemptHandlers, TmHubConnect, TmHubRefusal } from './tm-hub';

// One attempt's connection over the SignalR client (spec 0025 §8.2): the JSON
// protocol, the library's transports in its own order, the session cookie
// sent with every request, the server's timeout at 30 s and the keep-alive
// at 15 s, and no reconnect of the library's own: when the connection
// closes, the loop in tm-hub.ts makes a new one. The library's warnings and
// errors go to telemetry, with no message content.

/** The hub method through which the server sends every event. */
const EVENT_METHOD = 'event';

/** How long the client waits for the server's messages or pings before it closes the connection. */
const SERVER_TIMEOUT_MS = 30_000;

/** How often the client pings the server while it has nothing else to send. */
const KEEP_ALIVE_MS = 15_000;

/** A negotiate request's address, which is the only one whose refusal is for good. */
const NEGOTIATE = /\/negotiate(\?|$)/;

/** The statuses of a negotiate the server refused for good. */
const REFUSALS: readonly number[] = [401, 403, 404] satisfies TmHubRefusal[];

/**
 * The connection's HTTP client: the library's own, with the tab's user
 * check before every request, a refused negotiate's status kept, and every
 * request in flight aborted when the attempt ends. Internal.
 */
export class TmHubHttpClient extends DefaultHttpClient {
  readonly #check: () => TmSessionCheck;
  /** Aborts each request in flight. */
  readonly #inFlight = new Set<() => void>();
  #closed = false;
  #refusal: TmHubRefusal | null = null;

  /**
   * Creates the client.
   *
   * @param check - The tab's user check, which must find the same user before a request leaves.
   */
  constructor(check: () => TmSessionCheck) {
    super(NullLogger.instance);
    this.#check = check;
  }

  /**
   * Why the server refused the connection for good, once it did: a
   * negotiate's 401, 403 or 404, or a user check that failed.
   *
   * @returns The refusal, or `null`.
   */
  refusal(): TmHubRefusal | null {
    return this.#refusal;
  }

  /** Aborts every request in flight, and refuses every later one. */
  close(): void {
    this.#closed = true;
    for (const abort of [...this.#inFlight]) {
      abort();
    }
  }

  /**
   * Sends a request, once the user check finds the same user.
   *
   * @param request - The request.
   * @returns The response.
   */
  override async send(request: HttpRequest): Promise<HttpResponse> {
    if (this.#closed) {
      throw new Error('The connection was stopped.');
    }
    // A tab whose user changed or signed out sends nothing in the old user's name.
    if (this.#check() !== 'same') {
      this.#refusal ??= 'user';
      throw new Error('The session no longer belongs to the user who connected.');
    }

    // --- Every request gets a signal of its own, which the close aborts ---
    // The library sends the negotiate without one, so a close could not end it otherwise.
    const own: TmSignalRAbortSignal = { aborted: false, onabort: null };
    const abort = (): void => {
      if (!own.aborted) {
        own.aborted = true;
        own.onabort?.();
      }
    };
    const outer = request.abortSignal;
    if (outer !== undefined) {
      if (outer.aborted) {
        abort();
      } else {
        outer.onabort = abort;
      }
    }
    this.#inFlight.add(abort);
    try {
      return await super.send({ ...request, abortSignal: own });
    } catch (error) {
      if (error instanceof HttpError && REFUSALS.includes(error.statusCode) && NEGOTIATE.test(request.url ?? '')) {
        this.#refusal ??= error.statusCode as 401 | 403 | 404;
      }
      throw error;
    } finally {
      this.#inFlight.delete(abort);
    }
  }
}

/** A warning or error the SignalR client logged; its message holds no content. */
export class TmHubLogError extends Error {
  /** @inheritDoc */
  override readonly name = 'TmHubLogError';
}

/**
 * The library's logger: warnings and errors go to telemetry, the rest
 * nowhere, and nothing once the attempt ended. Internal.
 */
export class TmHubLogger implements ILogger {
  readonly #telemetry: TmTelemetry;
  #muted = false;

  /**
   * Creates the logger.
   *
   * @param telemetry - Where warnings and errors go.
   */
  constructor(telemetry: TmTelemetry) {
    this.#telemetry = telemetry;
  }

  /** Drops every later message: an ended attempt's teardown is not news. */
  mute(): void {
    this.#muted = true;
  }

  /**
   * Records a warning or an error.
   *
   * @param level - The message's level.
   * @param message - The message, whose access tokens are masked.
   */
  log(level: LogLevel, message: string): void {
    if (this.#muted || level < LogLevel.Warning || level === LogLevel.None) {
      return;
    }
    this.#telemetry.error(new TmHubLogError(message.replace(/(access_token=)[^&\s'"]*/gi, '$1***')), {
      source: 'tm-hub',
      level: level === LogLevel.Warning ? 'warning' : 'error',
    });
  }
}

/** What an attempt over the SignalR client needs. */
export interface TmSignalROptions {
  /** The hub's address. */
  readonly url: string;
  /** The tab's user check. */
  readonly check: () => TmSessionCheck;
  /** Where the library's warnings and errors go. */
  readonly telemetry: TmTelemetry;
}

/**
 * Makes the attempts of a hub over the SignalR client.
 *
 * @param options - The hub's address, the user check and the telemetry.
 * @returns The function that makes each attempt.
 */
export function tmSignalRConnect(options: TmSignalROptions): TmHubConnect {
  return (handlers: TmHubAttemptHandlers): TmHubAttempt => {
    const httpClient = new TmHubHttpClient(options.check);
    const logger = new TmHubLogger(options.telemetry);
    const connection = new HubConnectionBuilder()
      .withUrl(options.url, { httpClient, withCredentials: true })
      .withServerTimeout(SERVER_TIMEOUT_MS)
      .withKeepAliveInterval(KEEP_ALIVE_MS)
      .configureLogging(logger)
      .build();
    connection.on(EVENT_METHOD, (envelope: unknown) => handlers.event(envelope));
    connection.onclose((error) => handlers.closed(error));
    return {
      start: () => connection.start(),
      stop: () => {
        logger.mute();
        httpClient.close();
        return connection.stop();
      },
      refusal: () => httpClient.refusal(),
    };
  };
}
