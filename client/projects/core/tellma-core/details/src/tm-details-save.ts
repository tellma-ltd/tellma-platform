// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import { afterNextRender, computed, DestroyRef, DOCUMENT, effect, type Injector, type Signal, signal, untracked, type Type } from '@angular/core';
import { type FieldTree, submit, type ValidationError } from '@angular/forms/signals';
import { TM_UI_TRANSLATE, tmResolveFieldErrors } from '@tellma/core-ui';
import { TmBusyOverlay } from '@tellma/core-ui/busy-overlay';
import {
  TmApiError,
  type TmDetailsDeclaration,
  type TmDetailsSnapshot,
  type TmEntityDescriptor,
  TmEntities,
  type TmId,
  type TmJsonObject,
  type TmLazyComponent,
  TmLocalization,
  type TmProblemError,
  TmServerText,
  type TmStringTree,
  TmTelemetry,
  TmUrls,
} from '@tellma/core';
import { TmConfirmations } from '@tellma/core/entity';
import { tmSplitErrorPath, tmWalkErrorPath, type TmErrorPathSegment } from '@tellma/core/error-paths';
import { TmPageState, tmPageStateKey } from '@tellma/core/memory';

import { type TmConflictInfo, TmDetailsDialogs } from './tm-details-dialogs';
import type { TmDetailsCollectionPlace, TmDetailsRecordState } from './tm-details-record';
import { type TmDetailsServerError, ɵtmDetailsNodeAt } from './tm-details-schema';
import type { TmDetailsRecord } from './tm-details-source';
import { TmDetailsSummary, type TmDetailsSummaryItem, TmDetailsSummaryState } from './tm-details-summary';

// The save flow (spec 0031 §8, §9.1–§9.3), loaded when a record enters edit
// or create mode, so the route's first frame carries none of it. One Save
// waits for the record's uploads, checks the form on the client, builds the
// body and runs one blocking write under the busy overlay, through the
// confirmation loop; then it answers every outcome: the saved record, the
// error summary with each error on its field, the concurrency and deletion
// dialogs, or the error presenter.

/** The save flow's English strings, merged under `tm.detailsSave` on first use; a language pack carries the same tree. */
export const TM_DETAILS_SAVE_STRINGS_EN = {
  finishing: 'Finishing uploads…',
  saving: 'Saving…',
  heading: 'Fix {count, plural, one {# problem} other {# problems}} to save',
  show: 'Show problems',
  more: 'and {count} more',
  fixed: 'Fixed',
  field: '{label}: {message}',
  cell: '{collection} · row {row} · {column}: {message}',
  row: '{collection} · row {row}: {message}',
  uploadFailed: "The file didn't upload. Try it again or remove it.",
} as const satisfies TmStringTree;

/** The kind of a recorded server or source error, which the summary tells apart from a client rule's. */
const SERVER = 'tm.server';

/** The summary the host's status strip renders: the component, and its inputs. */
export interface TmDetailsSummaryView {
  /** The summary's component. */
  readonly component: Type<unknown>;
  /** Its inputs. */
  readonly inputs: Readonly<Record<string, unknown>>;
}

/** What the save flow reads from its host and asks of it. Internal: the details host builds it. */
export interface TmDetailsSaveHost<T> {
  /** The app's injector, which outlives the page. */
  readonly injector: Injector;
  /** Whether the host is the routed details page. */
  readonly routed: boolean;
  /** Whether the record a create copies is another one. */
  readonly cloning: boolean;
  /** The error summary the status strip shows. */
  readonly summary: { set(view: TmDetailsSummaryView | null): void };
  /** Whether the page is gone. */
  destroyed(): boolean;
  /** The record on screen. */
  current(): TmDetailsRecordState<T> | null;
  /** Says something in the polite status region. */
  announce(message: string): void;
  /** Focuses Edit once the next render has finished. */
  focusEdit(): void;
  /** Clears the record's draft. */
  clearDraft(record: TmDetailsRecordState<T>): void;
  /** Feeds the display names with a saved or read record. */
  remember(entity: string, record: TmDetailsRecord<T>): void;
  /** Shows an error through the error presenter; `retry` re-runs the save. */
  present(error: TmApiError, retry?: () => void): void;
  /** Navigates as the host's own navigation, which the leave guard never asks about. */
  navigate(url: string, extras: { readonly replaceUrl: boolean; readonly state?: Readonly<Record<string, unknown>> }): Promise<unknown>;
  /** Returns a clean edit to view mode, sending nothing. */
  leaveClean(record: TmDetailsRecordState<T>): void;
  /** Saves again, as Try again does. */
  save(): Promise<boolean>;
}

