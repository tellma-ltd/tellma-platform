// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import { computed, type Signal } from '@angular/core';
import { TmApiError, type TmProblemError, type TmServerText } from '@tellma/core';

// What a failed upload says on its field (spec 0031 §6.3 step 5): an
// abort says nothing; a file the server refuses (`blob-rejected`, or any
// other answer in the 4xx range with a problem) says why, through the
// server's text for its first item, else the problem's title; anything
// else may pass on a retry, which the field offers.

/** What a failed upload says on its field. */
export type TmUploadFailure =
  /** A later pick, a removal or the host aborted it: it says nothing. */
  | { readonly kind: 'aborted' }
  /** The server refused the file: the reason, in the active language. */
  | { readonly kind: 'rejected'; readonly message: Signal<string> }
  /** A failure a retry may mend: a server error, a lost connection, a timeout. */
  | { readonly kind: 'failed' };

/**
 * Reads a failed upload's error into what its field says.
 *
 * @param error - What the upload rejected with.
 * @param text - The server's texts.
 * @returns The failure.
 *
 * @example
 * ```ts
 * const failure = tmUploadFailure(error, inject(TmServerText));
 * if (failure.kind === 'rejected') {
 *   errors.set([{ kind: 'upload', message: failure.message() }]);
 * }
 * ```
 */
export function tmUploadFailure(error: unknown, text: TmServerText): TmUploadFailure {
  if ((error instanceof DOMException && error.name === 'AbortError') || (error instanceof TmApiError && error.kind === 'cancelled')) {
    return { kind: 'aborted' };
  }
  const problem = error instanceof TmApiError && error.status >= 400 && error.status < 500 ? error.problem : null;
  if (problem === null) {
    return { kind: 'failed' };
  }
  // The first item names the reason (Blob.TooLarge, Blob.UnsupportedType, Blob.ImageRejected …); the title is the fallback.
  const items = problem['errors'];
  const item = Array.isArray(items) ? (items[0] as TmProblemError | undefined) : undefined;
  if (item !== undefined && typeof item === 'object' && item !== null && typeof item.code === 'string') {
    return { kind: 'rejected', message: text.error(item) };
  }
  const title = text.problem(problem);
  return { kind: 'rejected', message: computed(() => title().title) };
}
