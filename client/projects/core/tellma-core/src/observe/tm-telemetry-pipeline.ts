// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import { DestroyRef, DOCUMENT, effect, type Injector, untracked, type WritableSignal } from '@angular/core';

import { TmApi } from '../api/tm-api';
import { TM_DEV_MODE } from '../app/tm-dev-mode';
import { TM_NOW } from '../app/tm-now';
import { TmPageContexts } from '../context/tm-page-contexts';
import { TmClientMetadataStore } from '../metadata/tm-client-metadata';
import { TmRequestLog, type TmRequestRecord } from '../requests/tm-request-log';
import { type TmRequestCollection, TmRequestMeasures } from '../requests/tm-request-measures';
import { TmSession } from '../session/tm-session';
import { TmTabBus } from '../state/bus/tm-tab-bus';
import { TmLaunchCache } from '../state/launch/tm-launch-cache';
import { TmUrls } from '../urls/tm-urls';
import { TmErrorCapture } from './tm-error-capture';
import type { TmErrorRecord, TmErrorSource } from './tm-error-handler';
import type { TmTelemetryCall, TmTelemetryCallContext, TmTelemetryItem, TmTelemetryOptions, TmTelemetryProps } from './tm-telemetry';

// The telemetry pipeline (spec 0026 §3, §4): a lazy chunk the front loads at
// idle after the first render. Each call is guarded, stamped with the
// envelope's per-item members, sampled, kept in the `recent` ring and
// queued. The queue flushes at 50 items, 30 s after its first item, 1 s
// after an error, when the tab hides or the page is hidden, on a tenant
// switch and on `flush()`; a flush cuts it into one tenant's batches of at
// most 100 items and 16 KB, sent through TmApi.send one a second, so the
// `pagehide` of an unload sends one batch. An item takes its tenant, page and
// recent requests from what its call saw where it was made, so a call the
// front held until this chunk loaded is recorded as it was made. Offline,
// without an active session or before the UI version is known, the queue
// holds within its cap of 200. The request outcomes (§5.3) are measured here
// too: each change of the request log is collected into the flush window's
// summaries, and its slow requests are recorded as they settle; each flush
// first records the window's summaries, and the page hidden for good first
// records the slow requests still pending. Nothing here awaits, retries or
// throws in production.

/** The relay's route. */
export const TM_TELEMETRY_ROUTE = '/api/client-telemetry';

/** Why the pipeline dropped something, as `tm.telemetry.dropped` reports it. */
export type TmTelemetryDropReason = 'overflow' | 'name' | 'prop' | 'foreign' | 'throttled';

/** The telemetry switches of the distribution document. */
export interface TmTelemetrySettings {
  /** Whether the client sends telemetry at all. */
  readonly enabled: boolean;
  /** The share of tabs that send sampled items, from 0 to 1. */
  readonly sampleRate: number;
}

/** The routed page, as every item's reserved properties name it. */
export interface TmTelemetryPage {
  /** The page's route template, such as `/:t/centers/:id`. */
  readonly route: string;
  /** The page's kind, such as `details`. */
  readonly pageKind: string;
}

/** One item as the relay receives it. */
export interface TmTelemetryWireItem {
  /** What the item records. */
  readonly kind: TmTelemetryItem['kind'];
  /** The item's name. */
  readonly name: string;
  /** When the item was recorded, in ISO 8601. */
  readonly at: string;
  /** The trace id of the request the item belongs to. */
  readonly traceId?: string;
  /** The item's properties. */
  readonly properties: TmTelemetryProps;
}

/** One relay call's body: one tenant's items, with the envelope. */
export interface TmTelemetryBatch {
  /** The UI version. */
  readonly version: string;
  /** The tab's id. */
  readonly tabId: string;
  /** The batch's one tenant, or `null` for tenantless pages' items. */
  readonly tenantId: number | null;
  /** The client clock at send, in ISO 8601, for skew correction. */
  readonly sentAt: string;
  /** The items, oldest first. */
  readonly items: readonly TmTelemetryWireItem[];
}

