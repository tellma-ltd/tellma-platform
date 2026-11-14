// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import { Component, inject } from '@angular/core';

import { TmAlert } from '@tellma/core-ui/alert';
import { TmButton } from '@tellma/core-ui/button';
import { TmDatePicker } from '@tellma/core-ui/date-picker';
import { TmFilePreview } from '@tellma/core-ui/file-preview';
import { TmDropzone } from '@tellma/core-ui/files';
import { TmFormField } from '@tellma/core-ui/form-field';
import { TmImage } from '@tellma/core-ui/image';
import { TmInput } from '@tellma/core-ui/input';
import { TmModal } from '@tellma/core-ui/modal';

/** A modal body with nothing in it but text: the battery measures its close glyph. */
@Component({
  template: `<p>The close button carries the X glyph.</p>`,
})
export class BasicsGlyphsModalContent {}

/**
 * The basic components' built-in glyphs side by side, each in the state that
 * draws it: the four alert tones; the date picker's invalid mark, calendar
 * button and (once open) popup chevrons; the dropzone's upload glyph; the
 * image's error, empty, edit-chrome and edit-pane glyphs; the modal's close
 * X; the file preview's two cards; and the validation bubble's mark. The
 * Playwright battery pins every glyph's box and stroke here, in both
 * directions, and the popup chevrons across a live direction flip.
 */
@Component({
  imports: [TmAlert, TmButton, TmDatePicker, TmDropzone, TmFormField, TmImage, TmInput],
  template: `
    <h2>Basics glyphs</h2>

    <!-- Alerts: one per tone, heading-less, so each glyph centres on its box. -->
    <section class="stack">
      <tm-alert kind="info" data-testid="alert-info">Information</tm-alert>
      <tm-alert kind="success" data-testid="alert-success">Saved</tm-alert>
      <tm-alert kind="warning" data-testid="alert-warning">Not posted yet</tm-alert>
      <tm-alert kind="error" data-testid="alert-error">Could not save</tm-alert>
    </section>

    <!-- Date picker, invalid: the mark beside the text and the calendar
         button; the popup's chevrons show once the button opens it. -->
    <section class="fields">
      <tm-form-field label="Due date" data-testid="ff-date">
        <tm-date-picker invalid touched value="2026-03-15" data-testid="date" />
      </tm-form-field>

      <!-- A field error: focusing the input shows the validation bubble. -->
      <tm-form-field label="Account" error="Pick an account" data-testid="ff-error">
        <input tmInput value="4110" data-testid="ff-error-input" />
      </tm-form-field>
    </section>

    <!-- The dropzone's upload glyph. -->
    <section class="fields">
      <tm-dropzone data-testid="dropzone" />
    </section>

    <!-- Image: a failed load, an empty box, and the edit chrome over a
         loaded image (its adjust action opens the pane with the check). -->
    <section class="row">
      <tm-image
        data-testid="image-error"
        src="/missing-image.png"
        alt="Broken"
        [width]="96"
        [height]="96"
        [defer]="false"
      />
      <tm-image data-testid="image-empty" src="" alt="" [width]="96" [height]="96" [defer]="false" />
      <tm-image
        data-testid="image-edit"
        mode="edit"
        src="/test-image.png"
        editSrc="/test-image.png"
        etag="v1"
        alt="Test pattern"
        [width]="160"
        [height]="160"
        [defer]="false"
      />
    </section>

    <!-- The modal's close X and the file preview's two cards. -->
    <section class="row">
      <button tmButton data-testid="open-modal" (click)="openModal()">Open a modal</button>
      <button tmButton data-testid="preview-unsupported" (click)="previewUnsupported()">
        Preview an archive
      </button>
      <button tmButton data-testid="preview-error" (click)="previewFailing()">
        Preview a failing file
      </button>
    </section>
  `,
  styles: `
    section {
      margin-block-end: 16px;
    }
    .stack {
      display: grid;
      gap: 8px;
      max-inline-size: 420px;
    }
    .fields {
      display: grid;
      gap: 16px;
      max-inline-size: 320px;
    }
    .row {
      display: flex;
      align-items: center;
      gap: 12px;
    }
  `,
})
export class BasicsGlyphsStory {
  private readonly modal = inject(TmModal);
  private readonly preview = inject(TmFilePreview);

  /** Opens a titled modal, which draws the close X. */
  protected openModal(): void {
    this.modal.open(BasicsGlyphsModalContent, { size: 'sm', title: 'Glyphs' });
  }

  /** Previews a kind with no viewer, which draws the document card. */
  protected previewUnsupported(): void {
    this.preview.open({
      name: 'archive.bin',
      type: 'application/octet-stream',
      size: 3,
      source: new Blob(['abc'], { type: 'application/octet-stream' }),
    });
  }

  /** Previews a file whose loader fails, which draws the error card. */
  protected previewFailing(): void {
    this.preview.open({
      name: 'report.pdf',
      type: 'application/pdf',
      source: () => Promise.reject(new Error('The download failed.')),
    });
  }
}
