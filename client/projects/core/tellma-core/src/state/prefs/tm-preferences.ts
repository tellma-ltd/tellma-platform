// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import {
  computed,
  DestroyRef,
  effect,
  inject,
  Injectable,
  Injector,
  signal,
  untracked,
  type Signal,
} from '@angular/core';

import { TmApi } from '../../api/tm-api';
import { TmApiError } from '../../api/tm-api-error';
import { TM_API_HOST } from '../../api/tm-api-host';
import { TM_APP_DECLARATIONS } from '../../app/tm-app-declarations';
import { TM_NOW } from '../../app/tm-now';
import { TmTelemetry } from '../../observe/tm-telemetry';
import { TmSession } from '../../session/tm-session';
import type { TmPrefsChangedMessage } from '../bus/tm-bus-messages';
import { TmTabBus } from '../bus/tm-tab-bus';
import type { TmMeResult, TmPreferencesResult, TmTenantFacts } from '../launch/tm-launch-docs';
import { TmClientStore } from '../store/tm-client-store';
import type { TmTenantPartition } from '../store/tm-store-layout';
import { TmCacheRefresher } from '../sync/tm-cache-refresher';
import { TM_STATE_PARTS } from '../tm-state-dump';
import type { TmPreferenceKey, TmPreferenceKind } from './tm-preference-key';

// The user's preferences (spec 0025 §9). The server's bag, `me.preferences`,
// is an opaque map of stored strings; this service owns their JSON encoding,
// the overlay that shows a value in every tab before the bag holds it, and
// the flush policy that brings this tab's changes to the server. A value set
// here shows at once in this tab and, over `prefs.changed`, in the others;
// only the tab that set it flushes it, curated keys at once and write-behind
// keys coalesced. The store holds server truth only: a flush's result is
// adopted through the cache refresher, and the overlay entry ends once the
// bag holds the same string.

declare const ngDevMode: unknown;

/** A stored string, or `null` for a key the bag does not hold. */
type TmStoredText = string | null;

/** One key this tab set and has not yet saved. */
interface TmDirty {
  /** The newest value: set here, or received from another tab while this tab's write was in flight. */
  value: TmStoredText;
  /** The key's kind. */
  readonly kind: TmPreferenceKind;
  /** When the value last changed, in epoch milliseconds. */
  at: number;
  /** Whether the stored form exceeds 8 KB, which flushes a write-behind key at once. */
  big: boolean;
  /** Whether a write of the key is in flight. */
  sending: boolean;
  /** The earliest time of the next flush after a transient failure; 0 when none. */
  retryAt: number;
}

/** What this tab holds of one tenant's preferences. */
interface TmTenantPrefs {
  /** The tenant. */
  readonly t: number;
  /** The keys this tab set and has not yet saved. */
  readonly dirty: Map<string, TmDirty>;
  /** The timer of the next flush. */
  timer?: ReturnType<typeof setTimeout>;
  /** Whether a flush at once is queued for the end of the task. */
  queued?: boolean;
}

/** The key whose flushes are spaced 5 minutes apart. */
const NAV_RECENT = 'nav.recent';

/** The largest stored form the server keeps, in UTF-8 bytes. */
const MAX_VALUE_BYTES = 32_768;

/** The stored form above which a write-behind key flushes at once, in UTF-8 bytes. */
const EAGER_BYTES = 8_192;

/** The most the `pagehide` bodies carry together, in UTF-8 bytes. */
const PAGEHIDE_BYTES = 16_384;

/** How long a write-behind key waits after its last change. */
const SETTLE_MS = 2_000;

/** The shortest time between two write-behind flushes of the tab. */
const SPACING_MS = 10_000;

/** The shortest time between two flushes of `nav.recent`. */
const RECENT_SPACING_MS = 300_000;

/** How long a key waits after a transient failure. */
const RETRY_MS = 10_000;

/** How many parsed stored strings are kept. */
const PARSED_CAP = 512;

/** The validation code of the bag's key cap. */
const KEY_CAP = 'Users.TooManyPreferenceKeys';

/** The parse result of a stored string that is not JSON. */
const INVALID = {};

