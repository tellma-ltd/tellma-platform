// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import { ChangeDetectionStrategy, Component, computed, inject, input, type InputSignal, signal, type Signal, untracked } from '@angular/core';
import { FormField, type FieldTree } from '@angular/forms/signals';
import { TM_UI_TRANSLATE, tmResolveFieldErrors } from '@tellma/core-ui';
import { TmButton } from '@tellma/core-ui/button';
import { TmChip } from '@tellma/core-ui/chip';
import { TmDropzone, type TmFileRejectionReason, type TmFileSelection } from '@tellma/core-ui/files';
import { TmIcon } from '@tellma/core-ui/icon';
import { TmInput } from '@tellma/core-ui/input';
import { TmProgressRing } from '@tellma/core-ui/progress-ring';
import { TmApi, type TmBlobDescriptor, TmEntities, type TmId, type TmJsonObject, TmLocalization, TmServerText } from '@tellma/core';
import { TmFormats, ɵtmByteSize, tmCheckBlobFile } from '@tellma/core/entity';
import { tmUploadFailure } from '@tellma/core/uploads';

import type { ɵTmChildFileItem, ɵTmChildFilesRef, ɵTmChildFilesShape } from './tm-child-files-body';
import { injectTmDetailsRecord } from './tm-details-record';

// The edit side of a child collection of files (spec 0031 §6.5, §6.3), in
// its own chunk: a drop zone above the list; each file it takes is checked
// against its kind's policy, adds its row `{ id: −n, fileId: null }` at
// once and uploads through the form's uploads, under the row's file path,
// so the form is pending and Save waits; the row's file is set once the
// blob is staged. A refused file says why at once; a refused upload says
// the server's reason on its row; a failed one offers a retry. Removing a
// row aborts its upload, and the later rows' uploads move up with them.

/** A field without errors. */
const NO_ERRORS: Signal<readonly string[]> = signal<readonly string[]>([]).asReadonly();

/** What a row's file says while it uploads or after its upload failed. */
interface TmRowUpload {
  /** The file picked for the row, until its blob is staged. */
  readonly file: File;
  /** Why its upload failed; `null` while it runs. */
  readonly failure: string | null;
  /** Whether a retry may mend the failure. */
  readonly retryable: boolean;
}

/** A row as its chip, ring and errors show it. */
interface TmRowView {
  /** The file's name. */
  readonly name: string;
  /** The file's size, as shown; empty when unknown. */
  readonly size: string;
  /** The chip's text. */
  readonly label: string;
  /** Whether the row's file uploads. */
  readonly uploading: boolean;
  /** The upload's progress in percent; `null` spins the ring. */
  readonly percent: number | null;
  /** The ring's name. */
  readonly uploadingLabel: string;
  /** The row's errors: its upload's, then its file field's. */
  readonly errors: readonly string[];
  /** Whether the row offers a retry. */
  readonly retryable: boolean;
}

/**
 * The edit side of `tm-child-files`: the drop zone, each row's progress
 * ring, errors, remove button and caption. Internal: the collection's body
 * loads and renders it.
 */
