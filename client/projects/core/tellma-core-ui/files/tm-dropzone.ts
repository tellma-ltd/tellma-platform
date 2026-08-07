// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import { Component, computed, DestroyRef, inject, signal } from '@angular/core';

import { TM_UI_TRANSLATE } from '@tellma/core-ui';

import { tmMaxMegabytes } from './internal/byte-size';
import { tmAcquireDocumentDragGuard } from './internal/document-drag-guard';
import { TmFilePicker } from './tm-file-picker';

/**
 * A visual drop target over the same selection engine as `tmFilePicker` —
 * the picker directive is applied as a HOST DIRECTIVE, so the browse
 * path, the guardrail inputs (`accept`, `multiple`, `maxFileSize`,
 * `maxFiles`), and the `filesSelected` output are literally the
 * directive's, re-exposed; the dropzone adds only the drop-target
 * handling and visuals.
 *
 * The whole region is one focusable `role="button"` control: Enter/Space
 * opens the OS dialog — keyboard users' full-fidelity path, since
 * drag-and-drop has no keyboard equivalent. The focused zone also accepts
 * paste (`Ctrl+V` with files on the clipboard). Dropped folders are
 * rejected with a localized reason (flat file lists only); with
 * `multiple` false, the first file is taken and the rest rejected.
 *
 * While at least one dropzone is connected, a document-level guard
 * cancels `dragover`/`drop` defaults outside designated targets, so a
 * missed drop can never navigate the SPA away.
 *
 * @tmGroup files
 * @tmA11yNotes One `role="button"` control named by its visible hint;
 *   Enter/Space picks files; rejections announce via LiveAnnouncer; the
 *   browse affordance is presentational text, never a nested interactive
 *   element.
 */
@Component({
  selector: 'tm-dropzone',
  hostDirectives: [
    {
      directive: TmFilePicker,
      inputs: ['accept', 'multiple', 'maxFileSize', 'maxFiles'],
      outputs: ['filesSelected'],
    },
  ],
  template: `
    <svg
      class="tm-dropzone__icon"
      viewBox="0 0 24 24"
      fill="none"
      stroke="currentColor"
      stroke-width="1.75"
      stroke-linecap="round"
      stroke-linejoin="round"
      aria-hidden="true"
    >
      <path d="M21 15v4a2 2 0 0 1-2 2H5a2 2 0 0 1-2-2v-4" />
      <polyline points="17 8 12 3 7 8" />
      <line x1="12" x2="12" y1="3" y2="15" />
    </svg>
    <p class="tm-dropzone__hint">
      {{ hintLabel() }}
      <span class="tm-dropzone__browse">{{ browseLabel() }}</span>
    </p>
    @if (typesLine(); as types) {
      <p class="tm-dropzone__meta">{{ types }}</p>
    }
    <p class="tm-dropzone__meta">{{ sizeLine() }}</p>
  `,
  styleUrl: './tm-dropzone.css',
  host: {
    class: 'tm-dropzone',
    role: 'button',
    tabindex: '0',
    // The zone IS the button, and it is not a <button> element — the ARIA
    // state is what says "already opening" here.
    '[attr.aria-disabled]': 'picker.awaitingDialog() ? "true" : null',
    '[class.tm-dropzone--dragover]': 'dragOver()',
    '(keydown)': 'onKeydown($event)',
    '(dragenter)': 'onDragEnter($event)',
    '(dragover)': 'onDragOver($event)',
    '(dragleave)': 'onDragLeave()',
    '(drop)': 'onDrop($event)',
    '(paste)': 'onPaste($event)',
  },
})
export class TmDropzone {
  private readonly translate = inject(TM_UI_TRANSLATE);
  /** The host-directive picker — the shared engine + browse path. */
  protected readonly picker = inject(TmFilePicker);

  /** Localized drop-or-paste hint line. */
  protected readonly hintLabel = this.translate('filePicker.hint');
  /** Localized "browse" text (presentational — the whole zone is the button). */
  protected readonly browseLabel = this.translate('filePicker.browse');

  /** The accepted-types line; absent when everything is accepted. */
  protected readonly typesLine = computed(() => {
    const accept = this.picker.accept().trim();
    return accept === '' ? null : this.translate('filePicker.acceptedTypes', { types: accept })();
  });
  /** The localized size-limit line — "each" only when there can be more than one. */
  protected readonly sizeLine = computed(() =>
    this.translate(this.picker.multiple() ? 'filePicker.maxSize' : 'filePicker.maxSizeSingle', {
      maxMb: tmMaxMegabytes(this.picker.maxFileSize()),
    })(),
  );

  /** Whether a drag is over the zone — drives the highlight class. */
  protected readonly dragOver = signal(false);
  /** Depth counter: dragenter/leave fire per descendant boundary. */
  private dragDepth = 0;

  constructor() {
    const releaseGuard = tmAcquireDocumentDragGuard();
    inject(DestroyRef).onDestroy(releaseGuard);
  }

  /** Enter/Space opens the browse dialog (the zone is a single button). */
  protected onKeydown(event: KeyboardEvent): void {
    if (event.key === 'Enter' || event.key === ' ') {
      event.preventDefault();
      this.picker.open();
    }
  }

  /** Tracks enter depth and turns the highlight on. */
  protected onDragEnter(event: DragEvent): void {
    event.preventDefault();
    this.dragDepth += 1;
    this.dragOver.set(true);
  }

  /** Marks the zone a valid copy target so the drop is allowed. */
  protected onDragOver(event: DragEvent): void {
    event.preventDefault();
    if (event.dataTransfer !== null) {
      event.dataTransfer.dropEffect = 'copy';
    }
  }

  /** Unwinds the enter depth; the highlight drops at zero. */
  protected onDragLeave(): void {
    this.dragDepth = Math.max(0, this.dragDepth - 1);
    if (this.dragDepth === 0) {
      this.dragOver.set(false);
    }
  }

  /** Feeds dropped files (with folder detection) to the shared engine. */
  protected onDrop(event: DragEvent): void {
    event.preventDefault();
    this.dragDepth = 0;
    this.dragOver.set(false);
    const transfer = event.dataTransfer;
    if (transfer === null) {
      return;
    }
    // Prefer the items list: it carries the directory information
    // (webkitGetAsEntry) that the flat files list loses.
    const files: File[] = [];
    const folderFlags: boolean[] = [];
    if (transfer.items.length > 0) {
      for (const item of Array.from(transfer.items)) {
        if (item.kind !== 'file') {
          continue;
        }
        const entry = item.webkitGetAsEntry?.();
        const file = item.getAsFile();
        if (file !== null) {
          files.push(file);
          folderFlags.push(entry?.isDirectory === true);
        }
      }
    } else {
      files.push(...Array.from(transfer.files));
    }
    if (files.length > 0) {
      this.picker.ɵprocess(files, folderFlags.length === files.length ? folderFlags : null);
    }
  }

  /** The focused zone accepts clipboard files (a screenshot). */
  protected onPaste(event: ClipboardEvent): void {
    const files = event.clipboardData === null ? [] : Array.from(event.clipboardData.files);
    if (files.length > 0) {
      event.preventDefault();
      this.picker.ɵprocess(files, null);
    }
  }
}
