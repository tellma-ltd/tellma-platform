// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import { computed, DestroyRef, inject, Injectable, Injector, signal, untracked, type Signal } from '@angular/core';
import {
  TM_NOW,
  type TmId,
  type TmJson,
  TmSession,
  TmTelemetry,
  ɵTM_API_HOST,
  ɵTM_DRAFT_KEY_PREFIX,
  ɵTM_DRAFTS_STORAGE,
  ɵTM_STATE_PARTS,
  ɵtmDraftPrefix,
  ɵtmRemoveDrafts,
  ɵtmStorageKeys,
} from '@tellma/core';

// Drafts (spec 0025 §11): a tab's unsaved form copies in session storage, so
// a reload or a re-authentication returns the user to edit mode with nothing
// lost, while a new tab starts clean. Saves are debounced and flushed
// synchronously when the page hides; a full storage gives up this tab's
// oldest other drafts before the draft lives in memory alone.

/** A draft as stored: its format, when it was written, and the form's copy. */
interface TmStoredDraft {
  /** The stored format. */
  readonly v: 1;
  /** When it was written, in epoch milliseconds. */
  readonly at: number;
  /** The form's copy. */
  readonly data: TmJson;
}

/** How long a save waits for the next one. */
const DEBOUNCE_MS = 300;

/** A stored draft's value, or `undefined` when the text is absent or not a draft. */
function parse(text: string | null): TmStoredDraft | undefined {
  try {
    const draft = JSON.parse(text ?? 'null') as Partial<TmStoredDraft> | null;
    return draft?.v === 1 && typeof draft.at === 'number' && 'data' in draft ? (draft as TmStoredDraft) : undefined;
  } catch {
    return undefined;
  }
}

/** Whether an error is the browser's quota refusal. */
function isQuota(error: unknown): boolean {
  return error instanceof DOMException && error.name === 'QuotaExceededError';
}

/**
 * Keeps a tab's unsaved form copies in its session storage: a reload, Back
 * from another site or "reopen closed tab" keeps them; a new tab does not.
 * A draft belongs to the signed-in user, and lasts until it is cleared.
 *
 * @example
 * ```ts
 * const drafts = inject(TmDrafts);
 * const key = drafts.key(17, 'gl.Center', 5);
 * const restored = drafts.load(key);
 * drafts.save(key, form.value());   // on every change
 * drafts.clear(key);                // after the save, a cancel or a discard
 * ```
 */
@Injectable({ providedIn: 'root' })
export class TmDrafts {
  readonly #injector = inject(Injector);
  readonly #storage = inject(ɵTM_DRAFTS_STORAGE);
  readonly #now = inject(TM_NOW);
  readonly #telemetry = inject(TmTelemetry);

  /** The saves waiting for the debounce, by key. */
  readonly #pending = new Map<string, TmJson>();
  /** The drafts the storage could not take, by key, which live in this tab's memory alone. */
  readonly #memory = new Map<string, TmStoredDraft>();
  /** Every draft key this tab holds, stored, pending or in memory. */
  readonly #keys = signal<ReadonlySet<string>>(new Set(ɵtmStorageKeys(this.#storage, ɵTM_DRAFT_KEY_PREFIX)));
  /** The debounce's timer, while saves wait for it. */
  #timer: ReturnType<typeof setTimeout> | undefined;

