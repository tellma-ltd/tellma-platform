// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import {
  afterNextRender,
  computed,
  DestroyRef,
  DOCUMENT,
  inject,
  Injectable,
  Injector,
  signal,
  untracked,
  type Signal,
  type WritableSignal,
} from '@angular/core';
import { Observable } from 'rxjs';

import { TM_NOW } from '../../app/tm-now';
import { TmTelemetry } from '../../observe/tm-telemetry';
import { TmSession } from '../../session/tm-session';
import type { TmStoreChangedMessage } from '../bus/tm-bus-messages';
import { TmTabBus } from '../bus/tm-tab-bus';
import { TM_BOOT_RECORD_KEY, tmParseBootRecord, type TmBootRecord } from '../tm-boot-record';
import { tmWriteItem } from '../tm-storage-json';
import { tmDocFormat, TM_STORE_EXTRA_FORMATS, TM_STORE_FORMATS } from './tm-doc-formats';
import { TM_STORE_HOST } from './tm-store-host';
import {
  TM_IMAGES_CACHE,
  TM_STORE_DATABASE,
  TM_STORE_DOCS,
  TM_STORE_KEY_PATH,
  TM_STORE_VERSION,
  TM_USERS_PREFIX,
  tmPartitionKey,
  tmPartitionKeyBounds,
  tmPrefixKeyBounds,
  tmSessionsPrefix,
  tmStoredName,
  type TmPartition,
} from './tm-store-layout';

// The client store (spec 0025 §2). One IndexedDB database, `tm-app`, holds
// every cached document in one object store keyed `[p, n]`: the partition's
// key and the document's stored name. A partition is a key range, so a
// user's, a tenant's or a session's data goes in one delete. Each tab keeps a
// mirror of the documents it read, which `doc()` signals show and which
// follows the other tabs through `store.changed` on the tab bus. A write is
// refused unless the session is the partition's user's and is active, so a
// response that lands after another user signed in never reaches the wrong
// partition. When IndexedDB cannot open, the store runs on its mirror alone.

/** One stored document: its value, the wire tag it was fetched under, and when it was written. */
export interface TmStoredDoc<T> {
  /** The wire tag the document was fetched under, for documents that have one; else `null`. */
  readonly tag: string | null;
  /** When the writer stored it, in epoch milliseconds; a newer stored copy is never replaced by an older one. */
  readonly savedAt: number;
  /** The document's value. */
  readonly data: T;
}

/**
 * What a sweep deletes: every user's partitions when a session ends, the
 * signed-in user's session partitions other than the current one, or one
 * tenant partition of the signed-in user.
 */
export type TmSweepRule = 'session-ended' | { readonly sessionOtherThan: string } | { readonly tenant: number };

/** Why the client store refused a write. */
export type TmStoreRefusedReason = 'other-user' | 'cookie' | 'session';

/**
 * The error a refused write rejects with: the partition is another user's,
 * the profile cookie no longer names the tab's user, or the session is not
 * active. A refused write writes nothing.
 */
export class TmStoreRefusedError extends Error {
  /** Why the write was refused. */
  readonly reason: TmStoreRefusedReason;

  /**
   * Creates the error.
   *
   * @param reason - Why the write was refused.
   */
  constructor(reason: TmStoreRefusedReason) {
    super(`The client store refused a write (${reason}).`);
    this.name = 'TmStoreRefusedError';
    this.reason = reason;
  }
}

/** The boot record's reader and writer, and another tab's writes to it. */
export interface TmBootRecordAccess {
  /** Reads the boot record now; the defaults when it is absent or invalid. */
  read(): TmBootRecord;
  /**
   * Changes members of the boot record. The app's only writer of it.
   *
   * @param delta - The members to change.
   * @throws Error when the result is not a valid boot record.
   */
  patch(delta: Partial<Omit<TmBootRecord, 'v'>>): void;
  /** The boot record after each write another tab makes to it. */
  readonly changes: Observable<TmBootRecord>;
}