/** What the pipeline reads and calls; the app's services in an app, fakes in a spec. */
export interface TmTelemetryHost {
  /** The ring {@link TmTelemetry.recent} reads. */
  readonly recent: WritableSignal<readonly TmTelemetryItem[]>;
  /** The tab's id. */
  readonly tabId: string;
  /** The app's origin, which tells the app's scripts from foreign ones. */
  readonly origin: string;
  /** Whether the app runs in development mode. */
  readonly devMode: () => boolean;
  /** A random number in [0, 1), drawn once for the tab's sampling decision. */
  readonly random: () => number;
  /** The current time in epoch milliseconds. */
  readonly now: () => number;
  /** The distribution's telemetry switches, or `undefined` before its document is known. */
  readonly settings: () => TmTelemetrySettings | undefined;
  /** The UI version, or `null` before the client metadata is loaded. */
  readonly version: () => string | null;
  /** The routed tenant, or `null` outside a tenant. */
  readonly tenantId: () => number | null;
  /** The routed page, or `null` before the first page. */
  readonly page: () => TmTelemetryPage | null;
  /** Whether sending is held: offline, or no active session. */
  readonly held: () => boolean;
  /** The three most recent requests' trace ids, newest first. */
  readonly recentTraceIds: () => readonly string[];
  /** The request log, newest first. */
  readonly requests: () => readonly TmRequestRecord[];
  /** Sends a batch: unread, `keepalive`, background. */
  readonly send: (batch: TmTelemetryBatch) => void;
  /** Writes an item to the console, in development. */
  readonly debug: (name: string, props: TmTelemetryProps) => void;
}

/** What a malformed name or property throws in development. */
export class TmTelemetryError extends Error {
  /** The error's name. */
  override readonly name = 'TmTelemetryError';
}

/** A telemetry name: `<scope>.<area>.<event>[.<more>]`. */
const NAME = /^[a-z][a-z0-9]*(\.[a-z0-9][a-z0-9-]*){2,5}$/;

/** A property key. */
const KEY = /^[a-z][a-zA-Z0-9]{0,31}$/;

/** A string property value. */
const VALUE = /^[A-Za-z0-9_.:/@+-]*$/;

/** A trace id: 32 lowercase hexadecimal digits, not all zeros. */
const TRACE_ID = /^(?!0{32}$)[0-9a-f]{32}$/;

/** The platform names sampled by default; every other scope's are too. */
const SAMPLED = /^tm\.(page|vitals|action)\./;

/** The error properties exempt from the value grammar, which the redaction bounds instead. */
const REDACTED_PROPS: ReadonlySet<string> = new Set(['message', 'stack', 'recentTraceIds']);

/** The order the drop reasons are reported in. */
const DROP_REASONS: readonly TmTelemetryDropReason[] = ['overflow', 'name', 'prop', 'foreign', 'throttled'];

/** The switches before the distribution document is known: enabled, every tab sampled. */
const COLD_SETTINGS: TmTelemetrySettings = { enabled: true, sampleRate: 1 };

/** The longest name or string value. */
const MAX_CHARS = 64;

/** The most properties an item carries, the reserved ones included. */
const MAX_PROPS = 16;

/** The size of the `recent` ring. */
const RING = 100;

/** The queue's cap. */
const QUEUE_CAP = 200;

/** The queue length that flushes at once. */
const FLUSH_ITEMS = 50;

/** How long after its first item the queue flushes. */
const FIRST_ITEM_MS = 30_000;

/** How long after an error item the queue flushes, debounced. */
const ERROR_MS = 1_000;

/** The pause between the batches of one flush. */
const PACE_MS = 1_000;

/** The most items in a batch. */
const BATCH_ITEMS = 100;

/** The most bytes in a batch's body. */
const BATCH_BYTES = 16 * 1024;

/** The UTF-8 encoder that measures bodies. */
const UTF8 = new TextEncoder();

