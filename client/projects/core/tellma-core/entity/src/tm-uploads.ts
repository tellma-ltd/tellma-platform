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
// and drafts. A form's host provides one; an editor outside any host
// uploads alone.

/**
 * The uploads of one form: the paths with an upload in flight and the blob
 * each path's last upload staged. Provided by a form's host.
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
  /** The uploads in flight, by path, each with the controller that aborts it. */
  readonly #running = signal<ReadonlyMap<string, AbortController>>(new Map());

  /** The blob each path's last upload staged. */
  readonly #staged = signal<ReadonlyMap<string, TmBlobDescriptor>>(new Map());

  /** The paths with an upload in flight. */
  readonly running: Signal<ReadonlySet<string>> = computed(() => new Set(this.#running().keys()));

  constructor() {
    // A host that goes away takes its uploads with it.
    inject(DestroyRef).onDestroy(() => this.#running().forEach((controller) => controller.abort()));
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
   * Runs an upload for a path, listing the path as running until it
   * settles. A new upload for a path aborts the one in flight there, whose
   * result is then ignored. A successful upload stages its blob.
   *
   * @param path - The field's path.
   * @param upload - The upload, given the signal that aborts it.
   * @returns The staged blob.
   * @throws The upload's error, or an `AbortError` when a later upload or the host aborted it.
   */
  async start(path: string, upload: (signal: AbortSignal) => Promise<TmBlobDescriptor>): Promise<TmBlobDescriptor> {
    this.cancel(path);
    const controller = new AbortController();
    this.#running.update((running) => new Map(running).set(path, controller));
    try {
      const blob = await upload(controller.signal);
      controller.signal.throwIfAborted();
      this.stage(path, blob);
      return blob;
    } finally {
      // Only the path's own upload clears it; a later one has replaced it.
      if (this.#running().get(path) === controller) {
        this.#running.update((running) => {
          const next = new Map(running);
          next.delete(path);
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
    const controller = this.#running().get(path);
    if (controller !== undefined) {
      controller.abort();
      this.#running.update((running) => {
        const next = new Map(running);
        next.delete(path);
        return next;
      });
    }
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
}
