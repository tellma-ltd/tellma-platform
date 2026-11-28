// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import { Location } from '@angular/common';
import {
  afterNextRender,
  computed,
  DestroyRef,
  effect,
  EnvironmentInjector,
  inject,
  Injectable,
  Injector,
  reflectComponentType,
  resource,
  type Signal,
  signal,
  type Type,
  untracked,
} from '@angular/core';
import { Router } from '@angular/router';
import { TmConfirm } from '@tellma/core-ui/confirm';
import {
  TmApiError,
  TmDisplayNames,
  type TmDisplayNameParts,
  type TmEntityDescriptor,
  type TmEntityRules,
  TmEntities,
  type TmId,
  type TmJsonObject,
  type TmLazyComponent,
  TmLaunchCache,
  TmLocalization,
  type TmPageAction,
  TmPageContexts,
  TmPermissions,
  TmServerText,
  TmUrls,
} from '@tellma/core';
import { TmDrafts } from '@tellma/core/drafts';

import type { TmDetailsMode, TmDetailsStatus } from './tm-details-context';
import { tmDetailsCloneStrip, tmDetailsCreateModel } from './tm-details-model';
import { type TmDetailsHostView, TmDetailsRecordState } from './tm-details-record';
import type { TmDetailsRecord, TmDetailsSource } from './tm-details-source';
import { TM_DETAILS_STRINGS_EN } from './tm-details-strings';

// The host's state and commands (spec 0031 §3, §4): the routed record's key,
// the one read keyed by that record and a refresh generation, the record on
// screen (held across a read for another one until it lands), the modes,
// the title and the abilities' reasons. The frame renders it; the routed host
// and the source page only set it up.

/** What the routed host or a source page sets the host up with. */
export interface TmDetailsSetup<T> {
  /** The page's source. */
  readonly source: Signal<TmDetailsSource<T>>;
  /** The source of the record a create copies, or `null`. */
  readonly cloneSource: Signal<TmDetailsSource<T> | null>;
  /** The layout: a component, or its loader. */
  readonly layout: Signal<TmLazyComponent | Type<unknown>>;
  /** Whether the host is the routed details page, with a crumb and New; a source page elsewhere has neither. */
  readonly routed: boolean;
}

/** The routed record: its key, its id, the record a create copies, and the sources. */
interface TmRouted<T> {
  /** The record's key: entity, id or `new`, and the copied record's id. */
  readonly key: string;
  /** The record's id; `null` in create mode. */
  readonly id: TmId | null;
  /** The copied record's id, or `null`. */
  readonly clone: TmId | null;
  /** The page's source. */
  readonly source: TmDetailsSource<T>;
  /** The copied record's source, or `null`. */
  readonly cloneSource: TmDetailsSource<T> | null;
}

/** One read's request: the routed record and its generation, and whether it is quiet. */
interface TmReadParams<T> {
  /** The routed record. */
  readonly routed: TmRouted<T>;
  /** The refresh generation. */
  readonly generation: number;
  /** Whether the read shows no progress bar and no busy state. */
  readonly quiet: boolean;
}

/** What a read returns: its request, so its record lands under the right key, and the record. */
interface TmReadResult<T> {
  /** The request. */
  readonly params: TmReadParams<T>;
  /** The record. */
  readonly record: TmDetailsRecord<T>;
}

/** The title the header shows: a display name's parts, plain text, or nothing yet (a skeleton). */
export interface TmDetailsTitle {
  /** The display name's parts, for `tm-display-name`; `null` for plain text or a skeleton. */
  readonly parts: TmDisplayNameParts | null;
  /** The plain text, which the document title takes; empty while unknown. */
  readonly text: string;
}

/** A failed layout load, as an error the page's error state describes. */
const LAYOUT_FAILED = (): TmApiError => new TmApiError({ kind: 'offline', code: 'layout-not-loaded' });

/**
 * A read's failure as the API error the page describes: itself, or a server
 * error carrying its message when something other than the API threw.
 *
 * @param error - The failure.
 * @returns The API error.
 */
