// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import { inject, Injector, type Provider, signal, type Signal, untracked, type WritableSignal } from '@angular/core';
import {
  type TmApiError,
  TmEntities,
  type TmEntityDescriptor,
  type TmEntityRules,
  type TmId,
  type TmJsonObject,
  type TmPageAction,
} from '@tellma/core';
// A layout test runs without the route, so the fake builds the record's state statically from the entry point the route loads.
import {
  type TmDetailsContext,
  type TmDetailsMode,
  type TmDetailsRecord,
  type TmDetailsSource,
  type TmDetailsStatus,
  ɵTM_DETAILS_CONTEXT,
  ɵTM_DETAILS_RECORD,
  ɵtmDetailsCreateModel,
  type ɵTmDetailsHostView,
  ɵTmDetailsRecordState,
} from '@tellma/core/details';
import { TmUploads } from '@tellma/core/uploads';

// A details context for a layout's unit test (spec 0031 §1): the real
// record state, with its form, dirty rule, refusals and registrations, over
// a host whose commands move the mode at once and record their calls, and
// whose read state the test sets. No route, no read and no write.

/** A command the layout called on the fake context. */
export type TmFakeDetailsCall = 'edit' | 'cancel' | 'save' | 'refresh' | 'update';

/** How a fake details context starts. */
export interface TmFakeDetailsOptions<T> {
  /** The entity, such as `acme.Customer`, whose descriptor the registry gives. */
  readonly entity: string;
  /** The descriptor, when the test has one of its own instead of the registry's. */
  readonly descriptor?: TmEntityDescriptor;
  /** The record the read returned, with its related rows and extras; absent in create mode, and for a record still loading. */
  readonly record?: TmDetailsRecord<T>;
  /** The mode on entry: `view` by default, `create` for a new record with the entity's defaults. */
  readonly mode?: TmDetailsMode;
  /** The record's id; the record's own `id` by default, `null` in create mode. */
  readonly id?: TmId | null;
  /** The abilities, all `true` by default. */
  readonly can?: Partial<Readonly<Record<'edit' | 'create' | 'delete' | 'activate', boolean>>>;
  /**
   * The declaration's rules, as its `validate` loader resolves them: the
   * form applies them after the platform's. The fake builds the form at
   * once, so it never loads them itself.
   */
  readonly rules?: TmEntityRules<T>;
}

/**
 * A details context a test drives: the read's state and the busy flag are
 * writable, `land()` takes a read's record, and every command is recorded.
 */
export interface TmFakeDetails<T> extends TmDetailsContext<T> {
  /** The read's state, which the test sets: `ready` once a record is given, `loading` before. */
  readonly status: WritableSignal<TmDetailsStatus>;
  /** The read's failure, which the test sets. */
  readonly error: WritableSignal<TmApiError | null>;
  /** Whether a write is in flight, which the test sets. */
  readonly busy: WritableSignal<boolean>;
  /** The commands the layout called, oldest first. */
  readonly calls: Signal<readonly TmFakeDetailsCall[]>;
  /** The providers that give a component under test this context through `injectTmDetails()`, and the layout kit its record. */
  readonly providers: Provider[];
  /**
   * Takes a read's record as the server copy, as a read landing does, and turns `ready`.
   *
   * @param record - The record.
   */
  land(record: TmDetailsRecord<T>): void;
}

/** The fake host: the read state the test sets, and commands that act at once. */
class TmFakeDetailsHost<T> implements ɵTmDetailsHostView<T> {
  /** The read's state. */
  readonly status = signal<TmDetailsStatus>('loading');

  /** The read's failure. */
  readonly error = signal<TmApiError | null>(null);

  /** Whether a write is in flight. */
  readonly busy = signal(false);

  /** The page's actions: none, without a page scope. */
  readonly actions: Signal<readonly TmPageAction[]> = signal([]).asReadonly();

  /** The commands called. */
  readonly calls = signal<readonly TmFakeDetailsCall[]>([]);

  /** The record the commands act on, once created. */
  record: ɵTmDetailsRecordState<T> | null = null;

  /** Enters edit mode, as the host does: the base is the server copy. */
  edit(): void {
    this.#call('edit');
    const record = this.#record();
    if (untracked(record.mode) === 'view') {
      record.base.set(untracked(record.entity) ?? null);
      record.mode.set('edit');
    }
  }

  /**
   * Leaves edit mode without asking, the working copy back at its base and
   * the edit's uploads discarded; a new record stays.
   *
   * @returns `true`.
   */
  cancel(): Promise<boolean> {
    this.#call('cancel');
    const record = this.#record();
    if (untracked(record.mode) === 'edit') {
      record.uploads.cancelAll();
      record.model.set(untracked(record.base) ?? untracked(record.model));
      record.base.set(null);
      record.mode.set('view');
    }
    return Promise.resolve(true);
  }

  /**
   * Saves at once: the working copy becomes the server copy, in view mode.
   *
   * @returns `true`.
   */
  save(): Promise<boolean> {
    this.#call('save');
    const record = this.#record();
    const model = untracked(record.model);
    record.mode.set('view');
    record.land({ entity: model });
    return Promise.resolve(true);
  }

  /** Records the refresh; the test lands a record when it wants one. */
  refresh(): void {
    this.#call('refresh');
  }