/** The size of a text in UTF-8 bytes. */
function utf8(text: string): number {
  return new TextEncoder().encode(text).length;
}

/** Whether two JSON values are deep-equal. */
function same(a: unknown, b: unknown): boolean {
  if (a === b) {
    return true;
  }
  if (typeof a !== 'object' || typeof b !== 'object' || a === null || b === null || Array.isArray(a) !== Array.isArray(b)) {
    return false;
  }
  const keys = Object.keys(a);
  return (
    keys.length === Object.keys(b).length &&
    keys.every(
      (key) => Object.hasOwn(b, key) && same((a as Record<string, unknown>)[key], (b as Record<string, unknown>)[key]),
    )
  );
}

/** The `keys` bucket of a flush's telemetry. */
function bucket(count: number): string {
  return count === 1 ? '1' : count <= 5 ? '2-5' : '6+';
}

/**
 * The user's preferences in the routed tenant: typed values that apply at
 * once, show in the user's other tabs at once, and reach the server by the
 * key's kind. Outside a tenant, `get` returns the fallback and `set` is
 * ignored. Values are shared by reference: treat them as immutable.
 *
 * @example
 * ```ts
 * const thresholdKey = tmPreferenceKey<number>('acme.creditReview.threshold', 'write-behind', 90);
 *
 * // inside the page component
 * private readonly prefs = inject(TmPreferences);
 * readonly threshold = this.prefs.get(thresholdKey);
 * setThreshold(percent: number) { this.prefs.set(thresholdKey, percent); }
 * ```
 */
@Injectable({ providedIn: 'root' })
export class TmPreferences {
  readonly #injector = inject(Injector);
  readonly #api = inject(TmApi);
  readonly #bus = inject(TmTabBus);
  readonly #store = inject(TmClientStore);
  readonly #refresher = inject(TmCacheRefresher);
  readonly #host = inject(TM_API_HOST);
  readonly #now = inject(TM_NOW);
  readonly #telemetry = inject(TmTelemetry);
  /** The app's lifetime: once it ends, its injector resolves nothing, so nothing more is flushed or armed. */
  readonly #app = inject(DestroyRef);

  /** Each tenant's overlay: the values this tab shows before the bag holds them, by key. */
  readonly #overlay = signal<Readonly<Record<number, Readonly<Record<string, TmStoredText>>>>>({});
  /** Each tenant's unsaved keys, for the tenants this tab set a key in. */
  readonly #tenants = new Map<number, TmTenantPrefs>();
  /** Moves whenever a key joins or leaves a dirty map, so the overlay's end is decided again. */
  readonly #version = signal(0);
  /** Each distinct stored string's parsed value, oldest first. */
  readonly #parsed = new Map<string, unknown>();
  /** The keys and strings already reported invalid. */
  readonly #invalid = new Set<string>();
  /** When the tab last flushed write-behind keys, in epoch milliseconds. */
  #lastFlush = -Infinity;
  /** When the tab last flushed `nav.recent`, in epoch milliseconds. */
  #lastRecent = -Infinity;

