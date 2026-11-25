// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import {
  computed,
  type DestroyableInjector,
  DestroyRef,
  inject,
  Injector,
  runInInjectionContext,
  type Signal,
  signal,
  untracked,
  type WritableSignal,
} from '@angular/core';
import { type FieldTree, form, type SchemaPathTree } from '@angular/forms/signals';
import {
  type TmApiError,
  type TmEntityDescriptor,
  type TmEntities,
  type TmId,
  type TmJson,
  type TmJsonObject,
  type TmPageAction,
} from '@tellma/core';

import {
  TM_DETAILS_CONTEXT,
  type TmDetailsCollectionRef,
  type TmDetailsContext,
  type TmDetailsFieldRef,
  type TmDetailsMode,
  type TmDetailsStatus,
  type TmRelated,
} from './tm-details-context';
import { tmDetailsRefused } from './tm-details-model';
import type { TmDetailsAbilities, TmDetailsRecord, TmDetailsSource } from './tm-details-source';

// One record's state (spec 0031 §3.1, §4.2): its working copy and the one
// Signal Forms tree over it, its server copy, base, extras, related rows and
// registrations, in an injector of its own that the layout renders in. A
// re-key replaces the whole of it, so the form, the extras and the
// registrations never outlive their record; the header's state stays with
// the host.

/** What a record reads from its host: the page-wide state and the commands. */
export interface TmDetailsHostView<T> {
  /** The read's state. */
  readonly status: Signal<TmDetailsStatus>;
  /** The read's failure. */
  readonly error: Signal<TmApiError | null>;
  /** Whether a write is in flight. */
  readonly busy: Signal<boolean>;
  /** The page's actions. */
  readonly actions: Signal<readonly TmPageAction[]>;
  /** Enters edit mode. */
  edit(): void;
  /** Leaves edit mode, or the page in create mode. */
  cancel(): Promise<boolean>;
  /** Saves the record. */
  save(): Promise<boolean>;
  /** Reads the record again. */
  refresh(options?: { readonly quiet?: boolean }): void;
  /** Writes the working copy in code. */
  update(recipe: (model: T) => T): void;
}

/** How a record starts. */
export interface TmDetailsRecordInit<T> {
  /** The record's key: its entity, its id or `new`, and the record a create copies. */
  readonly key: string;
  /** The record's id; `null` in create mode. */
  readonly id: TmId | null;
  /** Its mode on entry. */
  readonly mode: TmDetailsMode;
  /** The working copy on entry: the create model, or `{}` until the read lands. */
  readonly model: T;
  /** Whether the working copy on entry is the record's data: a new record's, not a placeholder for a read. */
  readonly landed: boolean;
  /** The entity registry, for the related rows' keys. */
  readonly entities: TmEntities;
  /** The source. */
  readonly source: TmDetailsSource<T>;
  /** The entity's descriptor. */
  readonly descriptor: TmEntityDescriptor;
  /** The host. */
  readonly host: TmDetailsHostView<T>;
  /** The injector the record's own injector descends from. */
  readonly parent: Injector;
}

/** The related rows of one read, indexed by entity and id on first ask. */
class TmRelatedRows implements TmRelated {
  /** The rows by entity name, as the read returned them. */
  readonly #rows: Readonly<Record<string, readonly TmJsonObject[]>>;

  /** Each entity's rows by id text, built on first ask. */
  readonly #index = new Map<string, ReadonlyMap<string, TmJsonObject>>();

  /** Resolves an entity's key member. */
  readonly #entities: TmEntities;

  /**
   * Holds one read's related rows.
   *
   * @param rows - The rows by entity name.
   * @param entities - The entity registry, for each entity's key member.
   */
  constructor(rows: Readonly<Record<string, readonly TmJsonObject[]>>, entities: TmEntities) {
    this.#rows = rows;
    this.#entities = entities;
  }