/**
 * The pipeline behind {@link TmTelemetry}, over a host of its dependencies.
 * Internal: the front starts it, a spec drives it directly.
 */
export class TmTelemetryPipeline {
  readonly #host: TmTelemetryHost;
  readonly #capture: TmErrorCapture;
  readonly #measures = new TmRequestMeasures();

  /** The tab's one random draw: sampled items are queued when it is below the rate. */
  readonly #draw: number;

  /** The items waiting to be sent, oldest first. */
  readonly #queue: TmTelemetryItem[] = [];

  /** What was dropped since the last `tm.telemetry.dropped`, by reason. */
  readonly #drops = new Map<TmTelemetryDropReason, number>();

  /** The flush 30 s after the queue's first item. */
  #firstTimer: ReturnType<typeof setTimeout> | undefined;

  /** The flush 1 s after the latest error item. */
  #errorTimer: ReturnType<typeof setTimeout> | undefined;

  /** The end of the second after a batch, when the next flush may send. */
  #paceTimer: ReturnType<typeof setTimeout> | undefined;

  /** What the call being taken saw where it was made; `undefined` to read the host. */
  #call: TmTelemetryCallContext | undefined;

  /**
   * Creates the pipeline and draws the tab's sampling decision.
   *
   * @param host - What the pipeline reads and calls.
   */
  constructor(host: TmTelemetryHost) {
    this.#host = host;
    this.#capture = new TmErrorCapture(host.origin);
    this.#draw = host.random();
  }

  /** The tab's last 50 error records, newest last, for the support dump. */
  get errors(): readonly TmErrorRecord[] {
    return this.#capture.records;
  }