/** The value of one record in the `docs` store. */
interface TmDocRecord {
  readonly p: string;
  readonly n: string;
  readonly tag: string | null;
  readonly savedAt: number;
  readonly data: unknown;
}

/** One mirrored document: its stored copy, and the view `doc()` hands out. */
interface TmMirrorEntry {
  readonly p: string;
  readonly n: string;
  readonly stored: WritableSignal<TmStoredDoc<unknown> | undefined>;
  view?: Signal<TmStoredDoc<unknown> | undefined>;
}

/** How long the database may take to open before the tab runs in memory. */
const OPEN_TIMEOUT_MS = 3_000;

/** The idle window when the stored distribution names none: 7 days. */
const DEFAULT_IDLE_DAYS = 7;

/** One day in milliseconds. */
const DAY_MS = 86_400_000;

/** The user documents the idle window ends by their own age. */
const AGED_USER_DOCS: ReadonlySet<string> = new Set(['tenants', 'distribution']);

/** The mirror's key of a document. */
function mirrorKey(p: string, n: string): string {
  return `${p}\u0000${n}`;
}

/** Whether a stored value is a document record, member by member; anything else reads as absent. */
function toDoc(value: unknown): TmStoredDoc<unknown> | undefined {
  const record = value as Partial<TmDocRecord> | undefined;
  return typeof record === 'object' &&
    record !== null &&
    typeof record.savedAt === 'number' &&
    (record.tag === null || typeof record.tag === 'string') &&
    'data' in record
    ? { tag: record.tag, savedAt: record.savedAt, data: record.data }
    : undefined;
}

/** Whether an error is the browser's quota refusal. */
function isQuota(error: unknown): boolean {
  return error instanceof DOMException && error.name === 'QuotaExceededError';
}

/**
 * The client store: the only owner of IndexedDB and of the boot record. It
 * decides where each cached document lives, when a stored document counts as
 * absent, who may write it, and what a session's end deletes.
 *
 * @example
 * ```ts
 * const store = inject(TmClientStore);
 * const tenant: TmPartition = { kind: 'tenant', sub: profile.sub, tenantId: 17 };
 * const settings = store.doc<TenantSettings>(tenant, 'settings');
 * await store.put(tenant, 'settings', { tag: '1.42', savedAt: Date.now(), data });
 * ```
 */
@Injectable({ providedIn: 'root' })
export class TmClientStore {
  readonly #host = inject(TM_STORE_HOST);
  readonly #injector = inject(Injector);
  readonly #bus = inject(TmTabBus);
  readonly #telemetry = inject(TmTelemetry);
  readonly #now = inject(TM_NOW);
  readonly #window = inject(DOCUMENT).defaultView;
  // Extra formats are exact names, so they join the fixed ones in one table.
  readonly #formats: Readonly<Record<string, number>> = { ...inject(TM_STORE_FORMATS), ...inject(TM_STORE_EXTRA_FORMATS) };

  /** Every document this tab mirrors, by partition and stored name. */
  readonly #mirror = new Map<string, TmMirrorEntry>();
  /** The documents this tab is writing, whose own commit settles their mirror. */
  readonly #pending = new Map<string, number>();
  #mode: 'indexeddb' | 'memory' = 'indexeddb';
  /** The open (or opening) connection; `null` before the first access and after a `versionchange` closed it. */
  #connection: Promise<IDBDatabase | null> | null = null;
  #persistAsked = false;
  /** The boot record, once local storage refused it. */
  #memoryBoot: TmBootRecord | null = null;

