// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import { createEnvironmentInjector, DestroyRef, EnvironmentInjector, signal, type Provider, type Signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import {
  TM_LEADER_DOCUMENTS,
  TM_NOW,
  TmCacheRefresher,
  TmClientStore,
  TmLeader,
  TmTabBus,
  TmTelemetry,
  type TmBusMessage,
  type TmCacheChangedEvent,
  type TmLeaderDocument,
  type TmPartition,
  type TmSyncTrigger,
  type TmTenantFacts,
  ɵTM_API_HOST,
  type ɵTmApiHost,
  ɵTmClientMetadataStore,
  type ɵTmSyncDoc,
} from '@tellma/core';
import {
  provideTellmaStateTesting,
  provideTellmaTesting,
  TmFakeApi,
  TmFakeHub,
  type TmFakeHubConnection,
  TmFakeSession,
  TmFakeTelemetry,
  tmTestMetadata,
} from '@tellma/core-testing';
import { Subject, type Observable } from 'rxjs';

import { TM_LEADER_LOCKS, TmLeaderElection } from './tm-leader-election';
import { TM_HUB_FACTORY } from './tm-leadership';

// The leader election's test rig: tabs of one browser in one test page. Each
// tab is an injector of its own over the test's root, with its own election,
// TmLeader, page (visibility and page events, scripted), tab bus, telemetry,
// refresher stand-in and realtime connection stand-in, and with the real Web
// Locks of an iframe of its own, so each tab is a client of its own to the
// browser's lock manager, and closing the iframe releases its locks as a
// crash would. The tabs share the root's client store, as tabs share the
// browser's IndexedDB, the scripted API and the session.

/** The real `setTimeout`, captured before a spec fakes the timers. */
const realSetTimeout = globalThis.setTimeout.bind(globalThis);

/** A tenant's partition of the signed-in user. */
export type TmTenantPartition = Extract<TmPartition, { readonly kind: 'tenant' }>;

/** The test user's partition of tenant 17. */
export const TM_LEADER_T17: TmTenantPartition = { kind: 'tenant', sub: 'test-user', tenantId: 17 };

/** The test user's partition of tenant 18. */
export const TM_LEADER_T18: TmTenantPartition = { kind: 'tenant', sub: 'test-user', tenantId: 18 };

/** The hub lock of tenant 17. */
export const TM_LEADER_LOCK = 'tm:acme:test-user:17:hub';

/**
 * A distribution's job list, as a leader document whose stored copy asks for
 * polling while `running` is set. Named apart from the platform's own leader
 * documents, so a rig that also has them registered never registers a name twice.
 */
export const TM_JOBS_DOC: TmLeaderDocument = {
  name: 'acme.jobs',
  route: 'acme/jobs/summary',
  events: ['acme.jobs.changed', 'job.changed'],
  formatVersion: 1,
  pollWhile: (document) => (document as { running?: boolean } | null)?.running === true,
};

/** A distribution's task list, as a leader document that never asks for polling. */
export const TM_TASKS_DOC: TmLeaderDocument = {
  name: 'acme.tasks',
  route: 'acme/tasks/summary',
  events: ['acme.tasks.changed'],
  formatVersion: 1,
};

/** The hub events this build knows. */
const CLIENT_EVENTS = ['cache.changed', 'acme.jobs.changed', 'job.changed', 'acme.tasks.changed', 'session.ended'];

/** A tab bus network: what one tab's bus posts, every other tab's bus receives, a task later. */
export class TmBusNetwork {
  readonly #buses = new Set<TmLinkedTabBus>();

  /** Creates a bus on the network. */
  bus(): TmLinkedTabBus {
    const bus = new TmLinkedTabBus(this);
    this.#buses.add(bus);
    return bus;
  }

  /** Delivers a post to every other open bus, as a structured clone. */
  ɵpost(from: TmLinkedTabBus, message: TmBusMessage): void {
    for (const bus of this.#buses) {
      if (bus !== from) {
        const copy = structuredClone(message);
        void Promise.resolve().then(() => bus.ɵreceive(copy));
      }
    }
  }

