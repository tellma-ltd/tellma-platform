// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

// Reading and writing the small JSON items the boot script and the session
// keep in web storage. A browser may refuse storage outright or throw on any
// call (a full quota, a sandboxed frame, blocked site data), so every access
// here swallows the failure and reports it as an absent item. Plain code with
// no Angular import: the boot script bundles it too, and its size counts.

/** The few members of `Storage` the boot script and the session use; any of them may throw. */
export interface TmStorageLike {
  /** Reads one item. */
  getItem(key: string): string | null;
  /** Writes one item. */
  setItem(key: string, value: string): void;
  /** Removes one item. */
  removeItem(key: string): void;
}

/**
 * Whether a value is a non-empty string.
 *
 * @param value - The value.
 * @returns Whether it is a non-empty string.
 */
export function tmIsText(value: unknown): value is string {
  return typeof value === 'string' && value !== '';
}

/**
 * Parses JSON text whose value is an object, or `null` when the text is
 * absent, is not JSON, or holds no object.
 *
 * @param text - The text, or `null`.
 * @returns The object's members, or `null`.
 */
export function tmParseJsonObject(text: string | null): Readonly<Record<string, unknown>> | null {
  try {
    const value: unknown = JSON.parse(text || 'null');
    return typeof value === 'object' ? (value as Readonly<Record<string, unknown>> | null) : null;
  } catch {
    return null;
  }
}

/**
 * Reads one item, or `null` when it is absent or the storage refuses.
 *
 * @param storage - The storage, or `null` when the browser refuses it.
 * @param key - The item's key.
 * @returns The item's text, or `null`.
 */
export function tmReadItem(storage: TmStorageLike | null, key: string): string | null {
  try {
    return storage ? storage.getItem(key) : null;
  } catch {
    return null;
  }
}

/**
 * Writes one item, or removes it for `null`.
 *
 * @param storage - The storage, or `null` when the browser refuses it.
 * @param key - The item's key.
 * @param value - The value to write as JSON, or `null` to remove the item.
 * @returns Whether the storage took the change.
 */
export function tmWriteItem(storage: TmStorageLike | null, key: string, value: object | null): boolean {
  try {
    if (storage) {
      if (value === null) {
        storage.removeItem(key);
      } else {
        storage.setItem(key, JSON.stringify(value));
      }
      return true;
    }
  } catch {
    // A full or refusing storage.
  }
  return false;
}