  /**
   * Takes one call of the front. In production nothing throws; in
   * development a malformed name or property throws {@link TmTelemetryError}.
   *
   * @param call - The method, its arguments, when it was called and what it saw.
   */
  take([method, args, at, context]: TmTelemetryCall): void {
    this.#call = context;
    try {
      this.#guarded(() => {
        switch (method) {
          case 'event':
            this.#record('event', args[0], args[1], args[2] as TmTelemetryOptions | undefined, at);
            break;
          case 'timing':
            this.#record('timing', args[0], args[2], args[3] as TmTelemetryOptions | undefined, at, args[1]);
            break;
          case 'error':
            this.#error(args[0], args[1], args[2], at);
            break;
          case 'flush':
            this.flush();
            break;
        }
      });
    } finally {
      this.#call = undefined;
    }
  }

  /**
   * Sends what is queued: one batch now, then the next a second later while
   * items remain. Within the second after a batch, any flush joins the next
   * one, so `pagehide` sends one batch at most, even when the unload also
   * fires `visibilitychange`.
   */
  flush(): void {
    this.#guarded(() => {
      clearTimeout(this.#firstTimer);
      clearTimeout(this.#errorTimer);
      this.#firstTimer = this.#errorTimer = undefined;
      // A distribution that turned telemetry off is sent nothing, not even
      // what queued under the cold-launch defaults.
      if (this.#host.settings()?.enabled === false) {
        this.#queue.length = 0;
        this.#drops.clear();
        this.#measures.clear();
        return;
      }
      // --- The flush window's request summaries, which end with it ---
      this.#measure(this.#measures.collect(this.#host.requests(), this.#host.now()));
      this.#measure({ events: this.#measures.drain(this.#host.now()), throttled: 0 });
      if ((this.#queue.length === 0 && this.#drops.size === 0) || this.#paceTimer !== undefined) {
        return;
      }
      const version = this.#host.version();
      if (version === null || this.#host.held()) {
        // Held: the queue waits within its cap, and is tried again later.
        this.#armFirstTimer();
        return;
      }
      this.#sendOne(version);
      // The next batch waits a second; after `pagehide`, it is sent only if the page stays.
      this.#paceTimer = setTimeout(() => {
        this.#paceTimer = undefined;
        this.flush();
      }, PACE_MS);
    });
  }

  /**
   * Collects the requests that settled since the last change of the request
   * log, recording the slow ones; a window with requests is flushed within
   * 30 s.
   */
  measure(): void {
    this.#guarded(() => {
      this.#measure(this.#measures.collect(this.#host.requests(), this.#host.now()));
      if (this.#measures.waiting) {
        this.#armFirstTimer();
      }
    });
  }

  /**
   * Flushes as the page is hidden for good: after the requests that settled,
   * the slow ones still pending are recorded as pending.
   */
  pagehide(): void {
    this.#guarded(() => {
      const now = this.#host.now();
      this.#measure(this.#measures.collect(this.#host.requests(), now));
      this.#measure(this.#measures.pending(this.#host.requests(), now));
    });
    this.flush();
  }

  /** Stops the pipeline's timers, as the app is destroyed. */
  stop(): void {
    clearTimeout(this.#firstTimer);
    clearTimeout(this.#errorTimer);
    clearTimeout(this.#paceTimer);
    this.#firstTimer = this.#errorTimer = this.#paceTimer = undefined;
  }

  // --- Recording ---

  /**
   * Records an event or a timing: guards its name and properties, adds the
   * reserved ones, and keeps and queues the item.
   *
   * @param kind - The item's kind.
   * @param name - The name, as the caller passed it.
   * @param props - The caller's properties.
   * @param options - The caller's options.
   * @param at - When the call was made.
   * @param ms - A timing's duration, which becomes its reserved `ms`.
   */
  #record(kind: 'event' | 'timing', name: unknown, props: unknown, options: TmTelemetryOptions | undefined, at: number, ms?: unknown): void {
    if (typeof name !== 'string' || name.length > MAX_CHARS || !NAME.test(name)) {
      this.#bad('name', `The telemetry name ${JSON.stringify(name)} is malformed: it must read <scope>.<area>.<event>, in lowercase, in at most 64 characters.`);
      return;
    }
    const reserved = kind === 'timing' ? this.#ms(name, ms) : {};
    const sampled = options?.sampled ?? (!name.startsWith('tm.') || SAMPLED.test(name));
    const rate = this.#rate();
    const own = this.#reserved(sampled ? { ...reserved, sampleRate: rate } : reserved);
    const traceId = this.#traceId(options?.traceId);
    this.#keep({ kind, name, at, ...(traceId === undefined ? {} : { traceId }), tenantId: this.#tenantId(), props: this.#props(name, own, props) }, !sampled || this.#draw < rate);
  }

  /**
   * Records an error: captures it, builds its item and queues it unless
   * the throttles hold it back.
   *
   * @param error - What was thrown or rejected.
   * @param context - The caller's context; its `source` names how the error arrived.
   * @param event - The event the browser was dispatching at the call, if any.
   * @param at - When the call was made.
   */
  #error(error: unknown, context: unknown, event: unknown, at: number): void {
    const { source: given, ...rest } = (typeof context === 'object' && context !== null ? context : {}) as Record<string, unknown>;
    // Without a source of its own, an error reported while the browser
    // dispatches an uncaught error or rejection is that global error.
    let source: TmErrorSource = event instanceof ErrorEvent || event instanceof PromiseRejectionEvent ? 'global' : 'render';
    if (given === 'api' || given === 'render' || given === 'global') {
      source = given;
    } else if (given !== undefined) {
      this.#bad('prop', `The error source ${JSON.stringify(given)} is not one of api, render or global.`);
    }
    // The caller's context is guarded before anything is kept.
    const contextProps = this.#props('tm.error.captured', {}, rest);
    const captured = this.#capture.capture(error, source, at, this.#page()?.route ?? '', this.#traceIds());
    if (captured === 'filtered') {
      return;
    }
    if (captured === 'foreign') {
      this.#drop('foreign');
      return;
    }
    const own = this.#reserved({
      source,
      errorClass: captured.errorClass,
      fingerprint: captured.fingerprint,
      kind: captured.kind,
      code: captured.code,
      status: captured.status,
      message: captured.message,
      stack: captured.stack === '' ? undefined : captured.stack,
      recentTraceIds: captured.recentTraceIds.length === 0 ? undefined : captured.recentTraceIds.join(','),
    });
    const props = this.#props('tm.error.captured', own, contextProps);
    const item: TmTelemetryItem = {
      kind: 'error',
      name: 'tm.error.captured',
      at,
      ...(captured.traceId === undefined ? {} : { traceId: captured.traceId }),
      tenantId: this.#tenantId(),
      props,
    };
    if (this.#keep(item, false)) {
      if (this.#capture.admit(captured.fingerprint, at)) {
        this.#enqueue(item);
        clearTimeout(this.#errorTimer);
        this.#errorTimer = setTimeout(() => this.flush(), ERROR_MS);
      } else {
        this.#drop('throttled');
      }
    }
  }

  /**
   * Records request measurements, which are always sent, and counts the
   * slow events the throttle held back.
   *
   * @param collection - The measurements and the throttled count.
   */
  #measure({ events, throttled }: TmRequestCollection): void {
    for (const { name, at, tenantId, traceId, props } of events) {
      this.#keep({ kind: 'event', name, at, ...(traceId === undefined ? {} : { traceId }), tenantId, props: this.#reserved(props) }, true);
    }
    for (let count = 0; count < throttled; count++) {
      this.#drop('throttled');
    }
  }

  /**
   * Keeps an item in the ring, writes it to the console in development, and
   * queues it when telemetry is on and the item is not sampled out.
   *
   * @param item - The item.
   * @param queued - Whether the sampling lets it be sent; an error decides after the throttles.
   * @returns Whether telemetry is on, so an error may be queued.
   */
  #keep(item: TmTelemetryItem, queued: boolean): boolean {
    this.#host.recent.update((items) => [...items.slice(Math.max(0, items.length + 1 - RING)), item]);
    if (this.#host.devMode()) {
      this.#host.debug(item.name, item.props);
    }
    const enabled = (this.#host.settings() ?? COLD_SETTINGS).enabled;
    if (enabled && queued) {
      this.#enqueue(item);
    }
    return enabled;
  }

  /**
   * The reserved properties of an item: the routed page's, then the kind's
   * own. One that does not hold a valid value is dropped and counted, never
   * thrown in development, since the platform derived it.
   *
   * @param own - The kind's own properties; an `undefined` one is left out.
   * @param page - The routed page, by default the one the call saw.
   * @returns The properties.
   */
  #reserved(own: Readonly<Record<string, unknown>>, page = this.#page()): Record<string, string | number | boolean> {
    const props: Record<string, string | number | boolean> = {};
    for (const [key, value] of Object.entries(page === null ? own : { route: page.route, pageKind: page.pageKind, ...own })) {
      if (value === undefined) {
        continue;
      }
      if (REDACTED_PROPS.has(key) ? typeof value === 'string' : valid(value)) {
        props[key] = value as string | number | boolean;
      } else {
        this.#drop('prop');
      }
    }
    return props;
  }

  /**
   * Adds the caller's properties after the reserved ones, guarding each:
   * a malformed key or value, a reserved key, or one past the 16th throws
   * in development and is dropped and counted in production.
   *
   * @param name - The item's name, for the error.
   * @param reserved - The item's reserved properties.
   * @param props - The caller's properties.
   * @returns The item's properties.
   */
  #props(name: string, reserved: Readonly<Record<string, string | number | boolean>>, props: unknown): TmTelemetryProps {
    const all: Record<string, string | number | boolean> = { ...reserved };
    let count = Object.keys(all).length;
    for (const [key, value] of Object.entries(typeof props === 'object' && props !== null ? props : {})) {
      const problem =
        key in reserved
          ? 'is reserved'
          : !KEY.test(key)
            ? 'is not a camelCase key of at most 32 characters'
            : !valid(value)
              ? 'has a value that is not a number, a boolean or a code of at most 64 characters drawn from letters, digits and _.:/@+-'
              : count >= MAX_PROPS
                ? 'is past the 16 properties an item carries'
                : null;
      if (problem === null) {
        all[key] = value as string | number | boolean;
        count++;
      } else {
        this.#bad('prop', `The property "${key}" of ${name} ${problem}.`);
      }
    }
    return all;
  }

  /**
   * A timing's duration as its reserved property, when it is a finite
   * number; anything else is dropped as a property would be.
   *
   * @param name - The timing's name, for the error.
   * @param ms - The duration the caller passed.
   * @returns The reserved property, or none.
   */
  #ms(name: unknown, ms: unknown): Readonly<Record<string, number>> {
    if (typeof ms === 'number' && Number.isFinite(ms)) {
      return { ms };
    }
    this.#bad('prop', `The duration of ${String(name)} is not a finite number of milliseconds.`);
    return {};
  }

  /**
   * A caller's trace id, when it is one; anything else is dropped as a
   * property would be.
   *
   * @param traceId - The option's value.
   * @returns The trace id, or `undefined`.
   */
  #traceId(traceId: unknown): string | undefined {
    if (traceId === undefined || (typeof traceId === 'string' && TRACE_ID.test(traceId))) {
      return traceId;
    }
    this.#bad('prop', `The trace id ${JSON.stringify(traceId)} is not 32 lowercase hexadecimal digits.`);
    return undefined;
  }

  /**
   * Reports a malformed call: it throws in development, and is counted
   * into the next `tm.telemetry.dropped` in production.
   *
   * @param reason - What was malformed.
   * @param message - The development error's message.
   * @throws TmTelemetryError in development.
   */
  #bad(reason: 'name' | 'prop', message: string): void {
    if (this.#host.devMode()) {
      throw new TmTelemetryError(message);
    }
    this.#drop(reason);
  }

  /**
   * Counts a drop into the next `tm.telemetry.dropped`, which the next
   * batch carries; with nothing queued, a flush is due in 30 s.
   *
   * @param reason - Why.
   */
  #drop(reason: TmTelemetryDropReason): void {
    this.#drops.set(reason, (this.#drops.get(reason) ?? 0) + 1);
    this.#armFirstTimer();
  }

  // --- What the call saw, else what the host sees now ---

  /**
   * The routed tenant when the call was made.
   *
   * @returns The tenant, or `null` outside a tenant.
   */
  #tenantId(): number | null {
    return this.#call === undefined ? this.#host.tenantId() : this.#call.tenantId;
  }

  /**
   * The routed page when the call was made.
   *
   * @returns The page, or `null` before the first page.
   */
  #page(): TmTelemetryPage | null {
    const page = this.#call === undefined ? undefined : this.#call.page;
    return page === undefined ? this.#host.page() : page === null ? null : { route: page.routeTemplate, pageKind: page.kind };
  }

  /**
   * The three most recent requests' trace ids when the call was made, newest first.
   *
   * @returns The trace ids.
   */
  #traceIds(): readonly string[] {
    return this.#call === undefined ? this.#host.recentTraceIds() : this.#call.requests.slice(0, 3).map((request) => request.traceId);
  }

  /**
   * The sampling rate in force, from 0 to 1.
   *
   * @returns The rate.
   */
  #rate(): number {
    const rate = (this.#host.settings() ?? COLD_SETTINGS).sampleRate;
    return Number.isFinite(rate) ? Math.min(1, Math.max(0, rate)) : 1;
  }

  // --- Queueing and sending ---

  /**
   * Queues an item. A full queue drops its oldest item that is not an
   * error, else its oldest, and counts the overflow; 50 items flush at once.
   *
   * @param item - The item.
   */
  #enqueue(item: TmTelemetryItem): void {
    if (this.#queue.length >= QUEUE_CAP) {
      const oldest = this.#queue.findIndex((queued) => queued.kind !== 'error');
      this.#queue.splice(Math.max(oldest, 0), 1);
      this.#drops.set('overflow', (this.#drops.get('overflow') ?? 0) + 1);
    }
    this.#queue.push(item);
    if (this.#queue.length >= FLUSH_ITEMS) {
      this.flush();
    } else {
      this.#armFirstTimer();
    }
  }

  /** Arms the flush 30 s ahead, unless it is armed. */
  #armFirstTimer(): void {
    this.#firstTimer ??= setTimeout(() => {
      this.#firstTimer = undefined;
      this.flush();
    }, FIRST_ITEM_MS);
  }

  /**
   * Sends one batch: the drop counts first, then the queue's items of the
   * first item's tenant, in order, up to 100 items and 16 KB.
   *
   * @param version - The UI version.
   */
  #sendOne(version: string): void {
    // The first item's tenant, `null` included; with only drop counts, the routed one.
    const first = this.#queue[0];
    const tenantId = first === undefined ? this.#host.tenantId() : first.tenantId;
    const sentAt = this.#host.now();
    const envelope = { version, tabId: this.#host.tabId, tenantId, sentAt: new Date(sentAt).toISOString() };
    // The body is the envelope with its items: its bytes, then each item's and a comma between them.
    let bytes = UTF8.encode(JSON.stringify({ ...envelope, items: [] })).byteLength;
    const items: TmTelemetryWireItem[] = [];
    const add = (item: TmTelemetryItem): boolean => {
      const wire: TmTelemetryWireItem = {
        kind: item.kind,
        name: item.name,
        at: new Date(item.at).toISOString(),
        ...(item.traceId === undefined ? {} : { traceId: item.traceId }),
        properties: item.props,
      };
      const size = UTF8.encode(JSON.stringify(wire)).byteLength + (items.length > 0 ? 1 : 0);
      if (items.length >= BATCH_ITEMS || bytes + size > BATCH_BYTES) {
        return false;
      }
      items.push(wire);
      bytes += size;
      return true;
    };
    // --- The drop counts, each reason once ---
    for (const reason of DROP_REASONS) {
      const count = this.#drops.get(reason);
      if (count !== undefined && add({ kind: 'event', name: 'tm.telemetry.dropped', at: sentAt, tenantId, props: this.#reserved({ count, reason }, this.#host.page()) })) {
        this.#drops.delete(reason);
      }
    }
    // --- The tenant's items, oldest first ---
    for (let index = 0; index < this.#queue.length; ) {
      const item = this.#queue[index] as TmTelemetryItem;
      if (item.tenantId !== tenantId) {
        index++;
      } else if (add(item)) {
        this.#queue.splice(index, 1);
      } else {
        // An item that cannot fit an empty batch could never be sent.
        if (items.length === 0) {
          this.#queue.splice(index, 1);
        }
        break;
      }
    }
    if (items.length > 0) {
      this.#host.send({ ...envelope, items });
    }
  }

  /**
   * Runs a step so that it never throws in production: a malformed call is
   * counted, and anything unexpected is swallowed. In development it throws.
   *
   * @param step - The step.
   */
  #guarded(step: () => void): void {
    try {
      step();
    } catch (error) {
      if (this.#host.devMode()) {
        throw error;
      }
    }
  }
}