  /** The boot record: `localStorage['tm.boot']`, in memory when local storage refuses. */
  readonly boot: TmBootRecordAccess = {
    read: () => this.#readBoot(),
    patch: (delta) => this.#patchBoot(delta),
    changes: new Observable<TmBootRecord>((subscriber) => {
      const events = this.#host.events;
      // The storage event fires in every other tab of the origin, never in
      // the writer; a `null` key is a clear() of the whole storage.
      const listener = (event: Event): void => {
        const key = (event as StorageEvent).key;
        if (key === TM_BOOT_RECORD_KEY || key === null) {
          subscriber.next(this.#readBoot());
        }
      };
      events?.addEventListener('storage', listener);
      return () => events?.removeEventListener('storage', listener);
    }),
  };

  /** Creates the store; the database opens at the first access. */
  constructor() {
    // Another tab's commit: re-read what this tab mirrors of it.
    const subscription = this.#bus.messages.subscribe((message) => {
      if (message.type === 'store.changed') {
        void this.#changed(message).catch((error: unknown) => this.#telemetry.error(error, { source: 'tm-client-store' }));
      }
    });
    // Back from the back-forward cache, the tab missed every message posted
    // meanwhile (spec 0025 §6.1): every mirrored document is read again.
    const show = (event: Event): void => {
      if ((event as PageTransitionEvent).persisted) {
        void this.#reread(() => true).catch((error: unknown) => this.#telemetry.error(error, { source: 'tm-client-store' }));
      }
    };
    this.#host.events?.addEventListener('pageshow', show);
    // The app's end lets go of the bus, the page and the database.
    inject(DestroyRef).onDestroy(() => {
      subscription.unsubscribe();
      this.#host.events?.removeEventListener('pageshow', show);
      void this.#connection?.then((db) => db?.close());
      this.#connection = null;
    });
  }

  /** Where the documents live: `memory` once IndexedDB failed to open, and every launch is then cold. */
  get mode(): 'indexeddb' | 'memory' {
    return this.#mode;
  }

  /**
   * Reads every document of the partitions into the mirror, in one readonly
   * transaction. A mirrored document a partition no longer holds reads as
   * absent afterwards; another build's copies are skipped.
   *
   * @param partitions - The partitions.
   * @returns A promise that resolves once the mirror holds them.
   */
  async readAll(...partitions: TmPartition[]): Promise<void> {
    const keys = partitions.map((partition) => tmPartitionKey(partition));
    await this.#transact('readonly', (store) => {
      partitions.forEach((partition, index) => {
        const p = keys[index]!;
        const request = store.getAll(IDBKeyRange.bound(...tmPartitionKeyBounds(partition)));
        request.onsuccess = () => {
          const found = new Set<string>();
          for (const value of request.result as TmDocRecord[]) {
            const { n } = value;
            // Only this build's formats: another build's copy is not read.
            const at = n.lastIndexOf('@');
            if (at > 0 && tmDocFormat(this.#formats, n.slice(0, at)) === Number(n.slice(at + 1))) {
              found.add(n);
              this.#settle(p, n, toDoc(value));
            }
          }
          for (const entry of this.#mirror.values()) {
            if (entry.p === p && !found.has(entry.n)) {
              this.#settle(p, entry.n, undefined);
            }
          }
        };
      });
      return () => undefined;
    });
  }

  /**
   * The mirrored document, which follows this tab's writes and other tabs'.
   * It reads `undefined` while the document is absent: not stored, stored by
   * another build, or past the idle window.
   *
   * @param partition - The document's partition.
   * @param name - The document's name, such as `settings` or `entity:acme.Currency`.
   * @returns The document's signal.
   * @throws Error when the name has no client format.
   */
  doc<T>(partition: TmPartition, name: string): Signal<TmStoredDoc<T> | undefined> {
    const p = tmPartitionKey(partition);
    const entry = this.#entry(p, this.#storedName(name));
    entry.view ??= this.#view(partition, name, entry.stored);
    return entry.view as Signal<TmStoredDoc<T> | undefined>;
  }

  /**
   * Stores a document, unless the stored copy was saved later: then nothing
   * is written and the mirror keeps the stored copy. The mirror shows the
   * document at once; other tabs re-read it once it commits. A write the
   * browser's quota refuses is dropped, and the document stays in this tab's
   * mirror.
   *
   * @param partition - The document's partition.
   * @param name - The document's name.
   * @param doc - The document.
   * @returns A promise that resolves once the write commits or is dropped.
   * @throws TmStoreRefusedError when the write guard refuses.
   */
  async put<T>(partition: TmPartition, name: string, doc: TmStoredDoc<T>): Promise<void> {
    this.#guard(partition);
    const p = tmPartitionKey(partition);
    const n = this.#storedName(name);
    const entry = this.#entry(p, n);
    if ((untracked(entry.stored)?.savedAt ?? -Infinity) <= doc.savedAt) {
      entry.stored.set(doc);
    }
    await this.#write(partition, p, n, 'put', (store, fail) => {
      let written = false;
      const request = store.get([p, n]);
      request.onsuccess = () => {
        const stored = toDoc(request.result);
        if (stored !== undefined && stored.savedAt > doc.savedAt) {
          // A later copy is stored: it wins, here and in the mirror.
          entry.stored.set(stored);
          return;
        }
        try {
          store.put({ p, n, tag: doc.tag, savedAt: doc.savedAt, data: doc.data } satisfies TmDocRecord);
          written = true;
        } catch (error) {
          // A value IndexedDB cannot clone.
          fail(error);
        }
      };
      return () => written;
    });
  }

  /**
   * Replaces a document with what `update` makes of its stored value, in one
   * readwrite transaction, so no other tab's write lands between the read
   * and the write. `undefined` deletes the document. The stored tag is kept,
   * and `savedAt` becomes now.
   *
   * @param partition - The document's partition.
   * @param name - The document's name.
   * @param update - Makes the new value from the stored one, or `undefined` to delete; runs synchronously.
   * @returns A promise that resolves once the write commits or is dropped.
   * @throws TmStoreRefusedError when the write guard refuses; whatever `update` throws, writing nothing.
   */
  async patch<T>(
    partition: TmPartition,
    name: string,
    update: (current: T | undefined) => T | undefined,
  ): Promise<void> {
    this.#guard(partition);
    const p = tmPartitionKey(partition);
    const n = this.#storedName(name);
    const entry = this.#entry(p, n);
    // The new copy, from the stored one: the mirror's when the store is in memory.
    const next = (current: TmStoredDoc<unknown> | undefined): TmStoredDoc<unknown> | undefined => {
      const data = update(current?.data as T | undefined);
      return data === undefined ? undefined : { tag: current?.tag ?? null, savedAt: this.#now(), data };
    };
    if ((await this.#database()) === null) {
      entry.stored.set(next(untracked(entry.stored)));
      return;
    }
    await this.#write(partition, p, n, 'patch', (store, fail) => {
      let written = false;
      const request = store.get([p, n]);
      request.onsuccess = () => {
        const current = toDoc(request.result);
        let doc: TmStoredDoc<unknown> | undefined;
        try {
          doc = next(current);
          if (doc !== undefined) {
            store.put({ p, n, ...doc } satisfies TmDocRecord);
            written = true;
          } else if (current !== undefined) {
            store.delete([p, n]);
            written = true;
          }
        } catch (error) {
          // What update threw, or a value IndexedDB cannot clone: nothing is written.
          fail(error);
          return;
        }
        entry.stored.set(doc);
      };
      return () => written;
    });
  }

  /**
   * Deletes a key range of user partitions; the device partition survives
   * every rule. Every tab's mirror of the deleted documents empties. A
   * session's end also deletes the image cache.
   *
   * @param rule - What to delete.
   * @returns A promise that resolves once the delete commits.
   */
  async sweep(rule: TmSweepRule): Promise<void> {
    const started = performance.now();
    const profile = untracked(() => this.#injector.get(TmSession).profile());
    let prefix: string;
    let ranges: IDBKeyRange[];
    let deletes: (p: string) => boolean;
    let name: string;
    if (rule === 'session-ended') {
      // Every user partition, other users' leftovers included.
      name = 'session-ended';
      prefix = TM_USERS_PREFIX;
      ranges = [IDBKeyRange.bound(...tmPrefixKeyBounds(prefix))];
      deletes = (p) => p.startsWith(prefix);
    } else if (profile === null) {
      // The other rules are the signed-in user's; with no user, nothing is theirs.
      return;
    } else if ('sessionOtherThan' in rule) {
      // The two ranges on either side of the current session's partition.
      name = 'session-other-than';
      prefix = tmSessionsPrefix(profile.sub);
      const kept = tmPartitionKey({ kind: 'session', sub: profile.sub, sessionHandle: rule.sessionOtherThan });
      const [, upper] = tmPrefixKeyBounds(prefix);
      ranges = [IDBKeyRange.bound([prefix], [kept], false, true), IDBKeyRange.bound([kept, []], upper, true)];
      deletes = (p) => p.startsWith(prefix) && p !== kept;
    } else {
      name = 'tenant';
      const partition: TmPartition = { kind: 'tenant', sub: profile.sub, tenantId: rule.tenant };
      prefix = tmPartitionKey(partition);
      ranges = [IDBKeyRange.bound(...tmPartitionKeyBounds(partition))];
      deletes = (p) => p === prefix;
    }
    for (const entry of this.#mirror.values()) {
      if (deletes(entry.p)) {
        entry.stored.set(undefined);
      }
    }
    const [committed] = await Promise.all([
      this.#transact('readwrite', (store) => {
        for (const range of ranges) {
          store.delete(range);
        }
        return () => undefined;
      }),
      rule === 'session-ended' ? this.#deleteImages() : undefined,
    ]);
    if (committed !== null) {
      // No names: every tab re-reads what it mirrors under the prefix.
      this.#bus.post({ type: 'store.changed', partition: prefix, names: null });
    }
    this.#telemetry.timing('tm.store.sweep', performance.now() - started, { rule: name });
  }

  /**
   * The store's boot step: an initializer of the app's startup. It opens the
   * database and, once the tab bus is open, so that no other tab's change
   * goes unheard, reads the device partition, the signed-in user's partition
   * and, on a tenant path, that tenant's, in one readonly transaction: a warm
   * launch's only store access. A boot record that names no user, or another
   * user, sweeps every user's data first. Then it records the user in the
   * boot record, and once the app has rendered, at idle, sweeps the user's
   * other sessions. Internal.
   *
   * @param tenantId - The tenant of the path the app starts on, or `null`.
   * @returns A promise that resolves once the documents are mirrored.
   */
  async ɵstart(tenantId: number | null): Promise<void> {
    const profile = untracked(() => this.#injector.get(TmSession).profile());
    await Promise.all([this.#database(), this.#bus.ɵopened]);

    // Spec 0025 §2.6, step 1: the boot script resets the record whenever its
    // user differs from the profile's, so a record naming no user is a cold
    // path. It backs up the boot script's time-boxed expiry delete, and
    // deliberately takes every user's leftovers too.
    const recorded = this.boot.read().sub;
    if (recorded === null || recorded !== profile?.sub) {
      await this.sweep('session-ended');
    }

    // Step 2: one readAll.
    const partitions: TmPartition[] = [{ kind: 'device' }];
    if (profile !== null) {
      partitions.push({ kind: 'user', sub: profile.sub });
      if (tenantId !== null) {
        partitions.push({ kind: 'tenant', sub: profile.sub, tenantId });
      }
    }
    await this.readAll(...partitions);
    if (profile === null) {
      return;
    }

    // The session's write of its user (spec 0024 §8.1), only after the sweep
    // and the read: until then the record names no user whose data is kept.
    if (recorded !== profile.sub) {
      this.boot.patch({ sub: profile.sub });
    }

    // At idle after the first render, the user's other sessions go.
    afterNextRender(
      () => {
        const sweep = (): void =>
          void this.sweep({ sessionOtherThan: profile.sh }).catch((error: unknown) =>
            this.#telemetry.error(error, { source: 'tm-client-store' }),
          );
        if (this.#window?.requestIdleCallback) {
          this.#window.requestIdleCallback(sweep);
        } else {
          setTimeout(sweep);
        }
      },
      { injector: this.#injector },
    );
  }

  /**
   * Refuses a write that the session does not allow: into another user's
   * partition, under a cookie that no longer names the tab's user, or while
   * the session is not active.
   */
  #guard(partition: TmPartition): void {
    // Spec 0025 §2.5. A response the HTTP layer checked can still land after
    // the cookie changed, so this is the last line between two users' data.
    const reason = untracked((): TmStoreRefusedReason | null => {
      const session = this.#injector.get(TmSession);
      if (partition.kind !== 'device' && partition.sub !== session.profile()?.sub) {
        return 'other-user';
      }
      if (session.check() !== 'same') {
        return 'cookie';
      }
      return session.state() === 'active' ? null : 'session';
    });
    if (reason !== null) {
      this.#telemetry.event('tm.store.refused', { reason });
      throw new TmStoreRefusedError(reason);
    }
  }

  /** The stored name of a document name in this build's format. */
  #storedName(name: string): string {
    const format = tmDocFormat(this.#formats, name);
    if (format === undefined) {
      throw new Error(`The client store has no format for the document '${name}'. Register its format before storing it.`);
    }
    return tmStoredName(name, format);
  }

  /** The mirror entry of a document, created empty on first use. */
  #entry(p: string, n: string): TmMirrorEntry {
    const key = mirrorKey(p, n);
    let entry = this.#mirror.get(key);
    if (entry === undefined) {
      entry = { p, n, stored: signal<TmStoredDoc<unknown> | undefined>(undefined) };
      this.#mirror.set(key, entry);
    }
    return entry;
  }

  /** Sets a mirrored copy read from the database, unless this tab is writing it. */
  #settle(p: string, n: string, doc: TmStoredDoc<unknown> | undefined): void {
    if (!this.#pending.has(mirrorKey(p, n))) {
      this.#entry(p, n).stored.set(doc);
    }
  }

  /**
   * The view `doc()` hands out: the stored copy, or `undefined` while it
   * counts as absent by the idle rule. The rule is applied
   * when the view is read, not as time passes: a session left idle for the
   * whole window ends anyway.
   */
  #view(
    partition: TmPartition,
    name: string,
    stored: Signal<TmStoredDoc<unknown> | undefined>,
  ): Signal<TmStoredDoc<unknown> | undefined> {
    // Spec 0025 §2.4, "Idle".
    if (partition.kind === 'tenant') {
      // Every document of a tenant is absent without a recent sync.
      const sync = this.#entry(tmPartitionKey(partition), this.#storedName('sync')).stored;
      return computed(() => {
        const at = (sync()?.data as { readonly at?: unknown } | undefined)?.at;
        return typeof at === 'number' && this.#fresh(partition.sub, at) ? stored() : undefined;
      });
    }
    if (partition.kind === 'user' && AGED_USER_DOCS.has(name)) {
      // The user's tenants and distribution are absent once older than the window.
      return computed(() => {
        const doc = stored();
        return doc !== undefined && this.#fresh(partition.sub, doc.savedAt) ? doc : undefined;
      });
    }
    return stored;
  }

  /** Whether a time lies within the user's idle window: the stored distribution's `idleDays`, else 7 days. */
  #fresh(sub: string, at: number): boolean {
    const distribution = this.#entry(tmPartitionKey({ kind: 'user', sub }), this.#storedName('distribution')).stored();
    const days = (distribution?.data as { readonly session?: { readonly idleDays?: unknown } } | undefined)?.session
      ?.idleDays;
    const window = (typeof days === 'number' && days > 0 ? days : DEFAULT_IDLE_DAYS) * DAY_MS;
    return this.#now() - at <= window;
  }

  /**
   * Runs one write of a document: it counts as pending until it settles, a
   * commit posts `store.changed` and, after the first tenant write, asks the
   * browser to keep the site's storage, and a quota refusal is dropped.
   */
  async #write(
    partition: TmPartition,
    p: string,
    n: string,
    kind: 'put' | 'patch',
    work: (store: IDBObjectStore, fail: (error: unknown) => void) => () => boolean,
  ): Promise<void> {
    const key = mirrorKey(p, n);
    this.#pending.set(key, (this.#pending.get(key) ?? 0) + 1);
    try {
      const result = await this.#transact('readwrite', work);
      if (result?.value === true) {
        this.#bus.post({ type: 'store.changed', partition: p, names: [n] });
        if (partition.kind === 'tenant' && !this.#persistAsked) {
          // Spec 0025 §2.7: once, after the first tenant-partition write.
          this.#persistAsked = true;
          this.#host.persist();
        }
      }
    } catch (error) {
      if (!isQuota(error)) {
        throw error;
      }
      // The write is dropped; the value stays in this tab's mirror.
      this.#telemetry.event('tm.store.quota', { reason: kind });
    } finally {
      const count = this.#pending.get(key)! - 1;
      if (count === 0) {
        this.#pending.delete(key);
      } else {
        this.#pending.set(key, count);
      }
    }
  }

  /** Re-reads what this tab mirrors of another tab's commit. */
  #changed(message: TmStoreChangedMessage): Promise<void> {
    const { partition, names } = message;
    if (typeof partition !== 'string' || !(names === null || Array.isArray(names))) {
      return Promise.resolve();
    }
    // Names are stored names, so another build's copies match nothing here.
    // A sweep's prefix may cover partitions this tab keeps (the current
    // session, or tenant 170 under tenant 17's prefix), so the mirror reads
    // them again rather than emptying them.
    return this.#reread((entry) =>
      names === null ? entry.p.startsWith(partition) : entry.p === partition && names.includes(entry.n),
    );
  }

  /** Reads the matching mirrored documents again, in one readonly transaction; none, no transaction. */
  async #reread(matches: (entry: TmMirrorEntry) => boolean): Promise<void> {
    if (this.#mode === 'memory') {
      return;
    }
    const entries = [...this.#mirror.values()].filter(
      (entry) => matches(entry) && !this.#pending.has(mirrorKey(entry.p, entry.n)),
    );
    if (entries.length === 0) {
      return;
    }
    await this.#transact('readonly', (store) => {
      for (const { p, n } of entries) {
        const request = store.get([p, n]);
        request.onsuccess = () => this.#settle(p, n, toDoc(request.result));
      }
      return () => undefined;
    });
  }

  /**
   * Runs work in one transaction. Resolves with the work's result once the
   * transaction commits, or `null` when the store runs in memory; rejects
   * with the transaction's error, or what the work passed to `fail`.
   */
  async #transact<T>(
    mode: IDBTransactionMode,
    work: (store: IDBObjectStore, fail: (error: unknown) => void) => () => T,
    retried = false,
  ): Promise<{ readonly value: T } | null> {
    const db = await this.#database();
    if (db === null) {
      return null;
    }
    const connection = this.#connection;
    let transaction: IDBTransaction;
    try {
      transaction = db.transaction(TM_STORE_DOCS, mode, { durability: 'relaxed' });
    } catch (error) {
      // A connection that closed under us (the database was deleted or
      // upgraded elsewhere): open again, once.
      if (retried || !(error instanceof DOMException) || error.name !== 'InvalidStateError') {
        throw error;
      }
      if (this.#connection === connection) {
        this.#connection = null;
      }
      return this.#transact(mode, work, true);
    }
    return new Promise((resolve, reject) => {
      let failure: { readonly error: unknown } | null = null;
      const fail = (error: unknown): void => {
        failure = { error };
        transaction.abort();
      };
      const result = work(transaction.objectStore(TM_STORE_DOCS), fail);
      transaction.oncomplete = () => resolve({ value: result() });
      transaction.onabort = () =>
        reject(failure !== null ? failure.error : (transaction.error ?? new DOMException('The write was aborted.', 'AbortError')));
    });
  }

  /** The open connection, opening it on first use; `null` in memory mode. */
  #database(): Promise<IDBDatabase | null> {
    if (this.#mode === 'memory') {
      return Promise.resolve(null);
    }
    this.#connection ??= this.#open();
    return this.#connection;
  }

  /** Opens the database, creating the `docs` store; falls back to memory mode on any failure. */
  #open(): Promise<IDBDatabase | null> {
    const factory = this.#host.indexedDB;
    const opening = new Promise<IDBDatabase | null>((resolve) => {
      let settled = false;
      const fallback = (reason: string): void => {
        if (!settled) {
          settled = true;
          clearTimeout(timer);
          this.#mode = 'memory';
          this.#telemetry.event('tm.store.unavailable', { reason });
          resolve(null);
        }
      };
      // A database that never opens (a browser bug, a blocked upgrade) must
      // not hold the app's start: the tab runs in memory instead.
      const timer = setTimeout(() => fallback('timeout'), OPEN_TIMEOUT_MS);
      if (factory === null) {
        fallback('missing');
        return;
      }
      let request: IDBOpenDBRequest;
      try {
        request = factory.open(TM_STORE_DATABASE, TM_STORE_VERSION);
      } catch {
        fallback('failed');
        return;
      }
      // A first open creates `docs`; a later schema version recreates it,
      // since the store is only a cache.
      request.onupgradeneeded = () => {
        const db = request.result;
        if (db.objectStoreNames.contains(TM_STORE_DOCS)) {
          db.deleteObjectStore(TM_STORE_DOCS);
        }
        db.createObjectStore(TM_STORE_DOCS, { keyPath: [...TM_STORE_KEY_PATH] });
      };
      request.onerror = () => fallback(request.error?.name === 'VersionError' ? 'version' : 'failed');
      request.onsuccess = () => {
        const db = request.result;
        if (settled) {
          // Too late: this tab already runs in memory.
          db.close();
          return;
        }
        settled = true;
        clearTimeout(timer);
        // Every opener of a tm-* database yields to a delete or an upgrade of
        // it (spec 0010 §D.5); the next access opens again.
        db.onversionchange = () => {
          db.close();
          if (this.#connection === opening) {
            this.#connection = null;
          }
        };
        resolve(db);
      };
    });
    return opening;
  }

  /** Deletes the image cache; a browser without CacheStorage, or refusing it, has none. */
  async #deleteImages(): Promise<void> {
    try {
      await this.#host.caches?.delete(TM_IMAGES_CACHE);
    } catch {
      // Nothing to delete where the cache cannot be reached.
    }
  }

  /** The boot record now: the stored one, the in-memory one once storage refused, else the defaults. */
  #readBoot(): TmBootRecord {
    let record = this.#memoryBoot;
    const storage = this.#host.localStorage;
    if (record === null && storage !== null) {
      try {
        record = tmParseBootRecord(storage.getItem(TM_BOOT_RECORD_KEY));
      } catch {
        // A storage that refuses reads: the defaults, until a write keeps them in memory.
      }
    }
    if (record !== null) {
      return record;
    }
    const { lang, dir } = this.#host.documentLanguage();
    return { v: 1, theme: 'system', lang, dir, sub: null, lastTenantId: null };
  }

  /** Writes members of the boot record; in memory once local storage refuses. */
  #patchBoot(delta: Partial<Omit<TmBootRecord, 'v'>>): void {
    const record: TmBootRecord = { ...this.#readBoot(), ...delta, v: 1 };
    if (tmParseBootRecord(JSON.stringify(record)) === null) {
      throw new Error(`The boot record patch ${JSON.stringify(delta)} makes an invalid boot record.`);
    }
    if (this.#memoryBoot !== null || !tmWriteItem(this.#host.localStorage, TM_BOOT_RECORD_KEY, record)) {
      if (this.#memoryBoot === null) {
        this.#telemetry.event('tm.store.unavailable', { reason: 'local-storage' });
      }
      this.#memoryBoot = record;
    }
  }
}