  /**
   * Writes the working copy, in edit and create mode.
   *
   * @param recipe - The next value from the current one.
   * @throws Error in view mode, as the host does in development.
   */
  update(recipe: (model: T) => T): void {
    this.#call('update');
    const record = this.#record();
    if (untracked(record.mode) === 'view') {
      throw new Error('A details record takes update() in edit and create mode only: call edit() first.');
    }
    record.model.update(recipe);
  }

  /**
   * Records a command.
   *
   * @param call - The command.
   */
  #call(call: TmFakeDetailsCall): void {
    this.calls.update((calls) => [...calls, call]);
  }

  /**
   * The record.
   *
   * @returns The record.
   */
  #record(): ɵTmDetailsRecordState<T> {
    if (this.record === null) {
      throw new Error('The fake details context is not set up yet.');
    }
    return this.record;
  }
}

/**
 * A details context for a layout's or a hatch's unit test, without a page:
 * the record's form, dirty rule, refusals and registrations are the real
 * ones; the commands act at once and are recorded. Call it in an injection
 * context whose entity registry has loaded, such as
 * `TestBed.runInInjectionContext`.
 *
 * @typeParam T - The record's type.
 * @param options - The entity, the record, the mode and the abilities.
 * @returns The context, with its providers.
 *
 * @example
 * ```ts
 * const details = TestBed.runInInjectionContext(() =>
 *   tmFakeDetails({ entity: 'acme.Customer', record: { entity: { id: 5, name: 'Al Noor' } } }),
 * );
 * TestBed.configureTestingModule({ providers: details.providers });
 * const fixture = TestBed.createComponent(CustomerDetails);
 * details.edit();
 * expect(details.calls()).toEqual(['edit']);
 * ```
 */
export function tmFakeDetails<T = TmJsonObject>(options: TmFakeDetailsOptions<T>): TmFakeDetails<T> {
  const injector = inject(Injector);
  const descriptor = options.descriptor ?? inject(TmEntities).get(options.entity);
  const mode = options.mode ?? 'view';
  const keyJson = descriptor.property(descriptor.key.property)?.json ?? 'id';
  const recordId = (options.record?.entity as TmJsonObject | undefined)?.[keyJson] as TmId | undefined;
  const id = options.id !== undefined ? options.id : mode === 'create' ? null : (recordId ?? null);
  const can = (ability: 'edit' | 'create' | 'delete' | 'activate'): Signal<boolean> => signal(options.can?.[ability] ?? true).asReadonly();

  // --- A source that never reaches a server ---
  const source: TmDetailsSource<T> = {
    kind: 'details',
    entity: descriptor.name,
    id: signal(id).asReadonly(),
    draftKey: signal(null).asReadonly(),
    read: () => Promise.reject(new Error('A fake details context reads nothing: land() a record instead.')),
    write: ({ model }) => Promise.resolve({ entity: model }),
    writeRoute: `${descriptor.segment ?? descriptor.name}/save`,
    can: { edit: can('edit'), create: can('create'), delete: can('delete'), activate: can('activate') },
  };

  // --- The real record state, over the fake host ---
  const host = new TmFakeDetailsHost<T>();
  const created = mode === 'create';
  const model = (created ? ɵtmDetailsCreateModel(descriptor) : {}) as T;
  // An edit starts in view mode, so the record lands as the server copy, then enters edit as the host does.
  const record = new ɵTmDetailsRecordState<T>({
    key: `${descriptor.name}|${id ?? 'new'}|`,
    id,
    mode: created ? 'create' : 'view',
    model,
    landed: created,
    entities: inject(TmEntities),
    source,
    descriptor,
    host,
    parent: injector,
    rules: options.rules,
  });
  host.record = record;
  if (created) {
    record.base.set(model);
    host.status.set('ready');
  }
  if (options.record !== undefined) {
    record.land(options.record);
    host.status.set('ready');
  }
  if (mode === 'edit') {
    // The base is the server copy itself, so the edit is clean until a write.
    record.base.set(untracked(record.entity) ?? null);
    record.mode.set('edit');
  }

  // --- The context: the record's members, with the host's state writable ---
  const fake: TmFakeDetails<T> = {
    descriptor,
    source,
    id: record.id,
    mode: record.mode,
    status: host.status,
    error: host.error,
    entity: record.entity,
    model: record.model,
    form: record.form,
    dirty: record.dirty,
    busy: host.busy,
    related: record.related,
    extras: record.extras,
    can: record.can,
    refused: record.refused,
    actions: host.actions,
    childTarget: record.childTarget,
    calls: host.calls.asReadonly(),
    providers: [],
    edit: () => record.edit(),
    cancel: () => record.cancel(),
    save: () => record.save(),
    refresh: (refresh) => record.refresh(refresh),
    update: (recipe) => record.update(recipe),
    // An omitted destroyRef stays omitted, so the record takes the caller's own.
    registerField: (path, field, destroyRef) => record.registerField(path, field, destroyRef),
    registerCollection: (name, collection, destroyRef) => record.registerCollection(name, collection, destroyRef),
    land: (landed) => {
      record.land(landed);
      host.status.set('ready');
    },
  };
  // The layout kit reads the record itself, and its edit controls the record's uploads.
  fake.providers.push(
    { provide: ɵTM_DETAILS_CONTEXT, useValue: fake },
    { provide: ɵTM_DETAILS_RECORD, useValue: record },
    { provide: TmUploads, useValue: record.uploads },
  );
  return fake;
}
