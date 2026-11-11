// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import type { TmSessionStorage } from '../session/tm-session-host';

// Web storage stand-ins for the specs: one that keeps items in memory, and
// one that refuses every call, as a browser with blocked site data does.

/** An in-memory `Storage`. */
export class TmFakeStorage implements TmSessionStorage {
  readonly #items = new Map<string, string>();

  /**
   * Creates the storage with its first items.
   *
   * @param items - The items, by key.
   */
  constructor(items: Readonly<Record<string, string>> = {}) {
    for (const [key, value] of Object.entries(items)) {
      this.#items.set(key, value);
    }
  }

  /** The number of items. */
  get length(): number {
    return this.#items.size;
  }

  /** The key at an index, or `null` past the end. */
  key(index: number): string | null {
    return [...this.#items.keys()][index] ?? null;
  }

  /** Reads one item. */
  getItem(key: string): string | null {
    return this.#items.get(key) ?? null;
  }

  /** Writes one item. */
  setItem(key: string, value: string): void {
    this.#items.set(key, value);
  }

  /** Removes one item. */
  removeItem(key: string): void {
    this.#items.delete(key);
  }

  /** Reads one item as JSON, or `null` when it is absent. */
  json(key: string): unknown {
    const text = this.#items.get(key);
    return text === undefined ? null : (JSON.parse(text) as unknown);
  }
}

/** A `Storage` whose every call throws, as a browser with blocked site data does. */
export const TM_REFUSING_STORAGE: TmSessionStorage = {
  get length(): number {
    throw new DOMException('The operation is insecure.', 'SecurityError');
  },
  key(): string | null {
    throw new DOMException('The operation is insecure.', 'SecurityError');
  },
  getItem(): string | null {
    throw new DOMException('The operation is insecure.', 'SecurityError');
  },
  setItem(): void {
    throw new DOMException('The quota has been exceeded.', 'QuotaExceededError');
  },
  removeItem(): void {
    throw new DOMException('The operation is insecure.', 'SecurityError');
  },
};