/** How a save ended, as `tm.details.save` records it. */
type TmSaveOutcome = 'ok' | 'client-invalid' | 'validation' | 'conflict' | 'declined' | 'deleted' | 'step-up' | 'error';

/** One problem a failed save found, before the summary words it: where it sits, and its message. */
interface TmSaveProblem {
  /** Its node's path from the record: json names and row indices; empty for the record. */
  readonly json: readonly TmErrorPathSegment[];
  /** The same path by member names, as the page registers fields: `['Lines', 1, 'Amount']`. */
  readonly names: readonly TmErrorPathSegment[];
  /** Its message. */
  readonly message: string;
  /** Whether a field or a cell shows it: its whole path resolved. */
  readonly resolved: boolean;
  /** Whether its error has cleared since the save. */
  readonly fixed: Signal<boolean>;
}

/** The kinds a 5xx dialog offers Try again for, which re-runs the save. */
const RETRIED: ReadonlySet<string> = new Set(['unavailable', 'server']);

/**
 * Recalls the tab's fresh snapshot of a record's details page. Internal: the
 * details host reads it through this chunk because only a Save remembers a
 * snapshot, so the page state stays off the route until one has run.
 *
 * @param injector - An injector reaching the app's page state.
 * @param path - The record's details path.
 * @returns The snapshot, or `undefined` when the tab holds none or a stale one.
 */
export function ɵtmRecallDetailsSnapshot(injector: Injector, path: string): TmDetailsSnapshot | undefined {
  const pageState = injector.get(TmPageState);
  const key = tmPageStateKey(path);
  return pageState.isStale(key) ? undefined : pageState.recall<TmDetailsSnapshot>(key);
}

/**
 * Runs one Save of a record. Internal: the details host calls it with its
 * busy state already set, and clears that state once it settles.
 *
 * @typeParam T - The record's type.
 * @param host - The host.
 * @param record - The record.
 * @returns `true` once saved, or once a clean edit is back in view mode.
 */
export function ɵtmDetailsSave<T>(host: TmDetailsSaveHost<T>, record: TmDetailsRecordState<T>): Promise<boolean> {
  return new TmDetailsSaveRun(host, record).run();
}

/** One Save, from the click to its outcome. */
class TmDetailsSaveRun<T> {
  readonly #host: TmDetailsSaveHost<T>;
  readonly #record: TmDetailsRecordState<T>;
  readonly #injector: Injector;
  readonly #l10n: TmLocalization;
  readonly #overlay: TmBusyOverlay;

  /** When the Save began, for its timing. */
  readonly #started = performance.now();

  /** The record's mode when the Save began. */
  readonly #mode: 'edit' | 'create';

  /** The entity's descriptor. */
  readonly #descriptor: TmEntityDescriptor;

  /** Ends the overlay run that spans the upload wait; does nothing while none is held. */
  #release: () => void = () => undefined;

  /**
   * Starts a Save.
   *
   * @param host - The host.
   * @param record - The record.
   */
  constructor(host: TmDetailsSaveHost<T>, record: TmDetailsRecordState<T>) {
    this.#host = host;
    this.#record = record;
    this.#injector = host.injector;
    this.#l10n = host.injector.get(TmLocalization);
    this.#l10n.ɵregisterStrings('detailsSave', TM_DETAILS_SAVE_STRINGS_EN);
    this.#overlay = host.injector.get(TmBusyOverlay);
    this.#mode = untracked(record.mode) === 'create' ? 'create' : 'edit';
    this.#descriptor = record.descriptor;
  }

  /**
   * Runs the Save's steps.
   *
   * @returns `true` once saved, or once a clean edit is back in view mode.
   */
  async run(): Promise<boolean> {
    try {
      return await this.#steps();
    } finally {
      // Whatever way the Save ends, the overlay run that spans its upload wait ends with it.
      this.#release();
    }
  }

  /**
   * The Save's steps, from the upload check to the write's outcome.
   *
   * @returns `true` once saved, or once a clean edit is back in view mode.
   */
  async #steps(): Promise<boolean> {
    const record = this.#record;
    const uploads = record.uploads;
    // Spec 0031 §8.5: every server error, and the summary, clear on the next Save, whichever way it ends.
    this.#clear();
    // --- A file whose upload failed holds the write: the edit stays, and the summary points at the file ---
    if (untracked(uploads.failed).size > 0) {
      return this.#uploadFailed();
    }
    // --- A clean edit returns to view mode and sends nothing; an upload in flight is a change still arriving ---
    const running = untracked(uploads.running).size > 0;
    if (this.#mode === 'edit' && !untracked(record.dirty) && !running) {
      this.#host.leaveClean(record);
      return true;
    }

