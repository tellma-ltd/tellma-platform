// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import type { TmPin, TmPinFolder, TmPinItem, TmPinsDoc } from '@tellma/core';

// The pinned bar's rules over the pins model (spec 0027 §8.2, §8.3): which
// stored items the bar shows, what a pin of the routed page captures, the
// URL a pin opens, the current pin and a chip's label, new ids, and the
// edits that make each next document. An item that fails a rule (its shape,
// id, lengths, the caps or the one-level rule) leaves the view but stays
// stored until the next write.

/** The pins model's caps: pins and folders per user and tenant, and the members' lengths in characters. */
export const TM_PIN_LIMITS = Object.freeze({ pins: 30, folders: 10, url: 512, title: 80, label: 40, folder: 40 } as const);

/** A pin's or folder's id: 8 lower-case letters and digits. */
const ID = /^[0-9a-z]{8}$/;

/** The base32 alphabet new ids draw from: digits and lower-case letters without i, l, o and u. */
const ALPHABET = '0123456789abcdefghjkmnpqrstvwxyz';

/** Whether a value is a non-null object. */
function isObject(value: unknown): value is Readonly<Record<string, unknown>> {
  return typeof value === 'object' && value !== null;
}

/** A string's length in characters (code points), as the caps count them. */
function length(text: string): number {
  return [...text].length;
}

/** Whether a value is a name of 1 to `max` characters, ignoring the spaces around it. */
function isName(value: unknown, max: number): value is string {
  return typeof value === 'string' && value.trim() !== '' && length(value) <= max;
}

/** Whether an item is a pin by its shape alone: an id, a URL within the cap without a leading slash, a title and an optional label. */
function isPin(item: unknown): item is TmPin {
  return (
    isObject(item) &&
    !('folder' in item) &&
    typeof item['id'] === 'string' &&
    ID.test(item['id']) &&
    typeof item['url'] === 'string' &&
    item['url'].length <= TM_PIN_LIMITS.url &&
    !item['url'].startsWith('/') &&
    typeof item['title'] === 'string' &&
    length(item['title']) <= TM_PIN_LIMITS.title &&
    (item['label'] === undefined || isName(item['label'], TM_PIN_LIMITS.label))
  );
}

/**
 * The items of a pins document the bar shows, in stored order: every item
 * that keeps the rules. A pin needs an 8-character id, a URL of at most 512
 * characters without a leading slash, a title of at most 80 and, if any, a
 * label of 1 to 40; a folder needs an id, a name of 1 to 40 and a list of
 * such pins, never a folder. An id already taken, a pin past the 30th and a
 * folder past the 10th drop out too, each with what it holds.
 *
 * @param doc - The stored document, as `tmPinsKey` reads it.
 * @returns The document of the valid items: `doc` itself when every item is.
 */
export function tmPinsView(doc: TmPinsDoc): TmPinsDoc {
  const ids = new Set<string>();
  let pins = 0;
  let folders = 0;
  let dropped = false;
  // Takes a pin that keeps the rules, counting it against the cap; drops any other.
  const keep = (item: unknown): item is TmPin => {
    const kept = isPin(item) && !ids.has(item.id) && pins < TM_PIN_LIMITS.pins;
    if (kept) {
      ids.add(item.id);
      pins++;
    }
    dropped ||= !kept;
    return kept;
  };
  const items: TmPinItem[] = [];
  for (const item of doc.items as readonly unknown[]) {
    if (isObject(item) && 'folder' in item) {
      const folder = item as Readonly<Record<string, unknown>>;
      const id = folder['id'];
      if (typeof id !== 'string' || !ID.test(id) || ids.has(id) || folders >= TM_PIN_LIMITS.folders || !isName(folder['folder'], TM_PIN_LIMITS.folder) || !Array.isArray(folder['items'])) {
        dropped = true;
        continue;
      }
      ids.add(id);
      folders++;
      const inner = (folder['items'] as readonly unknown[]).filter(keep);
      items.push(inner.length === (folder['items'] as readonly unknown[]).length ? (item as unknown as TmPinFolder) : { id, folder: folder['folder'], items: inner });
    } else if (keep(item)) {
      items.push(item);
    }
  }
  return dropped ? { v: 1, items } : doc;
}

/**
 * Creates a pin or folder id that no item holds yet: 8 random base32
 * characters. The pinned bar creates pins and folders with it.
 *
 * @param taken - The ids the document holds.
 * @returns The new id.
 */
export function tmNewPinId(taken: ReadonlySet<string>): string {
  let id: string;
  do {
    id = Array.from(crypto.getRandomValues(new Uint8Array(8)), (byte) => ALPHABET[byte & 31]).join('');
  } while (taken.has(id));
  return id;
}

