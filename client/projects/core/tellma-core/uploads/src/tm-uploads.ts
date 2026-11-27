// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import { computed, DestroyRef, inject, Injectable, signal, type Signal } from '@angular/core';
import type { TmBlobDescriptor } from '@tellma/core';

// A form's uploads (spec 0029 §12.1): the image and file editors upload a
// pick as soon as it is made, and list its field's path here until the
// upload settles, so the form's schema reports the path pending and Save
// waits for it. The blob an upload staged stays here by path, for previews
// and drafts, and the share each upload has sent, for its progress ring.
// A child row's path holds its index (`attachments.2.fileId`), so removing
// a row moves the later rows' entries up with their rows (spec 0031 §6.5).
// A form's host provides one; an editor outside any host uploads alone.

/** An upload in flight: what aborts it, and the share of the file it has sent. */
interface TmRunning {
  /** Aborts the upload. */
  readonly controller: AbortController;
  /** The share sent, from 0 to 1; `null` before the first progress report. */
  readonly sent: number | null;
}

/** A child row's path: the collection, the row's index, and the member under the row. */
const ROW_PATH = /^(.*?)\.(\d+)(\..+)$/;

/**
 * The uploads of one form: the paths with an upload in flight, the share
 * each has sent, and the blob each path's last upload staged. Provided by a
 * form's host.
 *
 * @example
 * ```ts
 * @Component({ providers: [TmUploads], template: '<tm-value-editor [property]="image" [field]="f.imageId" path="imageId" />' })
 * class Details {
 *   readonly uploads = inject(TmUploads);
 *   readonly saving = computed(() => this.uploads.running().size > 0);
 * }
 * ```
 */
@Injectable()
export class TmUploads {
  /** The uploads in flight, by path. */
  readonly #running = signal<ReadonlyMap<string, TmRunning>>(new Map());

  /** The blob each path's last upload staged. */
  readonly #staged = signal<ReadonlyMap<string, TmBlobDescriptor>>(new Map());

  /** The paths with an upload in flight. */
  readonly running: Signal<ReadonlySet<string>> = computed(() => new Set(this.#running().keys()));

  constructor() {
    // A host that goes away takes its uploads with it.
    inject(DestroyRef).onDestroy(() => this.#running().forEach((running) => running.controller.abort()));
  }

  /**
   * The blob staged for a path: what its last upload returned, until the
   * path is cleared.
   *
   * @param path - The field's path, such as `imageId`.
   * @returns The blob, or `undefined`.
   */
  staged(path: string): TmBlobDescriptor | undefined {
    return this.#staged().get(path);
  }

  /**
   * How far a path's upload has got: the share of the file sent, from 0
   * to 1, read in a reactive context to follow it.
   *
   * @param path - The field's path.
   * @returns The share; `null` while the upload has reported none, and when no upload runs for the path.
   */
  progress(path: string): number | null {
    return this.#running().get(path)?.sent ?? null;
  }

  /**
   * Runs an upload for a path, listing the path as running until it
   * settles. A new upload for a path aborts the one in flight there, whose
   * result is then ignored. A successful upload stages its blob, under the
   * path its row has moved to if a row before it was removed meanwhile.
   *
   * @param path - The field's path.
   * @param upload - The upload, given the signal that aborts it and the callback that reports the share sent.
   * @returns The staged blob.
   * @throws The upload's error, or an `AbortError` when a later upload, a removal, `cancelAll` or the host aborted it.
   */
  async start(path: string, upload: (signal: AbortSignal, progress: (sent: number) => void) => Promise<TmBlobDescriptor>): Promise<TmBlobDescriptor> {
    this.cancel(path);
    const controller = new AbortController();
    this.#running.update((running) => new Map(running).set(path, { controller, sent: null }));
    const report = (sent: number): void => {
      const at = this.#pathOf(controller);
      if (at !== null && !controller.signal.aborted) {
        this.#running.update((running) => new Map(running).set(at, { controller, sent: Math.min(1, Math.max(0, sent)) }));
      }
    };
    try {
      const blob = await upload(controller.signal, report);
      controller.signal.throwIfAborted();
      this.stage(this.#pathOf(controller) ?? path, blob);
      return blob;
    } finally {
      // Only the upload's own entry leaves, wherever its row has moved; a later upload has replaced an aborted one.
      const at = this.#pathOf(controller);
      if (at !== null) {
        this.#running.update((running) => {
          const next = new Map(running);
          next.delete(at);
          return next;
        });
      }
    }
  }

  /**
   * Aborts the upload in flight for a path, if any.
   *
   * @param path - The field's path.
   */
  cancel(path: string): void {
    const running = this.#running().get(path);
    if (running !== undefined) {
      running.controller.abort();
      this.#running.update((entries) => {
        const next = new Map(entries);
        next.delete(path);
        return next;
      });
    }
  }

