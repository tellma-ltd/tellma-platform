// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import { inject, Injectable, Injector, signal, type Signal, untracked, type WritableSignal } from '@angular/core';

import { TM_NOW } from '../app/tm-now';
import type { TmErrorRecord } from './tm-error-handler';

// The telemetry API (spec 0026 §3). Code anywhere records events, timings and
// errors through TmTelemetry, and nothing awaits it. The root service is a
// thin front that stamps each call and holds it; the pipeline that guards,
// samples, queues and sends the items is a lazy chunk of its own
// (tm-telemetry-pipeline), which the app's startup loads at idle after the
// first render. From then on the front hands every call straight to it. Each
// call carries what it saw where it was made (the routed tenant, the page and
// the recent requests), so a held call is replayed as it was made. An
// injector whose app never starts the pipeline, such as a unit test's, keeps
// at most a thousand calls and sends nothing.

/** The properties of one telemetry item: flat strings, numbers and booleans. */
export type TmTelemetryProps = Readonly<Record<string, string | number | boolean>>;

/** Per-item options of {@link TmTelemetry}. */
export interface TmTelemetryOptions {
  /** Whether the item is sampled; overrides the default for its name. */
  readonly sampled?: boolean;
  /** The trace id of the request the item belongs to: 32 lowercase hexadecimal digits. */
  readonly traceId?: string;
}

/** One telemetry record, as {@link TmTelemetry.recent} lists it. */
export interface TmTelemetryItem {
  /** What the item records. */
  readonly kind: 'event' | 'timing' | 'error';
  /** The item's name, such as `tm.launch.warm`. */
  readonly name: string;
  /** When the item was recorded, in epoch milliseconds. */
  readonly at: number;
  /** The trace id of the request the item belongs to, when it belongs to one. */
  readonly traceId?: string;
  /** The routed tenant when the item was recorded, or `null` outside a tenant. */
  readonly tenantId: number | null;
  /** The item's properties; a timing carries its duration as `ms`. */
  readonly props: TmTelemetryProps;
}

/**
 * Records telemetry from any code. Nothing awaits it, and it never throws in
 * production. In development, a malformed name or property throws an error
 * named `TmTelemetryError`.
 *
 * - **Names** read `<scope>.<area>.<event>`, in lowercase: `tm.api.slow`,
 *   `acme.credit-exposure.run`. Scope `tm` is the platform's; any other scope
 *   is a feature key.
 * - **Properties** are at most 16 per item. Keys are camelCase; values are
 *   numbers, booleans, or codes of at most 64 characters drawn from letters,
 *   digits and `_.:/@+-`. Telemetry never carries URLs, free text, field
 *   values, display names or user ids.
 *
 * @example
 * ```ts
 * inject(TmTelemetry).event('acme.credit-exposure.run', { kind: 'summary', rows: 120 });
 * ```
 */
@Injectable({ providedIn: 'root', useFactory: () => new TmTelemetryFront() })
export abstract class TmTelemetry {
  /** The last 100 items recorded, newest last, whether sent or not. */
  abstract readonly recent: Signal<readonly TmTelemetryItem[]>;

  /**
   * Records an event.
   *
   * @param name - The event's name, such as `tm.launch.cold`.
   * @param props - The event's properties.
   * @param options - Per-item options.
   */
  abstract event(name: string, props?: TmTelemetryProps, options?: TmTelemetryOptions): void;

  /**
   * Records a duration, which becomes the property `ms`.
   *
   * @param name - The timing's name.
   * @param ms - The duration in milliseconds.
   * @param props - The timing's other properties.
   * @param options - Per-item options.
   */
  abstract timing(name: string, ms: number, props?: TmTelemetryProps, options?: TmTelemetryOptions): void;

  /**
   * Records one error item, `tm.error.captured`, and logs the error to the
   * console. Its message and stack are redacted before they leave the tab.
   *
   * @param error - What was thrown or rejected.
   * @param context - Where it happened, as properties of the item. Its
   *   `source` names how the error arrived: `api` for a failed request the
   *   error presenter shows, `global` for an uncaught error or rejection,
   *   `render` for anything else. By default it is `global` while the
   *   browser dispatches an uncaught error or rejection, else `render`.
   */
  abstract error(error: unknown, context?: Readonly<Record<string, string>>): void;

  /** Sends what is queued now, as at sign-out. */
  abstract flush(): void;

  /**
   * The tab's last 50 captured errors, oldest first, redacted as they would
   * be sent: what the support dump lists. Internal: an implementation that
   * keeps no records, such as a test's, lists none.
   *
   * @returns The records.
   */
  ɵerrorRecords(): readonly TmErrorRecord[] {
    return [];
  }
}

