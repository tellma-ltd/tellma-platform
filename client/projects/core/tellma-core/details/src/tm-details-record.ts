// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import {
  computed,
  type DestroyableInjector,
  DestroyRef,
  inject,
  InjectionToken,
  Injector,
  runInInjectionContext,
  type Signal,
  signal,
  type TemplateRef,
  untracked,
  type WritableSignal,
} from '@angular/core';
import { type FieldTree, form, type SchemaPathTree } from '@angular/forms/signals';
import {
  type TmApiError,
  type TmChildDescriptor,
  type TmEntityDescriptor,
  type TmEntityRules,
  type TmEntities,
  type TmId,
  type TmJson,
  type TmJsonObject,
  TmLocalization,
  type TmPageAction,
} from '@tellma/core';
import { TmUploads } from '@tellma/core/uploads';

import {
  TM_DETAILS_CONTEXT,
  type TmDetailsCollectionRef,
  type TmDetailsContext,
  type TmDetailsFieldRef,
  type TmDetailsMode,
  type TmDetailsStatus,
  type TmRelated,
} from './tm-details-context';
import { tmDetailsComplete, tmDetailsRefused, tmDetailsSaveBody } from './tm-details-model';
import { type TmDetailsServerError, tmDetailsPlatformSchema } from './tm-details-schema';
import type { TmDetailsAbilities, TmDetailsRecord, TmDetailsSource } from './tm-details-source';
import { TM_DETAILS_STRINGS_EN } from './tm-details-strings';

// One record's state (spec 0031 §3.1, §4.2): its working copy and the one
// Signal Forms tree over it, its server copy, base, extras, related rows and
// registrations, in an injector of its own that the layout renders in. A
// re-key replaces the whole of it, so the form, the extras and the
// registrations never outlive their record; the header's state stays with
// the host. It also holds what the layout kit renders outside the layout
// (the aside, the title's extras) and the view state the page remembers.

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
  /** The declaration's rules, once loaded; a record that starts without them takes them with {@link TmDetailsRecordState.ɵrules}. */
  readonly rules?: TmEntityRules<T>;
}

/** The layout's aside, as the kit registers it for the frame to render. */
export interface TmDetailsAsideRef {
  /** The aside's content. */
  readonly template: TemplateRef<unknown>;
  /** The aside's heading, in the active language; empty for none. */
  readonly title: Signal<string>;
}

/** A layout's `tm-details-tabs`, as the kit registers it. */
export interface TmDetailsTabsRef {
  /** Renders every tab's content without selecting it, so the fields on unvisited tabs register. */
  instantiate(): void;
}

/** The view state a record's page remembers: what the user opened, collapsed and scrolled. */
export interface TmDetailsViewState {
  /** The selected tab of each `tm-details-tabs`, by its id. */
  readonly tabs: WritableSignal<Readonly<Record<string, string>>>;
  /** Whether each collapsible card is expanded, by its id. */
  readonly cards: WritableSignal<Readonly<Record<string, boolean>>>;
  /** The scrolling element of each scrollable card, by the card's id, and of the aside, under `aside`. */
  readonly scrollers: WritableSignal<ReadonlyMap<string, HTMLElement>>;
}

/**
 * The record on screen. Internal: the record's injector provides it, so the
 * layout kit reaches what it registers with the frame; a layout test's fake
 * provides the same.
 */
export const TM_DETAILS_RECORD = new InjectionToken<TmDetailsRecordState<unknown>>('TM_DETAILS_RECORD');

/**
 * Whether the app runs in development, where the kit's misuse throws.
 *
 * @returns Whether it does.
 */
export function tmDevMode(): boolean {
  return typeof ngDevMode !== 'undefined' && !!ngDevMode;
}

/**
 * The record of a details page, for the layout kit.
 *
 * @returns The record.
 * @throws Error outside a details page.
 */