function apiErrorOf(error: Error | undefined): TmApiError {
  return error instanceof TmApiError ? error : new TmApiError({ kind: 'server', message: error?.message });
}

/**
 * The details page's state and commands. Internal: the routed host or a
 * source page provides it and sets it up; the frame renders it.
 */
@Injectable()
export class TmDetailsController<T = TmJsonObject> implements TmDetailsHostView<T> {
  readonly #injector = inject(Injector);
  /** The app's injector, which outlives the page, for what runs after an await the page may not see the end of. */
  readonly #environment = inject(EnvironmentInjector);
  readonly #entities = inject(TmEntities);
  readonly #names = inject(TmDisplayNames);
  readonly #permissions = inject(TmPermissions);
  readonly #l10n = inject(TmLocalization);
  readonly #urls = inject(TmUrls);
  readonly #drafts = inject(TmDrafts);
  readonly #contexts = inject(TmPageContexts);

  /** The setup, which the routed host or the source page gives before anything reads. */
  #setup: TmDetailsSetup<T> | null = null;

  /** The page's source. */
  readonly source: Signal<TmDetailsSource<T>> = computed(() => this.#setupOf().source());

  /** The entity's descriptor. */
  readonly descriptor: Signal<TmEntityDescriptor> = computed(() => this.#entities.get(this.source().entity));

  /** Whether the host is the routed details page. */
  get routed(): boolean {
    return this.#setupOf().routed;
  }

  /** The routed record. */
  readonly #routed = computed<TmRouted<T>>(
    () => {
      const source = this.source();
      const id = source.id();
      const cloneSource = id === null ? this.#setupOf().cloneSource() : null;
      const clone = cloneSource === null ? null : cloneSource.id();
      return { key: `${source.entity}|${id ?? 'new'}|${clone ?? ''}`, id, clone, source, cloneSource };
    },
    { equal: (a, b) => a.key === b.key && a.source === b.source && a.cloneSource === b.cloneSource },
  );

  /** The refresh generation, and whether the refresh that set it is quiet, for the record it asked about. */
  readonly #generation = signal<{ readonly count: number; readonly quiet: boolean; readonly key: string }>({ count: 0, quiet: false, key: '' });

  /** The read's request: none for a new record that copies nothing. */
  readonly #params = computed<TmReadParams<T> | undefined>(() => {
    const routed = this.#routed();
    const generation = this.#generation();
    if (routed.id === null && routed.cloneSource === null) {
      return undefined;
    }
    return { routed, generation: generation.count, quiet: generation.quiet && generation.key === routed.key };
  });

  // Spec 0031 §3.3: one read keyed by the routed record and a refresh
  // generation; a newer key aborts the read in flight, so of fast steps only
  // the last lands, and leaving aborts it with the host.
  /** The read. */
  readonly #read = resource<TmReadResult<T>, TmReadParams<T> | undefined>({
    params: () => this.#params(),
    loader: async ({ params, abortSignal }) => {
      const reader = params.routed.cloneSource ?? params.routed.source;
      return { params, record: await reader.read(abortSignal) };
    },
  });

  /** The record on screen: the routed one, or the one held while the routed one loads. */
  readonly #record = signal<TmDetailsRecordState<T> | null>(null);

  /** The record on screen. */
  readonly record: Signal<TmDetailsRecordState<T> | null> = this.#record.asReadonly();

  /** The read result the record last took, so a result lands once. */
  #adopted: TmReadResult<T> | null = null;

  /** A read failure an edit already answered: the strip showed it, so it never replaces the record afterwards. */
  readonly #superseded = signal<unknown>(undefined);

  /** Whether the latest read failed and nothing has answered the failure yet. */
  readonly #readFailed = computed(() => this.#read.status() === 'error' && this.#read.error() !== this.#superseded());

  /** A refresh that failed under an edit, and the record it failed for. */
  readonly #refreshFailure = signal<{ readonly error: TmApiError; readonly record: TmDetailsRecordState<T> } | null>(null);

  /** Whether the page is gone, so the steps after an await stop there. */
  #destroyed = false;

  /** The layout's component, once loaded. */
  readonly #layout = signal<Type<unknown> | null>(null);

  /** The layout's component, once loaded. */
  readonly layout: Signal<Type<unknown> | null> = this.#layout.asReadonly();

  /** The layout's load failure, if any. */
  readonly #layoutFailure = signal<TmApiError | null>(null);

  /** Counts the layout's loads, so a superseded one is dropped. */
  #layoutRun = 0;

  /** The declaration's rules, once loaded with the layout; every record started after takes them. */
  #rules: TmEntityRules<T> | undefined = undefined;

  /** Whether a write is in flight. */
  readonly #busy = signal(false);

  /** Whether a write is in flight. */
  readonly busy: Signal<boolean> = this.#busy.asReadonly();

  /** The record's mode; `view` while no record shows. */
  readonly mode: Signal<TmDetailsMode> = computed(() => this.#record()?.mode() ?? 'view');

  /** The read's state: `loading`, `ready` or `error`. */
  readonly status: Signal<TmDetailsStatus> = computed(() => {
    const record = this.#record();
    const read = this.#read.status();
    if (this.#layoutFailure() !== null) {
      return 'error';
    }
    if (this.#readFailed()) {
      // A failed read under the edit of a landed record keeps the edit; any other shows the error state.
      return record !== null && record.landed() && record.mode() !== 'view' ? 'ready' : 'error';
    }
    if (read === 'error') {
      // A failure the edit answered: the record stays on screen.
      return record !== null && record.landed() && record.key === this.#routed().key ? 'ready' : 'loading';
    }
    if (read === 'loading' || read === 'reloading') {
      return 'loading';
    }
    return record !== null && record.landed() && record.key === this.#routed().key ? 'ready' : 'loading';
  });

  /** The read's failure, while the error state shows. */
  readonly error: Signal<TmApiError | null> = computed(() => {
    if (this.status() !== 'error') {
      return null;
    }
    const failure = this.#layoutFailure();
    if (failure !== null) {
      return failure;
    }
    return apiErrorOf(this.#read.error());
  });

  /** A refresh that failed while the record is edited, which the status strip shows; `null` otherwise. */
  readonly refreshFailure: Signal<TmApiError | null> = computed(() => {
    const failure = this.#refreshFailure();
    const record = this.#record();
    return failure !== null && failure.record === record && record.mode() !== 'view' && this.#read.status() === 'error' ? failure.error : null;
  });

  /** Whether a read runs. */
  readonly reading: Signal<boolean> = computed(() => this.#read.isLoading());

  /** Whether the running read shows its progress: not a quiet one. */
  readonly loud: Signal<boolean> = computed(() => this.reading() && this.#params()?.quiet !== true);

  /** Whether a record is held under a running read: a refresh, a step or a stale return, whose progress bar shows. */
  readonly holding: Signal<boolean> = computed(() => this.loud() && this.#record()?.landed() === true);

  /** The page's actions, as the page scope's context lists them. */
  readonly actions: Signal<readonly TmPageAction[]> = computed(() => this.#contexts.current()?.actions() ?? []);

  /** What the polite status region says. */
  readonly #announcement = signal('');

  /** What the polite status region says. */
  readonly announcement: Signal<string> = this.#announcement.asReadonly();

  /** Focuses the Edit button, once the frame renders it. */
  ɵfocusEdit: () => void = () => undefined;

  /** The server copy's display name, as a signal of parts, for the title. */
  readonly #recordName = computed(() => {
    const record = this.#record();
    const entity = record?.entity();
    return record === null || entity === undefined ? null : this.#names.parts(record.source.entity, entity as TmJsonObject);
  });

  /** The server copy's display name, as text. */
  readonly #recordText = computed(() => {
    const record = this.#record();
    const entity = record?.entity();
    return record === null || entity === undefined ? null : this.#names.of(record.source.entity, entity as TmJsonObject);
  });

  /** The routed record's cached display name, for a fresh open and the error state. */
  readonly #cachedName = computed(() => {
    const { id, source } = this.#routed();
    return id === null ? null : { parts: this.#names.labelParts(source.entity, id), text: this.#names.label(source.entity, id) };
  });

  // Spec 0031 §3.3 step 4, §3.2: the title names the displayed record, the
  // held one until the new one lands; a fresh open shows the cached name, or
  // a skeleton; create mode the new-record label; the error state the cached
  // name, else the entity's label.
  /** The header's title. */
  readonly title: Signal<TmDetailsTitle> = computed(() => {
    const source = this.source();
    const record = this.#record();
    if (source.titleKey !== undefined) {
      return { parts: null, text: this.#l10n.ɵtext(source.titleKey)() };
    }
    if (record?.mode() === 'create') {
      const summary = this.#entities.summary(source.entity);
      return { parts: null, text: summary ? this.#l10n.ɵtext(summary.newLabelKey, undefined, summary.labelFallback)() : source.entity };
    }
    const name = this.#recordName();
    if (name !== null && this.status() !== 'error') {
      return { parts: name(), text: this.#recordText()?.() ?? '' };
    }
    const cached = this.#cachedName();
    const parts = cached?.parts() ?? null;
    if (parts !== null) {
      return { parts, text: cached?.text() ?? '' };
    }
    return this.status() === 'error' ? { parts: null, text: this.#entities.label(source.entity)() } : { parts: null, text: '' };
  });

  /** Why Edit is disabled for a fact the user can act on, or `null`. */
  readonly editReason: Signal<string | null> = computed(() => {
    const source = this.source();
    if (!source.can.edit()) {
      return this.#reason(source);
    }
    const refusal = this.#record()?.refused()['save'];
    if (refusal === undefined) {
      return null;
    }
    if (refusal === 'outside-filter') {
      const [resource, action] = source.editSecurable ?? source.saveSecurable?.() ?? [source.entity, 'Save'];
      return this.#l10n.ɵtext('tm.details.outsideFilter', { permission: this.#permissions.label(resource, action)() })();
    }
    return this.#injector.get(TmServerText).message(refusal)();
  });

  /** Why New is disabled for a fact the user can act on, or `null`. */
  readonly createReason: Signal<string | null> = computed(() => {
    const source = this.source();
    return source.can.create() ? null : this.#reason(source);
  });

  /** Registers the page's strings and sets up the host's effects. */
  constructor() {
    this.#l10n.ɵregisterStrings('details', TM_DETAILS_STRINGS_EN);

    // --- The record on screen follows the routed one ---
    // A read over a landed record in view mode holds it until the new one
    // lands; with nothing to hold, the routed record starts at once, empty
    // until its read lands. A failed read leaves no record, unless it failed
    // under the edit of a landed one: the error state shows instead.
    effect(() => {
      const routed = this.#routed();
      const read = this.#read.status();
      const failed = this.#layoutFailure() !== null || this.#readFailed();
      untracked(() => {
        const current = this.#record();
        if (failed) {
          if (current !== null && (current.mode() === 'view' || !current.landed())) {
            this.#replace(null);
          }
          return;
        }
        if (current?.key === routed.key) {
          return;
        }
        if (current !== null && current.landed() && current.mode() === 'view' && read === 'loading') {
          return;
        }
        this.#replace(this.#start(routed, routed.cloneSource === null && routed.id === null));
      });
    });

    // --- A read's record lands under its own key ---
    effect(() => {
      const snapshot = this.#read.snapshot();
      if (snapshot.status !== 'resolved' || snapshot.value === undefined || snapshot.value === this.#adopted) {
        return;
      }
      const result = snapshot.value;
      this.#adopted = result;
      untracked(() => this.#land(result));
    });

    // --- A refresh that fails under an edit keeps the edit and shows in the status strip ---
    effect(() => {
      const failed = this.#readFailed();
      untracked(() => {
        const record = this.#record();
        if (!failed || record === null || !record.landed() || record.mode() === 'view') {
          return;
        }
        const error = this.#read.error();
        this.#superseded.set(error);
        this.#refreshFailure.set({ error: apiErrorOf(error), record });
      });
    });

    // --- A tenant that is gone or unavailable leaves the page ---
    effect(() => {
      const error = this.error();
      if (error?.kind === 'tenant-not-found' || error?.kind === 'tenant-unavailable') {
        untracked(() => void this.#present(error, 'read'));
      }
    });

    // --- The layout, loaded beside the read ---
    effect(() => {
      const layout = this.#setupOf().layout();
      untracked(() => void this.#loadLayout(layout));
    });

    inject(DestroyRef).onDestroy(() => {
      this.#destroyed = true;
      this.#layoutRun++;
      this.#record()?.dispose();
    });
  }

  /**
   * Sets the host up. Internal: the routed host or the source page calls it in its constructor, before anything reads.
   *
   * @param setup - The source, the copied record's source, the layout, and whether the host is routed.
   */
  ɵsetup(setup: TmDetailsSetup<T>): void {
    this.#setup = setup;
  }

  /** Enters edit mode: no copy and no request; focus goes to the first editable field. */
  edit(): void {
    const record = this.#record();
    if (record === null || record.mode() !== 'view' || this.status() !== 'ready' || !record.can.edit()) {
      return;
    }
    // Spec 0031 §3.4, §4.2: the base is the server copy itself; the first write copies the root.
    record.base.set(record.entity() ?? null);
    record.mode.set('edit');
    this.#announce(this.#l10n.ɵtext('tm.details.editing', { name: this.title().text })());
    afterNextRender(
      () => {
        for (const field of record.fields().values()) {
          if (field.editable()) {
            field.focus();
            return;
          }
        }
      },
      { injector: this.#injector },
    );
  }

  /**
   * Leaves edit mode: asks first when there are changes, then aborts the
   * edit's uploads and drops their staged files, resets the working copy to
   * the base, clears the draft and focuses Edit. In create mode it leaves
   * the page instead: Back, else the search page. While Save runs, it
   * refuses.
   *
   * @returns `false` when the user kept editing, or Save is running.
   */
  async cancel(): Promise<boolean> {
    const record = this.#record();
    if (record === null || record.mode() === 'view') {
      return true;
    }
    // Save owns the record while it waits for uploads and writes; a hatch's cancel() then changes nothing.
    if (this.#busy()) {
      return false;
    }
    if (record.dirty() && !(await this.#discard())) {
      return false;
    }
    // A page left, or a record replaced, while the question was open has nothing left to cancel.
    if (this.#destroyed || this.#record() !== record) {
      return true;
    }
    // --- The edit's uploads are discarded with it: none finishing later writes into the restored record ---
    record.uploads.cancelAll();
    this.#clearDraft(record);
    if (record.mode() === 'create') {
      await this.#leave(record);
      return true;
    }
    record.model.set(record.base() ?? record.model());
    record.base.set(null);
    record.mode.set('view');
    this.#afterRender(() => this.ɵfocusEdit());
    return true;
  }

  /**
   * Saves the record: a clean edit with no upload in flight returns to view
   * mode and sends nothing; otherwise, once the record's uploads have
   * finished, one write through the source, whose read-back becomes the
   * server copy. While a file's upload has failed, or when one Save waited
   * for fails, nothing is written: the edit stays, the failure shows on its
   * field, and the page says why. A create lands on the new record's page.
   * A failure keeps the edit and shows the error. Uploads still running
   * when the write lands abort.
   *
   * @returns `true` once saved and back in view mode.
   */
  async save(): Promise<boolean> {
    const record = this.#record();
    if (record === null || record.mode() === 'view' || !record.landed() || this.#busy()) {
      return false;
    }
    // --- A file whose upload failed holds Save until it is retried or removed: nothing is written without it ---
    if (untracked(record.uploads.failed).size > 0) {
      this.#announce(this.#l10n.ɵtext('tm.details.uploadsFailed')());
      return false;
    }
    // An upload in flight is a change still arriving: its field takes the staged id only once it lands.
    if (record.mode() === 'edit' && !record.dirty() && untracked(record.uploads.running).size === 0) {
      this.#leaveClean(record);
      return true;
    }
    const mode = record.mode();
    const failures = untracked(record.uploads.failures);
    this.#busy.set(true);
    try {
      // --- The uploads finish first, so the body carries their staged ids (spec 0031 §8.1 step 2) ---
      if (!(await this.#uploadsSettled(record))) {
        return false;
      }
      // Cancel refuses while Save waits, but a record whose mode changed meanwhile has nothing of this Save to write.
      if (record.mode() !== mode) {
        return false;
      }
      // An upload Save waited for failed, even one its editor dismissed as refused: the edit stays,
      // showing the failure on its field. Spec 0031 §8.1 step 2 does not say; this keeps the user's file.
      if (untracked(record.uploads.failures) !== failures || untracked(record.uploads.failed).size > 0) {
        this.#announce(this.#l10n.ɵtext('tm.details.uploadsFailed')());
        return false;
      }
      // An upload that landed with nothing else changed, and whose field took no new id, leaves nothing to write.
      if (mode === 'edit' && !record.dirty()) {
        this.#leaveClean(record);
        return true;
      }
      const saved = await record.source.write({
        model: record.ɵsaveBody(),
        base: mode === 'create' ? null : record.base(),
        concurrency: 'Check',
        confirmations: [],
      });
      this.#remember(record.source.entity, saved);
      this.#clearDraft(record);
      // An upload started while the write ran belongs to nothing saved: it aborts, so no late result writes into the saved record.
      record.uploads.cancelAll();
      // A page left while the write ran shows nothing of it.
      if (this.#destroyed) {
        return true;
      }
      if (mode === 'create' && this.routed) {
        // The new record's own page shows it; Back skips the create page.
        const descriptor = untracked(this.descriptor);
        const key = descriptor.property(descriptor.key.property)?.json ?? 'id';
        const id = (saved.entity as TmJsonObject)[key] as TmId;
        await this.#environment.get(Router).navigateByUrl(this.#urls.details(record.source.entity, id), { replaceUrl: true });
        return true;
      }
      record.mode.set('view');
      record.land(saved);
      this.#announce(this.#l10n.ɵtext('tm.details.saved')());
      this.#afterRender(() => this.ɵfocusEdit());
      return true;
    } catch (error) {
      if (error instanceof TmApiError) {
        await this.#present(error, 'save');
        return false;
      }
      throw error;
    } finally {
      if (!this.#destroyed) {
        this.#busy.set(false);
      }
    }
  }

  /**
   * Reads the record again: in view mode the whole record, in edit mode its
   * related rows and extras only; a new record has nothing to read again.
   * After a failed layout load, loads the layout again too.
   *
   * @param options - `quiet` shows no progress bar and no busy state.
   */
  refresh(options?: { readonly quiet?: boolean }): void {
    if (this.#layoutFailure() !== null) {
      this.#layoutFailure.set(null);
      void this.#loadLayout(untracked(this.#setupOf().layout));
    }
    const routed = untracked(this.#routed);
    if (routed.id === null && untracked(this.#read.status) !== 'error') {
      return;
    }
    this.#generation.update((generation) => ({ count: generation.count + 1, quiet: options?.quiet === true, key: routed.key }));
  }

  /**
   * Writes the working copy in code, in edit and create mode; the record turns dirty.
   *
   * @param recipe - The next working copy from the current one.
   * @throws Error in development, in view mode.
   */
  update(recipe: (model: T) => T): void {
    const record = this.#record();
    if (record === null || record.mode() === 'view') {
      if (typeof ngDevMode !== 'undefined' && ngDevMode) {
        throw new Error('A details record takes update() in edit and create mode only: call edit() first.');
      }
      return;
    }
    record.model.update(recipe);
  }

  /**
   * Starts the routed record: a new record's model, or `{}` until its read lands.
   *
   * @param routed - The routed record.
   * @param landed - Whether its data is in hand: a new record that copies nothing.
   * @returns The record.
   */
  #start(routed: TmRouted<T>, landed: boolean): TmDetailsRecordState<T> {
    const descriptor = untracked(this.descriptor);
    const model = (routed.id === null && landed ? tmDetailsCreateModel(descriptor) : {}) as T;
    const record = new TmDetailsRecordState<T>({
      key: routed.key,
      id: routed.id,
      mode: routed.id === null ? 'create' : 'view',
      model,
      landed,
      source: routed.source,
      descriptor,
      host: this,
      parent: this.#injector,
      entities: this.#entities,
      rules: this.#rules,
    });
    if (routed.id === null) {
      record.base.set(model);
    }
    return record;
  }

  /**
   * Lands a read's record: on the record on screen when it is the same one,
   * otherwise on a new record that replaces the held one. A copied record
   * becomes the new record's stripped copy.
   *
   * @param result - The read's result.
   */
  #land(result: TmReadResult<T>): void {
    const { routed } = result.params;
    this.#remember(routed.cloneSource?.entity ?? routed.source.entity, result.record);
    let record = this.#record();
    if (record?.key !== routed.key) {
      record = this.#start(routed, false);
      this.#replace(record);
    }
    if (routed.cloneSource !== null) {
      // The copy starts the new record once: a later read of the source never replaces its edits.
      if (record.landed()) {
        return;
      }
      const descriptor = untracked(this.descriptor);
      const copy = tmDetailsCloneStrip(result.record.entity as TmJsonObject, descriptor, (entity) => this.#entities.get(entity));
      record.start(copy as T);
      return;
    }
    record.land(result.record);
  }

  /**
   * Puts a record on screen and ends the one before.
   *
   * @param record - The record, or `null` for none.
   */
  #replace(record: TmDetailsRecordState<T> | null): void {
    const previous = this.#record();
    this.#record.set(record);
    previous?.dispose();
  }

  /**
   * Feeds the display names with a read's or a save's record and related rows.
   *
   * @param entity - The record's entity.
   * @param record - The record.
   */
  #remember(entity: string, record: TmDetailsRecord<T>): void {
    this.#names.rememberRelated(record.related ?? {});
    this.#names.remember(entity, [record.entity as TmJsonObject]);
  }

  /**
   * Loads the layout's component and, beside it, the declaration's rules:
   * the layout renders once both are in hand, after the record on screen
   * has taken the rules, and a failure of either is the layout's.
   *
   * @param layout - The component, or its loader.
   */
  async #loadLayout(layout: TmLazyComponent | Type<unknown>): Promise<void> {
    const run = ++this.#layoutRun;
    const rulesLoader = untracked(this.descriptor).declaration['validate'] as
      | (() => Promise<TmEntityRules<T> | { readonly default: TmEntityRules<T> }>)
      | undefined;
    const component = reflectComponentType(layout as Type<unknown>) !== null ? (layout as Type<unknown>) : null;
    // With no rules to wait for, a component renders at once.
    if (component !== null && rulesLoader === undefined) {
      this.#layout.set(component);
      return;
    }
    try {
      const [loaded, rules] = await Promise.all([component ?? (layout as TmLazyComponent)(), rulesLoader?.()]);
      if (run === this.#layoutRun) {
        this.#rules = typeof rules === 'function' ? rules : rules?.default;
        if (this.#rules !== undefined) {
          this.#record()?.ɵrules(this.#rules);
        }
        this.#layout.set(typeof loaded === 'function' ? loaded : loaded.default);
      }
    } catch {
      if (run === this.#layoutRun) {
        this.#layoutFailure.set(LAYOUT_FAILED());
      }
    }
  }

  /**
   * Why an ability the source refuses is refused: an entity's from its
   * permissions; a settings source's from the tenant's state, else the
   * permission Edit names.
   *
   * @param source - The source.
   * @returns The reason, or `null` for a transient cause.
   */
  #reason(source: TmDetailsSource<T>): string | null {
    if (source.kind === 'details') {
      return this.#permissions.disabledReason(source.entity, 'save')();
    }
    // Spec 0031 §10.1: a source page fills the same strings itself.
    const launch = this.#injector.get(TmLaunchCache);
    const state = launch.docs.tenant()?.state;
    if (state === 'ReadOnly' || state === 'Frozen') {
      const names = launch.docs.settings()?.names;
      const tenant = names?.[this.#l10n.contentSlot() - 1] ?? names?.[0] ?? '';
      return this.#l10n.ɵtext(state === 'Frozen' ? 'tm.entity.tenantFrozen' : 'tm.entity.tenantReadOnly', { tenant })();
    }
    const securable = source.editSecurable ?? source.saveSecurable?.();
    return securable
      ? this.#l10n.ɵtext('tm.entity.permissionNeeded', { permission: this.#permissions.label(securable[0], securable[1])() })()
      : null;
  }

  /**
   * Asks whether to discard the changes.
   *
   * @returns Whether the user chose to discard them.
   */
  async #discard(): Promise<boolean> {
    const text = (key: string): string => this.#l10n.ɵtext(`tm.details.discard.${key}`)();
    const answer = await this.#injector.get(TmConfirm).ask<'discard' | 'keep'>({
      title: text('title'),
      message: text('message'),
      choices: [
        { key: 'discard', label: text('confirm'), variant: 'ghost' },
        { key: 'keep', label: text('keep'), variant: 'primary' },
      ],
      initialFocus: 'keep',
    });
    return answer === 'discard';
  }

  /**
   * Leaves a new record's page: Back when the app opened another page
   * before it, else the entity's search page.
   *
   * @param record - The new record.
   */
  async #leave(record: TmDetailsRecordState<T>): Promise<void> {
    const location = this.#environment.get(Location);
    // The router numbers its navigations in the history state; the app's first one is 1.
    const state = location.getState() as { readonly navigationId?: number } | null;
    if ((state?.navigationId ?? 1) > 1) {
      location.back();
      return;
    }
    await this.#environment.get(Router).navigateByUrl(this.#urls.search(record.source.entity));
  }

  /**
   * Leaves an edit with no changes: back to view mode, sending nothing, with focus on Edit.
   *
   * @param record - The record.
   */
  #leaveClean(record: TmDetailsRecordState<T>): void {
    record.base.set(null);
    record.mode.set('view');
    this.#afterRender(() => this.ɵfocusEdit());
  }

  /**
   * Clears the record's draft.
   *
   * @param record - The record.
   */
  #clearDraft(record: TmDetailsRecordState<T>): void {
    const key = untracked(record.source.draftKey);
    if (key !== null) {
      this.#drafts.clear(key);
    }
  }

  /**
   * Shows an error through the error presenter, loaded on first use.
   *
   * @param error - The error.
   * @param kind - What failed.
   */
  async #present(error: TmApiError, kind: 'read' | 'save'): Promise<void> {
    const { TmErrorPresenter } = await import('@tellma/core/errors');
    await this.#environment.get(TmErrorPresenter).present(error, { kind, entity: untracked(this.source).entity });
  }

  /**
   * Waits until no upload of the record runs: each one has staged its blob
   * or failed on its field.
   *
   * @param record - The record.
   * @returns `true` once none runs; `false` when the record or the page went away first.
   */
  #uploadsSettled(record: TmDetailsRecordState<T>): Promise<boolean> {
    if (untracked(record.uploads.running).size === 0) {
      return Promise.resolve(true);
    }
    return new Promise((resolve) => {
      const watch = effect(
        () => {
          if (record.uploads.running().size === 0) {
            watch.destroy();
            resolve(!this.#destroyed && untracked(this.#record) === record);
          }
        },
        { injector: record.injector, manualCleanup: true },
      );
      record.injector.get(DestroyRef).onDestroy(() => {
        watch.destroy();
        resolve(false);
      });
    });
  }

  /**
   * Says something in the polite status region.
   *
   * @param message - What to say.
   */
  #announce(message: string): void {
    // A repeated message is announced again: the region empties first.
    this.#announcement.set('');
    queueMicrotask(() => this.#announcement.set(message));
  }

  /**
   * Runs work once the next render has finished.
   *
   * @param work - The work.
   */
  #afterRender(work: () => void): void {
    afterNextRender(work, { injector: this.#injector });
  }

  /**
   * The setup.
   *
   * @returns The setup.
   * @throws Error before the host has given it.
   */
  #setupOf(): TmDetailsSetup<T> {
    if (this.#setup === null) {
      throw new Error('A details page read its setup before its host gave it.');
    }
    return this.#setup;
  }
}