/** What a call saw where it was made. Internal. */
export interface TmTelemetryCallContext {
  /** The routed tenant, or `null` outside a tenant. */
  readonly tenantId: number | null;
  /** The routed page's template and kind, or `null` before the first page. */
  readonly page: { readonly routeTemplate: string; readonly kind: string } | null;
  /** The most recent requests, newest first. */
  readonly requests: readonly { readonly traceId: string }[];
}

/**
 * One call to the front: its method, its arguments, when it was made, in
 * epoch milliseconds, and what it saw, once the app's startup lets the front
 * read it. An error's arguments end with the event the browser was
 * dispatching at the call. Internal.
 */
export type TmTelemetryCall = readonly [
  method: 'event' | 'timing' | 'error' | 'flush',
  args: readonly unknown[],
  at: number,
  context?: TmTelemetryCallContext,
];

/** The started pipeline, as the front reads it. Internal. */
export interface TmTelemetrySink {
  /** Takes one call of the front. */
  readonly take: (call: TmTelemetryCall) => void;
  /** Reads the tab's last 50 error records, oldest first. */
  readonly errors: () => readonly TmErrorRecord[];
}

/** What the pipeline's chunk exports, as far as the front reads it. Internal. */
export interface TmTelemetryChunk {
  /**
   * Starts the pipeline over the calls taken so far.
   *
   * @param injector - The root injector, for the pipeline's dependencies.
   * @param recent - The ring the pipeline fills.
   * @param calls - The calls the front held, oldest first; the pipeline empties it.
   * @returns What takes every later call, and the error records it keeps.
   */
  tmStartTelemetry(injector: Injector, recent: WritableSignal<readonly TmTelemetryItem[]>, calls: TmTelemetryCall[]): TmTelemetrySink;
}

/**
 * The most calls the front holds before the pipeline starts; later ones are
 * dropped. It is well past the queue's cap, so the pipeline's own overflow
 * order decides what a busy launch loses, and counts it.
 */
const HELD_CALLS = 1000;

/**
 * The root {@link TmTelemetry}: it stamps each call and holds it until the
 * app's startup loads the pipeline, then forwards every call to it. Internal.
 */
export class TmTelemetryFront extends TmTelemetry {
  readonly #now = inject(TM_NOW);

  /** {@inheritDoc TmTelemetry.recent} The pipeline fills it; to every reader it is read-only. */
  override readonly recent: Signal<readonly TmTelemetryItem[]> = signal<readonly TmTelemetryItem[]>([]);

  /** The calls held until the pipeline starts, oldest first. */
  readonly #held: TmTelemetryCall[] = [];

  /** Reads what a call sees; the app's startup sets it. */
  context: (() => TmTelemetryCallContext) | undefined;

  /** Takes a call: held until the pipeline starts, then the pipeline's. */
  #sink = (call: TmTelemetryCall): unknown => this.#held.length < HELD_CALLS && this.#held.push(call);

  /** Reads the error records: none until the pipeline starts, then the pipeline's. */
  #records = (): readonly TmErrorRecord[] => [];

  /** {@inheritDoc TmTelemetry.event} */
  override event(...args: Parameters<TmTelemetry['event']>): void {
    this.#take('event', args);
  }

  /** {@inheritDoc TmTelemetry.timing} */
  override timing(...args: Parameters<TmTelemetry['timing']>): void {
    this.#take('timing', args);
  }

  /** {@inheritDoc TmTelemetry.error} */
  override error(error: unknown, context?: Readonly<Record<string, string>>): void {
    console.error(error);
    // The event the browser is dispatching, if any, tells an uncaught error from a reported one.
    this.#take('error', [error, context, (globalThis as { readonly event?: unknown }).event]);
  }

  /** {@inheritDoc TmTelemetry.flush} */
  override flush(): void {
    this.#take('flush', []);
  }

  /** {@inheritDoc TmTelemetry.ɵerrorRecords} */
  override ɵerrorRecords(): readonly TmErrorRecord[] {
    return this.#records();
  }

  /**
   * Loads the pipeline and hands it the held calls. The app's startup calls
   * it once, at idle after the first render. A chunk that cannot load leaves
   * the calls held, and nothing is sent.
   *
   * @param injector - The root injector, for the pipeline's dependencies.
   */
  start(injector: Injector): void {
    void (import('./tm-telemetry-pipeline') as Promise<TmTelemetryChunk>).then(
      (chunk) => {
        const started = chunk.tmStartTelemetry(injector, this.recent as WritableSignal<readonly TmTelemetryItem[]>, this.#held);
        this.#sink = started.take;
        this.#records = started.errors;
      },
      () => undefined,
    );
  }

  /**
   * Stamps a call with its time and what it sees, and passes it on.
   *
   * @param method - The method called.
   * @param args - Its arguments.
   */
  #take(method: TmTelemetryCall[0], args: readonly unknown[]): void {
    // Read untracked: a caller in a reactive context depends on none of it.
    this.#sink([method, args, this.#now(), this.context && untracked(this.context)]);
  }
}
