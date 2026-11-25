// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import {
  booleanAttribute,
  ChangeDetectionStrategy,
  Component,
  computed,
  DestroyRef,
  inject,
  Injector,
  input,
  signal,
  untracked,
  viewChild,
  type InputSignal,
  type InputSignalWithTransform,
} from '@angular/core';
import { FormField, type FieldState, type FieldTree } from '@angular/forms/signals';
import {
  TmApi,
  TmApiError,
  TmDecimals,
  TmEntities,
  TmLocalization,
  TmServerText,
  TmUrls,
  type TmBlobDescriptor,
  type TmBlobKindPolicy,
  type TmId,
  type TmNavigationDescriptor,
  type TmPropertyDescriptor,
  type TmValueDescriptor,
} from '@tellma/core';
import { TM_UI_TRANSLATE, TmL10n, tmResolveFieldErrors } from '@tellma/core-ui';
import { TmButton } from '@tellma/core-ui/button';
import { TmCheckbox } from '@tellma/core-ui/checkbox';
import { TmChip } from '@tellma/core-ui/chip';
import type { TmFieldError } from '@tellma/core-ui/contracts';
import { TmDatePicker } from '@tellma/core-ui/date-picker';
import { TmFilePicker, type TmFileSelection } from '@tellma/core-ui/files';
import { TM_FORM_FIELD_CONTROL } from '@tellma/core-ui/form-field';
import { TmImage, type TmImageEdit } from '@tellma/core-ui/image';
import { TmInput } from '@tellma/core-ui/input';
import { TmNumber } from '@tellma/core-ui/number';
import { TmOption, TmSelect } from '@tellma/core-ui/select';
import { TmMultiSelect } from '@tellma/core-ui/multi-select';
import { ɵtmCropCoverSquare, tmCheckBlobFile, TmFormats, ɵtmEditorKind, ɵtmParseExact, type ɵTmEditorKind } from '@tellma/core/entity';

import { ɵTmExactBinding } from './tm-exact-binding';
import { ɵTmFormFieldDelegate } from './tm-form-field-delegate';
import { TmEntityField } from './tm-entity-field';
import { TmEntityMultiField } from './tm-entity-multi-field';
import { TmUploads } from './tm-uploads';

// A typed value in edit mode (spec 0029 §12.2's control column): the one
// control a value descriptor takes, bound to a Signal Forms field and placed
// in the caller's `tm-form-field`. Values the user may not write show as
// read-only text; a related row takes the entity field, a list of them the
// entity list field; an image or a file uploads as soon as it is picked, its
// path listed in the nearest `TmUploads` until the upload settles, and the
// staged blob's id becomes the value.

/** What an editor knows of the property it edits. */
type TmEditedProperty = TmValueDescriptor | TmPropertyDescriptor | TmNavigationDescriptor;

/** The largest original a cropped image is picked from: the crop is what uploads. */
const LARGEST_PICK = 20 * 1024 * 1024;

/** The editor's per-instance ids. */
let nextId = 0;

/** The formats a file size is written in, by locale. */
const SIZES = new Map<string, Intl.NumberFormat>();

/**
 * The type facts of what an editor edits: a navigation's are its id's.
 *
 * @param property - The edited property.
 * @returns The type facts.
 */
function valueOf(property: TmEditedProperty): TmValueDescriptor {
  return 'foreignKey' in property ? { type: 'integer', nullable: property.nullable, target: property.target } : property;
}

/**
 * Edits a typed value: the control its type takes, bound to a Signal Forms
 * field, inside the caller's `tm-form-field`. Text takes an input, or a
 * textarea when it may be longer than 255 characters; numbers take a
 * number input, decimals and longs held exactly as decimal text; dates a
 * date picker; enums a select in the enum's order; images and files their
 * pickers, which upload as soon as a file is picked; a related row, a
 * navigation or a value that names a target, takes the entity field, and
 * a list of them the entity list field. A value the server owns, an
 * instant and a type with no control show as read-only text; a related
 * row's key property shows its id, so pass the navigation to choose the
 * row by its display name. A write-once value edits only while the record
 * is being created.
 *
 * @example
 * ```html
 * <tm-form-field [label]="creditLimit.label()">
 *   <tm-value-editor [property]="creditLimit" [field]="form.creditLimit" />
 * </tm-form-field>
 * ```
 */
