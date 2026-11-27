// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import { NgComponentOutlet } from '@angular/common';
import {
  ChangeDetectionStrategy,
  Component,
  computed,
  effect,
  inject,
  Injector,
  input,
  type InputSignal,
  signal,
  type Signal,
  type Type,
  untracked,
} from '@angular/core';
import { TmChip } from '@tellma/core-ui/chip';
import { TmIcon, type TmIconSource } from '@tellma/core-ui/icon';
import { TmImage } from '@tellma/core-ui/image';
import {
  TmApi,
  type TmBlobKindPolicy,
  type TmChildDescriptor,
  TmEntities,
  type TmEntityDescriptor,
  type TmId,
  type TmJson,
  type TmJsonObject,
  TmLocalization,
  type TmNavigationDescriptor,
  TmUrls,
} from '@tellma/core';
import { TmFormats, ɵtmByteSize } from '@tellma/core/entity';
import File from 'lucide/dist/esm/icons/file.mjs';
import FileArchive from 'lucide/dist/esm/icons/file-archive.mjs';
import FileImage from 'lucide/dist/esm/icons/file-image.mjs';
import FileSpreadsheet from 'lucide/dist/esm/icons/file-spreadsheet.mjs';
import FileText from 'lucide/dist/esm/icons/file-text.mjs';

import { injectTmDetailsRecord, tmDevMode } from './tm-details-record';

// The body of a child collection of files (spec 0031 §6.5), in its own
// chunk, which `tm-child-files` defers until it nears the viewport: one row
// per file, keyed to its blob. View mode lists the files as chips, each a
// download link that previews on a plain click and shows a thumbnail or a
// type icon, the name and the size. Edit mode adds a drop zone above the
// list: each file adds its row at once and uploads with a progress ring,
// and the row's file is set once the blob is staged; each row has a remove
// button and its caption edits inline. The edit side loads in a chunk of
// its own, once the record may be edited, so a view-mode visit never
// downloads it.

/** The width and height of a file's thumbnail in its chip, in pixels. */
const THUMBNAIL = 20;

/** A blob URL as built, for the thumbnail. */
const AS_IS = (src: string): string => src;

/** One file of the collection, as its chip shows it. Internal: the edit side renders the same items. */
export interface ɵTmChildFileItem {
  /** The row's id. */
  readonly id: TmId;
  /** The row's index in the collection. */
  readonly index: number;
  /** The path of the row's file in the form, such as `attachments.2.fileId`. */
  readonly path: string;
  /** The blob's id; `null` while the file uploads. */
  readonly blobId: number | null;
  /** The file's name. */
  readonly name: string;
  /** The file's size in bytes; `null` when unknown. */
  readonly bytes: number | null;
  /** The file's size as shown, such as `2.4 MB`; empty when unknown. */
  readonly size: string;
  /** The chip's text: the name, and the size when known. */
  readonly label: string;
  /** The file's media type; empty when unknown. */
  readonly type: string;
  /** The download link; `null` while the file uploads. */
  readonly href: string | null;
  /** The thumbnail's URL, when the blob has one. */
  readonly thumb: string | null;
  /** The type icon, without a thumbnail. */
  readonly icon: TmIconSource;
  /** The caption; empty without one. */
  readonly caption: string;
}

/** The collection's shape: the collection, its row's key, and its file's key and blob policy. Internal: the edit side writes by it. */
export interface ɵTmChildFilesShape {
  /** The collection. */
  readonly child: TmChildDescriptor;
  /** The child row's key member. */
  readonly keyJson: string;
  /** The file's key member on the child row, such as `fileId`. */
  readonly fileJson: string;
  /** The file's blob kind and its policy. */
  readonly policy: TmBlobKindPolicy;
}

/** The collection as the edit side reads it. Internal: the body hands it to its editor. */
export interface ɵTmChildFilesRef {
  /** The collection's shape; `undefined` for a collection or a file the child entity lacks. */
  readonly shape: Signal<ɵTmChildFilesShape | undefined>;
  /** The caption's member on the child row; `null` without a caption. */
  readonly captionJson: Signal<string | null>;
  /** The caption's label, in the active language. */
  readonly captionLabel: Signal<string>;
  /** The files, as their chips show them. */
  readonly items: Signal<readonly ɵTmChildFileItem[]>;
  /**
   * Follows a chip's link: a plain click previews the file, any other opens the link as the browser does.
   *
   * @param event - The click.
   * @param item - The file.
   */
  open(event: MouseEvent, item: ɵTmChildFileItem): void;
}