    // --- Step 2: the uploads finish first, so the body carries their staged ids ---
    if (running) {
      // One overlay run spans the wait, the client's checks and the write's start, so the page stays
      // inert and the scrim stays on between them; its label moves on to the write's once the wait ends.
      const label = signal(this.#text('finishing'));
      this.#hold(label);
      const failures = untracked(uploads.failures);
      const settled = await this.#uploadsSettled();
      // Cancel refuses while Save waits, but a record whose mode changed meanwhile has nothing of this Save to write.
      if (!settled || untracked(record.mode) !== this.#mode) {
        return false;
      }
      // An upload Save waited for failed, even one its editor dismissed as refused: the edit stays.
      if (untracked(uploads.failures) !== failures || untracked(uploads.failed).size > 0) {
        return this.#uploadFailed();
      }
      // An upload that landed with nothing else changed leaves nothing to write.
      if (this.#mode === 'edit' && !untracked(record.dirty)) {
        this.#release();
        this.#host.leaveClean(record);
        return true;
      }
      label.set(this.#text('saving'));
    }

    // --- Step 3: the client's checks; any error opens the summary and nothing is sent ---
    const source = record.source;
    const problems = source.validate?.(untracked(record.model)) ?? [];
    const placed = this.#place(problems.map((problem) => ({ ...problem, message: '' }) as TmProblemError));
    let valid = false;
    await submit(record.form as FieldTree<unknown>, { action: () => ((valid = true), Promise.resolve(undefined)) });
    if (!valid) {
      const found = [...placed, ...this.#clientProblems()];
      // The summary opens on a live page: the run that spanned the upload wait ends first.
      this.#release();
      await this.#open(found);
      return this.#done('client-invalid', found.length);
    }

    // --- Step 4: the payload, on a copy: beforeSave changes values only, so each index into the body matches the model ---
    const beforeSave = this.#descriptor.declaration['beforeSave'] as ((entity: T) => T | Promise<T>) | undefined;
    let body = record.ɵsaveBody();
    if (beforeSave !== undefined) {
      body = await beforeSave(structuredClone(body));
    }
    return this.#write(this.#stamp(body), 'Check', [], false);
  }

  /**
   * Step 5: one write through the confirmation loop, under the overlay, and its outcome.
   *
   * @param body - The record as sent.
   * @param concurrency - `Check`, or `Override` after Overwrite.
   * @param confirmed - The warnings' keys confirmed so far in this Save.
   * @param immediate - Whether the overlay's scrim shows at once: its trigger, a dialog, has closed.
   * @returns `true` once saved.
   */
  async #write(body: T, concurrency: 'Check' | 'Override', confirmed: readonly string[], immediate: boolean): Promise<boolean> {
    const record = this.#record;
    const source = record.source;
    const base = this.#mode === 'create' ? null : untracked(record.base);
    // The keys of the attempt that ended the loop: a conflict's Overwrite resends them (spec 0031 §9.3).
    let keys = confirmed;
    const send = this.#injector.get(TmConfirmations).run(
      (attempt) => {
        keys = attempt.confirmations;
        return source.write({ model: body, base, concurrency, confirmations: attempt.confirmations });
      },
      { entity: source.entity, confirmations: confirmed },
    );
    // Spec 0031 §8.7: the browser asks before leaving only while the write is in flight.
    const view = this.#injector.get(DOCUMENT).defaultView;
    const unload = (event: BeforeUnloadEvent): void => event.preventDefault();
    view?.addEventListener('beforeunload', unload);
    let saved: TmDetailsRecord<T> | null;
    try {
      const blocked = this.#overlay.run(send, { label: this.#text('saving'), immediate });
      // The write's own run has begun, so the run that spanned the upload wait ends with no gap.
      this.#release();
      saved = await blocked;
    } catch (error) {
      if (!(error instanceof TmApiError)) {
        throw error;
      }
      return this.#failed(error, body, keys);
    } finally {
      view?.removeEventListener('beforeunload', unload);
    }
    if (saved === null) {
      // Cancel in the confirmation dialog: edit mode stays, and nothing more is sent.
      return this.#done('declined');
    }
    return this.#saved(saved);
  }