  /** Takes a bus off the network. */
  ɵclose(bus: TmLinkedTabBus): void {
    this.#buses.delete(bus);
  }
}

/** One tab's bus on a {@link TmBusNetwork}. */
export class TmLinkedTabBus extends TmTabBus {
  /** The tab's id. */
  override readonly tabId: string = crypto.randomUUID();
  readonly #received = new Subject<TmBusMessage>();
  readonly #posted: TmBusMessage[] = [];
  #open = true;

  /** The messages other tabs posted. */
  override readonly messages: Observable<TmBusMessage> = this.#received.asObservable();

  /** Open from the start. */
  override readonly ɵopened: Promise<void> = Promise.resolve();

  /**
   * Creates a bus on a network.
   *
   * @param network - The network.
   */
  constructor(readonly network: TmBusNetwork) {
    super();
  }

  /** Every message this tab posted, oldest first. */
  get posted(): readonly TmBusMessage[] {
    return this.#posted;
  }

  /** Posts to every other tab. */
  override post(message: TmBusMessage): void {
    if (this.#open) {
      this.#posted.push(message);
      this.network.ɵpost(this, message);
    }
  }

  /** Delivers another tab's post. */
  ɵreceive(message: TmBusMessage): void {
    if (this.#open) {
      this.#received.next(message);
    }
  }

  /** Closes the bus: the tab neither posts nor receives again. */
  close(): void {
    this.#open = false;
    this.network.ɵclose(this);
  }
}

/** The refresher's surface the election uses, recorded. */
export class TmRecordingRefresher {
  /** The routed tenant's partition, which the spec moves. */
  readonly partition = signal<TmTenantPartition | null>(TM_LEADER_T17);
  /** The routed tenant's partition, as the election reads it. */
  readonly ɵpartition: Signal<TmTenantPartition | null> = this.partition.asReadonly();
  /** Every refresh asked for, oldest first. */
  readonly refreshes: { readonly tenantId: number; readonly docs: readonly string[]; readonly trigger: TmSyncTrigger }[] = [];
  /** Every `cache.changed` handed over, oldest first. */
  readonly cacheChanges: { readonly tenantId: number; readonly event: TmCacheChangedEvent }[] = [];
  /** What each refresh does before it settles, such as storing a sync's time. */
  onRefresh: (trigger: TmSyncTrigger) => Promise<void> = () => Promise.resolve();

  /** Records a refresh. */
  refresh(tenantId: number, docs: readonly string[], trigger: TmSyncTrigger): Promise<void> {
    this.refreshes.push({ tenantId, docs, trigger });
    return this.onRefresh(trigger);
  }

  /** Records a `cache.changed`. */
  ɵcacheChanged(tenantId: number, event: TmCacheChangedEvent): void {
    this.cacheChanges.push({ tenantId, event });
  }

  /** The triggers of the refreshes so far. */
  triggers(): TmSyncTrigger[] {
    return this.refreshes.map((refresh) => refresh.trigger);
  }
}

/**
 * A tab's page, as the rig scripts it: the browser seam, with the visibility
 * and the page events the test sets. The primary's fake API host is one; a
 * spec passes it in, since a rig may not reach another entry point's files.
 */
export interface TmLeaderPage extends ɵTmApiHost {
  /** Whether the page is visible. */
  visibleNow: boolean;
  /**
   * Fires an event at its listeners.
   *
   * @param target - Where it fires.
   * @param type - The event's name.
   */
  fire(target: 'window' | 'document', type: string): void;
}

/** One tab of the rig. */
export interface TmLeaderTab {
  /** The tab's election. */
  readonly election: TmLeaderElection;
  /** The tab's view of the leader. */
  readonly leader: TmLeader;
  /** The tab's page: visibility and page events. */
  readonly host: TmLeaderPage;
  /** The tab's bus. */
  readonly bus: TmLinkedTabBus;
  /** The tab's telemetry. */
  readonly telemetry: TmFakeTelemetry;
  /** The tab's refresher stand-in. */
  readonly refresher: TmRecordingRefresher;
  /** The realtime connections the tab's leaderships created, oldest first. */
  readonly hubs: readonly TmFakeHubConnection[];
  /** The iframe whose lock manager the tab uses. */
  readonly frame: HTMLIFrameElement;
  /** Hides the page. */
  hide(): void;
  /** Shows the page. */
  show(): void;
  /** Fires a page event, such as `freeze`. */
  fire(target: 'window' | 'document', type: string): void;
  /** The props of the tab's telemetry events of one name. */
  events(name: string): unknown[];
  /** Ends the tab as a crash or a discard does: the browser releases its locks, and nothing in the tab runs again. */
  crash(): void;
  /** Ends the tab's app, as closing the tab does. */
  close(): void;
}