/**
 * What a pin of a routed URL stores: the path after the
 * tenant, the query without `page`, and the `#d=` fragment, which holds a
 * search page's filter or a drill-through list's payload. Filters, columns,
 * order, `view`, `p.*` and `child` stay. The 512-character cap is the
 * caller's to apply, since it disables the pin control with its own reason.
 * The pinned bar captures the routed page through it.
 *
 * @param url - The routed URL, such as `/17/customers?f.CustomerType=in:Corporate&page=3`.
 * @returns The tenant-relative URL, such as `customers?f.CustomerType=in:Corporate`,
 * or `null` when the URL is not under a tenant.
 *
 * @example
 * ```ts
 * tmPinUrl('/17/list/sales-invoice-lines?page=2#d=eyJm'); // 'list/sales-invoice-lines#d=eyJm'
 * ```
 */
export function tmPinUrl(url: string): string | null {
  const match = /^\/[1-9]\d*(\/[^?#]*)?(\?[^#]*)?(#.*)?$/.exec(url);
  if (match === null) {
    return null;
  }
  const [, path = '', query = '', fragment = ''] = match;
  const kept = query
    .slice(1)
    .split('&')
    .filter((part) => part !== '' && part.split('=', 1)[0] !== 'page')
    .join('&');
  return `${path.slice(1)}${kept === '' ? '' : `?${kept}`}${fragment.startsWith('#d=') ? fragment : ''}`;
}

/**
 * A pin's URL in a tenant, such as `/17/customers?…`; the home page's pin,
 * whose URL starts with no path, is the tenant's root.
 *
 * @param tenantId - The tenant.
 * @param url - The pin's tenant-relative URL.
 * @returns The URL.
 */
export function tmPinHref(tenantId: number, url: string): string {
  return url === '' || url.startsWith('?') || url.startsWith('#') ? `/${tenantId}${url}` : `/${tenantId}/${url}`;
}

/**
 * The routed page's pin: the first pin whose URL equals
 * the routed URL's capture, its path, query and `#d=` without `page`;
 * failing that, the first pin of the same path with no query or fragment.
 * Pins in folders count.
 *
 * @param items - The pins document's items, as the view keeps them.
 * @param routed - The routed URL's capture, from {@link tmPinUrl}.
 * @returns The pin, or `null`.
 */
export function tmCurrentPin(items: readonly TmPinItem[], routed: string | null): TmPin | null {
  if (routed === null) {
    return null;
  }
  const pins = items.flatMap((item) => ('folder' in item ? item.items : [item]));
  const path = routed.split(/[?#]/, 1)[0];
  return pins.find((pin) => pin.url === routed) ?? pins.find((pin) => pin.url === path) ?? null;
}

/**
 * A pin chip's label: the user's own label; without one, a
 * URL without a query shows the label of the launcher entry at that URL, in
 * the active language; otherwise the title stored when it was pinned.
 * The pinned bar labels its chips through it.
 *
 * @param pin - The pin.
 * @param entryLabel - The label of the visible launcher entry at a URL, such as `TmNavigation.byHref`'s.
 * @param tenantId - The routed tenant.
 * @returns The label.
 */
export function tmPinLabel(pin: TmPin, entryLabel: (href: string) => string | undefined, tenantId: number): string {
  return pin.label ?? (pin.url.includes('?') ? undefined : entryLabel(tmPinHref(tenantId, pin.url))) ?? pin.title;
}

// --- Edits: each takes a document of valid items (the view) and returns the next one ---
// Every write starts from the view, so an item that fails a rule leaves the
// stored value at the next write, as the model promises.

/** Why a folder name is refused: blank, past 40 characters, or the name of another folder ignoring case. */
export type TmPinFolderNameProblem = 'blank' | 'long' | 'duplicate';

/**
 * The pins of a document's items, those in folders included, in the bar's order.
 *
 * @param items - The items.
 * @returns The pins.
 */
export function tmPinsOf(items: readonly TmPinItem[]): readonly TmPin[] {
  return items.flatMap((item) => ('folder' in item ? item.items : [item]));
}

/**
 * Judges a folder name: trimmed, it must be 1 to 40 characters and unique
 * among the folders ignoring case.
 *
 * @param items - The top-level items: a document's, or the bar's views of them.
 * @param name - The name, as typed.
 * @param except - The id of the folder being renamed, whose own name does not count.
 * @returns The problem, or `null` for a name that may be used.
 */
export function tmPinFolderNameProblem(
  items: readonly { readonly id: string; readonly folder?: string }[],
  name: string,
  except: string | null = null,
): TmPinFolderNameProblem | null {
  const trimmed = name.trim();
  if (trimmed === '') {
    return 'blank';
  }
  if (length(trimmed) > TM_PIN_LIMITS.folder) {
    return 'long';
  }
  const folded = trimmed.toLocaleLowerCase();
  return items.some((item) => item.folder !== undefined && item.id !== except && item.folder.trim().toLocaleLowerCase() === folded) ? 'duplicate' : null;
}

/** The error a refused folder name throws. */
function nameError(problem: TmPinFolderNameProblem): RangeError {
  return new RangeError(problem === 'duplicate' ? 'Another folder has this name.' : `A folder's name is 1 to ${TM_PIN_LIMITS.folder} characters.`);
}

/** The document of a list of items. */
function docOf(items: readonly TmPinItem[]): TmPinsDoc {
  return { v: 1, items };
}

/** A copy of a list with `count` items at `start` replaced by `insert`. */
function spliced<T>(list: readonly T[], start: number, count: number, ...insert: T[]): readonly T[] {
  const next = [...list];
  next.splice(start, count, ...insert);
  return next;
}

/**
 * Where a pin sits: the index of its folder among the items, `-1` at the
 * top level, and its index in that list; `null` when no pin has the id.
 */
function locate(items: readonly TmPinItem[], id: string): { readonly folder: number; readonly index: number } | null {
  for (const [at, item] of items.entries()) {
    if (!('folder' in item)) {
      if (item.id === id) {
        return { folder: -1, index: at };
      }
      continue;
    }
    const index = item.items.findIndex((pin) => pin.id === id);
    if (index !== -1) {
      return { folder: at, index };
    }
  }
  return null;
}

/** The list at a folder's index: the top level for `-1`, else the folder's pins. */
function listAt(items: readonly TmPinItem[], folder: number): readonly TmPinItem[] {
  return folder === -1 ? items : (items[folder] as TmPinFolder).items;
}

/** The items with the list at a folder's index (the top level for `-1`) replaced. */
function withList(items: readonly TmPinItem[], folder: number, list: readonly TmPinItem[]): readonly TmPinItem[] {
  return folder === -1 ? list : spliced(items, folder, 1, { ...(items[folder] as TmPinFolder), items: list as readonly TmPin[] });
}

/** The index of a folder among the items, `-1` for `null` (the top level). */
function folderIndex(items: readonly TmPinItem[], folderId: string | null): number {
  if (folderId === null) {
    return -1;
  }
  const at = items.findIndex((item) => item.id === folderId && 'folder' in item);
  if (at === -1) {
    throw new RangeError(`No folder has the id '${folderId}'.`);
  }
  return at;
}

/**
 * Appends a pin at the top level.
 *
 * @param doc - The current document's view.
 * @param pin - The new pin.
 * @returns The next document.
 * @throws RangeError when the user already has 30 pins.
 */
export function tmPinsAppend(doc: TmPinsDoc, pin: TmPin): TmPinsDoc {
  if (tmPinsOf(doc.items).length >= TM_PIN_LIMITS.pins) {
    throw new RangeError(`A user pins at most ${TM_PIN_LIMITS.pins} pages.`);
  }
  return docOf([...doc.items, pin]);
}

/**
 * Changes a pin's label or folder. A label is trimmed, and a blank or
 * `null` one removes the user's own name; another folder takes the pin at
 * its end, and `null` at the end of the top level. A pin no item has, or a
 * change to what the pin already holds, returns the document itself.
 *
 * @param doc - The current document's view.
 * @param id - The pin's id.
 * @param patch - The label (`undefined` keeps it) and the folder (`undefined` keeps it).
 * @returns The next document.
 * @throws RangeError for a label past 40 characters or a folder no item has.
 */
export function tmPinsUpdate(doc: TmPinsDoc, id: string, patch: { readonly label?: string | null; readonly folderId?: string | null }): TmPinsDoc {
  const at = locate(doc.items, id);
  if (at === null) {
    return doc;
  }
  const pin = listAt(doc.items, at.folder)[at.index] as TmPin;
  let next = pin;
  if (patch.label !== undefined) {
    const label = (patch.label ?? '').trim();
    if (length(label) > TM_PIN_LIMITS.label) {
      throw new RangeError(`A pin's label is at most ${TM_PIN_LIMITS.label} characters.`);
    }
    const bare: TmPin = { id: pin.id, url: pin.url, title: pin.title };
    next = label === '' ? bare : { ...bare, label };
  }
  const target = patch.folderId === undefined ? at.folder : folderIndex(doc.items, patch.folderId);
  if (next.label === pin.label && target === at.folder) {
    return doc;
  }
  if (target === at.folder) {
    return docOf(withList(doc.items, at.folder, spliced(listAt(doc.items, at.folder), at.index, 1, next)));
  }
  // Another list: out of this one, onto the end of the other. Taking a pin
  // out of the top level may shift the folders, so the target is found again.
  const removed = withList(doc.items, at.folder, spliced(listAt(doc.items, at.folder), at.index, 1));
  const into = folderIndex(removed, patch.folderId ?? null);
  return docOf(withList(removed, into, [...listAt(removed, into), next]));
}

/**
 * Moves a pin or a folder. A folder stays at the top level, since folders
 * hold pins only. The index is the item's position in the target list once
 * moved, clamped to its ends.
 *
 * @param doc - The current document's view.
 * @param id - The pin's or folder's id.
 * @param to - The target folder, `null` for the top level, and the index there.
 * @returns The next document, or the document itself when no item has the id or the item is already there.
 * @throws RangeError when a folder would go into a folder, or the target folder does not exist.
 */
export function tmPinsMove(doc: TmPinsDoc, id: string, to: { readonly folderId: string | null; readonly index: number }): TmPinsDoc {
  const top = doc.items.findIndex((item) => item.id === id && 'folder' in item);
  const at = top === -1 ? locate(doc.items, id) : { folder: -1, index: top };
  if (at === null) {
    return doc;
  }
  if (top !== -1 && to.folderId !== null) {
    throw new RangeError('A folder holds pins only, not other folders.');
  }
  const item = listAt(doc.items, at.folder)[at.index];
  const removed = withList(doc.items, at.folder, spliced(listAt(doc.items, at.folder), at.index, 1));
  const target = folderIndex(removed, to.folderId);
  const list = listAt(removed, target);
  const index = Math.max(0, Math.min(Math.trunc(to.index), list.length));
  if (target === at.folder && index === at.index) {
    return doc;
  }
  return docOf(withList(removed, target, spliced(list, index, 0, item)));
}

/**
 * Removes a pin, from the top level or a folder.
 *
 * @param doc - The current document's view.
 * @param id - The pin's id.
 * @returns The next document, or the document itself when no pin has the id.
 */
export function tmPinsRemove(doc: TmPinsDoc, id: string): TmPinsDoc {
  const at = locate(doc.items, id);
  return at === null ? doc : docOf(withList(doc.items, at.folder, spliced(listAt(doc.items, at.folder), at.index, 1)));
}

/**
 * Appends a folder at the top level.
 *
 * @param doc - The current document's view.
 * @param folder - The folder's id and name, which is trimmed.
 * @returns The next document.
 * @throws RangeError for a name {@link tmPinFolderNameProblem} refuses, or an eleventh folder.
 */
export function tmPinsAddFolder(doc: TmPinsDoc, folder: { readonly id: string; readonly name: string }): TmPinsDoc {
  const problem = tmPinFolderNameProblem(doc.items, folder.name);
  if (problem !== null) {
    throw nameError(problem);
  }
  if (doc.items.filter((item) => 'folder' in item).length >= TM_PIN_LIMITS.folders) {
    throw new RangeError(`A user has at most ${TM_PIN_LIMITS.folders} folders.`);
  }
  return docOf([...doc.items, { id: folder.id, folder: folder.name.trim(), items: [] }]);
}

/**
 * Removes a folder; its pins take its place at the top level, in order.
 *
 * @param doc - The current document's view.
 * @param id - The folder's id.
 * @returns The next document, or the document itself when no folder has the id.
 */
export function tmPinsRemoveFolder(doc: TmPinsDoc, id: string): TmPinsDoc {
  const at = doc.items.findIndex((item) => item.id === id && 'folder' in item);
  return at === -1 ? doc : docOf(spliced(doc.items, at, 1, ...(doc.items[at] as TmPinFolder).items));
}

/**
 * Renames a folder.
 *
 * @param doc - The current document's view.
 * @param id - The folder's id.
 * @param name - The new name, which is trimmed.
 * @returns The next document, or the document itself when no folder has the id or the name is unchanged.
 * @throws RangeError for a name {@link tmPinFolderNameProblem} refuses.
 */
export function tmPinsRenameFolder(doc: TmPinsDoc, id: string, name: string): TmPinsDoc {
  const at = doc.items.findIndex((item) => item.id === id && 'folder' in item);
  if (at === -1) {
    return doc;
  }
  const problem = tmPinFolderNameProblem(doc.items, name, id);
  if (problem !== null) {
    throw nameError(problem);
  }
  const folder = doc.items[at] as TmPinFolder;
  return folder.folder === name.trim() ? doc : docOf(spliced(doc.items, at, 1, { ...folder, folder: name.trim() }));
}