export function injectTmDetailsRecord(): TmDetailsRecordState<TmJsonObject> {
  const record = inject(TM_DETAILS_RECORD, { optional: true });
  if (record === null) {
    throw new Error('The details layout kit renders inside a details page: use it in a layout or a hatch the page renders.');
  }
  return record as TmDetailsRecordState<TmJsonObject>;
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

  /** The Signal Forms tree over the working copy; rebuilt once when the declaration's rules arrive after the record started. */
  form: FieldTree<T>;

  /** Whether the form carries the declaration's rules, or the declaration has none to wait for. */
  #ruled = false;

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

  /** The form's uploads, which the record's injector provides to its editors. */
  readonly uploads: TmUploads;

  /** The recorded server and source errors the form shows on their nodes. */
  readonly errors = signal<readonly TmDetailsServerError[]>([]);

  /** The layout's aside, which the frame renders in its column. */
  readonly aside = signal<TmDetailsAsideRef | null>(null);

  /** The layout's title extras, which the frame renders after the badges, in registration order. */
  readonly titleExtras = signal<readonly TemplateRef<unknown>[]>([]);

  /** The view state the page remembers. */
  readonly view: TmDetailsViewState = { tabs: signal({}), cards: signal({}), scrollers: signal(new Map()) };

  /** The layout's `tm-details-tabs`, by id: what a failed save renders before it marks the fields touched. */
  readonly tabs = signal<ReadonlyMap<string, TmDetailsTabsRef>>(new Map());

  /** The host's view. */
  readonly #host: TmDetailsHostView<T>;

  /** The last id a new child row took; 0 before the first. */
  #lastRowId = 0;

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
    this.injector = Injector.create({
      providers: [{ provide: TM_DETAILS_CONTEXT, useValue: this }, { provide: TM_DETAILS_RECORD, useValue: this }, TmUploads],
      parent: init.parent,
    });
    this.uploads = this.injector.get(TmUploads);
    // The kit's own strings (the lock, the error names, the audit card), merged once, as the page's frame merges them.
    this.injector.get(TmLocalization).ɵregisterStrings('details', TM_DETAILS_STRINGS_EN);

    // --- The form: one tree per record, the platform's schema first, then the declaration's (spec 0031 §4.2, §7) ---
    this.form = this.#build(init.rules);
    this.#ruled = init.rules !== undefined;

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
      // Every writable member present, so each has its field and its rules.
      const entity = this.#complete(record.entity);
      this.entity.set(entity);
      this.model.set(entity);
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
    const complete = this.#complete(model);
    this.model.set(complete);
    this.base.set(complete);
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

  /**
   * The id of a new child row: −1, −2 and so on through the record's life,
   * below every negative id its collections already hold, such as a
   * restored draft's new rows, so no two new rows share one. The server
   * inserts a row with a negative id. Internal: the kit's child grids and
   * child files call it.
   *
   * @returns The id.
   */
  ɵnewRowId(): number {
    // A negative id is a new row to the server (spec 0031 §6.4).
    let lowest = this.#lastRowId;
    const model = untracked(this.model) as Readonly<Record<string, unknown>>;
    for (const child of this.descriptor.children) {
      const rows = model[child.json];
      for (const row of Array.isArray(rows) ? rows : []) {
        const id = (row as { readonly id?: unknown } | null)?.id;
        if (typeof id === 'number' && id < lowest) {
          lowest = id;
        }
      }
    }
    this.#lastRowId = lowest - 1;
    return this.#lastRowId;
  }

  /**
   * A new row of a child collection: the values given, the next new row's
   * id, and every other member the client writes, as `null`, so each one
   * has its node in the form. Its key to the record stays absent, for the
   * server to write. Internal: the kit's child grids and child files call it.
   *
   * @param collection - The collection.
   * @param values - The row's values, such as a layout's defaults.
   * @returns The row.
   */
  ɵnewChildRow(collection: TmChildDescriptor, values: TmJsonObject = {}): TmJsonObject {
    const descriptor = this.#child(collection.entity);
    const key = descriptor?.property(descriptor.key.property)?.json ?? 'id';
    // The id comes last, so no default can take a stored row's id.
    const row: TmJsonObject = { ...values, [key]: this.ɵnewRowId() };
    return descriptor === undefined ? row : tmDetailsComplete(row, descriptor, (entity) => this.#child(entity), collection.parentKey);
  }

  /**
   * Registers the layout's aside, until `destroyRef` ends. Internal: the kit's `tm-details-aside` calls it.
   *
   * @param aside - The aside.
   * @param destroyRef - What ends the registration.
   * @throws Error in development, when the layout already has an aside.
   */
  registerAside(aside: TmDetailsAsideRef, destroyRef: DestroyRef): void {
    if (untracked(this.aside) !== null) {
      if (tmDevMode()) {
        throw new Error('A details layout renders one tm-details-aside: merge the second one into the first.');
      }
      return;
    }
    this.aside.set(aside);
    destroyRef.onDestroy(() => this.aside.update((current) => (current === aside ? null : current)));
  }

  /**
   * Registers one of the layout's title extras, until `destroyRef` ends. Internal: the kit's `tmDetailsTitleExtra` calls it.
   *
   * @param template - The extra's content.
   * @param destroyRef - What ends the registration.
   */
  registerTitleExtra(template: TemplateRef<unknown>, destroyRef: DestroyRef): void {
    this.titleExtras.update((extras) => [...extras, template]);
    destroyRef.onDestroy(() => this.titleExtras.update((extras) => extras.filter((extra) => extra !== template)));
  }

  /**
   * Registers a `tm-details-tabs` by its id, until `destroyRef` ends. Internal: the kit's tabs call it.
   *
   * @param id - The tabs' id.
   * @param tabs - The tabs.
   * @param destroyRef - What ends the registration.
   * @throws Error in development, when another rendered `tm-details-tabs` has the id.
   */
  registerTabs(id: string, tabs: TmDetailsTabsRef, destroyRef: DestroyRef): void {
    if (untracked(this.tabs).has(id)) {
      if (tmDevMode()) {
        throw new Error(`Two tm-details-tabs in one details layout have the id '${id}': give each its own id.`);
      }
      return;
    }
    this.tabs.update((all) => new Map(all).set(id, tabs));
    destroyRef.onDestroy(() => this.tabs.update((all) => (all.get(id) === tabs ? without(all, id) : all)));
  }

  /**
   * Registers an element whose scroll the page remembers, until `destroyRef` ends. Internal: scrollable cards and the aside call it.
   *
   * @param id - The scrollable card's id, or `aside`.
   * @param element - The element that scrolls.
   * @param destroyRef - What ends the registration.
   */
  registerScroller(id: string, element: HTMLElement, destroyRef: DestroyRef): void {
    this.view.scrollers.update((scrollers) => new Map(scrollers).set(id, element));
    destroyRef.onDestroy(() => this.view.scrollers.update((scrollers) => (scrollers.get(id) === element ? without(scrollers, id) : scrollers)));
  }

  /**
   * Takes the declaration's rules, which load with the layout: a record
   * that started before they loaded rebuilds its form with them once,
   * before the layout renders, so nothing has read the form without them.
   *
   * @param rules - The rules.
   */
  ɵrules(rules: TmEntityRules<T>): void {
    if (this.#ruled) {
      return;
    }
    this.#ruled = true;
    this.form = this.#build(rules);
  }

  /**
   * Builds the form over the working copy: the platform's rules, then the declaration's.
   *
   * @param rules - The declaration's rules, if any.
   * @returns The form.
   */
  #build(rules: TmEntityRules<T> | undefined): FieldTree<T> {
    const descriptor = this.descriptor;
    const options = { mode: this.mode, uploads: this.uploads, errors: this.errors, child: (entity: string) => this.#child(entity) };
    return runInInjectionContext(this.injector, () =>
      form(this.model, (path: SchemaPathTree<T>) => {
        tmDetailsPlatformSchema(path, descriptor, options);
        rules?.(path);
      }),
    );
  }

  /**
   * The working copy as a save sends it: a defaulted member left empty is
   * left out, so the server fills it.
   *
   * @returns The body's record.
   */
  ɵsaveBody(): T {
    return tmDetailsSaveBody(untracked(this.model) as TmJsonObject, this.descriptor, (entity) => this.#child(entity)) as T;
  }

  /** Ends the record: its form's effects, its registrations and everything rendered in its injector. */
  dispose(): void {
    this.injector.destroy();
  }

  /**
   * A record with every writable member present, recursively in its child rows.
   *
   * @param record - The record, wire-shaped.
   * @returns It, or a completed copy.
   */
  #complete(record: T): T {
    return tmDetailsComplete(record as TmJsonObject, this.descriptor, (entity) => this.#child(entity)) as T;
  }

  /**
   * A child collection's entity descriptor.
   *
   * @param entity - The entity's name.
   * @returns Its descriptor, or `undefined` when the metadata does not list it.
   */
  #child(entity: string): TmEntityDescriptor | undefined {
    try {
      return untracked(() => this.#entities.get(entity));
    } catch {
      return undefined;
    }
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