  /**
   * Discards every upload of the form, as Cancel does with its changes:
   * each upload in flight aborts, so its result is ignored, and every
   * staged blob clears.
   *
   * @example
   * ```ts
   * uploads.cancelAll(); // running() is empty and staged('imageId') is undefined
   * ```
   */
  cancelAll(): void {
    this.#running().forEach((running) => running.controller.abort());
    this.#running.set(new Map());
    this.#staged.set(new Map());
  }

  /**
   * Stages a blob for a path, as a successful upload does, or clears it.
   * A host restoring a draft stages the blobs the draft held.
   *
   * @param path - The field's path.
   * @param blob - The blob, or `null` to clear the path.
   */
  stage(path: string, blob: TmBlobDescriptor | null): void {
    this.#staged.update((staged) => {
      const next = new Map(staged);
      if (blob === null) {
        next.delete(path);
      } else {
        next.set(path, blob);
      }
      return next;
    });
  }

  /**
   * Follows the removal of a child row: the row's uploads abort and its
   * staged blobs clear, and every later row's entries move up one index
   * with their row, as the form's paths do.
   *
   * @param collection - The collection's path, such as `attachments`.
   * @param index - The removed row's index.
   *
   * @example
   * ```ts
   * uploads.removeRow('attachments', 0); // attachments.1.fileId's upload is now attachments.0.fileId's
   * ```
   */
  removeRow(collection: string, index: number): void {
    // --- The removed row's uploads abort first, so their settling reports nothing ---
    for (const [path, running] of this.#running()) {
      if (rowOf(path, collection)?.index === index) {
        running.controller.abort();
      }
    }
    this.#running.update((running) => shifted(running, collection, index));
    this.#staged.update((staged) => shifted(staged, collection, index));
  }

  /**
   * The path an upload in flight is listed under now.
   *
   * @param controller - The upload's controller.
   * @returns The path, or `null` once it is no longer listed.
   */
  #pathOf(controller: AbortController): string | null {
    for (const [path, running] of this.#running()) {
      if (running.controller === controller) {
        return path;
      }
    }
    return null;
  }
}

/**
 * A path's place in a collection's rows.
 *
 * @param path - The path, such as `attachments.2.fileId`.
 * @param collection - The collection's path.
 * @returns The row's index and the member under it, or `null` for a path outside the collection's rows.
 */
function rowOf(path: string, collection: string): { readonly index: number; readonly rest: string } | null {
  const match = ROW_PATH.exec(path);
  return match === null || match[1] !== collection ? null : { index: Number(match[2]), rest: match[3]! };
}

/**
 * A map by path after a row's removal: the row's entries gone, later rows' moved up one index.
 *
 * @param entries - The entries, by path.
 * @param collection - The collection's path.
 * @param removed - The removed row's index.
 * @returns The entries after the removal.
 */
function shifted<V>(entries: ReadonlyMap<string, V>, collection: string, removed: number): ReadonlyMap<string, V> {
  const next = new Map<string, V>();
  for (const [path, value] of entries) {
    const row = rowOf(path, collection);
    if (row === null || row.index < removed) {
      next.set(path, value);
    } else if (row.index > removed) {
      next.set(`${collection}.${row.index - 1}${row.rest}`, value);
    }
  }
  return next;
}