  /** The number of this tab's drafts of the signed-in user. */
  readonly dirtyCount: Signal<number> = computed(() => {
    const prefix = this.#prefix();
    return prefix === null ? 0 : [...this.#keys()].filter((key) => key.startsWith(prefix)).length;
  });

  /** Creates the service, which flushes the saves when the page hides. */
  constructor() {
    inject(ɵTM_STATE_PARTS).drafts = () => this.ɵkeys();
    const host = inject(ɵTM_API_HOST);
    const stops = [
      host.listen('window', 'pagehide', () => this.flush()),
      host.listen('document', 'visibilitychange', () => {
        if (!host.visible()) {
          this.flush();
        }
      }),
    ];
    inject(DestroyRef).onDestroy(() => {
      stops.forEach((stop) => stop());
      clearTimeout(this.#timer);
    });
  }

  /**
   * The key of a record's draft for the signed-in user.
   *
   * @param tenantId - The tenant.
   * @param entity - The entity, such as `gl.Center`.
   * @param id - The record's id, or `'new'` for a record not yet saved.
   * @returns The key, such as `tm.draft:<sub>:17:gl.Center:5`.
   */
  key(tenantId: number, entity: string, id: TmId | 'new'): string {
    return `${ɵtmDraftPrefix(this.#sub() ?? '')}${tenantId}:${entity}:${id}`;
  }

  /**
   * Saves a draft: written after 300 ms without another save, and at once
   * when the page hides. A key of another user saves nothing.
   *
   * @param key - The draft's key.
   * @param value - The form's copy, as plain JSON.
   */
  save(key: string, value: TmJson): void {
    if (!this.#mine(key)) {
      return;
    }
    this.#pending.set(key, value);
    this.#hold(key);
    clearTimeout(this.#timer);
    this.#timer = setTimeout(() => this.flush(), DEBOUNCE_MS);
  }

  /**
   * The draft saved under a key.
   *
   * @param key - The draft's key.
   * @returns The form's copy, or `undefined` when there is none, or the key is another user's.
   */
  load(key: string): TmJson | undefined {
    if (!this.#mine(key)) {
      return undefined;
    }
    if (this.#pending.has(key)) {
      return this.#pending.get(key);
    }
    return (this.#memory.get(key) ?? this.#read(key))?.data;
  }

  /**
   * Deletes a draft, as a save, a cancel or a discard does.
   *
   * @param key - The draft's key.
   */
  clear(key: string): void {
    this.#pending.delete(key);
    this.#memory.delete(key);
    try {
      this.#storage?.removeItem(key);
    } catch {
      // A storage that refuses holds nothing to delete.
    }
    this.#drop((held) => held === key);
  }

  /**
   * Deletes every draft of a user, as signing out or a changed user does.
   *
   * @param sub - The user's subject.
   */
  clearAll(sub: string): void {
    const prefix = ɵtmDraftPrefix(sub);
    for (const key of [...this.#pending.keys(), ...this.#memory.keys()]) {
      if (key.startsWith(prefix)) {
        this.#pending.delete(key);
        this.#memory.delete(key);
      }
    }
    ɵtmRemoveDrafts(this.#storage, sub);
    this.#drop((key) => key.startsWith(prefix));
  }

  /** Writes every pending save now. Internal: the page's hiding, and the debounce. */
  flush(): void {
    clearTimeout(this.#timer);
    const pending = [...this.#pending];
    this.#pending.clear();
    for (const [key, data] of pending) {
      this.#write(key, { v: 1, at: this.#now(), data });
    }
  }

  /**
   * The keys of this tab's drafts, without the user part: the dump's
   * `drafts`. Internal.
   *
   * @returns The keys, such as `17:gl.Center:5`.
   */
  ɵkeys(): string[] {
    return [...this.#keys()].map((key) => key.slice(key.indexOf(':', ɵTM_DRAFT_KEY_PREFIX.length) + 1));
  }

  /** The signed-in user's subject, or `null`. */
  #sub(): string | null {
    return this.#injector.get(TmSession).profile()?.sub ?? null;
  }

  /** The signed-in user's draft prefix, or `null` with no user. */
  #prefix(): string | null {
    const sub = this.#sub();
    return sub === null ? null : ɵtmDraftPrefix(sub);
  }

  /** Whether a key is a draft of the signed-in user. */
  #mine(key: string): boolean {
    const prefix = this.#prefix();
    return prefix !== null && key.startsWith(prefix);
  }

  /** A stored draft, or `undefined`. */
  #read(key: string): TmStoredDraft | undefined {
    try {
      return parse(this.#storage?.getItem(key) ?? null);
    } catch {
      return undefined;
    }
  }

  /**
   * Stores a draft. A full storage gives up this tab's oldest other drafts
   * to this tab's memory, enough to fit, and retries once; failing that,
   * the draft lives in memory alone, as it does when the storage refuses.
   */
  #write(key: string, draft: TmStoredDraft): void {
    const text = JSON.stringify(draft);
    let outcome = this.#put(key, text);
    if (outcome === 'quota') {
      try {
        this.#evict(key, text.length);
      } catch {
        // A storage that refuses its reads holds nothing more to give up.
      }
      outcome = this.#put(key, text);
      if (outcome !== 'stored') {
        this.#telemetry.event('tm.drafts.quota-exceeded');
      }
    }
    if (outcome !== 'stored') {
      this.#memory.set(key, draft);
    }
  }

  /** Writes one item: stored, refused for its quota, or refused otherwise. */
  #put(key: string, text: string): 'stored' | 'quota' | 'failed' {
    try {
      if (this.#storage === null) {
        return 'failed';
      }
      this.#storage.setItem(key, text);
      this.#memory.delete(key);
      return 'stored';
    } catch (error) {
      return isQuota(error) ? 'quota' : 'failed';
    }
  }

  /**
   * Moves this tab's other drafts to its memory, oldest first, until the
   * space they free covers a draft of the given length.
   */
  #evict(key: string, length: number): void {
    const storage = this.#storage!;
    const others = ɵtmStorageKeys(storage, ɵTM_DRAFT_KEY_PREFIX)
      .filter((other) => other !== key)
      .map((other) => {
        const text = storage.getItem(other) ?? '';
        return { other, text, draft: parse(text) };
      })
      .sort((a, b) => (a.draft?.at ?? 0) - (b.draft?.at ?? 0));
    let freed = 0;
    for (const { other, text, draft } of others) {
      if (freed >= length) {
        break;
      }
      if (draft !== undefined) {
        this.#memory.set(other, draft);
      }
      storage.removeItem(other);
      freed += other.length + text.length;
    }
  }

  /** Adds a key to this tab's drafts. */
  #hold(key: string): void {
    if (!untracked(this.#keys).has(key)) {
      this.#keys.update((keys) => new Set(keys).add(key));
    }
  }

  /** Removes the matching keys from this tab's drafts. */
  #drop(matches: (key: string) => boolean): void {
    this.#keys.update((keys) => new Set([...keys].filter((key) => !matches(key))));
  }
}
