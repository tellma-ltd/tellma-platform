// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import { computed, inject, type Signal, signal, untracked } from '@angular/core';
import {
  TmApi,
  type TmEntityDescriptor,
  type TmId,
  type TmJson,
  type TmJsonObject,
  TmPermissions,
  type TmSecurableRef,
  TmUrls,
} from '@tellma/core';
import { TmDrafts } from '@tellma/core/drafts';

// The details page's source (spec 0031 §3.1): where the host reads and writes
// the record, so entity pages and settings pages share one lifecycle. The
// entity source reads `{segment}/get` and writes `{segment}/save`; a settings
// page brings a source of its own.

/** The record a read or a write returns: the record itself, its related rows and its extras. */
export interface TmDetailsRecord<T> {
  /** The record, wire-shaped. */
  readonly entity: T;
  /** The related rows, by entity name. */
  readonly related?: Readonly<Record<string, readonly TmJsonObject[]>>;
  /** The extras, by name. */
  readonly extras?: Readonly<Record<string, TmJson>>;
}

/** A client check's problem, worded as a 422's `errors[]` item. */
export interface TmValidationProblem {
  /** The member's path, in the write's path grammar. */
  readonly path: string;
  /** The problem's code. */
  readonly code: string;
  /** The problem's arguments. */
  readonly arguments?: TmJsonObject;
}

/** What a write sends: the working copy, the base, the concurrency choice and the confirmed warnings. */
export interface TmDetailsChange<T> {
  /** The working copy, as the body sends it. */
  readonly model: T;
  /** The base the edit began from, whose stamp a save checks; `null` in create mode. */
  readonly base: T | null;
  /** `Check`, or `Override` once the user chose to overwrite another's change. */
  readonly concurrency: 'Check' | 'Override';
  /** The keys of the warnings the user confirmed so far. */
  readonly confirmations: readonly string[];
}

/** What the page's abilities answer: whether the user may edit, create, delete and activate. */
export interface TmDetailsAbilities {
  /** Whether the record may be edited. */
  readonly edit: Signal<boolean>;
  /** Whether a record may be created. */
  readonly create: Signal<boolean>;
  /** Whether the record may be deleted. */
  readonly delete: Signal<boolean>;
  /** Whether the record may be activated or deactivated. */
  readonly activate: Signal<boolean>;
}

/**
 * Where a details page reads and writes its record: an entity's API, or a
 * settings page's own endpoints. The host reads it once per record key and
 * on each refresh, and writes it on Save.
 *
 * @example
 * ```ts
 * const source: TmDetailsSource<GeneralSettings> = {
 *   kind: 'settings', entity: 'core.GeneralSettings', id: signal(1), titleKey: 'Settings_General',
 *   draftKey: signal(null), writeRoute: 'settings/general/save',
 *   read: (abort) => api.read('settings/general', {}, { signal: abort }).then((entity) => ({ entity })),
 *   write: ({ model }) => api.write('settings/general/save', model).then((entity) => ({ entity })),
 *   can: { edit, create: signal(false), delete: signal(false), activate: signal(false) },
 * };
 * ```
 */
