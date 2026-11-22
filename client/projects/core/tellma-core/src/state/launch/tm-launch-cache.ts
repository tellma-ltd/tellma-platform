// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import {
  afterNextRender,
  computed,
  DestroyRef,
  DOCUMENT,
  effect,
  inject,
  Injectable,
  Injector,
  untracked,
  type Signal,
} from '@angular/core';

import { TmApi } from '../../api/tm-api';
import { TmApiError } from '../../api/tm-api-error';
import { TM_NOW } from '../../app/tm-now';
import type { TmId, TmJson } from '../../json/tm-json';
import type { TmMembership } from '../../launch/tm-membership';
import { TmClientMetadataStore } from '../../metadata/tm-client-metadata';
import { TmTelemetry } from '../../observe/tm-telemetry';
import { TmSession } from '../../session/tm-session';
import { TmClientStore, TmStoreRefusedError } from '../store/tm-client-store';
import type { TmTenantPartition } from '../store/tm-store-layout';
import { TmCacheRefresher } from '../sync/tm-cache-refresher';
import type { TmDistributionInfo } from '../tm-distribution-info';
import type { TmCachedEntitySet, TmLaunchDocs, TmMeResult, TmSyncDoc } from './tm-launch-docs';

// The launch documents (spec 0025 §3): what the chrome renders from on a warm
// launch. The routed tenant's `settings`, `me` and `tenant` come from the
// store, which the boot step filled; entering a tenant inside the app reads
// its partition once and, when a document is missing, syncs before the page
// is created. Cacheable lists load lazily, at their first read, and keep
// serving while a newer list loads. The user's tenants and the distribution's
// facts serve from the store while a background read replaces them.

/** The tenant documents a warm entry needs. */
const LAUNCH = ['settings', 'me', 'tenant'] as const;

/** The prefix of a list's document. */
const LIST = 'entity:';

/** The user documents, each read from its own route. */
type TmUserDoc = 'tenants' | 'distribution';

/**
 * The launch documents of the routed tenant and the signed-in user, and the
 * tenant's cacheable lists.
 *
 * @example
 * ```ts
 * const launch = inject(TmLaunchCache);
 * const timeZone = computed(() => launch.docs.settings()?.timeZone);
 * const currencies = launch.entitySet<Currency>('acme.Currency');
 * ```
 */
@Injectable({ providedIn: 'root' })
export class TmLaunchCache {
  readonly #injector = inject(Injector);
  readonly #store = inject(TmClientStore);
  readonly #refresher = inject(TmCacheRefresher);
  readonly #api = inject(TmApi);
  readonly #now = inject(TM_NOW);
  readonly #telemetry = inject(TmTelemetry);

