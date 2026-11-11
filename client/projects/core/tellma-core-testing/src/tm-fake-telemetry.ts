// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import { Injectable, signal, type Signal } from '@angular/core';
import { TmTelemetry, type TmTelemetryItem, type TmTelemetryProps } from '@tellma/core';

/**
 * A {@link TmTelemetry} that records every item and sends nothing, so a test
 * can assert what the code under test reported.
 *
 * @example
 * ```ts
 * TestBed.configureTestingModule({ providers: [provideTellmaTesting()] });
 * // … run the code under test …
 * expect(TestBed.inject(TmFakeTelemetry).recent().map((item) => item.name)).toContain('acme.report.run');
 * ```
 */
@Injectable()
export class TmFakeTelemetry extends TmTelemetry {
  /** Every item recorded, oldest first. */
  readonly #items = signal<readonly TmTelemetryItem[]>([]);

  /** Every value passed to {@link TmFakeTelemetry.error}, oldest first. */
  readonly #errors = signal<readonly unknown[]>([]);

  /** How many times {@link TmFakeTelemetry.flush} was called. */
  readonly #flushes = signal(0);

  /** Every item recorded, oldest first; unlike the real pipeline, the fake keeps them all. */
  override readonly recent: Signal<readonly TmTelemetryItem[]> = this.#items.asReadonly();

  /** Every value passed to {@link TmFakeTelemetry.error}, oldest first. */
  readonly errors: Signal<readonly unknown[]> = this.#errors.asReadonly();

  /** How many times {@link TmFakeTelemetry.flush} was called. */
  readonly flushes: Signal<number> = this.#flushes.asReadonly();

  /** Records an event item. */
  override event(name: string, props: TmTelemetryProps = {}): void {
    this.#record('event', name, props);
  }

  /** Records a timing item, with the duration as the property `ms`. */
  override timing(name: string, ms: number, props: TmTelemetryProps = {}): void {
    this.#record('timing', name, { ...props, ms });
  }

  /**
   * Records an error item named `tm.error.captured`, with the context and the
   * error's class as its properties, and keeps the value itself in
   * {@link TmFakeTelemetry.errors}.
   */
  override error(error: unknown, context: Readonly<Record<string, string>> = {}): void {
    const errorClass = error instanceof Error ? error.name : typeof error;
    this.#errors.update((errors) => [...errors, error]);
    this.#record('error', 'tm.error.captured', { ...context, errorClass });
  }

  /** Counts the flush; there is nothing to send. */
  override flush(): void {
    this.#flushes.update((count) => count + 1);
  }

  /** Forgets every item, error and flush recorded so far. */
  clear(): void {
    this.#items.set([]);
    this.#errors.set([]);
    this.#flushes.set(0);
  }

  /** Appends one item. */
  #record(kind: TmTelemetryItem['kind'], name: string, props: TmTelemetryProps): void {
    const item: TmTelemetryItem = { kind, name, at: Date.now(), tenantId: null, props };
    this.#items.update((items) => [...items, item]);
  }
}