@Component({
  selector: 'tm-value-editor',
  imports: [
    FormField,
    ɵTmExactBinding,
    TmButton,
    TmCheckbox,
    TmChip,
    TmDatePicker,
    TmEntityField,
    TmEntityMultiField,
    TmFilePicker,
    TmImage,
    TmInput,
    TmMultiSelect,
    TmNumber,
    TmOption,
    TmSelect,
  ],
  changeDetection: ChangeDetectionStrategy.OnPush,
  providers: [{ provide: TM_FORM_FIELD_CONTROL, useFactory: () => inject(TmValueEditor).ɵcontrol }],
  host: { class: 'tm-value-editor' },
  styles: `
    :host {
      display: contents;
    }
    .tm-value-editor__binding {
      display: none;
    }
    .tm-value-editor__blob {
      display: flex;
      align-items: center;
      flex-wrap: wrap;
      gap: var(--tm-space-2, 0.5rem);
    }
  `,
  template: `
    @let state = fieldState();
    @switch (kind()) {
      <!-- A value the user may not write: its text, in a read-only input the field labels -->
      @case ('readonly') {
        <input tmInput readonly [value]="readonlyText()" [errors]="state.errors()" [invalid]="state.invalid()" [touched]="state.touched()" />
      }
      <!-- Text: one line up to 255 characters, else three rows -->
      @case ('text') {
        <input tmInput [formField]="textField()" [attr.maxlength]="maxLength()" [attr.dir]="contentDir()" />
      }
      @case ('textarea') {
        <textarea tmInput rows="3" [formField]="textField()" [attr.maxlength]="maxLength()" [attr.dir]="contentDir()"></textarea>
      }
      @case ('plain') {
        <input tmInput class="tm-value-editor__mono" [formField]="textField()" />
      }
      <!-- Numbers: an integer binds as it is; a decimal or a long converts at the binding -->
      @case ('integer') {
        <input tmNumber [formField]="numberField()" [maxDecimals]="0" />
      }
      @case ('exact') {
        <!-- The field's binding: it writes what the text reads as and carries a refusal into the field -->
        <span tmExactBinding class="tm-value-editor__binding" [formField]="field()"></span>
        <input
          #exact
          tmNumber
          [value]="exactNumber()"
          (input)="setExact(exact.value)"
          (touch)="state.markAsTouched()"
          [minDecimals]="scale()"
          [maxDecimals]="scale()"
          [disabled]="state.disabled()"
          [readonly]="state.readonly()"
          [required]="state.required()"
          [invalid]="state.invalid()"
          [touched]="state.touched()"
          [dirty]="state.dirty()"
          [pending]="state.pending()"
          [errors]="state.errors()"
        />
      }
      @case ('bool') {
        <tm-checkbox [formField]="boolField()" />
      }
      @case ('date') {
        <tm-date-picker [formField]="dateField()" />
      }
      <!-- An enum: its values in the enum's order -->
      @case ('enum') {
        <tm-select [formField]="enumField()">
          @for (option of options(); track option.value) {
            <tm-option [value]="option.value" [label]="option.label">{{ option.label }}</tm-option>
          }
        </tm-select>
      }
      @case ('enums') {
        <tm-multi-select [formField]="enumsField()">
          @for (option of options(); track option.value) {
            <tm-option [value]="option.value" [label]="option.label">{{ option.label }}</tm-option>
          }
        </tm-multi-select>
      }
      <!-- A related row: the entity field; a list of them: the entity list field -->
      @case ('entity') {
        <tm-entity-field [entity]="target()" [formField]="idField()" />
      }
      @case ('entities') {
        <tm-entity-multi-field [entity]="target()" [formField]="idsField()" />
      }
      <!-- An image: the image editor, uploading each committed pick -->
      @case ('image') {
        <div
          class="tm-value-editor__blob"
          role="group"
          [id]="ownId"
          [attr.aria-labelledby]="control.labelledBy()"
          [attr.aria-describedby]="control.ownDescribedBy()"
          [attr.aria-busy]="uploading() ? 'true' : null"
        >
          <tm-image
            [mode]="writable() ? 'edit' : 'view'"
            [src]="blobSrc()"
            alt=""
            [width]="96"
            [height]="96"
            [srcForSize]="asIs"
            [maxFileBytes]="pickLimit()"
            (imageChange)="onImage($event)"
          />
          @if (retryable()) {
            <button tmButton type="button" variant="ghost" (click)="retry()">{{ retryLabel() }}</button>
          }
        </div>
      }
      <!-- A file: the picked file's chip, and the picker -->
      @case ('file') {
        <div
          class="tm-value-editor__blob"
          role="group"
          [id]="ownId"
          [attr.aria-labelledby]="control.labelledBy()"
          [attr.aria-describedby]="control.ownDescribedBy()"
          [attr.aria-busy]="uploading() ? 'true' : null"
        >
          @if (fileLabel(); as label) {
            <tm-chip [label]="label" [href]="fileHref()" [removable]="writable()" (remove)="clear()" />
          }
          @if (writable()) {
            <button
              tmButton
              tmFilePicker
              type="button"
              [accept]="accept()"
              [maxFileSize]="policy()?.maxSize ?? 0"
              (filesSelected)="onFiles($event)"
            >
              {{ chooseLabel() }}
            </button>
          }
          @if (retryable()) {
            <button tmButton type="button" variant="ghost" (click)="retry()">{{ retryLabel() }}</button>
          }
        </div>
      }
    }
  `,
})
export class TmValueEditor {
  readonly #injector = inject(Injector);
  readonly #api = inject(TmApi);
  readonly #urls = inject(TmUrls);
  readonly #entities = inject(TmEntities);
  readonly #formats = inject(TmFormats);
  readonly #localization = inject(TmLocalization);
  readonly #l10n = inject(TmL10n);
  readonly #serverText = inject(TmServerText);
  readonly #uploads = inject(TmUploads, { optional: true });

