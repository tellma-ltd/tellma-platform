// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import type { TmEntityDescriptor, TmId, TmJson, TmJsonObject } from '@tellma/core';

// The record's model rules (spec 0031 §3.6, §4.1): a new record's values, the
// strip that turns a copy of a record into a new one, the route's id, and the
// record's row of the `permissions` extra. Pure functions over descriptors,
// so the unit tests read them without a page.

/** The ownerships a write never sends: the server or the database writes them, or they are derived. */
const SERVER_WRITTEN: ReadonlySet<string> = new Set(['server-owned', 'database-owned', 'derived']);

/** The ownerships a new record's values may set. */
const CLIENT_WRITTEN: ReadonlySet<string> = new Set(['editable', 'write-once']);

/**
 * A new record's values: every child collection empty, every non-nullable
 * flag the client writes `false`, and the declaration's `defaults` merged
 * over them; every other property the client writes `null`.
 *
 * @param descriptor - The entity's descriptor.
 * @returns A fresh object.
 */
export function tmDetailsCreateModel(descriptor: TmEntityDescriptor): TmJsonObject {
  const model: Record<string, TmJson> = {};
  for (const child of descriptor.children) {
    model[child.json] = [];
  }
  // A flag the server writes, such as IsActive, is the server's to set on a new record.
  for (const property of descriptor.properties) {
    if (property.type === 'bool' && !property.nullable && CLIENT_WRITTEN.has(property.ownership)) {
      model[property.json] = false;
    }
  }
  const defaults = descriptor.declaration['defaults'] as (() => TmJsonObject) | undefined;
  return tmDetailsComplete({ ...model, ...defaults?.() }, descriptor, () => undefined);
}

/**
 * A record with every property the client writes present, `null` where the
 * record omits it, recursively in the rows of its loaded child collections.
 * The wire omits null members, and a Signal Forms tree has no field, so no
 * validation, for a member the model lacks: the working copy carries every
 * writable property so each one has its field.
 * Two members stay absent, since a `null` for either is one the server
 * refuses: a child row's key to its parent, which the server writes, and a
 * member that is not nullable but that the server fills when it is omitted.
 * A collection the record lacks stays absent, since a save never sends one
 * it did not load. An object that lacks nothing is returned as it is, so a
 * complete record keeps its identity.
 *
 * @param record - The record, wire-shaped.
 * @param descriptor - Its entity's descriptor.
 * @param childDescriptor - The descriptor of a child collection's entity, by name; `undefined` leaves the collection's rows as they are.
 * @param parentKey - The property, by name, that keys a child row to its parent; omitted on the record itself.
 * @returns The record, or a completed copy of it.
 */
export function tmDetailsComplete(
  record: TmJsonObject,
  descriptor: TmEntityDescriptor,
  childDescriptor: (entity: string) => TmEntityDescriptor | undefined,
  parentKey?: string,
): TmJsonObject {
  // The members to write over the record; empty while it lacks nothing.
  const writes: Record<string, TmJson> = {};
  let lacks = false;

  // --- Every property the client writes that the record omits, as null ---
  // Not the parent key, nor a defaulted member a null would override: the server writes both.
  for (const property of descriptor.properties) {
    const omittable = property.name === parentKey || (property.defaulted === true && !property.nullable);
    if (CLIENT_WRITTEN.has(property.ownership) && !omittable && record[property.json] === undefined) {
      writes[property.json] = null;
      lacks = true;
    }
  }

  // --- Each loaded collection's rows, completed by their own entity ---
  for (const child of descriptor.children) {
    const rows = record[child.json];
    const target = Array.isArray(rows) && rows.length > 0 ? childDescriptor(child.entity) : undefined;
    if (!Array.isArray(rows) || target === undefined) {
      continue;
    }
    let changed = false;
    const completed = rows.map((row) => {
      if (row === null || typeof row !== 'object' || Array.isArray(row)) {
        return row;
      }
      const next = tmDetailsComplete(row, target, childDescriptor, child.parentKey);
      changed ||= next !== row;
      return next;
    });
    if (changed) {
      writes[child.json] = completed;
      lacks = true;
    }
  }
  return lacks ? { ...record, ...writes } : record;
}

