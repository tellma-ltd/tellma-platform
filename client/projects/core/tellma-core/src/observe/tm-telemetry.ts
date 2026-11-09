// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import { Injectable, signal, type Signal } from '@angular/core';

// The telemetry seam (spec 0026 §3). Code anywhere records events, timings
// and errors through TmTelemetry. Until the telemetry pipeline is provided,
// the root default is quiet: it accepts every item and sends nothing.

/** The properties of one telemetry item: flat strings, numbers and booleans. */
export type TmTelemetryProps = Readonly<Record<string, string | number | boolean>>;

/** Per-item options of {@link TmTelemetry}. */
export interface TmTelemetryOptions {
  /** Whether the item is sampled; overrides the default for its name. */
  readonly sampled?: boolean;
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
 * Records telemetry from any code. Nothing awaits it, and it never throws.
 *
 * Inject it and record:
 *
 * @example
 * ```ts
 * inject(TmTelemetry).event('acme.credit-exposure.run', { kind: 'summary', rows: 120 });
 * ```
 */
@Injectable({ providedIn: 'root', useFactory: () => new TmQuietTelemetry() })
export abstract class TmTelemetry {
  /** The last items recorded, newest last, whether sent or not. */
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
   * Records one error item.
   *
   * @param error - What was thrown or rejected.
   * @param context - Where it happened, as flat strings.
   */
  abstract error(error: unknown, context?: Readonly<Record<string, string>>): void;

  /** Sends what is queued now, as at sign-out. */
  abstract flush(): void;
}

/** The root default until the pipeline is provided: it accepts every item and keeps none. */
class TmQuietTelemetry extends TmTelemetry {
  /** Always empty: nothing is recorded. */
  override readonly recent: Signal<readonly TmTelemetryItem[]> = signal<readonly TmTelemetryItem[]>([]).asReadonly();

  /** Accepts the event and drops it. */
  override event(): void {
    // Quiet by design: no pipeline is provided.
  }

  /** Accepts the timing and drops it. */
  override timing(): void {
    // Quiet by design: no pipeline is provided.
  }

  /** Accepts the error and drops it. */
  override error(): void {
    // Quiet by design: no pipeline is provided.
  }

  /** Has nothing to send. */
  override flush(): void {
    // Quiet by design: no pipeline is provided.
  }
}