  /**
   * Answers a failed write.
   *
   * @param error - The failure.
   * @param body - The record as sent, which Overwrite sends again.
   * @param keys - The warnings' keys confirmed so far.
   * @returns `true` once an Overwrite saved.
   */
  async #failed(error: TmApiError, body: T, keys: readonly string[]): Promise<boolean> {
    const host = this.#host;
    if (error.kind === 'conflict' && error.code === 'concurrency-conflict') {
      return this.#conflict(error, body, keys);
    }
    if (error.kind === 'not-found' && this.#mode === 'edit') {
      const choice = await this.#injector.get(TmDetailsDialogs).deleted();
      if (choice === 'discard' && this.#live()) {
        await this.#discard();
      }
      return this.#done('deleted');
    }
    if (error.kind === 'validation') {
      const items = error.problem?.['errors'];
      const found = this.#place(Array.isArray(items) ? (items as readonly TmProblemError[]) : []);
      await this.#open(found);
      return this.#done('validation', found.length);
    }
    // Spec 0031 §8.6: an outcome the server may have committed offers Try again, which saves again and meets its own stamp.
    const retry = RETRIED.has(error.kind) || (error.kind === 'offline' && error.code === 'outcome-unknown') ? () => void host.save() : undefined;
    host.present(error, retry);
    return this.#done(error.kind === 'step-up' ? 'step-up' : 'error');
  }

  /**
   * The concurrency dialog and its choice: Overwrite sends
   * the same body again with `Override` and the keys confirmed so far;
   * Reload takes the saved version; dismissing keeps the edit.
   *
   * @param error - The conflict.
   * @param body - The record as sent.
   * @param keys - The warnings' keys confirmed so far.
   * @returns `true` once an Overwrite saved.
   */
  async #conflict(error: TmApiError, body: T, keys: readonly string[]): Promise<boolean> {
    const details = error.problem?.['errorDetails'] as { readonly conflicts?: readonly TmConflictInfo[] } | undefined;
    const info = details?.conflicts?.[0] ?? null;
    // The dialog records the choice, and whether its copy named the current user.
    const choice = await this.#injector.get(TmDetailsDialogs).conflict(info);
    if (!this.#live()) {
      return this.#done('conflict');
    }
    if (choice === 'overwrite') {
      // The dialog that triggered the write has closed: the scrim shows at once.
      return this.#write(body, 'Override', keys, true);
    }
    if (choice === 'reload') {
      await this.#reload();
    }
    return this.#done('conflict');
  }

  /**
   * Reload in the concurrency dialog: reads the record under the overlay,
   * then shows it in view mode, the draft and the edit's uploads discarded.
   * A failed read keeps the edit and shows why.
   */
  async #reload(): Promise<void> {
    const record = this.#record;
    let fresh: TmDetailsRecord<T>;
    try {
      fresh = await this.#overlay.run(record.source.read(new AbortController().signal), { immediate: true });
    } catch (error) {
      if (error instanceof TmApiError) {
        this.#host.present(error);
        return;
      }
      throw error;
    }
    if (!this.#live()) {
      return;
    }
    this.#host.remember(record.source.entity, fresh);
    this.#settle();
    this.#adopt(fresh);
  }

  /** Discard changes in the deletion dialog: the draft and the snapshot go, and the search page replaces the record's URL. */
  async #discard(): Promise<void> {
    const record = this.#record;
    const host = this.#host;
    this.#settle();
    const entity = record.source.entity;
    const urls = this.#injector.get(TmUrls);
    const id = untracked(record.id);
    if (id !== null) {
      this.#injector.get(TmPageState).forget(tmPageStateKey(urls.details(entity, id)));
    }
    if (host.routed) {
      await host.navigate(urls.search(entity), { replaceUrl: true });
    } else {
      // A page that is not the record's own has no search page to go to: the edit ends in place.
      record.model.set(untracked(record.base) ?? untracked(record.model));
      host.leaveClean(record);
    }
  }

  /**
   * A write's success: the saved record becomes the server
   * copy in view mode, or, for a create on the routed page, the new record's
   * page replaces the create page, rendering the saved copy with no request.
   *
   * @param saved - The committed read-back.
   * @returns `true`.
   */
  async #saved(saved: TmDetailsRecord<T>): Promise<boolean> {
    const record = this.#record;
    const host = this.#host;
    const entity = record.source.entity;
    host.remember(entity, saved);
    this.#settle();
    // --- Remembered lists refetch on return ---
    const urls = this.#injector.get(TmUrls);
    const pageState = this.#injector.get(TmPageState);
    const tenantId = urls.tenantId();
    if (tenantId !== null) {
      pageState.markStale({ tenantId, entity });
    }
    this.#done('ok');
    // A page left while the write ran shows nothing of it.
    if (host.destroyed()) {
      return true;
    }
    if (this.#mode === 'create' && host.routed) {
      // --- The new record's page, remembered first so it renders the saved copy with no request; Back skips the create page ---
      const descriptor = this.#descriptor;
      const key = descriptor.property(descriptor.key.property)?.json ?? 'id';
      const id = (saved.entity as TmJsonObject)[key] as TmId;
      const details = descriptor.declaration['details'] as TmDetailsDeclaration<string> | TmLazyComponent | undefined;
      const createdUrl = typeof details === 'object' ? details.createdUrl : undefined;
      const target = createdUrl === undefined ? urls.details(entity, id) : createdUrl(saved.entity as never, urls);
      const snapshot: TmDetailsSnapshot = {
        entity: saved.entity as TmJsonObject,
        related: saved.related ?? {},
        extras: saved.extras ?? {},
        tabs: untracked(record.view.tabs),
        cards: untracked(record.view.cards),
        scroll: { body: 0, aside: 0, panels: {} },
      };
      pageState.remember(tmPageStateKey(target), snapshot);
      // The new page says Saved and focuses Edit itself, so the router's focus move to the title stands aside.
      await host.navigate(target, { replaceUrl: true, state: { tmSaved: true, tmPageFocus: false } });
      return true;
    }
    this.#adopt(saved);
    host.announce(this.#l10n.ɵtext('tm.details.saved')());
    return true;
  }

  /**
   * Ends the edit's side state: the draft clears, the recorded errors and
   * the summary go, and an upload still running aborts, since it belongs to
   * nothing saved, so no late result writes into the record.
   */
  #settle(): void {
    this.#host.clearDraft(this.#record);
    this.#record.uploads.cancelAll();
    this.#clear();
  }

  /**
   * Shows a record as the server copy in view mode: the form resets to it, and focus goes to Edit.
   *
   * @param fresh - The saved or re-read record.
   */
  #adopt(fresh: TmDetailsRecord<T>): void {
    const record = this.#record;
    record.mode.set('view');
    record.land(fresh);
    record.form().reset();
    this.#host.focusEdit();
  }

  /**
   * Refuses the write while a file has not uploaded: the edit and its other
   * changes stay, and the summary points at each failed file, which shows
   * as fixed once it is tried again or removed.
   *
   * @returns `false`.
   */
  async #uploadFailed(): Promise<boolean> {
    const record = this.#record;
    this.#release();
    this.#host.announce(this.#l10n.ɵtext('tm.details.uploadsFailed')());
    const message = this.#text('uploadFailed');
    const found = [...untracked(record.uploads.failed)].map((path) => {
      // An upload's path joins json names and row indices with dots, such as `attachments.0.fileId`.
      const segments = path.split('.').map((segment) => (/^\d+$/.test(segment) ? Number(segment) : segment));
      const target = this.#walk(segments);
      return {
        json: segments.slice(0, target.path.length),
        names: target.path,
        message,
        resolved: target.resolved,
        fixed: computed(() => !record.uploads.failed().has(path)),
      };
    });
    await this.#open(found);
    return this.#done('client-invalid', found.length);
  }

  /**
   * Records problems on their nodes, as a 422 maps them: each one's path
   * splits into segments, the record's leading `entities[0]` drops, and the
   * form is walked by the entity's json names; a path that does not resolve
   * lands on the deepest node reached. The platform schema then shows each
   * one while its node keeps the value it had at the save.
   *
   * @param items - The problems, as a 422's `errors[]` items.
   * @returns The problems, placed.
   */
  #place(items: readonly TmProblemError[]): TmSaveProblem[] {
    const record = this.#record;
    const source = record.source;
    const serverText = this.#injector.get(TmServerText);
    const recorded: TmDetailsServerError[] = [];
    const found: TmSaveProblem[] = [];
    for (const item of items) {
      const path = typeof item['path'] === 'string' ? item['path'] : '';
      const segments = source.errorSegments === undefined ? recordSegments(path) : source.errorSegments(path);
      if (segments === null) {
        // A root error, such as one on the body: listed, never on a field.
        found.push({ json: [], names: [], message: serverText.error(item, source.entity)(), resolved: false, fixed: signal(false) });
        continue;
      }
      const target = this.#walk(segments);
      const json = segments.slice(0, target.path.length);
      const message = serverText.error(item, target.entity ?? source.entity)();
      recorded.push({ segments: json, value: untracked(target.field().value), error: { kind: SERVER, message } as ValidationError.WithoutFieldTree });
      found.push({
        json,
        names: target.path,
        message,
        resolved: target.resolved,
        fixed: computed(() => !this.#node(json)().errors().some((shown) => shown.kind === SERVER && shown.message === message)),
      });
    }
    record.errors.set(recorded);
    return found;
  }

  /**
   * The client rules' errors, walked over the record and every row of its
   * child collections, each with its paths. A recorded server or source
   * error is listed by {@link TmDetailsSaveRun.place} instead.
   *
   * @returns The problems.
   */
  #clientProblems(): TmSaveProblem[] {
    const translate = this.#injector.get(TM_UI_TRANSLATE);
    const entities = this.#injector.get(TmEntities);
    const found: TmSaveProblem[] = [];
    const visit = (node: FieldTree<unknown>, json: readonly TmErrorPathSegment[], names: readonly TmErrorPathSegment[], descriptor: TmEntityDescriptor | undefined): void => {
      // --- The node's own errors ---
      const own = untracked(node().errors).filter((error) => error.kind !== SERVER);
      untracked(tmResolveFieldErrors(signal(own), translate)).forEach((error, index) => {
        const kind = own[index]!.kind;
        found.push({ json, names, message: error.message, resolved: true, fixed: computed(() => !this.#node(json)().errors().some((shown) => shown.kind === kind)) });
      });
      if (descriptor === undefined) {
        return;
      }
      // --- Its members, then each child collection's rows by their own entity ---
      const members = node as unknown as Readonly<Record<string, FieldTree<unknown> | undefined>>;
      for (const property of descriptor.properties) {
        const child = members[property.json];
        if (child !== undefined) {
          visit(child, [...json, property.json], [...names, property.name], undefined);
        }
      }
      for (const collection of descriptor.children) {
        const rows = members[collection.json];
        if (rows === undefined) {
          continue;
        }
        const own = untracked(rows().errors).filter((error) => error.kind !== SERVER);
        untracked(tmResolveFieldErrors(signal(own), translate)).forEach((error, index) => {
          const kind = own[index]!.kind;
          const at = [...json, collection.json];
          found.push({ json: at, names: [...names, collection.name], message: error.message, resolved: true, fixed: computed(() => !this.#node(at)().errors().some((shown) => shown.kind === kind)) });
        });
        const value = untracked(rows().value);
        const child = untracked(() => entities.get(collection.entity));
        for (let index = 0; Array.isArray(value) && index < value.length; index++) {
          const row = (rows as unknown as Readonly<Record<number, FieldTree<unknown> | undefined>>)[index];
          if (row !== undefined) {
            visit(row, [...json, collection.json, index], [...names, collection.name, index], child);
          }
        }
      }
    };
    visit(this.#record.form as FieldTree<unknown>, [], [], this.#descriptor);
    return found;
  }

  /**
   * Opens the summary over a failed save's problems. When a
   * problem sits on a path no rendered field or collection holds, every
   * `tm-details-tabs` first renders its tabs' content, and once that render
   * is done the form is marked touched, so the new fields show their errors
   * and their tabs' dots appear.
   *
   * @param found - The problems.
   */
  async #open(found: readonly TmSaveProblem[]): Promise<void> {
    const record = this.#record;
    const tabs = untracked(record.tabs);
    if (tabs.size > 0 && found.some((problem) => !this.#registered(problem))) {
      for (const group of tabs.values()) {
        group.instantiate();
      }
      await this.#rendered();
      untracked(record.form).markAsTouched();
    }
    if (!this.#live()) {
      return;
    }
    const items = found.map((problem) => this.#item(problem));
    this.#host.summary.set({ component: TmDetailsSummary, inputs: { summary: new TmDetailsSummaryState(items) } });
  }

  /**
   * Words a problem as the summary lists it, and decides what activating it does.
   *
   * @param problem - The problem.
   * @returns The item.
   */
  #item(problem: TmSaveProblem): TmDetailsSummaryItem {
    const record = this.#record;
    const descriptor = this.#descriptor;
    const { names, message, fixed } = problem;
    const [first, row, column] = names;
    const text = (key: string, args: Readonly<Record<string, string | number>>): string => this.#l10n.ɵtext(`tm.detailsSave.${key}`, args)();
    if (typeof first !== 'string') {
      // --- The record itself, or the body ---
      return { text: text('field', { label: untracked(descriptor.label), message }), fixed, activate: null };
    }
    const collection = descriptor.child(first);
    if (collection !== undefined) {
      // --- A child row's cell, the row, or the collection itself ---
      const label = untracked(collection.label);
      const place = [...record.places].find((candidate) => untracked(candidate.name) === first) ?? null;
      if (typeof row !== 'number') {
        return { text: text('field', { label, message }), fixed, activate: place === null ? null : () => void this.#show(place) };
      }
      const entity = untracked(() => this.#injector.get(TmEntities).get(collection.entity));
      const columnLabel = typeof column === 'string' ? (entity.property(column)?.label ?? entity.navigation(column)?.label) : undefined;
      const words =
        columnLabel === undefined
          ? text('row', { collection: label, row: row + 1, message })
          : text('cell', { collection: label, row: row + 1, column: untracked(columnLabel), message });
      const go =
        place === null || !problem.resolved
          ? null
          : place.grid
            ? () => void this.#reveal(place, collection.json, row, typeof column === 'string' ? column : undefined)
            : () => void this.#focusRow(place, row);
      return { text: words, fixed, activate: go };
    }
    // --- A field: a button that focuses it while a layout renders it, else plain text ---
    const field = names.length === 1 ? untracked(record.fields).get(first) : undefined;
    const label = field?.label ?? descriptor.property(first)?.label ?? descriptor.navigation(first)?.label;
    return {
      text: text('field', { label: label === undefined ? first : untracked(label), message }),
      fixed,
      activate: field === undefined || !problem.resolved ? null : () => field.focus(),
    };
  }

  /**
   * Whether a rendered field or collection holds a problem; the record's own problems need none.
   *
   * @param problem - The problem.
   * @returns Whether it is held.
   */
  #registered(problem: TmSaveProblem): boolean {
    const [first] = problem.names;
    if (typeof first !== 'string') {
      return true;
    }
    if (this.#descriptor.child(first) !== undefined) {
      return [...this.#record.places].some((place) => untracked(place.name) === first);
    }
    return untracked(this.#record.fields).has(first);
  }

  /**
   * Shows where a collection renders: its tab opens and its card expands,
   * then it scrolls into view, which renders a deferred grid.
   *
   * @param place - Where the collection renders.
   */
  async #show(place: TmDetailsCollectionPlace): Promise<void> {
    place.scope?.reveal();
    await this.#rendered();
    place.element.scrollIntoView({ block: 'nearest' });
  }

  /**
   * Goes to a child row's cell: shows the collection, waits for its grid,
   * then reveals the row and focuses the cell.
   *
   * @param place - Where the collection renders.
   * @param json - The collection's json name.
   * @param row - The row's index in the working copy.
   * @param column - The cell's member, such as `Amount`; the row alone when absent.
   */
  async #reveal(place: TmDetailsCollectionPlace, json: string, row: number, column: string | undefined): Promise<void> {
    const record = this.#record;
    await this.#show(place);
    const name = untracked(place.name);
    const grid = await this.#until(() => {
      const collection = record.collections().get(name);
      return collection?.ready() === true ? collection : undefined;
    });
    const rows = (untracked(record.model) as Readonly<Record<string, unknown>>)[json];
    const target = Array.isArray(rows) ? (rows[row] as { readonly id?: TmId } | undefined) : undefined;
    if (grid !== undefined && target?.id !== undefined) {
      grid.reveal(target.id, column === undefined ? {} : { column });
    }
  }

  /**
   * Goes to a row of a collection no grid renders, such as a record's
   * files: shows the collection, then focuses the row's own control (its
   * caption, else its first button), or the collection's first control
   * while its rows have not rendered. Nothing waits for a registration.
   *
   * @param place - Where the collection renders.
   * @param row - The row's index in the working copy.
   */
  async #focusRow(place: TmDetailsCollectionPlace, row: number): Promise<void> {
    await this.#show(place);
    const item = place.element.querySelector(`[data-tm-row="${row}"]`) ?? place.element;
    (item.querySelector<HTMLElement>('input') ?? item.querySelector<HTMLElement>('button'))?.focus();
  }

  /**
   * Builds the stamp into the body: an edit sends the
   * base's `modifiedAt` as the server sent it; a create sends no key and no
   * stamp.
   *
   * @param body - The record as sent.
   * @returns The body with its stamp.
   */
  #stamp(body: T): T {
    const record = body as TmJsonObject;
    if (this.#mode === 'create') {
      const descriptor = this.#descriptor;
      const key = descriptor.property(descriptor.key.property)?.json ?? 'id';
      return Object.fromEntries(Object.entries(record).filter(([member]) => member !== key && member !== 'modifiedAt')) as T;
    }
    const stamp = (untracked(this.#record.base) as TmJsonObject | null)?.['modifiedAt'];
    return (stamp === undefined ? record : { ...record, modifiedAt: stamp }) as T;
  }

  /**
   * Waits until no upload of the record runs: each one has staged its blob or failed on its field.
   *
   * @returns `true` once none runs; `false` when the record or the page went away first.
   */
  #uploadsSettled(): Promise<boolean> {
    const record = this.#record;
    return this.#until(() => (record.uploads.running().size === 0 ? true : undefined)).then((settled) => settled === true && this.#live());
  }

  /**
   * Waits until a reactive read gives a value, while the record lives.
   *
   * @typeParam V - The value's type.
   * @param read - Reads the value; `undefined` while not there yet.
   * @returns The value, or `undefined` once the record has gone.
   */
  #until<V>(read: () => V | undefined): Promise<V | undefined> {
    const now = untracked(read);
    if (now !== undefined) {
      return Promise.resolve(now);
    }
    const injector = this.#record.injector;
    return new Promise((resolve) => {
      const watch = effect(
        () => {
          const value = read();
          if (value !== undefined) {
            watch.destroy();
            resolve(value);
          }
        },
        { injector, manualCleanup: true },
      );
      injector.get(DestroyRef).onDestroy(() => {
        watch.destroy();
        resolve(undefined);
      });
    });
  }

  /**
   * Waits for the next render to finish.
   *
   * @returns A promise that settles after it.
   */
  #rendered(): Promise<void> {
    return new Promise((resolve) => afterNextRender(() => resolve(), { injector: this.#record.injector }));
  }

  /**
   * Holds the page under one overlay run until the Save releases it.
   *
   * @param label - The run's label, read live.
   */
  #hold(label: Signal<string>): void {
    let end!: () => void;
    const held = new Promise<void>((resolve) => (end = resolve));
    this.#release = () => {
      this.#release = () => undefined;
      end();
    };
    void this.#overlay.run(held, { label });
  }

  /** Clears the recorded errors and the summary: the next Save, Cancel and success start clean. */
  #clear(): void {
    this.#record.errors.set([]);
    this.#host.summary.set(null);
  }

  /**
   * Walks the form by json names and row indices, as a 422's paths do.
   *
   * @param segments - The path's segments from the record.
   * @returns Where they lead.
   */
  #walk(segments: readonly TmErrorPathSegment[]): ReturnType<typeof tmWalkErrorPath> {
    return tmWalkErrorPath(this.#record.form as FieldTree<unknown>, segments, { entity: this.#record.source.entity, entities: this.#injector.get(TmEntities) });
  }

  /**
   * The form's node at a path.
   *
   * @param json - The path's json names and row indices.
   * @returns The node, or its deepest ancestor the form holds.
   */
  #node(json: readonly TmErrorPathSegment[]): FieldTree<unknown> {
    return ɵtmDetailsNodeAt(this.#record.form as FieldTree<unknown>, json);
  }

  /**
   * Whether the page and the record the Save began on are still on screen.
   *
   * @returns Whether they are.
   */
  #live(): boolean {
    return !this.#host.destroyed() && this.#host.current() === this.#record;
  }

  /**
   * A string of the save flow.
   *
   * @param key - Its key under `tm.detailsSave`.
   * @returns It, in the active language.
   */
  #text(key: string): string {
    return this.#l10n.ɵtext(`tm.detailsSave.${key}`)();
  }

  /**
   * Records the Save's timing and ends it.
   *
   * @param outcome - How it ended.
   * @param errors - How many problems it found.
   * @returns Whether it saved.
   */
  #done(outcome: TmSaveOutcome, errors = 0): boolean {
    this.#injector.get(TmTelemetry).timing('tm.details.save', performance.now() - this.#started, {
      mode: this.#mode === 'edit' ? 'edit' : this.#host.cloning ? 'clone' : 'create',
      outcome,
      errors: errors === 0 ? '0' : errors <= 3 ? '1-3' : '4+',
    });
    return outcome === 'ok';
  }
}

/**
 * A 422 path's segments within the record: the leading `entities[0]` is the
 * record, so it drops; any other path is a root error.
 *
 * @param path - The path, such as `entities[0].lines[1].amount`.
 * @returns The segments, such as `['lines', 1, 'amount']`, or `null` for a root error.
 */
function recordSegments(path: string): readonly TmErrorPathSegment[] | null {
  const segments = tmSplitErrorPath(path);
  return segments !== null && segments[0] === 'entities' && segments[1] === 0 ? segments.slice(2) : null;
}