  /** Creates the service, which follows the other tabs, the page's life and the server's reach. */
  constructor() {
    inject(TM_STATE_PARTS).preferences = () => this.ɵdirty();

    // --- Another tab's choice replaces this tab's ---
    const subscription = this.#bus.messages.subscribe((message) => {
      if (message.type === 'prefs.changed') {
        this.#received(message);
      }
    });

    // --- The page: hidden flushes the write-behind keys; pagehide sends what is left ---
    const stops = [
      this.#host.listen('document', 'visibilitychange', () => {
        if (!this.#host.visible()) {
          this.#tenants.forEach((tenant) => this.#fire(tenant, true));
        }
      }),
      this.#host.listen('window', 'pagehide', () => this.#pagehide()),
    ];

    // --- The bag and the tenant's state, per tenant this tab shows or saves keys of ---
    // An overlay entry ends once the bag holds its string and this tab has
    // nothing unsaved for it; a tenant that leaves `Frozen` flushes its keys.
    effect(() => {
      this.#version();
      const overlay = this.#overlay();
      const tenants = new Set([...Object.keys(overlay).map(Number), ...this.#tenants.keys()]);
      for (const t of tenants) {
        const partition = this.#partition(t);
        if (partition !== null) {
          const bag = this.#store.doc<TmMeResult>(partition, 'me')()?.data.preferences;
          this.#store.doc<TmTenantFacts>(partition, 'tenant')();
          untracked(() => {
            this.#settle(t, bag);
            this.#schedule(t);
          });
        }
      }
    });

    // --- The server reachable again: transient failures retry at once ---
    effect(() => {
      if (this.#api.online()) {
        untracked(() =>
          this.#tenants.forEach((tenant) => {
            tenant.dirty.forEach((dirty) => (dirty.retryAt = 0));
            this.#schedule(tenant.t);
          }),
        );
      }
    });

    this.#app.onDestroy(() => {
      subscription.unsubscribe();
      stops.forEach((stop) => stop());
      this.#tenants.forEach((tenant) => clearTimeout(tenant.timer));
    });
  }

  /**
   * The preference's value in the routed tenant: this browser's last choice
   * this tab has seen, else the stored value, else the fallback. A stored
   * value that is not JSON, or fails the key's check, reads as the fallback.
   *
   * @param key - The preference.
   * @returns The value's signal, which follows the routed tenant.
   * @throws Error in development, when the key's first segment is neither a
   *   platform segment nor a composed feature's key.
   */
  get<T>(key: TmPreferenceKey<T>): Signal<T> {
    this.#check(key);
    return computed(() => {
      const partition = this.#refresher.ɵpartition();
      const text = partition === null ? null : this.#text(partition, key.key);
      return text === null ? key.fallback : this.#decode(key, text);
    });
  }

  /**
   * Sets the preference in the routed tenant: `get` returns it in the same
   * frame, the user's other tabs show it at once, and this tab saves it by
   * the key's kind. A value deep-equal to the fallback deletes the key.
   *
   * @param key - The preference.
   * @param value - The value.
   * @throws Error in development, when the stored form exceeds 32 KB, which
   *   production drops, or when the key's first segment is neither a
   *   platform segment nor a composed feature's key.
   */
  set<T>(key: TmPreferenceKey<T>, value: T): void {
    this.#check(key);
    const partition = untracked(this.#refresher.ɵpartition);
    if (partition === null) {
      return;
    }
    const text = same(value, key.fallback) ? null : (JSON.stringify(value) ?? null);
    const size = text === null ? 0 : utf8(text);
    if (size > MAX_VALUE_BYTES) {
      // Refused before it applies (spec 0025 §9.1).
      this.#telemetry.event('tm.prefs.dropped', { reason: 'too-large' });
      if (typeof ngDevMode !== 'undefined' && ngDevMode) {
        throw new Error(
          `The preference '${key.key}' stores ${size} bytes, over the ${MAX_VALUE_BYTES} the server keeps. Store less, or split the value.`,
        );
      }
      return;
    }
    const t = partition.tenantId;
    if (text === untracked(() => this.#text(partition, key.key))) {
      return;
    }
    // --- Applied in this frame, then broadcast, then scheduled ---
    this.#show(t, { [key.key]: text });
    const tenant = this.#tenant(t);
    const dirty = tenant.dirty.get(key.key);
    const at = this.#now();
    if (dirty === undefined) {
      tenant.dirty.set(key.key, { value: text, kind: key.kind, at, big: size > EAGER_BYTES, sending: false, retryAt: 0 });
      this.#version.update((version) => version + 1);
    } else {
      Object.assign(dirty, { value: text, at, big: size > EAGER_BYTES, retryAt: 0 });
    }
    this.#bus.post({ type: 'prefs.changed', tenantId: t, values: { [key.key]: text } });
    this.#schedule(t);
  }

  /**
   * Deletes the preference in the routed tenant, so `get` returns the
   * fallback; saved as `set` saves.
   *
   * @param key - The preference.
   */
  reset(key: TmPreferenceKey<unknown>): void {
    this.set(key, key.fallback);
  }

  /**
   * The keys this tab set and has not yet saved, in every tenant: the dump's
   * `preferences.dirty`. Internal.
   *
   * @returns The keys, sorted.
   */
  ɵdirty(): string[] {
    const keys = new Set<string>();
    this.#tenants.forEach((tenant) => tenant.dirty.forEach((_, key) => keys.add(key)));
    return [...keys].sort();
  }

  /** The signed-in user's partition of a tenant, or `null` with no user. */
  #partition(t: number): TmTenantPartition | null {
    const sub = this.#injector.get(TmSession).profile()?.sub;
    return sub === undefined ? null : { kind: 'tenant', sub, tenantId: t };
  }

  /** The tenant's mirrored bag; tracked. */
  #bag(partition: TmTenantPartition): Readonly<Record<string, string>> | undefined {
    return this.#store.doc<TmMeResult>(partition, 'me')()?.data.preferences;
  }

  /** A key's string as this tab shows it: the overlay's, else the bag's; tracked. */
  #text(partition: TmTenantPartition, key: string): TmStoredText {
    const overlay = this.#overlay()[partition.tenantId];
    return overlay !== undefined && Object.hasOwn(overlay, key) ? overlay[key]! : (this.#bag(partition)?.[key] ?? null);
  }

  /** A stored string's value, parsed once; an invalid one reads as the fallback and is reported once. */
  #decode<T>(key: TmPreferenceKey<T>, text: string): T {
    let value = this.#parsed.get(text);
    if (value === undefined) {
      try {
        value = JSON.parse(text) as unknown;
      } catch {
        value = INVALID;
      }
      this.#parsed.set(text, value);
      if (this.#parsed.size > PARSED_CAP) {
        this.#parsed.delete(this.#parsed.keys().next().value!);
      }
    }
    if (value !== INVALID && (key.validate?.(value) ?? true)) {
      return value as T;
    }
    const reported = `${key.key}\u0000${text}`;
    if (!this.#invalid.has(reported)) {
      this.#invalid.add(reported);
      untracked(() => this.#telemetry.event('tm.prefs.invalid', { family: key.key.split('.', 1)[0]! }));
    }
    return key.fallback;
  }

  /** Puts values in a tenant's overlay. */
  #show(t: number, values: Readonly<Record<string, TmStoredText>>): void {
    this.#overlay.update((overlay) => ({ ...overlay, [t]: { ...overlay[t], ...values } }));
  }

  /** Ends overlay entries of a tenant: the bag's string for every one when `keys` is given, else those the bag now holds. */
  #settle(t: number, bag: Readonly<Record<string, string>> | undefined, keys?: readonly string[]): void {
    const overlay = untracked(this.#overlay)[t];
    const dirty = this.#tenants.get(t)?.dirty;
    const ended = keys ?? Object.keys(overlay ?? {}).filter(
      (key) => bag !== undefined && (bag[key] ?? null) === overlay![key] && !dirty?.has(key),
    );
    if (overlay !== undefined && ended.length > 0) {
      const kept = { ...overlay };
      ended.forEach((key) => delete kept[key]);
      // A tenant whose overlay empties leaves the map.
      this.#overlay.update((overlays) => {
        const next = { ...overlays, [t]: kept };
        if (Object.keys(kept).length === 0) {
          delete next[t];
        }
        return next;
      });
    }
  }

  /** A tenant's dirty map and timers, created at the first key set or received in it. */
  #tenant(t: number): TmTenantPrefs {
    let tenant = this.#tenants.get(t);
    if (tenant === undefined) {
      tenant = { t, dirty: new Map() };
      this.#tenants.set(t, tenant);
    }
    return tenant;
  }

  /**
   * Another tab's values: each replaces the overlay entry and drops this
   * tab's unsent value. A revert only takes back the refused value where the
   * overlay shows it, and never touches this tab's own unsaved values.
   */
  #received(message: TmPrefsChangedMessage): void {
    const { tenantId: t, values } = message;
    if (typeof t !== 'number' || typeof values !== 'object' || values === null) {
      return;
    }
    const entries = Object.entries(values).filter(([, text]) => text === null || typeof text === 'string');
    const dirty = this.#tenants.get(t)?.dirty;
    if (message.revert === true) {
      const shown = untracked(this.#overlay)[t] ?? {};
      const refused = entries
        .filter(([key, text]) => dirty?.has(key) !== true && Object.hasOwn(shown, key) && shown[key] === text)
        .map(([key]) => key);
      if (refused.length > 0) {
        this.#settle(t, undefined, refused);
      }
      return;
    }
    for (const [key, text] of entries) {
      const mine = dirty?.get(key);
      if (mine?.sending) {
        // This tab's write is in flight and will store its own value: the
        // later choice stays dirty here, so it is saved again after it lands
        // and the bag ends on it whatever order the server applies them in.
        Object.assign(mine, { value: text, at: this.#now(), retryAt: 0 });
      } else if (mine !== undefined) {
        dirty!.delete(key);
        this.#version.update((version) => version + 1);
      }
    }
    if (entries.length > 0) {
      this.#show(t, Object.fromEntries(entries));
    }
  }

  /** Whether the tenant's stored state is `Frozen`, which holds every flush. */
  #frozen(t: number): boolean {
    const partition = this.#partition(t);
    return partition !== null && untracked(this.#store.doc<TmTenantFacts>(partition, 'tenant'))?.data.state === 'Frozen';
  }

  /** When a key is due to flush, by its kind; `hidden` flushes write-behind keys at once. */
  #due(key: string, dirty: TmDirty, hidden: boolean): number {
    if (dirty.kind === 'curated') {
      return dirty.retryAt;
    }
    let due = hidden || dirty.big ? 0 : Math.max(dirty.at + SETTLE_MS, this.#lastFlush + SPACING_MS);
    if (key === NAV_RECENT) {
      // Its 5 minutes hold the hidden flush too; pagehide sends it.
      due = Math.max(due, this.#lastRecent + RECENT_SPACING_MS);
    }
    return Math.max(due, dirty.retryAt);
  }

  /** Arms the tenant's next flush: at the end of the task when one is due, else at the earliest due time. */
  #schedule(t: number): void {
    const tenant = this.#tenants.get(t);
    // A write that lands after the app's end arms nothing.
    if (tenant === undefined || this.#app.destroyed) {
      return;
    }
    clearTimeout(tenant.timer);
    if (this.#frozen(t)) {
      return;
    }
    let next = Infinity;
    tenant.dirty.forEach((dirty, key) => {
      if (!dirty.sending) {
        next = Math.min(next, this.#due(key, dirty, false));
      }
    });
    const wait = next - this.#now();
    if (wait <= 0) {
      // Sets within one task coalesce into one flush.
      if (!tenant.queued) {
        tenant.queued = true;
        queueMicrotask(() => {
          tenant.queued = false;
          this.#fire(tenant, false);
        });
      }
    } else if (wait < Infinity) {
      tenant.timer = setTimeout(() => this.#fire(tenant, false), Math.min(wait, 2_000_000_000));
    }
  }

  /** Flushes the tenant's due keys, one write per kind, then arms the next flush. */
  #fire(tenant: TmTenantPrefs, hidden: boolean): void {
    // A flush the app's end overtook, such as the one queued for the end of its last task, sends nothing.
    if (this.#app.destroyed || this.#frozen(tenant.t)) {
      return;
    }
    const now = this.#now();
    const due: Record<TmPreferenceKind, string[]> = { curated: [], 'write-behind': [] };
    tenant.dirty.forEach((dirty, key) => {
      if (!dirty.sending && this.#due(key, dirty, hidden) <= now) {
        due[dirty.kind].push(key);
      }
    });
    for (const kind of ['curated', 'write-behind'] as const) {
      if (due[kind].length > 0) {
        void this.#flush(tenant, kind, due[kind]);
      }
    }
    this.#schedule(tenant.t);
  }

  /** Saves keys of one kind: their sets in one write and their deletes in another. */
  async #flush(tenant: TmTenantPrefs, kind: TmPreferenceKind, keys: readonly string[]): Promise<void> {
    const sent = new Map<string, TmStoredText>();
    for (const key of keys) {
      const dirty = tenant.dirty.get(key)!;
      dirty.sending = true;
      sent.set(key, dirty.value);
    }
    if (kind === 'write-behind') {
      this.#lastFlush = this.#now();
      if (sent.has(NAV_RECENT)) {
        this.#lastRecent = this.#lastFlush;
      }
    }
    const sets = keys.filter((key) => sent.get(key) !== null);
    const deletes = keys.filter((key) => sent.get(key) === null);
    if (sets.length > 0) {
      const items = sets.map((key) => ({ key, value: sent.get(key)! }));
      await this.#write(tenant, kind, 'users/me/preferences/set', { items }, sets, sent);
    }
    if (deletes.length > 0) {
      await this.#write(tenant, kind, 'users/me/preferences/delete', { keys: deletes }, deletes, sent);
    }
  }

  /** One write of a flush: its result adopted, or its failure handled by kind; silent either way. */
  async #write(
    tenant: TmTenantPrefs,
    kind: TmPreferenceKind,
    route: string,
    body: object,
    keys: readonly string[],
    sent: ReadonlyMap<string, TmStoredText>,
  ): Promise<void> {
    const started = performance.now();
    try {
      const result = await this.#api.write<TmPreferencesResult>(route, body, { tenantId: tenant.t });
      // --- Adopted (spec 0025 §9.5): a key whose value is still the one sent is saved ---
      this.#refresher.adopt(tenant.t, { preferences: result });
      for (const key of keys) {
        const dirty = tenant.dirty.get(key);
        if (dirty !== undefined) {
          dirty.sending = false;
          if (dirty.value === sent.get(key)) {
            tenant.dirty.delete(key);
          }
        }
      }
      this.#version.update((version) => version + 1);
      this.#telemetry.timing('tm.prefs.flush', performance.now() - started, { kind, keys: bucket(keys.length) });
    } catch (error) {
      this.#failed(tenant, keys, sent, error);
    }
    this.#schedule(tenant.t);
  }

  /** Handles a failed write of the values sent by its kind. */
  #failed(tenant: TmTenantPrefs, keys: readonly string[], sent: ReadonlyMap<string, TmStoredText>, error: unknown): void {
    // Spec 0025 §9.4's outcomes, each counted in telemetry and none shown.
    const kind = error instanceof TmApiError ? error.kind : 'server';
    if (!(error instanceof TmApiError)) {
      this.#telemetry.error(error, { where: 'tm-preferences' });
    }
    this.#telemetry.event('tm.prefs.flush-failed', { kind });
    // Kept dirty for a later flush; a transient failure waits a while first.
    const keep = (kept: readonly string[], retryAt: number): void => {
      for (const key of kept) {
        const dirty = tenant.dirty.get(key);
        if (dirty !== undefined) {
          Object.assign(dirty, { sending: false, retryAt });
        }
      }
    };
    if (kind === 'offline' || kind === 'unavailable' || kind === 'server' || kind === 'cancelled') {
      keep(keys, this.#now() + RETRY_MS);
    } else if (kind === 'unauthenticated' || kind === 'step-up' || kind === 'client-outdated') {
      // The HTTP layer takes their paths; the keys wait for the next set,
      // the server's return, or the page's end.
      keep(keys, Infinity);
    } else if (kind === 'read-only') {
      this.#drop(tenant, keys, sent, 'read-only');
    } else if (kind === 'validation' && error instanceof TmApiError) {
      // The key cap drops the whole write; any other code drops the keys it names.
      const errors = (error.problem?.['errors'] ?? []) as readonly { readonly code?: unknown; readonly path?: unknown }[];
      if (errors.some((item) => item.code === KEY_CAP)) {
        this.#drop(tenant, keys, sent, 'key-cap');
        return;
      }
      const named = new Set<string>();
      for (const item of errors) {
        const index = /^(?:items|keys)\[(\d+)\]/.exec(typeof item.path === 'string' ? item.path : '')?.[1];
        const key = index === undefined ? undefined : keys[Number(index)];
        if (key !== undefined) {
          named.add(key);
        }
      }
      // A refusal that names no key of the write drops all of them; the
      // keys it does not name are saved again at once.
      this.#drop(tenant, named.size > 0 ? [...named] : keys, sent, 'rejected');
      keep(
        keys.filter((key) => named.size > 0 && !named.has(key)),
        0,
      );
    } else {
      this.#drop(tenant, keys, sent, 'rejected');
    }
  }

  /**
   * Drops the keys of a refused write: they leave the dirty map and the
   * overlay, and every tab showing the refused values returns to its stored
   * strings. A key whose value changed after it was sent holds a later
   * choice, which is saved again instead.
   */
  #drop(tenant: TmTenantPrefs, keys: readonly string[], sent: ReadonlyMap<string, TmStoredText>, reason: string): void {
    const refused: Record<string, TmStoredText> = {};
    for (const key of keys) {
      const dirty = tenant.dirty.get(key);
      if (dirty !== undefined && dirty.value !== sent.get(key)) {
        // A set here, or another tab's choice, arrived while the write was in flight.
        Object.assign(dirty, { sending: false, retryAt: 0 });
      } else {
        tenant.dirty.delete(key);
        refused[key] = sent.get(key) ?? null;
      }
    }
    this.#version.update((version) => version + 1);
    const dropped = Object.keys(refused);
    if (dropped.length > 0) {
      this.#telemetry.event('tm.prefs.dropped', { reason });
      this.#settle(tenant.t, undefined, dropped);
      // A revert, never a later choice: a tab with its own write of the key in flight keeps it.
      this.#bus.post({ type: 'prefs.changed', tenantId: tenant.t, values: refused, revert: true });
    }
  }

  /**
   * Sends every unsaved key, in flight ones included since the unload
   * aborts their writes, newest first and within 16 KB for both bodies
   * together; nothing of a `Frozen` tenant.
   */
  #pagehide(): void {
    const pending: { readonly t: number; readonly key: string; readonly value: TmStoredText; readonly at: number }[] = [];
    this.#tenants.forEach((tenant) => {
      if (!this.#frozen(tenant.t)) {
        tenant.dirty.forEach((dirty, key) => pending.push({ t: tenant.t, key, value: dirty.value, at: dirty.at }));
      }
    });
    pending.sort((a, b) => b.at - a.at);
    // Each tenant's two bodies, and the bytes left: a body's first item
    // costs its wrapper, each later one its comma.
    const bodies = new Map<number, { readonly items: { key: string; value: string }[]; readonly keys: string[] }>();
    let left = PAGEHIDE_BYTES;
    let dropped = false;
    for (const { t, key, value } of pending) {
      const body = bodies.get(t) ?? { items: [], keys: [] };
      const list: unknown[] = value === null ? body.keys : body.items;
      const item = value === null ? key : { key, value };
      const cost = utf8(JSON.stringify(item)) + (list.length > 0 ? 1 : value === null ? 11 : 12);
      if (cost > left) {
        dropped = true;
        continue;
      }
      left -= cost;
      list.push(item);
      bodies.set(t, body);
    }
    if (dropped) {
      this.#telemetry.event('tm.prefs.dropped', { reason: 'pagehide-cap' });
    }
    bodies.forEach(({ items, keys }, tenantId) => {
      if (items.length > 0) {
        this.#api.send('users/me/preferences/set', { items }, { tenantId });
      }
      if (keys.length > 0) {
        this.#api.send('users/me/preferences/delete', { keys }, { tenantId });
      }
    });
  }

  /** In development, refuses a key whose first segment is neither a platform segment nor a composed feature's key. */
  #check(key: TmPreferenceKey<unknown>): void {
    if (typeof ngDevMode !== 'undefined' && ngDevMode) {
      // Spec 0025 Appendix A's platform segments; the features are known
      // only once provideTellma composed them.
      const segment = key.key.split('.', 1)[0]!;
      const features = this.#injector.get(TM_APP_DECLARATIONS, null)?.features;
      const platform = ['nav', 'search', 'report', 'dashboard', 'home', 'announcements', 'notifications', 'tm'];
      if (!platform.includes(segment) && features !== undefined && !features.includes(segment)) {
        throw new Error(
          `The preference key '${key.key}' starts with '${segment}', which is neither a platform segment (${platform.join(', ')}) nor a composed feature's key. Start it with your feature's key.`,
        );
      }
    }
  }
}