/**
 * Whether a value may be a property's: a finite number, a boolean, or a
 * string of at most 64 characters drawn from letters, digits and `_.:/@+-`.
 *
 * @param value - The value.
 * @returns `true` when it may.
 */
function valid(value: unknown): boolean {
  return (
    typeof value === 'boolean' ||
    (typeof value === 'number' && Number.isFinite(value)) ||
    (typeof value === 'string' && value.length <= MAX_CHARS && VALUE.test(value))
  );
}

/**
 * The host of an app's pipeline: the app's own services.
 *
 * @param injector - The root injector.
 * @param recent - The front's ring.
 * @returns The host.
 */
function tmAppTelemetryHost(injector: Injector, recent: WritableSignal<readonly TmTelemetryItem[]>): TmTelemetryHost {
  const api = injector.get(TmApi);
  const session = injector.get(TmSession);
  const urls = injector.get(TmUrls);
  const contexts = injector.get(TmPageContexts);
  const launch = injector.get(TmLaunchCache);
  const requests = injector.get(TmRequestLog);
  const metadata = injector.get(TmClientMetadataStore);
  return {
    recent,
    tabId: injector.get(TmTabBus).tabId,
    origin: injector.get(DOCUMENT).location.origin,
    devMode: injector.get(TM_DEV_MODE),
    random: Math.random,
    now: injector.get(TM_NOW),
    settings: () => launch.distribution()?.telemetry,
    version: () => {
      try {
        return metadata.get().distribution.version;
      } catch {
        return null;
      }
    },
    tenantId: () => urls.tenantId(),
    page: () => {
      const context = contexts.current();
      return context === null ? null : { route: context.routeTemplate, pageKind: context.kind };
    },
    held: () => !api.online() || session.state() !== 'active',
    recentTraceIds: () => requests.recent().slice(0, 3).map((request) => request.traceId),
    requests: () => requests.recent(),
    send: (batch) => api.send(TM_TELEMETRY_ROUTE, batch, { background: true }),
    // Development builds write every item to the console (spec 0026 §3.6).
    debug: (name, props) => console.debug('[tm]', name, props),
  };
}