  /**
   * A related row by entity and id.
   *
   * @param entity - The row's entity.
   * @param id - The row's id.
   * @returns The row, or `undefined`.
   */
  get(entity: string, id: TmId | null | undefined): TmJsonObject | undefined {
    if (id === null || id === undefined) {
      return undefined;
    }
    let index = this.#index.get(entity);
    if (index === undefined) {
      const descriptor = untracked(() => this.#entities.get(entity));
      const key = descriptor.property(descriptor.key.property)?.json ?? 'id';
      index = new Map((this.#rows[entity] ?? []).map((row) => [String(row[key]), row]));
      this.#index.set(entity, index);
    }
    return index.get(String(id));
  }
}

/**
 * One record of a details page: the context its layout injects. Internal:
 * the host creates one per record key and disposes it on a re-key.
 */
export class TmDetailsRecordState<T> implements TmDetailsContext<T> {
  /** The record's key. */
  readonly key: string;

  /** The injector the layout renders in, which provides this context. */
  readonly injector: DestroyableInjector;

  /** The entity's descriptor. */
  readonly descriptor: TmEntityDescriptor;

  /** The page's source. */
  readonly source: TmDetailsSource<T>;

  /** The record's id; `null` in create mode. */
  readonly id: Signal<TmId | null>;

  /** The record's mode, which the host moves. */
  readonly mode: WritableSignal<TmDetailsMode>;

  /** The server copy. */
  readonly entity = signal<T | undefined>(undefined);

  /** The working copy. */
  readonly model: WritableSignal<T>;

  /** The base: the server copy when edit began, or the new record's start in create mode. */
  readonly base = signal<T | null>(null);

  /** The Signal Forms tree over the working copy. */
  readonly form: FieldTree<T>;

  /** Whether the record's data has landed: a read or a save filled it, or it is a new record. */
  readonly landed: WritableSignal<boolean>;

  /** Whether the working copy differs from the base, by reference. */
  readonly dirty: Signal<boolean>;

  /** The extras, by name. */
  readonly extras = signal<Readonly<Record<string, TmJson>>>({});

  /** The related rows of the last read or save. */
  readonly #related: WritableSignal<TmRelatedRows>;

  /** The related rows, by entity and id. */
  readonly related: TmRelated = { get: (entity, id) => this.#related().get(entity, id) };

  /** The refusals of the `permissions` extra. */
  readonly refused: Signal<Readonly<Record<string, string>>>;

  /** The abilities, refusals included. */
  readonly can: TmDetailsAbilities;

  /** The registered fields, in registration order. */
  readonly fields = signal<ReadonlyMap<string, TmDetailsFieldRef>>(new Map());

  /** The registered child collections. */
  readonly collections = signal<ReadonlyMap<string, TmDetailsCollectionRef>>(new Map());

  /** The child row a link asked to show. */
  readonly childTarget = signal<{ readonly collection: string; readonly id: TmId } | null>(null);

  /** The host's view. */
  readonly #host: TmDetailsHostView<T>;

  /** The entity registry. */
  readonly #entities: TmEntities;

  /**
   * Starts a record: its injector, its working copy and the form over it.
   *
   * @param init - How the record starts.
   */
  constructor(init: TmDetailsRecordInit<T>) {
    this.key = init.key;
    this.source = init.source;
    this.descriptor = init.descriptor;
    this.#host = init.host;
    this.id = signal(init.id).asReadonly();
    this.mode = signal(init.mode);
    this.model = signal(init.model);
    this.landed = signal(init.landed);
    this.#entities = init.entities;
    this.#related = signal(new TmRelatedRows({}, init.entities));
    this.injector = Injector.create({ providers: [{ provide: TM_DETAILS_CONTEXT, useValue: this }], parent: init.parent });

    // --- The form: one tree per record, with the declaration's schema ---
    const validate = init.descriptor.declaration['validate'] as ((path: SchemaPathTree<T>) => void) | undefined;
    this.form = runInInjectionContext(this.injector, () =>
      validate ? form(this.model, (path) => validate(path as SchemaPathTree<T>)) : form(this.model),
    );

    // --- Dirty by reference: a write copies the objects on its path, never the base ---
    this.dirty = computed(() => this.mode() !== 'view' && this.model() !== this.base());
    this.refused = computed(() => tmDetailsRefused(this.extras(), this.id()));
    const { can } = init.source;
    this.can = {
      edit: computed(() => can.edit() && this.refused()['save'] === undefined),
      create: can.create,
      delete: computed(() => can.delete() && this.refused()['delete'] === undefined),
      activate: computed(() => can.activate() && this.refused()['activate'] === undefined),
    };
  }

  /** The read's state. */
  get status(): Signal<TmDetailsStatus> {
    return this.#host.status;
  }

  /** The read's failure. */
  get error(): Signal<TmApiError | null> {
    return this.#host.error;
  }

  /** Whether a write is in flight. */
  get busy(): Signal<boolean> {
    return this.#host.busy;
  }

  /** The page's actions. */
  get actions(): Signal<readonly TmPageAction[]> {
    return this.#host.actions;
  }

  /**
   * Takes a read's or a save's record as the server copy: in view mode the
   * working copy becomes it; in edit and create mode the working copy stays,
   * and only the related rows and extras move.
   *
   * @param record - The record.
   */
  land(record: TmDetailsRecord<T>): void {
    if (this.mode() === 'view') {
      this.entity.set(record.entity);
      this.model.set(record.entity);
      this.base.set(null);
    }
    this.#related.set(new TmRelatedRows(record.related ?? {}, this.#entities));
    this.extras.set(record.extras ?? {});
    this.landed.set(true);
  }

  /**
   * Takes a new record's working copy, such as a stripped clone, as its base too.
   *
   * @param model - The working copy.
   */
  start(model: T): void {
    this.model.set(model);
    this.base.set(model);
    this.landed.set(true);
  }

  /** Enters edit mode. */
  edit(): void {
    this.#host.edit();
  }

  /**
   * Leaves edit mode, or the page in create mode.
   *
   * @returns `false` when the user kept editing.
   */
  cancel(): Promise<boolean> {
    return this.#host.cancel();
  }

  /**
   * Saves the record.
   *
   * @returns `true` once saved.
   */
  save(): Promise<boolean> {
    return this.#host.save();
  }

  /**
   * Reads the record again.
   *
   * @param options - Whether the read is quiet.
   */
  refresh(options?: { readonly quiet?: boolean }): void {
    this.#host.refresh(options);
  }

  /**
   * Writes the working copy in code.
   *
   * @param recipe - The next value from the current one.
   */
  update(recipe: (model: T) => T): void {
    this.#host.update(recipe);
  }

  /**
   * Registers a rendered field until `destroyRef` ends.
   *
   * @param path - The field's path.
   * @param field - The field.
   * @param destroyRef - What ends the registration.
   */
  registerField(path: string, field: TmDetailsFieldRef, destroyRef: DestroyRef = inject(DestroyRef)): void {
    this.fields.update((fields) => new Map(fields).set(path, field));
    destroyRef.onDestroy(() => this.fields.update((fields) => (fields.get(path) === field ? without(fields, path) : fields)));
  }

  /**
   * Registers a rendered child collection until `destroyRef` ends.
   *
   * @param name - The collection's name.
   * @param collection - The collection.
   * @param destroyRef - What ends the registration.
   */
  registerCollection(name: string, collection: TmDetailsCollectionRef, destroyRef: DestroyRef = inject(DestroyRef)): void {
    this.collections.update((collections) => new Map(collections).set(name, collection));
    destroyRef.onDestroy(() =>
      this.collections.update((collections) => (collections.get(name) === collection ? without(collections, name) : collections)),
    );
  }

  /** Ends the record: its form's effects, its registrations and everything rendered in its injector. */
  dispose(): void {
    this.injector.destroy();
  }
}

/**
 * A map without one key.
 *
 * @param map - The map.
 * @param key - The key.
 * @returns A copy without it.
 */
function without<V>(map: ReadonlyMap<string, V>, key: string): ReadonlyMap<string, V> {
  const copy = new Map(map);
  copy.delete(key);
  return copy;
}