/** How a tab of the rig starts. */
export interface TmLeaderTabOptions {
  /** Start hidden. */
  readonly hidden?: boolean;
  /** No realtime entry point: the leader never connects. */
  readonly noHub?: boolean;
  /** No Web Locks in the page. */
  readonly noLocks?: boolean;
  /** A lock manager the test scripts, in place of the iframe's real one. */
  readonly locks?: LockManager;
}

/** The rig: the root's services, and a way to open tabs. */
export interface TmLeaderRig {
  /** The shared store. */
  readonly store: TmClientStore;
  /** The shared scripted API. */
  readonly api: TmFakeApi;
  /** The shared session. */
  readonly session: TmFakeSession;
  /** Opens a tab. */
  tab(options?: TmLeaderTabOptions): TmLeaderTab;
  /** Stores a tenant's facts, tenant 17's unless named, with the realtime flag. */
  realtime(on: boolean, partition?: TmTenantPartition): Promise<void>;
}

/**
 * Configures the root, stores a day-old sync of tenants 17 and 18, without
 * which their documents read as absent, as a warm launch finds them, and
 * returns the rig. The spec fakes
 * the timers after this; the test's end closes every tab.
 *
 * @param page - Creates a tab's page.
 * @param docs - The leader documents.
 * @param providers - More root providers.
 * @returns The rig.
 */
export async function tmLeaderRig(
  page: () => TmLeaderPage,
  docs: readonly TmLeaderDocument[] = [TM_JOBS_DOC, TM_TASKS_DOC],
  providers: Provider[] = [],
): Promise<TmLeaderRig> {
  TestBed.configureTestingModule({
    providers: [
      provideTellmaTesting(),
      provideTellmaStateTesting(),
      ...docs.map((doc) => ({ provide: TM_LEADER_DOCUMENTS, multi: true, useValue: doc })),
      { provide: TM_NOW, useValue: () => Date.now() },
      {
        provide: ɵTmClientMetadataStore,
        useFactory: () => {
          const metadata = new ɵTmClientMetadataStore();
          metadata.set(tmTestMetadata({ clientEvents: CLIENT_EVENTS }));
          return metadata;
        },
      },
      ...providers,
    ],
  });
  const network = new TmBusNetwork();
  const store = TestBed.inject(TmClientStore);
  // A day old, as a warm launch finds it: older than the page, within the idle window.
  const at = Date.now() - 86_400_000;
  for (const partition of [TM_LEADER_T17, TM_LEADER_T18]) {
    const sync: ɵTmSyncDoc = { at, entities: null, entityTags: {} };
    await store.put(partition, 'sync', { tag: null, savedAt: at, data: sync });
  }
  const root = TestBed.inject(EnvironmentInjector);
  // The test's end ends every tab, so no lock outlives it.
  TestBed.inject(DestroyRef).onDestroy(() => tmLeaderRigEnd());
  return {
    store,
    api: TestBed.inject(TmFakeApi),
    session: TestBed.inject(TmFakeSession),
    realtime: (on, partition = TM_LEADER_T17) => {
      const facts: TmTenantFacts = { id: partition.tenantId, category: 'Live', state: 'Active', realtime: on };
      return store.put(partition, 'tenant', { tag: null, savedAt: Date.now(), data: facts });
    },
    tab: (options = {}) => {
      const frame = document.createElement('iframe');
      document.body.appendChild(frame);
      const host = page();
      host.visibleNow = options.hidden !== true;
      const bus = network.bus();
      const telemetry = new TmFakeTelemetry();
      const refresher = new TmRecordingRefresher();
      // Each tab's leaderships open their connections on a fake hub of the tab's own.
      const hub = new TmFakeHub();
      const injector = createEnvironmentInjector(
        [
          TmLeaderElection,
          TmLeader,
          { provide: ɵTM_API_HOST, useValue: host },
          { provide: TmTabBus, useValue: bus },
          { provide: TmTelemetry, useValue: telemetry },
          { provide: TmCacheRefresher, useValue: refresher },
          { provide: TM_LEADER_LOCKS, useValue: options.noLocks === true ? null : (options.locks ?? frame.contentWindow!.navigator.locks) },
          { provide: TM_HUB_FACTORY, useValue: options.noHub === true ? null : hub.factory },
        ],
        root,
      );
      opened.add(injector);
      const tab: TmLeaderTab = {
        election: injector.get(TmLeaderElection),
        leader: injector.get(TmLeader),
        host,
        bus,
        telemetry,
        refresher,
        get hubs() {
          return hub.connections;
        },
        frame,
        hide: () => {
          host.visibleNow = false;
          host.fire('document', 'visibilitychange');
        },
        show: () => {
          host.visibleNow = true;
          host.fire('document', 'visibilitychange');
        },
        fire: (target, type) => host.fire(target, type),
        events: (name) =>
          telemetry
            .recent()
            .filter((item) => item.name === name)
            .map((item) => item.props),
        crash: () => {
          // The page goes first, so the browser, not the app, lets the locks go.
          bus.close();
          frame.remove();
          if (opened.delete(injector)) {
            injector.destroy();
          }
        },
        close: () => {
          bus.close();
          if (opened.delete(injector)) {
            injector.destroy();
          }
          frame.remove();
        },
      };
      frames.add(frame);
      return tab;
    },
  };
}