  /** What the editor edits: type facts, a property, or a navigation. */
  readonly property: InputSignal<TmEditedProperty> = input.required<TmEditedProperty>();

  /** The Signal Forms field the value lives in. */
  readonly field: InputSignal<FieldTree<unknown>> = input.required<FieldTree<unknown>>();

  /** The field's path, under which an upload is listed in the nearest `TmUploads`; the field's name when omitted. */
  readonly path: InputSignal<string | undefined> = input<string | undefined>(undefined);

  /** Whether the record is being created, so a write-once value edits. */
  readonly create: InputSignalWithTransform<boolean, unknown> = input(false, { transform: booleanAttribute });

  /** The id of the editor's own group, which the field labels when it renders no inner control. */
  protected readonly ownId = `tm-value-editor-${nextId++}`;

  /** A blob URL is complete as built; the image asks for no size of its own. */
  protected readonly asIs = (src: string): string => src;

  /** The inner control the field labels, once the view renders one. */
  private readonly inner = viewChild(TM_FORM_FIELD_CONTROL);

  /** The field binding of a decimal or a long. */
  private readonly exactBinding = viewChild(ɵTmExactBinding);

  /** The bound field's state. */
  protected readonly fieldState = computed<FieldState<unknown>>(() => this.field()());

  /** The editor's own errors: a refused or failed upload. */
  readonly #local = signal<readonly TmFieldError[]>([]);

  /** Whether an upload is in flight. */
  protected readonly uploading = signal(false);

  /** The upload to try again after a failure that may pass on a retry. */
  readonly #retry = signal<(() => void) | null>(null);

  /** The blob this editor last uploaded, for its file name. */
  readonly #uploaded = signal<TmBlobDescriptor | null>(null);

  /** The controller of the editor's own upload, when no `TmUploads` lists it. */
  #controller: AbortController | null = null;