/**
 * Starts the app's pipeline: over the app's services, flushing when the tab
 * hides, the page is hidden, the tenant switches or a hold lifts, and
 * taking the calls the front held. The front loads this chunk and calls it
 * once. Internal.
 *
 * @param injector - The root injector.
 * @param recent - The front's ring.
 * @param calls - The calls the front held, oldest first; emptied here.
 * @returns What takes every later call.
 */
export function tmStartTelemetry(
  injector: Injector,
  recent: WritableSignal<readonly TmTelemetryItem[]>,
  calls: TmTelemetryCall[],
): (call: TmTelemetryCall) => void {
  const host = tmAppTelemetryHost(injector, recent);
  const pipeline = new TmTelemetryPipeline(host);
  const urls = injector.get(TmUrls);
  const document = injector.get(DOCUMENT);

  // --- A tenant switch, or a hold that lifts, flushes ---
  let tenantId = untracked(urls.tenantId);
  let held = untracked(host.held);
  effect(
    () => {
      const nextTenantId = urls.tenantId();
      const nextHeld = host.held();
      untracked(() => {
        const flush = nextTenantId !== tenantId || (held && !nextHeld);
        tenantId = nextTenantId;
        held = nextHeld;
        if (flush) {
          pipeline.flush();
        }
      });
    },
    { injector },
  );

  // --- Each change of the request log is measured ---
  effect(
    () => {
      host.requests();
      untracked(() => pipeline.measure());
    },
    { injector },
  );

  // --- The tab hiding, and the page being hidden ---
  const hidden = (): void => {
    if (document.visibilityState === 'hidden') {
      pipeline.flush();
    }
  };
  const pagehide = (): void => pipeline.pagehide();
  document.addEventListener('visibilitychange', hidden);
  document.defaultView?.addEventListener('pagehide', pagehide);
  injector.get(DestroyRef).onDestroy(() => {
    document.removeEventListener('visibilitychange', hidden);
    document.defaultView?.removeEventListener('pagehide', pagehide);
    pipeline.stop();
  });

  // --- The held calls, then every later one ---
  // A call runs untracked: a caller in a reactive context reads none of the
  // pipeline's signals. A held call that throws in development is reported
  // on its own, so the calls after it still run.
  const take = (call: TmTelemetryCall): void => untracked(() => pipeline.take(call));
  for (const call of calls.splice(0)) {
    try {
      take(call);
    } catch (error) {
      queueMicrotask(() => {
        throw error;
      });
    }
  }
  return take;
}