/**
 * Turns a copy of a record into a new record, in order: a structured copy;
 * no key and no member the server, the database or a derivation writes
 * (the stamp, the audit columns and `isActive` among them); every blob
 * reference `null`, since a blob has one owner; in every child collection,
 * recursively, the same, no parent key, and a fresh negative id; then the
 * declaration's `clone`.
 *
 * @param source - The record, wire-shaped.
 * @param descriptor - The entity's descriptor.
 * @param childDescriptor - The descriptor of a child collection's entity, by name.
 * @returns The new record.
 */
export function tmDetailsCloneStrip(
  source: TmJsonObject,
  descriptor: TmEntityDescriptor,
  childDescriptor: (entity: string) => TmEntityDescriptor,
): TmJsonObject {
  // --- 1. A structured copy, so no edit reaches the source ---
  const copy = structuredClone(source) as Record<string, TmJson>;
  // Child rows take -1, -2, … across the whole record, as new rows do.
  let next = 0;

  // --- 2 to 4. The members a new record cannot carry, then each child row's ---
  // A child row takes its fresh id before its own children do, so the ids
  // run in reading order down the whole tree.
  const strip = (row: Record<string, TmJson>, entity: TmEntityDescriptor, id?: number): void => {
    const key = jsonOf(entity, entity.key.property);
    delete row[key];
    for (const property of entity.properties) {
      if (SERVER_WRITTEN.has(property.ownership)) {
        delete row[property.json];
      } else if (property.blob !== undefined && property.json in row) {
        row[property.json] = null;
      }
    }
    if (id !== undefined) {
      row[key] = id;
    }
    for (const child of entity.children) {
      const rows = row[child.json];
      if (!Array.isArray(rows)) {
        continue;
      }
      const target = childDescriptor(child.entity);
      const parentKey = jsonOf(target, child.parentKey);
      for (const item of rows) {
        if (item !== null && typeof item === 'object' && !Array.isArray(item)) {
          const childRow = item as Record<string, TmJson>;
          delete childRow[parentKey];
          strip(childRow, target, --next);
        }
      }
    }
  };
  strip(copy, descriptor);

  // --- 5. The declaration's own resets, over the copy and the untouched source ---
  const clone = descriptor.declaration['clone'] as ((copy: TmJsonObject, source: TmJsonObject) => TmJsonObject) | undefined;
  return clone ? clone(copy, source) : copy;
}

/**
 * A property's wire name: its `json` member, or the camel-cased name when the
 * descriptor does not list it, as for a key the tenant's shape hides.
 *
 * @param entity - The entity's descriptor.
 * @param name - The property's name, such as `CustomerId`.
 * @returns The wire name, such as `customerId`.
 */
function jsonOf(entity: TmEntityDescriptor, name: string): string {
  return entity.property(name)?.json ?? name.charAt(0).toLowerCase() + name.slice(1);
}

/**
 * The record id a route names, as the app's URLs parse it: a number while it
 * is a safe integer; past that, a `long` key's decimal text, which stays
 * exact. `null` for anything else, `new` included.
 *
 * @param text - The route's `id` parameter.
 * @param keyType - The entity's key type.
 * @returns The id, or `null`.
 */
export function tmDetailsRouteId(text: string | null | undefined, keyType: 'int' | 'long'): TmId | null {
  if (text === null || text === undefined || !/^[1-9]\d*$/.test(text)) {
    return null;
  }
  const id = Number(text);
  if (Number.isSafeInteger(id)) {
    return id;
  }
  return keyType === 'long' ? (text as TmId) : null;
}

/**
 * The abilities the `permissions` extra refuses on one record, by ability,
 * with each refusal's reason: `outside-filter`, or a rule's resource key.
 *
 * @param extras - The record's extras.
 * @param id - The record's id.
 * @returns The refusals; empty when the extra is absent or refuses nothing.
 */
export function tmDetailsRefused(extras: Readonly<Record<string, TmJson>>, id: TmId | null): Readonly<Record<string, string>> {
  const rows = extras['permissions'];
  if (id === null || !Array.isArray(rows)) {
    return NONE;
  }
  for (const row of rows) {
    if (row !== null && typeof row === 'object' && !Array.isArray(row) && String(row['id']) === String(id)) {
      const refused = row['refused'];
      return refused !== null && typeof refused === 'object' && !Array.isArray(refused) ? (refused as Readonly<Record<string, string>>) : NONE;
    }
  }
  return NONE;
}

/** No refusals. */
const NONE: Readonly<Record<string, string>> = Object.freeze({});