  /** Each list's signal, by entity name. */
  readonly #sets = new Map<string, Signal<TmCachedEntitySet<unknown> | undefined>>();
  /** The lists loading now, by user, tenant and name. */
  readonly #loading = new Set<string>();
  /** The lists not to load again while their test holds: a refusal, or a list the server does not cache. */
  readonly #skips = new Map<string, () => boolean>();
  /** The user documents' reads in flight. */
  readonly #reads = new Map<TmUserDoc, Promise<void>>();
  /** The user documents read in this launch. */
  readonly #fetched = new Set<TmUserDoc>();
  /** Aborts the user documents' reads when the app is destroyed. */
  readonly #life = new AbortController();
  /** The entry into a tenant in flight, with the signal that aborts when the tab leaves the tenant. */
  #entry: {
    readonly tenantId: number;
    readonly sub: string;
    readonly signal: AbortSignal;
    readonly done: Promise<boolean>;
  } | null = null;

  /** The routed tenant's launch documents; each reads `undefined` while absent. */
  readonly docs: { readonly [K in keyof TmLaunchDocs]: Signal<TmLaunchDocs[K] | undefined> } = {
    settings: this.#tenantDoc('settings'),
    me: this.#tenantDoc('me'),
    tenant: this.#tenantDoc('tenant'),
  };

  /** Whether the routed tenant's `settings`, `me` and `tenant` are all present, so its chrome renders from them. */
  readonly warm: Signal<boolean> = computed(() => LAUNCH.every((name) => this.docs[name]() !== undefined));

  /** The signed-in user's tenants, as the BFF lists them. */
  readonly tenants: Signal<readonly TmMembership[] | undefined> = this.#userDoc('tenants');

  /** The distribution's facts. */
  readonly distribution: Signal<TmDistributionInfo | undefined> = this.#userDoc('distribution');

  /** Creates the launch cache, which follows the routed tenant. */
  constructor() {
    // --- Entering a tenant; on a tenantless route, the user's tenants are read again ---
    effect(() => {
      const partition = this.#refresher.ɵpartition();
      untracked(() => {
        if (partition !== null) {
          this.ɵenter(partition.tenantId).catch(() => undefined);
        } else if (this.#session.profile() !== null) {
          void this.#revalidate(['tenants']);
        }
      });
    });

    // --- Once per launch, at idle: the user documents this launch has not read yet ---
    const window = inject(DOCUMENT).defaultView;
    afterNextRender(
      () => {
        // An app torn down before its idle moment reads nothing.
        const read = (): void => {
          if (!this.#life.signal.aborted) {
            void this.#revalidate((['tenants', 'distribution'] as const).filter((name) => !this.#fetched.has(name)));
          }
        };
        if (window?.requestIdleCallback) {
          window.requestIdleCallback(read);
        } else {
          setTimeout(read);
        }
      },
      { injector: this.#injector },
    );
    inject(DestroyRef).onDestroy(() => this.#life.abort());
  }

  /**
   * A cacheable entity's whole list for the routed tenant. Its first read
   * while the list is absent or stale starts a load; a stale list keeps
   * serving until the newer one lands. It stays `undefined` while the
   * caller may not read the entity.
   *
   * @param name - The entity's name, such as `acme.Currency`.
   * @returns The list's signal.
   */
  entitySet<T = TmJson>(name: string): Signal<TmCachedEntitySet<T> | undefined> {
    let set = this.#sets.get(name);
    if (set === undefined) {
      set = computed(() => {
        const partition = this.#refresher.ɵpartition();
        if (partition === null) {
          return undefined;
        }
        const doc = this.#store.doc<readonly unknown[]>(partition, LIST + name)();
        // The envelope is read so a refused list is tried again once the caller's grants move.
        this.docs.me();
        const stale = doc === undefined || doc.tag !== this.#store.doc<TmSyncDoc>(partition, 'sync')()?.data.entityTags[name];
        if (stale) {
          queueMicrotask(() => void this.#load(partition, name));
        }
        return (
          doc && {
            items: doc.data,
            byId: new Map(doc.data.map((item) => [(item as { readonly id: TmId }).id, item])),
            stale,
          }
        );
      });
      this.#sets.set(name, set);
    }
    return set as Signal<TmCachedEntitySet<T> | undefined>;
  }

  /**
   * Reads the user's tenants and the distribution's facts again, in the
   * background, and stores them.
   *
   * @returns A promise that settles once both are stored or have failed; it never rejects.
   */
  revalidateUser(): Promise<void> {
    return this.#revalidate(['tenants', 'distribution']);
  }

  /**
   * Enters a tenant: reads its partition into the store unless the boot
   * step or an earlier entry has, then, when `settings`, `me` or `tenant` is
   * missing, runs a cold sync, which the tenant's page awaits. A call while
   * an entry into the same tenant is in flight returns its promise, unless
   * the tab left the tenant since; any other call checks the documents
   * again, so an entry after they went absent (a sweep, the idle rule)
   * syncs them. Internal: the tenant route calls it before it creates the
   * page.
   *
   * @param tenantId - The tenant.
   * @returns A promise that settles once the tenant's chrome can render,
   *   with whether the entry was cold: whether it ran a cold sync; it
   *   rejects with the cold sync's error.
   */
  ɵenter(tenantId: number): Promise<boolean> {
    const sub = untracked(this.#session.profile)?.sub;
    if (sub === undefined) {
      return Promise.resolve(false);
    }
    // An entry from before a leave is never joined: leaving aborted its sync.
    const entry = this.#entry;
    if (entry?.tenantId === tenantId && entry.sub === sub && !entry.signal.aborted) {
      return entry.done;
    }
    const partition: TmTenantPartition = { kind: 'tenant', sub, tenantId };
    const done = (async () => {
      await this.#store.ɵreadOnce(partition);
      if (LAUNCH.some((name) => untracked(this.#store.doc(partition, name)) === undefined)) {
        await this.#refresher.refresh(tenantId, LAUNCH, 'cold');
        return true;
      }
      return false;
    })();
    const current = { tenantId, sub, signal: this.#refresher.ɵsignal(tenantId), done };
    this.#entry = current;
    // Only an entry in flight is joined.
    const settled = (): void => {
      if (this.#entry === current) {
        this.#entry = null;
      }
    };
    done.then(settled, settled);
    return done;
  }

  /** The session, resolved at use: the session's own services reach the store. */
  get #session(): TmSession {
    return this.#injector.get(TmSession);
  }

  /** A launch document of the routed tenant. */
  #tenantDoc<T>(name: string): Signal<T | undefined> {
    return computed(() => {
      const partition = this.#refresher.ɵpartition();
      return partition === null ? undefined : this.#store.doc<T>(partition, name)()?.data;
    });
  }

  /** A user document of the signed-in user. */
  #userDoc<T>(name: TmUserDoc): Signal<T | undefined> {
    return computed(() => {
      const sub = this.#session.profile()?.sub;
      return sub === undefined ? undefined : this.#store.doc<T>({ kind: 'user', sub }, name)()?.data;
    });
  }

  /** Reads user documents again and stores them; a read already in flight is joined. */
  async #revalidate(names: readonly TmUserDoc[]): Promise<void> {
    const sub = untracked(this.#session.profile)?.sub;
    if (sub === undefined) {
      return;
    }
    await Promise.all(
      names.map((name) => {
        let read = this.#reads.get(name);
        if (read === undefined) {
          this.#fetched.add(name);
          read = this.#readUser(sub, name).finally(() => this.#reads.delete(name));
          this.#reads.set(name, read);
        }
        return read;
      }),
    );
  }

  /** Reads one user document and stores it. */
  async #readUser(sub: string, name: TmUserDoc): Promise<void> {
    try {
      const options = { method: 'GET', background: true, signal: this.#life.signal } as const;
      const data =
        name === 'tenants'
          ? (await this.#api.read<{ readonly tenants: readonly TmMembership[] }>('/bff/user', undefined, options)).tenants
          : await this.#api.read<TmDistributionInfo>('/api/distribution-info', undefined, options);
      await this.#store.put({ kind: 'user', sub }, name, { tag: null, savedAt: this.#now(), data });
    } catch (error) {
      this.#failed(error);
    }
  }

  /** Loads one list; one load per list at a time. */
  async #load(partition: TmTenantPartition, name: string): Promise<void> {
    // Spec 0025 §3.3.
    const { sub, tenantId } = partition;
    const key = `${sub}/${tenantId}:${name}`;
    if (this.#loading.has(key) || this.#skips.get(key)?.()) {
      return;
    }
    this.#loading.add(key);
    const read = <T>(doc: string): T | undefined => untracked(this.#store.doc<T>(partition, doc))?.data;
    const tagOf = (): string | undefined => read<TmSyncDoc>('sync')?.entityTags[name];
    const access = read<TmMeResult>('me')?.access.tag;
    try {
      // --- Step 1: the list's tag; a sync first when the tab has none, which
      // reports every list's, unless one in flight reports it ---
      if (tagOf() === undefined) {
        await this.#refresher.refresh(tenantId, [LIST + name], 'list');
      }
      const tag = tagOf();
      const segment = this.#injector
        .get(TmClientMetadataStore)
        .peek()
        ?.entities.find((entity) => entity.name === name)?.segment;
      if (tag === undefined || !segment) {
        // Not a list the server caches, or not one it routes: none to load
        // until the lists' composite moves.
        const entities = read<TmSyncDoc>('sync')?.entities;
        this.#skips.set(key, () => read<TmSyncDoc>('sync')?.entities === entities);
        return;
      }
      // --- Step 2: every row of the list ---
      const result = await this.#api.read<{ readonly entities: readonly TmJson[] }>(
        `${segment}/all`,
        {},
        { signal: this.#refresher.ɵsignal(tenantId), background: true, tenantId },
      );
      // --- Step 3: stored under the tag read before the rows; a list that
      // changed meanwhile is reported stale by the next sync ---
      await this.#store.put(partition, LIST + name, { tag, savedAt: this.#now(), data: result.entities });
    } catch (error) {
      if (error instanceof TmApiError && error.kind === 'forbidden') {
        // Nothing is stored; the list is tried again once the caller's grants move.
        this.#skips.set(key, () => read<TmMeResult>('me')?.access.tag === access);
      } else {
        this.#failed(error);
      }
    } finally {
      this.#loading.delete(key);
    }
  }

  /** Reports a failure the HTTP layer and the store have not; the next read tries again. */
  #failed(error: unknown): void {
    if (!(error instanceof TmApiError) && !(error instanceof TmStoreRefusedError)) {
      this.#telemetry.error(error, { source: 'tm-launch-cache' });
    }
  }
}
