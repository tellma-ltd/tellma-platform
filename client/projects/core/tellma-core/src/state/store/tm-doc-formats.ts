// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import { InjectionToken } from '@angular/core';

// The client formats of the stored documents (spec 0025 §2.4). A format is
// bumped whenever a document's stored shape changes, a server FormatVersion
// bump included. The store keeps each document under `<name>@<format>`, so
// tabs of two builds share the store after a deployment without reading, or
// blanking, each other's copies.

/**
 * The client format of each fixed document name, and of each name prefix
 * (a key ending in `:`, such as `entity:`), which covers every name that
 * starts with it. A leader document's format comes from its own registration.
 */
export const TM_DOC_FORMATS: Readonly<Record<string, number>> = Object.freeze({
  tenants: 1,
  distribution: 1,
  settings: 1,
  me: 1,
  tenant: 1,
  sync: 1,
  zone: 1,
  'entity:': 1,
  'tree:': 1,
});

/**
 * The formats the client store reads by: {@link TM_DOC_FORMATS} unless a test
 * stands in another build's. Internal.
 */
export const TM_STORE_FORMATS = new InjectionToken<Readonly<Record<string, number>>>('TM_STORE_FORMATS', {
  providedIn: 'root',
  factory: () => TM_DOC_FORMATS,
});

/**
 * The formats of documents other features register with the store, such as
 * the leader documents, by exact name. Internal: empty until those features
 * provide it.
 */
export const TM_STORE_EXTRA_FORMATS = new InjectionToken<Readonly<Record<string, number>>>('TM_STORE_EXTRA_FORMATS', {
  providedIn: 'root',
  factory: () => ({}),
});

/**
 * The client format of a document name: its own entry, else the entry of the
 * prefix it starts with, else `undefined` for a name with no format.
 *
 * @param formats - The formats, by name and by prefix.
 * @param name - The document's name, such as `entity:acme.Currency`.
 * @returns The format, or `undefined`.
 */
export function tmDocFormat(formats: Readonly<Record<string, number>>, name: string): number | undefined {
  // Object.hasOwn keeps a name such as `constructor` from reading the prototype.
  if (Object.hasOwn(formats, name)) {
    return formats[name];
  }
  const colon = name.indexOf(':');
  const prefix = colon > 0 ? name.slice(0, colon + 1) : '';
  return prefix !== '' && Object.hasOwn(formats, prefix) ? formats[prefix] : undefined;
}