/** The edit side's component, once loaded. */
let editorType: Promise<Type<unknown>> | null = null;

/**
 * Loads the edit side once: a view-mode visit never downloads it.
 *
 * @returns The editor's component.
 */
function loadEditor(): Promise<Type<unknown>> {
  editorType ??= import('./tm-child-files-editor').then(
    ({ ɵTmChildFilesEditor }) => ɵTmChildFilesEditor,
    (error: unknown) => {
      // A failed load is forgotten, so the next mode change loads it again.
      editorType = null;
      throw error;
    },
  );
  return editorType;
}

/**
 * The body of `tm-child-files`: the chips, and the edit side in edit and
 * create mode. Internal: `tm-child-files` renders it once it nears the
 * viewport.
 */
@Component({
  selector: 'tm-child-files-body',
  imports: [NgComponentOutlet, TmChip, TmIcon, TmImage],
  changeDetection: ChangeDetectionStrategy.OnPush,
  host: { class: 'tm-child-files-body' },
  styles: `
    :host {
      display: block;
      min-inline-size: 0;
    }

    /* The chips, wrapping; each with its caption after it. */
    .tm-child-files__list {
      display: flex;
      flex-wrap: wrap;
      gap: var(--space-2) var(--space-3);
      margin: 0;
      padding: 0;
      list-style: none;
    }

    .tm-child-files__item {
      display: inline-flex;
      align-items: center;
      gap: var(--space-2);
      min-inline-size: 0;
    }

    .tm-child-files__thumb,
    .tm-child-files__icon {
      flex: none;
      margin-inline-end: var(--space-1);
      vertical-align: middle;
    }

    .tm-child-files__size,
    .tm-child-files__caption,
    .tm-child-files__none {
      color: var(--text-secondary);
    }

    .tm-child-files__size {
      margin-inline-start: var(--space-1);
      white-space: nowrap;
    }

    .tm-child-files__none {
      margin: 0;
      font-size: var(--text-sm);
    }
  `,
  template: `
    @if (ref.shape() !== undefined) {
      @if (editing() && editor(); as component) {
        <!-- Edit mode: the drop zone, the rows' progress, remove buttons and captions -->
        <ng-container *ngComponentOutlet="component; inputs: { files: ref }" />
      } @else {
        <!-- View mode, and edit mode until its side loads: the chips -->
        @if (ref.items().length > 0) {
          <ul class="tm-child-files__list">
            @for (item of ref.items(); track item.id) {
              <li class="tm-child-files__item">
                <tm-chip [label]="item.label" [href]="item.href" (click)="ref.open($event, item)">
                  @if (item.thumb; as thumb) {
                    <tm-image class="tm-child-files__thumb" mode="view" [src]="thumb" alt="" [width]="thumbnail" [height]="thumbnail" [srcForSize]="asIs" />
                  } @else {
                    <tm-icon class="tm-child-files__icon" size="sm" [svg]="item.icon" />
                  }
                  {{ item.name }}
                  @if (item.size !== '') {
                    <span class="tm-child-files__size">{{ item.size }}</span>
                  }
                </tm-chip>
                @if (item.caption !== '') {
                  <span class="tm-child-files__caption">{{ item.caption }}</span>
                }
              </li>
            }
          </ul>
        } @else {
          <p class="tm-child-files__none">{{ noneText() }}</p>
        }
      }
    }
  `,
})
export class ɵTmChildFilesBody {
  readonly #record = injectTmDetailsRecord();
  readonly #injector = inject(Injector);
  readonly #entities = inject(TmEntities);
  readonly #l10n = inject(TmLocalization);
  readonly #urls = inject(TmUrls);
  readonly #api = inject(TmApi);

  /** The child collection's name, such as `Attachments`. */
  readonly collection: InputSignal<string> = input.required<string>();

  /** The child's file navigation, such as `File`. */
  readonly file: InputSignal<string> = input.required<string>();

  /** The child's caption: a text property or a multilingual group, such as `Caption`; none when omitted. */
  readonly caption: InputSignal<string | undefined> = input<string | undefined>(undefined);

  /** The thumbnail's width and height. */
  protected readonly thumbnail = THUMBNAIL;

  /** A blob URL as built. */
  protected readonly asIs = AS_IS;

  /** The collection's descriptor; a development error when the record's entity has none of the name. */
  protected readonly child: Signal<TmChildDescriptor | undefined> = computed(() => {
    const name = this.collection();
    const child = this.#record.descriptor.child(name);
    if (child === undefined && tmDevMode()) {
      throw new Error(`tm-child-files's collection '${name}' names no child collection of ${this.#record.descriptor.name}.`);
    }
    return child;
  });