@Component({
  selector: 'tm-child-files-editor',
  imports: [FormField, TmButton, TmChip, TmDropzone, TmIcon, TmInput, TmProgressRing],
  changeDetection: ChangeDetectionStrategy.OnPush,
  host: { class: 'tm-child-files-editor' },
  styles: `
    :host {
      display: flex;
      flex-direction: column;
      gap: var(--space-3);
      min-inline-size: 0;
    }

    .tm-child-files-editor__refusals,
    .tm-child-files-editor__list {
      display: flex;
      flex-direction: column;
      gap: var(--space-2);
      margin: 0;
      padding: 0;
      list-style: none;
    }

    .tm-child-files-editor__refusals,
    .tm-child-files-editor__error {
      color: var(--error);
      font-size: var(--text-sm);
    }

    /* A row: its chip, its ring or retry, and its caption at the end. */
    .tm-child-files-editor__item {
      display: flex;
      flex-wrap: wrap;
      align-items: center;
      gap: var(--space-2);
      min-inline-size: 0;
    }

    .tm-child-files-editor__icon {
      flex: none;
      margin-inline-end: var(--space-1);
      vertical-align: middle;
    }

    .tm-child-files-editor__size {
      margin-inline-start: var(--space-1);
      color: var(--text-secondary);
      white-space: nowrap;
    }

    .tm-child-files-editor__caption {
      flex: 1 1 12rem;
      min-inline-size: 8rem;
      max-inline-size: 24rem;
    }
  `,
  template: `
    @if (files().shape(); as shape) {
      <!-- The drop zone: each file it takes adds a row and uploads at once -->
      <tm-dropzone multiple [accept]="accept()" [maxFileSize]="shape.policy.maxSize" (filesSelected)="add($event)" />

      <!-- The files it refused, each with its reason -->
      @if (refusals().length > 0) {
        <ul class="tm-child-files-editor__refusals" role="alert">
          @for (refusal of refusals(); track $index) {
            <li>{{ refusal }}</li>
          }
        </ul>
      }

      <!-- The rows: each chip with its progress or its errors, and its caption -->
      @if (files().items().length > 0) {
        <ul class="tm-child-files-editor__list">
          @for (item of files().items(); track item.id) {
            @let row = view(item);
            <li class="tm-child-files-editor__item" [attr.aria-busy]="row.uploading ? 'true' : null">
              <tm-chip [label]="row.label" [href]="item.href" removable (remove)="remove(item)" (click)="files().open($event, item)">
                <tm-icon class="tm-child-files-editor__icon" size="sm" [svg]="item.icon" />
                {{ row.name }}
                @if (row.size !== '') {
                  <span class="tm-child-files-editor__size">{{ row.size }}</span>
                }
              </tm-chip>
              @if (row.uploading) {
                <tm-progress-ring [value]="row.percent" [diameter]="20" [decorative]="false" [attr.aria-label]="row.uploadingLabel" />
              }
              @for (error of row.errors; track $index) {
                <span class="tm-child-files-editor__error">{{ error }}</span>
              }
              @if (row.retryable) {
                <button tmButton type="button" variant="ghost" size="sm" (click)="retry(item)">{{ retryText() }}</button>
              }
              @if (captionOf(item); as caption) {
                <input
                  tmInput
                  class="tm-child-files-editor__caption"
                  [formField]="caption"
                  [attr.aria-label]="captionLabel(item)"
                  [attr.maxlength]="captionMaxLength()"
                />
              }
            </li>
          }
        </ul>
      }
    }
  `,
})
export class ɵTmChildFilesEditor {
  readonly #record = injectTmDetailsRecord();
  readonly #api = inject(TmApi);
  readonly #entities = inject(TmEntities);
  readonly #l10n = inject(TmLocalization);
  readonly #serverText = inject(TmServerText);
  readonly #translate = inject(TM_UI_TRANSLATE);

  /** The collection, as `tm-child-files` hands it over. */
  readonly files: InputSignal<ɵTmChildFilesRef> = input.required<ɵTmChildFilesRef>();

  /** The drop zone's accepted media types. */
  protected readonly accept = computed(() => this.files().shape()?.policy.accept.join(',') ?? '');

  /** The files the last drop or pick refused, each with its reason. */
  protected readonly refusals = signal<readonly string[]>([]);

  /** The retry button's label. */
  protected readonly retryText = this.#l10n.ɵtext('tm.errors.retry');

