// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import {
  booleanAttribute,
  Directive,
  effect,
  inject,
  input,
  type OnDestroy,
  output,
  signal,
  untracked,
} from '@angular/core';
import { LiveAnnouncer } from '@angular/cdk/a11y';

import { TM_UI_TRANSLATE } from '@tellma/core-ui';

import { tmMaxMegabytes } from './internal/byte-size';
import { tmSelectFiles } from './internal/file-selection';
import type { TmFileSelection } from './tm-file-selection';

/**
 * Turns a button into a file picker: clicking (or calling {@link open})
 * opens the OS file dialog through an internally managed hidden
 * `<input type="file">` that never receives focus — the button owns the
 * whole interaction.
 *
 * Every selection runs through the shared guardrail engine (`accept`,
 * `maxFileSize`, `maxFiles`/`multiple`) and emits a
 * {@link TmFileSelection}: native `File`s, upload is the consumer's job.
 * Rejections are announced via the CDK `LiveAnnouncer` and surfaced in
 * the output for the consumer's own UI. Client-side checks are UX
 * guardrails only — `File.type` is extension-derived and spoofable; real
 * validation (magic bytes, antivirus, limits) is the server's.
 *
 * @tmGroup files
 * @tmA11yNotes The hidden input is `aria-hidden` and untabbable; the
 *   visible button carries the keyboard interaction. Selection outcomes
 *   are announced politely.
 */
@Directive({
  selector: 'button[tmFilePicker]',
  host: {
    '(click)': 'open()',
    // Disabled, not just busy: the OS dialog is already on its way, and a
    // second click cannot bring it any sooner.
    '[attr.aria-busy]': 'awaitingDialog() ? "true" : null',
    '[attr.disabled]': 'awaitingDialog() ? "" : null',
  },
})
export class TmFilePicker implements OnDestroy {
  private readonly translate = inject(TM_UI_TRANSLATE);
  private readonly announcer = inject(LiveAnnouncer);

  /** Native accept string (extensions and MIME types), empty = any. */
  readonly accept = input<string>('');
  /** Whether more than one file can be selected. */
  readonly multiple = input(false, { transform: booleanAttribute });
  /** Per-file byte ceiling. Default 100 MB. */
  readonly maxFileSize = input(100 * 1024 * 1024);
  /** Selection-count ceiling (`null` = unbounded). */
  readonly maxFiles = input<number | null>(null);

  /** One guardrailed selection per dialog pick (or dropzone drop/paste). */
  readonly filesSelected = output<TmFileSelection>();

  private fileInput: HTMLInputElement | null = null;
  private destroyed = false;

  constructor() {
    // Keep the (lazily created) hidden input's dialog options current.
    effect(() => {
      const accept = this.accept();
      const multiple = this.multiple();
      if (this.fileInput !== null) {
        this.fileInput.accept = accept;
        this.fileInput.multiple = multiple;
      }
    });
  }

  /** Removes the body-appended hidden input. */
  ngOnDestroy(): void {
    this.destroyed = true;
    this.fileInput?.remove();
    this.fileInput = null;
  }

  /**
   * Whether the OS file dialog has been asked for and has not yet come
   * back. Opening it is the operating system's work, and on a loaded
   * machine that can take seconds during which nothing on the page
   * moves — the host reflects this as `aria-busy` and a wait cursor so
   * the click is visibly acknowledged.
   *
   * It clears on the window regaining focus, which happens whether the
   * user picked a file or dismissed the dialog; `change` alone would
   * stay stuck on a cancel.
   */
  readonly awaitingDialog = signal(false);

  /** Opens the OS file dialog. */
  open(): void {
    if (untracked(this.awaitingDialog)) {
      return; // a second click while the dialog is on its way
    }
    this.awaitingDialog.set(true);
    const settle = (): void => {
      window.removeEventListener('focus', settle);
      this.awaitingDialog.set(false);
    };
    window.addEventListener('focus', settle);
    this.ensureInput().click();
  }

  /**
   * Runs externally sourced files (a dropzone drop or paste) through the
   * same guardrail + announce + emit pipeline.
   * @internal
   */
  ɵprocess(files: readonly File[], folderFlags: readonly boolean[] | null): void {
    if (this.destroyed) {
      return; // an OS dialog that resolved after teardown
    }
    const maxFileSize = untracked(this.maxFileSize);
    const maxFiles = untracked(this.maxFiles);
    const selection = tmSelectFiles(files, folderFlags, {
      accept: untracked(this.accept),
      multiple: untracked(this.multiple),
      maxFileSize,
      maxFiles,
    });
    // The summary plus the first rejection's localized reason — every
    // rejection stays in the output for the consumer's own UI.
    const parts = [
      this.translate('filePicker.announce', {
        accepted: selection.accepted.length,
        rejectedCount: selection.rejected.length,
      })(),
    ];
    const first = selection.rejected[0];
    if (first !== undefined) {
      parts.push(
        this.translate(`filePicker.rejected.${first.reason}`, {
          name: first.file.name,
          maxMb: tmMaxMegabytes(maxFileSize),
          maxFiles: untracked(this.multiple) ? (maxFiles ?? 0) : 1,
        })(),
      );
    }
    void this.announcer.announce(parts.join(' '), 'polite');
    this.filesSelected.emit(selection);
  }

  /**
   * The hidden input lives in `document.body` (not the button — a click
   * on it must never re-trigger the host) and is created on first use.
   */
  private ensureInput(): HTMLInputElement {
    if (this.fileInput === null) {
      const element = document.createElement('input');
      element.type = 'file';
      element.accept = untracked(this.accept);
      element.multiple = untracked(this.multiple);
      element.tabIndex = -1;
      element.setAttribute('aria-hidden', 'true');
      element.style.display = 'none';
      element.addEventListener('change', () => {
        const files = element.files === null ? [] : [...element.files];
        element.value = '';
        if (files.length > 0) {
          this.ɵprocess(files, null);
        }
      });
      document.body.appendChild(element);
      this.fileInput = element;
    }
    return this.fileInput;
  }
}