  /** The child entity's descriptor. */
  readonly #childEntity = computed<TmEntityDescriptor | undefined>(() => {
    const child = this.child();
    return child === undefined ? undefined : this.#entities.get(child.entity);
  });

  /** The file navigation, its key member and its blob policy; a development error when `file` names no file of the child. */
  readonly #file = computed<{ readonly navigation: TmNavigationDescriptor; readonly json: string; readonly policy: TmBlobKindPolicy } | undefined>(() => {
    const entity = this.#childEntity();
    if (entity === undefined) {
      return undefined;
    }
    const end = this.#entities.resolve(entity.name, this.file())?.end;
    if (end?.kind !== 'file' && end?.kind !== 'image') {
      if (tmDevMode()) {
        throw new Error(`tm-child-files's file '${this.file()}' names no file navigation of ${entity.name}.`);
      }
      return undefined;
    }
    return { navigation: end.navigation, json: entity.property(end.navigation.foreignKey)?.json ?? end.navigation.json, policy: end.policy };
  });

  /** The caption's member on the child row, in the active language for a multilingual group. */
  readonly #captionJson = computed<string | null>(() => {
    const entity = this.#childEntity();
    const path = this.caption();
    if (entity === undefined || path === undefined) {
      return null;
    }
    const end = this.#entities.resolve(entity.name, path)?.end;
    if (end?.kind === 'value' && end.property.type === 'string') {
      return end.property.json;
    }
    if (end?.kind === 'text') {
      const slots = [...end.slots].sort((a, b) => (a.multilingual?.slot ?? 1) - (b.multilingual?.slot ?? 1));
      return (slots[this.#l10n.contentSlot() - 1] ?? slots[0]!).json;
    }
    if (tmDevMode()) {
      throw new Error(`tm-child-files's caption '${path}' names no text property of ${entity.name}.`);
    }
    return null;
  });

  /** The caption's label. */
  readonly #captionLabel = computed(() => {
    const entity = this.#childEntity();
    const path = this.caption();
    const end = entity === undefined || path === undefined ? undefined : this.#entities.resolve(entity.name, path)?.end;
    return end?.kind === 'value' ? end.property.label() : end?.kind === 'text' ? end.slots[0]!.label() : '';
  });

  /** Whether the files edit: in edit and create mode, when the user writes the file's key. */
  protected readonly editing = computed(() => {
    const ownership = this.#file()?.navigation.ownership;
    return this.#record.mode() !== 'view' && (ownership === 'editable' || ownership === 'write-once');
  });

  /** The edit side's component, once loaded. */
  protected readonly editor = signal<Type<unknown> | null>(null);

  /** The text of an empty list. */
  protected readonly noneText = computed(() => this.#l10n.ɵtext('tm.details.noFiles')());

  /** The files, as their chips show them. */
  readonly #items: Signal<readonly ɵTmChildFileItem[]> = computed(() => {
    const child = this.child();
    const entity = this.#childEntity();
    const file = this.#file();
    if (child === undefined || entity === undefined || file === undefined) {
      return [];
    }
    const rows = (this.#record.model() as Readonly<Record<string, TmJson | undefined>>)[child.json];
    const keyJson = entity.property(entity.key.property)?.json ?? 'id';
    const captionJson = this.#captionJson();
    const locale = this.#l10n.locale();
    const unnamed = this.#l10n.ɵtext('tm.value.file')();
    return (Array.isArray(rows) ? rows : []).map((value, index) => {
      const row = (value ?? {}) as TmJsonObject;
      const path = `${child.json}.${index}.${file.json}`;
      const id = row[file.json];
      const blobId = typeof id === 'number' ? id : null;
      // The blob as the read related it, else as this form's upload staged it.
      const staged = this.#record.uploads.staged(path);
      const blob: Readonly<Record<string, unknown>> | undefined =
        blobId === null
          ? undefined
          : (this.#record.related.get('core.Blob', blobId) ?? (staged?.id === blobId ? (staged as unknown as Readonly<Record<string, unknown>>) : undefined));
      const name = text(blob?.['fileName']) || unnamed;
      const bytes = blob?.['size'] === undefined || blob['size'] === null ? null : Number(blob['size']);
      const size = bytes === null ? '' : ɵtmByteSize(blob!['size'] as string, locale);
      const type = text(blob?.['contentType']);
      const variants = text(blob?.['variants']).split(',').map((variant) => variant.trim());
      return {
        id: row[keyJson] as TmId,
        index,
        path,
        blobId,
        name,
        bytes: bytes !== null && Number.isFinite(bytes) ? bytes : null,
        size,
        label: size === '' ? name : `${name} (${size})`,
        type,
        href: blobId === null ? null : this.#urls.blob(file.policy.kind, blobId, { download: true }),
        thumb: blobId !== null && file.policy.thumbnail && variants.includes('thumb') ? this.#urls.blob(file.policy.kind, blobId, { variant: 'thumb' }) : null,
        icon: iconOf(type, name),
        caption: captionJson === null ? '' : text(row[captionJson]),
      };
    });
  });

  /** The collection's shape, for the edit side. */
  readonly #shape = computed<ɵTmChildFilesShape | undefined>(() => {
    const child = this.child();
    const entity = this.#childEntity();
    const file = this.#file();
    return child === undefined || entity === undefined || file === undefined
      ? undefined
      : { child, keyJson: entity.property(entity.key.property)?.json ?? 'id', fileJson: file.json, policy: file.policy };
  });

  /** The collection as the edit side reads it. */
  protected readonly ref: ɵTmChildFilesRef = {
    shape: this.#shape,
    captionJson: this.#captionJson,
    captionLabel: this.#captionLabel,
    items: this.#items,
    open: (event, item) => this.#open(event, item),
  };

  /** Loads the edit side once the record may be edited, so Edit finds it ready. */
  constructor() {
    // The value strings (an unnamed file's name) register with the formats service.
    inject(TmFormats);

    // Both signals are read on every run, so a failed prefetch loads again when the mode changes.
    effect(() => {
      const mayEdit = this.#record.can.edit();
      const mode = this.#record.mode();
      if ((mayEdit || mode !== 'view') && this.editor() === null) {
        untracked(() =>
          loadEditor().then(
            (component) => this.editor.set(component),
            () => undefined,
          ),
        );
      }
    });
  }

  /**
   * Follows a chip's link: a plain primary click previews the file in the
   * file viewer; a modified or middle click, or one before the file has
   * staged, is the browser's.
   *
   * @param event - The click.
   * @param item - The file.
   */
  #open(event: MouseEvent, item: ɵTmChildFileItem): void {
    const link = (event.target as Element | null)?.closest('a');
    const file = untracked(this.#file);
    if (link === null || link === undefined || item.blobId === null || file === undefined) {
      return;
    }
    if (event.button !== 0 || event.ctrlKey || event.metaKey || event.shiftKey || event.altKey) {
      return;
    }
    event.preventDefault();
    const url = this.#urls.blob(file.policy.kind, item.blobId);
    void import('@tellma/core-ui/file-preview').then(({ TmFilePreview }) =>
      this.#injector.get(TmFilePreview).open({
        name: item.name,
        ...(item.type !== '' && { type: item.type }),
        ...(item.bytes !== null && { size: item.bytes }),
        // The viewer's signal aborts the read once it closes.
        source: (signal: AbortSignal) =>
          this.#api.fetchBlob(url, { signal }).then(({ blob }) => {
            if (blob === null) {
              throw new Error(`The file at ${url} came back empty.`);
            }
            return blob;
          }),
      }),
    );
  }
}

/**
 * A wire member as text.
 *
 * @param value - The member.
 * @returns It, when it is a string; else empty.
 */
function text(value: unknown): string {
  return typeof value === 'string' ? value : '';
}

/**
 * A file's type icon, by its media type, else its name's extension.
 *
 * @param type - The media type; empty when unknown.
 * @param name - The file's name.
 * @returns The icon.
 */
function iconOf(type: string, name: string): TmIconSource {
  const extension = /\.([a-z0-9]+)$/i.exec(name)?.[1]?.toLowerCase() ?? '';
  if (type.startsWith('image/')) {
    return FileImage;
  }
  if (/spreadsheet|excel|csv/.test(type) || ['xlsx', 'xls', 'csv'].includes(extension)) {
    return FileSpreadsheet;
  }
  if (/zip|compressed|archive/.test(type) || ['zip', '7z', 'rar'].includes(extension)) {
    return FileArchive;
  }
  if (type === 'application/pdf' || type.startsWith('text/') || /wordprocessing|msword/.test(type) || ['pdf', 'txt', 'doc', 'docx'].includes(extension)) {
    return FileText;
  }
  return File;
}