  /**
   * The control `tm-form-field` labels: the inner control, or the editor's
   * own group. Internal: the editor provides it as its form-field control.
   */
  readonly ɵcontrol: ɵTmFormFieldDelegate = new ɵTmFormFieldDelegate(
    this.inner,
    {
      controlId: signal(this.ownId).asReadonly(),
      state: this.fieldState,
      errors: tmResolveFieldErrors(
        computed(() => this.fieldState().errors()),
        inject(TM_UI_TRANSLATE),
      ),
      local: this.#local.asReadonly(),
      busy: this.uploading.asReadonly(),
    },
    this.#injector,
  );

  /** The delegate, for the template's own group. */
  protected readonly control = this.ɵcontrol;

  /** The value's type facts. */
  protected readonly descriptor = computed(() => valueOf(this.property()));

  /** The upload policy of a blob reference. */
  protected readonly policy = computed<TmBlobKindPolicy | null>(() => (this.property() as Partial<TmPropertyDescriptor>).blob ?? null);

  /** Whether the user may write the value: its ownership admits it now. */
  readonly #owned = computed(() => {
    const ownership = (this.property() as Partial<TmPropertyDescriptor>).ownership ?? 'editable';
    return ownership === 'editable' || (ownership === 'write-once' && this.create());
  });

  /** Whether a blob's picker offers a change: the value is writable and the field neither disabled nor read-only. */
  protected readonly writable = computed(() => {
    const state = this.fieldState();
    return this.#owned() && !state.disabled() && !state.readonly();
  });

  /** The control the value takes. */
  protected readonly kind = computed<ɵTmEditorKind>(() => {
    const property = this.property();
    const of = this.descriptor();
    const policy = this.policy();
    if (policy !== null) {
      return policy.image === null ? 'file' : 'image';
    }
    // A related row, its navigation or a reference by type facts, takes the
    // entity field; the key property itself names no target, so it shows its id.
    const keyOf = (property as Partial<TmPropertyDescriptor>).foreignKeyOf;
    if (!this.#owned() || keyOf !== undefined) {
      return 'readonly';
    }
    return ɵtmEditorKind(of);
  });

  /** A read-only value's text. */
  protected readonly readonlyText = computed(() => this.#formats.text(this.fieldState().value(), this.descriptor(), 'grid'));

  /** The longest text accepted. */
  protected readonly maxLength = computed(() => this.descriptor().maxLength ?? null);

  /** A slot's content direction. */
  protected readonly contentDir = computed(() => (this.property() as Partial<TmPropertyDescriptor>).contentDir ?? null);

  /** A decimal's fraction digits; none for a long. */
  protected readonly scale = computed(() => this.descriptor().scale ?? 0);

  /** The field as text, for the inputs. */
  protected readonly textField = computed(() => this.field() as FieldTree<string>);

  /** The field as a number, for an integer. */
  protected readonly numberField = computed(() => this.field() as FieldTree<number | null>);

  /** The field as a flag, for the checkbox. */
  protected readonly boolField = computed(() => this.field() as FieldTree<boolean>);

  /** The field as a date, for the date picker. */
  protected readonly dateField = computed(() => this.field() as FieldTree<string | null>);

  /** The field as an enum value, for the select. */
  protected readonly enumField = computed(() => this.field() as FieldTree<string | undefined>);

  /** The field as enum values, for the multi-select. */
  protected readonly enumsField = computed(() => this.field() as FieldTree<readonly string[]>);

  /** The entity a related row is chosen from. */
  protected readonly target = computed(() => this.descriptor().target ?? '');

  /** The field as an id, for the entity field. */
  protected readonly idField = computed(() => this.field() as FieldTree<TmId | null>);

  /** The field as ids, for the entity list field. */
  protected readonly idsField = computed(() => this.field() as FieldTree<readonly TmId[]>);

  /** An enum's values with their labels, in the enum's order. */
  protected readonly options = computed(() => {
    const name = this.descriptor().enum;
    const named = name === undefined ? undefined : this.#entities.enum(name);
    return (named?.values ?? []).map((value) => ({ value: value.value, label: named!.label(value.value)() }));
  });

  /** A decimal or a long as the number its control edits. */
  protected readonly exactNumber = computed(() => {
    const value = this.fieldState().value();
    return TmDecimals.isDecimal(value) ? TmDecimals.toNumber(value) : typeof value === 'number' ? value : null;
  });

  /** The content types a file picker offers. */
  protected readonly accept = computed(() => this.policy()?.accept.join(',') ?? '');

  /** The largest image the image editor takes: a cropped kind's crop is what uploads. */
  protected readonly pickLimit = computed(() => {
    const policy = this.policy();
    return policy?.image?.fit === 'cover-square' ? Math.max(policy.maxSize, LARGEST_PICK) : (policy?.maxSize ?? LARGEST_PICK);
  });

  /** The current blob's id. */
  readonly #blobId = computed(() => {
    const value = this.fieldState().value();
    return typeof value === 'number' ? value : typeof value === 'string' && /^\d+$/.test(value) ? Number(value) : null;
  });

  /** The current image's URL; empty for none. */
  protected readonly blobSrc = computed(() => {
    const id = this.#blobId();
    const policy = this.policy();
    return id === null || policy === null ? '' : this.#urls.blob(policy.kind, id);
  });

  /** The current file's download URL. */
  protected readonly fileHref = computed(() => {
    const id = this.#blobId();
    const policy = this.policy();
    return id === null || policy === null ? null : this.#urls.blob(policy.kind, id, { download: true });
  });

  /** The current file's chip label: its name when known, else "File"; `null` for none. */
  protected readonly fileLabel = computed(() => {
    const id = this.#blobId();
    if (id === null) {
      return null;
    }
    const staged = [this.#uploads?.staged(this.#path()), this.#uploaded()].find((blob) => blob?.id === id);
    return staged?.fileName || this.#localization.ɵtext('tm.value.file')();
  });

  /** Whether the last upload failed in a way a retry may mend. */
  protected readonly retryable = computed(() => this.#retry() !== null);

  /** The retry button's label. */
  protected readonly retryLabel = this.#localization.ɵtext('tm.errors.retry');

  /** The file picker's label. */
  protected readonly chooseLabel = this.#localization.ɵtext('tm.value.chooseFile');

  constructor() {
    // An editor that goes away aborts its own upload; a host's uploads are the host's.
    inject(DestroyRef).onDestroy(() => this.#controller?.abort());
  }

  /**
   * Reads a decimal's or a long's edit text into its field: decimal text at
   * the scale, or a refusal that keeps the field's value and holds it
   * invalid.
   *
   * @param text - The control's text.
   */
  protected setExact(text: string): void {
    this.exactBinding()?.write(ɵtmParseExact(text, untracked(this.#l10n.locale), untracked(this.scale)));
    untracked(this.fieldState).markAsDirty();
  }

  /**
   * Uploads an image the user picked or framed again; a removal clears the
   * value. A `cover-square` kind uploads the framed square.
   *
   * @param edit - The edit, or `null` for a removal.
   */
  protected async onImage(edit: TmImageEdit | null): Promise<void> {
    const policy = untracked(this.policy);
    if (policy === null) {
      return;
    }
    if (edit === null) {
      this.clear();
      return;
    }
    if (edit.blob === null) {
      return; // A re-fit of a stored image: the editor offers none, since the server never re-crops.
    }
    let image: Blob;
    try {
      image = policy.image === null ? edit.blob : await ɵtmCropCoverSquare(edit.blob, edit.fit, policy.image);
    } catch {
      this.#fail('fileType', 'tm.value.fileType');
      return;
    }
    const extension = image.type.split('/')[1] ?? 'bin';
    this.#upload(policy, new File([image], `image.${extension}`, { type: image.type }));
  }

  /**
   * Uploads the file the user picked, or reports why it was refused.
   *
   * @param selection - The picker's selection.
   */
  protected onFiles(selection: TmFileSelection): void {
    const policy = untracked(this.policy);
    const refused = selection.rejected[0];
    if (policy === null) {
      return;
    }
    if (refused !== undefined) {
      this.#refuse(policy, refused.reason === 'size' ? 'size' : 'type');
      return;
    }
    const file = selection.accepted[0];
    if (file !== undefined) {
      this.#upload(policy, file);
    }
  }

  /** Tries the failed upload again. */
  protected retry(): void {
    untracked(this.#retry)?.();
  }

  /** Clears the value, aborting any upload in flight and unstaging the path's blob. */
  protected clear(): void {
    this.#abort();
    this.#uploads?.stage(untracked(() => this.#path()), null);
    this.#local.set([]);
    this.#retry.set(null);
    const state = untracked(this.fieldState);
    state.value.set(null);
    state.markAsDirty();
    state.markAsTouched();
  }

  /** The path an upload is listed under. */
  #path(): string {
    return this.path() ?? this.fieldState().name();
  }

  /**
   * Checks a file and uploads it, listing the path in the nearest
   * `TmUploads` until it settles; the staged blob's id becomes the value.
   *
   * @param policy - The blob kind's policy.
   * @param file - The file.
   */
  #upload(policy: TmBlobKindPolicy, file: File): void {
    const check = tmCheckBlobFile(policy, file);
    if (check !== 'ok') {
      this.#refuse(policy, check);
      return;
    }
    this.#local.set([]);
    this.#retry.set(null);
    this.uploading.set(true);
    const send = (signal: AbortSignal): Promise<TmBlobDescriptor> => this.#api.upload(policy.kind, file, { fileName: file.name, signal });
    const path = untracked(() => this.#path());

    // --- Through the host's uploads when there are any, else alone ---
    let run: Promise<TmBlobDescriptor>;
    if (this.#uploads !== null) {
      run = this.#uploads.start(path, send);
    } else {
      this.#controller?.abort();
      const controller = (this.#controller = new AbortController());
      run = send(controller.signal).then((blob) => {
        controller.signal.throwIfAborted();
        return blob;
      });
    }
    run.then(
      (blob) => {
        this.uploading.set(false);
        this.#uploaded.set(blob);
        const state = untracked(this.fieldState);
        state.value.set(blob.id);
        state.markAsDirty();
        state.markAsTouched();
      },
      (error: unknown) => {
        // A later pick or a removal aborted this one; it reports nothing.
        if (error instanceof DOMException && error.name === 'AbortError') {
          return;
        }
        if (error instanceof TmApiError && error.kind === 'cancelled') {
          return;
        }
        this.uploading.set(false);
        if (error instanceof TmApiError && error.problem !== null && error.status >= 400 && error.status < 500) {
          this.#local.set([{ kind: 'upload', message: untracked(this.#serverText.problem(error.problem)).title }]);
          return;
        }
        this.#fail('upload', 'tm.value.uploadFailed');
        this.#retry.set(() => this.#upload(policy, file));
      },
    );
  }

  /**
   * Reports a file the policy refuses.
   *
   * @param policy - The blob kind's policy.
   * @param check - Why it is refused.
   */
  #refuse(policy: TmBlobKindPolicy, check: 'size' | 'type'): void {
    this.#retry.set(null);
    if (check === 'type') {
      this.#fail('fileType', 'tm.value.fileType');
      return;
    }
    const locale = untracked(this.#localization.locale);
    let format = SIZES.get(locale);
    if (format === undefined) {
      SIZES.set(locale, (format = new Intl.NumberFormat(locale, { style: 'unit', unit: 'megabyte', maximumFractionDigits: 1 })));
    }
    const max = format.format(policy.maxSize / (1024 * 1024));
    this.#local.set([{ kind: 'fileTooLarge', message: untracked(this.#localization.ɵtext('tm.value.fileTooLarge', { max })) }]);
  }

  /**
   * Shows one of the editor's own errors.
   *
   * @param kind - The error's kind.
   * @param key - Its message's key.
   */
  #fail(kind: string, key: string): void {
    this.#local.set([{ kind, message: untracked(this.#localization.ɵtext(key)) }]);
  }

  /** Aborts the upload in flight for the editor's path. */
  #abort(): void {
    this.uploading.set(false);
    if (this.#uploads !== null) {
      this.#uploads.cancel(untracked(() => this.#path()));
    } else {
      this.#controller?.abort();
      this.#controller = null;
    }
  }
}
