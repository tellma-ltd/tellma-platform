// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import type { TmPin, TmPinFolder, TmPinItem, TmPinsDoc } from '@tellma/core';

// The pinned bar's rules over the pins model (spec 0027 §8.2, §8.3): which
// stored items the bar shows, what a pin of the routed page captures, the
// URL a pin opens, the current pin and a chip's label, and new ids. An item
// that fails a rule (its shape, id, lengths, the caps or the one-level rule)
// leaves the view but stays stored until the next write.

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
