// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import { Component, signal } from '@angular/core';

import { TmButton } from '@tellma/core-ui/button';
import { TmDropzone, TmFilePicker, type TmFileSelection } from '@tellma/core-ui/files';

/**
 * tmFilePicker + tm-dropzone demo host — the toolbar button, a
 * guardrailed multi-file dropzone, and a single-file dropzone, with a
 * selection readout. Drives the Playwright battery (file dialog,
 * synthetic drops, paste, missed-drop guard, announcements).
 */
@Component({
  imports: [TmButton, TmDropzone, TmFilePicker],
  template: `
    <h2>Files</h2>

    <section>
      <h3>Picker button</h3>
      <button
        tmButton
        variant="secondary"
        tmFilePicker
        accept=".txt,.csv,image/*"
        multiple
        data-testid="attach-button"
        (filesSelected)="record($event)"
      >
        Attach
      </button>
    </section>

    <section>
      <h3>Dropzone (multiple, max 3, up to 1 MB)</h3>
      <tm-dropzone
        data-testid="dropzone"
        accept=".txt,.csv,image/*"
        multiple
        [maxFiles]="3"
        [maxFileSize]="1024 * 1024"
        (filesSelected)="record($event)"
      />
    </section>

    <section>
      <h3>Dropzone (single file)</h3>
      <tm-dropzone data-testid="dropzone-single" (filesSelected)="record($event)" />
    </section>

    <section>
      <h3>Last selection</h3>
      <output data-testid="selection-readout">{{ readout() }}</output>
    </section>
  `,
  styles: `
    section {
      margin-block-end: 24px;
    }
    tm-dropzone {
      max-inline-size: 420px;
    }
  `,
})
export class FilesStory {
  readonly readout = signal('none');

  protected record(selection: TmFileSelection): void {
    this.readout.set(
      JSON.stringify({
        accepted: selection.accepted.map((file) => file.name),
        rejected: selection.rejected.map((rejection) => ({
          name: rejection.file.name,
          reason: rejection.reason,
        })),
      }),
    );
  }
}