  /** The longest caption the child entity takes. */
  protected readonly captionMaxLength = computed(() => {
    const shape = this.files().shape();
    const json = this.files().captionJson();
    return shape === undefined || json === null ? null : (this.#entities.get(shape.child.entity).properties.find((property) => property.json === json)?.maxLength ?? null);
  });

  /** Each row's upload, by the row's id, until its blob is staged. */
  readonly #uploads = signal<ReadonlyMap<TmId, TmRowUpload>>(new Map());

  /** The form's uploads. */
  readonly #formUploads = this.#record.uploads;

  /** Each file field's shown error messages, made once per field. */
  readonly #errors = new WeakMap<FieldTree<unknown>, Signal<readonly string[]>>();

  /** Registers the value strings its refusals and failures read, as the formats service does. */
  constructor() {
    inject(TmFormats);
  }

  /**
   * A row as its chip, ring and errors show it.
   *
   * @param item - The row's file.
   * @returns The view.
   */
  protected view(item: ɵTmChildFileItem): TmRowView {
    const upload = this.#uploads().get(item.id);
    const locale = this.#l10n.locale();
    // A file that has not staged yet is named and sized from the pick.
    const name = item.blobId === null && upload !== undefined ? upload.file.name : item.name;
    const size = item.blobId === null && upload !== undefined ? ɵtmByteSize(upload.file.size, locale) : item.size;
    const uploading = this.#formUploads.running().has(item.path);
    const sent = this.#formUploads.progress(item.path);
    const fieldErrors = this.#fileErrors(item)();
    return {
      name,
      size,
      label: size === '' ? name : `${name} (${size})`,
      uploading,
      percent: sent === null ? null : Math.round(sent * 100),
      uploadingLabel: this.#l10n.ɵtext('tm.details.uploading', { name })(),
      errors: [...(upload?.failure === null || upload === undefined ? [] : [upload.failure]), ...fieldErrors],
      retryable: upload?.retryable === true && !uploading,
    };
  }

  /**
   * A row's caption field.
   *
   * @param item - The row's file.
   * @returns The field; `null` without a caption.
   */
  protected captionOf(item: ɵTmChildFileItem): FieldTree<string> | null {
    const json = this.files().captionJson();
    // A text input edits an empty caption as empty text, as the field editors do.
    return json === null ? null : ((this.#row(item.index)?.[json] as FieldTree<string> | undefined) ?? null);
  }

  /**
   * A row's caption label, naming its file.
   *
   * @param item - The row's file.
   * @returns The label.
   */
  protected captionLabel(item: ɵTmChildFileItem): string {
    return `${this.files().captionLabel()}: ${item.name}`;
  }

  /**
   * Takes a drop or a pick: each file the policy accepts adds its row and
   * uploads at once; each one refused says why.
   *
   * @param selection - The files.
   */
  protected add(selection: TmFileSelection): void {
    const shape = untracked(this.files().shape);
    const collection = this.#collection();
    if (shape === undefined || collection === undefined) {
      return;
    }
    const refusals = selection.rejected.map(({ file, reason }) => this.#refusal(shape, file, reason));
    const accepted: File[] = [];
    for (const file of selection.accepted) {
      const check = tmCheckBlobFile(shape.policy, file);
      if (check === 'ok') {
        accepted.push(file);
      } else {
        refusals.push(this.#refusal(shape, file, check));
      }
    }
    this.refusals.set(refusals);

    // --- Each accepted file adds its row at once, then uploads under the row's file path ---
    const state = collection();
    const start = (untracked(state.value) ?? []).length;
    const rows = accepted.map(() => this.#record.ɵnewChildRow(shape.child, { [shape.fileJson]: null }));
    if (rows.length === 0) {
      return;
    }
    state.value.update((current) => [...(current ?? []), ...rows]);
    state.markAsDirty();
    rows.forEach((row, offset) => this.#upload(shape, row[shape.keyJson] as TmId, accepted[offset]!, start + offset));
  }

  /**
   * Removes a row: its upload aborts, and the later rows' uploads move up with them.
   *
   * @param item - The row's file.
   */
  protected remove(item: ɵTmChildFileItem): void {
    const shape = untracked(this.files().shape);
    const collection = this.#collection();
    if (shape === undefined || collection === undefined) {
      return;
    }
    this.#formUploads.removeRow(shape.child.json, item.index);
    const state = collection();
    state.value.update((rows) => (rows ?? []).filter((_, index) => index !== item.index));
    state.markAsDirty();
    this.#forget(item.id);
  }

  /**
   * Uploads a row's file again after a failure a retry may mend.
   *
   * @param item - The row's file.
   */
  protected retry(item: ɵTmChildFileItem): void {
    const shape = untracked(this.files().shape);
    const upload = untracked(this.#uploads).get(item.id);
    if (shape !== undefined && upload !== undefined) {
      this.#upload(shape, item.id, upload.file, item.index);
    }
  }

  /**
   * Uploads a row's file under the row's file path; the staged blob's id becomes the row's file.
   *
   * @param shape - The collection's shape.
   * @param id - The row's id.
   * @param file - The file.
   * @param index - The row's index now.
   */
  #upload(shape: ɵTmChildFilesShape, id: TmId, file: File, index: number): void {
    this.#uploads.update((uploads) => new Map(uploads).set(id, { file, failure: null, retryable: false }));
    const path = `${shape.child.json}.${index}.${shape.fileJson}`;
    const run = this.#formUploads.start(path, (signal, progress) =>
      this.#api.upload(shape.policy.kind, file, { fileName: file.name, signal, onProgress: progress }),
    );
    run.then(
      (blob: TmBlobDescriptor) => {
        // The row may have moved since: it is found by its id.
        const at = this.#indexOf(shape, id);
        const node = at === -1 ? undefined : this.#row(at)?.[shape.fileJson];
        if (node !== undefined) {
          const state = node();
          state.value.set(blob.id);
          state.markAsDirty();
          state.markAsTouched();
        }
        this.#forget(id);
      },
      (error: unknown) => {
        // A removal or the host aborted it; it reports nothing.
        const failure = tmUploadFailure(error, this.#serverText);
        if (failure.kind === 'aborted') {
          return;
        }
        const message = failure.kind === 'rejected' ? untracked(failure.message) : untracked(this.#l10n.ɵtext('tm.value.uploadFailed'));
        this.#uploads.update((uploads) => new Map(uploads).set(id, { file, failure: message, retryable: failure.kind === 'failed' }));
      },
    );
  }

  /**
   * A refused file's message: its name, then why.
   *
   * @param shape - The collection's shape.
   * @param file - The file.
   * @param reason - Why it was refused.
   * @returns The message.
   */
  #refusal(shape: ɵTmChildFilesShape, file: File, reason: TmFileRejectionReason): string {
    const message =
      reason === 'size'
        ? this.#l10n.ɵtext('tm.value.fileTooLarge', { max: ɵtmByteSize(shape.policy.maxSize, untracked(this.#l10n.locale)) })
        : this.#l10n.ɵtext('tm.value.fileType');
    return untracked(this.#l10n.ɵtext('tm.details.fileRefused', { name: file.name, message: untracked(message) }));
  }

  /**
   * A row's file field's errors, as messages: a server error such as an expired staged file, once shown.
   *
   * @param item - The row's file.
   * @returns The messages.
   */
  #fileErrors(item: ɵTmChildFileItem): Signal<readonly string[]> {
    const shape = this.files().shape();
    const node = shape === undefined ? undefined : this.#row(item.index)?.[shape.fileJson];
    if (node === undefined) {
      return NO_ERRORS;
    }
    let messages = this.#errors.get(node);
    if (messages === undefined) {
      const state = node();
      const errors = tmResolveFieldErrors(state.errors, this.#translate);
      messages = computed(() => (state.touched() || state.dirty() ? errors().map((error) => error.message) : []));
      this.#errors.set(node, messages);
    }
    return messages;
  }

  /**
   * The collection's form field.
   *
   * @returns The field; `undefined` while the working copy lacks the collection.
   */
  #collection(): FieldTree<TmJsonObject[] | null> | undefined {
    const shape = untracked(this.files().shape);
    const form = this.#record.form as unknown as Readonly<Record<string, FieldTree<TmJsonObject[] | null> | undefined>>;
    return shape === undefined ? undefined : form[shape.child.json];
  }

  /**
   * A row's form field, by its index.
   *
   * @param index - The row's index.
   * @returns The row's members' fields, or `undefined` past the rows.
   */
  #row(index: number): Readonly<Record<string, FieldTree<unknown>>> | undefined {
    const collection = this.#collection() as unknown as Readonly<Record<number, Readonly<Record<string, FieldTree<unknown>>> | undefined>> | undefined;
    return collection?.[index];
  }

  /**
   * A row's index now, by its id.
   *
   * @param shape - The collection's shape.
   * @param id - The row's id.
   * @returns The index, or -1 once the row is gone.
   */
  #indexOf(shape: ɵTmChildFilesShape, id: TmId): number {
    const rows = untracked(this.#record.model) as Readonly<Record<string, unknown>>;
    const collection = rows[shape.child.json];
    return Array.isArray(collection) ? collection.findIndex((row) => (row as TmJsonObject | null)?.[shape.keyJson] === id) : -1;
  }

  /**
   * Forgets a row's upload.
   *
   * @param id - The row's id.
   */
  #forget(id: TmId): void {
    this.#uploads.update((uploads) => {
      const next = new Map(uploads);
      next.delete(id);
      return next;
    });
  }
}