/** The tabs' injectors not yet closed. */
const opened = new Set<EnvironmentInjector>();

/** The tabs' iframes. */
const frames = new Set<HTMLIFrameElement>();

/** Ends every tab the rig opened, so no lock outlives the spec; the test's end calls it too. */
export function tmLeaderRigEnd(): void {
  opened.forEach((injector) => injector.destroy());
  opened.clear();
  frames.forEach((frame) => frame.remove());
  frames.clear();
}

/** How {@link tmUntil} waits. */
export interface TmUntilOptions {
  /** How far the faked clock moves on each turn, in milliseconds: 0 runs only the timers due now. */
  readonly step?: number;
  /** How long to wait, in real milliseconds. */
  readonly timeoutMs?: number;
}

/**
 * Waits, in real time, until a condition holds: lock grants cross to the
 * browser and back, whatever the faked timers say. Each turn flushes the
 * effects and runs the faked timers due, moving the faked clock by the step;
 * a timer set while the clock ticks falls due a millisecond later, so a wait
 * on one takes a step of 1.
 *
 * @param condition - The condition.
 * @param options - How it waits.
 */
export async function tmUntil(condition: () => boolean, options: TmUntilOptions = {}): Promise<void> {
  const { step = 0, timeoutMs = 3_000 } = options;
  const end = performance.now() + timeoutMs;
  for (;;) {
    if (vi.isFakeTimers()) {
      await vi.advanceTimersByTimeAsync(step);
    }
    TestBed.tick();
    if (condition()) {
      return;
    }
    if (performance.now() > end) {
      throw new Error('The condition did not hold in time.');
    }
    await new Promise((resolve) => realSetTimeout(resolve, 5));
  }
}

/**
 * Lets real time pass, so the browser would have granted a lock it is going
 * to grant; for a spec that checks that something did not happen.
 *
 * @param ms - How long, in real milliseconds.
 */
export async function tmPause(ms = 60): Promise<void> {
  await new Promise((resolve) => realSetTimeout(resolve, ms));
  TestBed.tick();
}

/**
 * Advances the faked clock and runs the timers due, flushing effects.
 *
 * @param ms - How far, in milliseconds.
 */
export async function tmAdvance(ms: number): Promise<void> {
  await vi.advanceTimersByTimeAsync(ms);
  TestBed.tick();
  await vi.advanceTimersByTimeAsync(0);
}