export interface TmDetailsSource<T> {
  /** The page context's kind and the prefix of the page's action ids. */
  readonly kind: 'details' | 'settings';
  /** The entity the descriptor comes from, which the layout resolves paths against. */
  readonly entity: string;
  /** The record's id; `null` in create mode. A change re-keys the record. */
  readonly id: Signal<TmId | null>;
  /** A fixed title's key; absent, the title is the record's display name. */
  readonly titleKey?: string;
  /** The key of the record's draft, from `TmDrafts.key`; `null` keeps no draft. */
  readonly draftKey: Signal<string | null>;
  /**
   * Reads the record.
   *
   * @param signal - Cancels the read when the page leaves or a newer read supersedes it.
   * @returns The record, its related rows and its extras; it rejects with `TmApiError`.
   */
  read(signal: AbortSignal): Promise<TmDetailsRecord<T>>;
  /**
   * Writes the record.
   *
   * @param change - The working copy, the base, the concurrency choice and the confirmed keys.
   * @returns The saved record as the server read it back; it rejects with `TmApiError`.
   */
  write(change: TmDetailsChange<T>): Promise<TmDetailsRecord<T>>;
  /** The write's route, such as `customers/save`, whose contract stamps a draft. */
  readonly writeRoute: string;
  /** Whether the user may edit, create, delete and activate, by permission and the tenant's state. */
  readonly can: TmDetailsAbilities;
  /**
   * The client checks a schema cannot express, run before a write.
   *
   * @param model - The working copy.
   * @returns The problems; empty when there are none.
   */
  validate?(model: T): readonly TmValidationProblem[];
  /** The permission a save needs, for the confirm-it's-you notice; absent or `null`, none. */
  readonly saveSecurable?: Signal<TmSecurableRef | null>;
  /** The permission Edit's disabled reason names; `saveSecurable`'s value by default. */
  readonly editSecurable?: TmSecurableRef;
  /** A settings page's category, for its page context. */
  readonly category?: Signal<string>;
  /**
   * Maps a validation error's path to the model's segments; the platform's mapping by default.
   *
   * @param path - The error's path, in the write's path grammar.
   * @returns The segments, or `null` when the path names no member.
   */
  readonly errorSegments?: (path: string) => readonly (string | number)[] | null;
  /** A signal whose change re-reads the record in view mode; in edit mode, see `rebase`. */
  readonly changes?: Signal<unknown>;
  /**
   * Gives an edit a new base after a `changes` move.
   *
   * @param base - The current base.
   * @returns The new base, its stamp included, or `null` to keep the current one.
   */
  rebase?(base: T): T | null;
}

/** An entities result, as `{segment}/get` and `{segment}/save` return it. */
interface TmEntitiesResult<T> {
  /** The records, in request order. */
  readonly entities: readonly T[];
  /** The related rows, by entity name. */
  readonly related?: Readonly<Record<string, readonly TmJsonObject[]>>;
  /** The extras, by name. */
  readonly extras?: Readonly<Record<string, TmJson>>;
}

/**
 * The record of an entities result: its first record, its related rows and its extras.
 *
 * @param result - The result.
 * @returns The record.
 */
function recordOf<T>(result: TmEntitiesResult<T>): TmDetailsRecord<T> {
  const [entity] = result.entities;
  if (entity === undefined) {
    throw new Error('The server returned no record.');
  }
  return { entity, related: result.related ?? {}, extras: result.extras ?? {} };
}

/**
 * An entity's details source: it reads `{segment}/get` and writes
 * `{segment}/save`, both with every extra the descriptor lists; Edit and
 * New follow the `save` permission, Delete and Activate their own. Call it
 * in an injection context.
 *
 * @param descriptor - The entity's descriptor; the entity must have an API segment.
 * @param id - The record's id; `null` in create mode.
 * @returns The source.
 * @throws Error when the entity has no API segment.
 *
 * @example
 * ```ts
 * const source = tmEntitySource<AcmeCustomer>(inject(TmEntities).get('acme.Customer'), signal(5));
 * ```
 */
export function tmEntitySource<T = TmJsonObject>(descriptor: TmEntityDescriptor, id: Signal<TmId | null>): TmDetailsSource<T> {
  const api = inject(TmApi);
  const permissions = inject(TmPermissions);
  const drafts = inject(TmDrafts);
  const urls = inject(TmUrls);
  const { name, segment, extras } = descriptor;
  if (segment === null) {
    throw new Error(`The entity '${name}' has no API segment, so it has no details source.`);
  }
  const include = [...extras];
  const writeRoute = `${segment}/save`;
  return {
    kind: 'details',
    entity: name,
    id,
    // The routed tenant's draft of this record; none outside a tenant.
    draftKey: computed(() => {
      const tenantId = urls.tenantId();
      return tenantId === null ? null : drafts.key(tenantId, name, id() ?? 'new');
    }),
    read: (abort) =>
      api.read<TmEntitiesResult<T>>(`${segment}/get`, { id: untracked(id), include }, { signal: abort }).then(recordOf),
    write: ({ model, concurrency, confirmations }) =>
      api
        .write<TmEntitiesResult<T>>(writeRoute, { entities: [model], returnEntities: true, include, concurrency }, { confirmations })
        .then(recordOf),
    writeRoute,
    can: {
      edit: permissions.allows(name, 'save'),
      create: permissions.allows(name, 'save'),
      delete: permissions.allows(name, 'delete'),
      activate: permissions.allows(name, 'activate'),
    },
    saveSecurable: signal<TmSecurableRef | null>([name, 'Save']).asReadonly(),
  };
}
