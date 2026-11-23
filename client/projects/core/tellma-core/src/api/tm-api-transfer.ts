// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import type { TmDownload } from './tm-api';

// The HTTP layer's file transfers (spec 0024 §10.2): a download's streamed
// read with its progress and the file name from `Content-Disposition`, and
// an upload's send over XMLHttpRequest with its progress. Only the HTTP
// layer loads this file, with import(), at the first download's answer or
// the first upload's send: no first-frame call transfers a file, so it is a
// chunk of its own rather than part of the primary entry point. A chunk that
// cannot load fails the attempt as a network failure would.

/**
 * Reads a download's body: streams it, reporting the bytes received and
 * restarting the watchdog at each chunk, and names the file from
 * `Content-Disposition`.
 *
 * @param response - The successful response.
 * @param rearm - Restarts the call's watchdog.
 * @param onProgress - Receives the bytes received so far.
 * @returns The file.
 */
export async function tmReadDownload(
  response: Response,
  rearm: () => void,
  onProgress: ((bytes: number) => void) | undefined,
): Promise<TmDownload> {
  const type = response.headers.get('Content-Type') ?? '';
  const chunks: Uint8Array<ArrayBuffer>[] = [];
  if (response.body !== null) {
    const reader = response.body.getReader();
    let bytes = 0;
    for (;;) {
      const { done, value } = await reader.read();
      if (done) {
        break;
      }
      chunks.push(value);
      bytes += value.byteLength;
      // A slow export that keeps sending is alive: only a stall trips the watchdog.
      rearm();
      onProgress?.(bytes);
    }
  }
  const blob = new Blob(chunks, { type });
  return {
    fileName: tmDispositionFileName(response.headers.get('Content-Disposition')) ?? 'download',
    contentType: blob.type,
    blob,
  };
}

/**
 * The file name a `Content-Disposition` header gives, `filename*` first.
 *
 * @param value - The header's value.
 * @returns The file name, or `null` when the header gives none.
 */
export function tmDispositionFileName(value: string | null): string | null {
  if (value === null) {
    return null;
  }
  // RFC 6266: filename* (RFC 8187, charset''percent-encoded) wins over filename.
  const extended = /filename\*\s*=\s*([^']*)'[^']*'([^;]+)/i.exec(value);
  if (extended !== null) {
    try {
      return decodeURIComponent(extended[2].trim());
    } catch {
      // A malformed encoding falls through to the plain name.
    }
  }
  const plain = /filename\s*=\s*(?:"((?:[^"\\]|\\.)*)"|([^;]+))/i.exec(value);
  if (plain === null) {
    return null;
  }
  return plain[1] !== undefined ? plain[1].replace(/\\(.)/g, '$1') : plain[2].trim();
}

/** The parts of an upload the send reads. */
export interface TmFileSend {
  /** The URL sent to. */
  readonly url: string;
  /** The watchdog, in milliseconds, restarted at each progress event. */
  readonly watchdogMs: number;
  /** Cancels the upload, or `null`. */
  readonly cancel: AbortSignal | null;
}

/**
 * Sends a file over `XMLHttpRequest`, which reports the upload's progress.
 * The watchdog restarts at each progress event: a slow upload that keeps
 * sending is alive, while a stalled one, or a lost answer, ends.
 *
 * @param xhr - The request to send with.
 * @param call - The URL, the watchdog and the cancellation.
 * @param headers - The request headers, `Content-Type` among them.
 * @param file - The file.
 * @param onProgress - Receives the share of the file sent.
 * @param answered - Judges an answer from its status, headers and body text.
 * @param unanswered - Judges a send that got no answer, given whether the watchdog ended it.
 * @returns What the judges made of the attempt.
 */
export function tmSendFile<A>(
  xhr: XMLHttpRequest,
  call: TmFileSend,
  headers: Readonly<Record<string, string>>,
  file: Blob,
  onProgress: ((fraction: number) => void) | undefined,
  answered: (status: number, header: (name: string) => string | null, text: string) => Promise<A>,
  unanswered: (timedOut: boolean) => A,
): Promise<A> {
  return new Promise((resolve) => {
    // The caller may have cancelled while this chunk loaded.
    if (call.cancel?.aborted === true) {
      resolve(unanswered(false));
      return;
    }
    const abort = (): void => xhr.abort();
    let timedOut = false;
    let timer: ReturnType<typeof setTimeout> | undefined;
    const rearm = (): void => {
      clearTimeout(timer);
      timer = setTimeout(() => {
        timedOut = true;
        xhr.abort();
      }, call.watchdogMs);
    };
    const settle = (attempt: Promise<A> | A): void => {
      clearTimeout(timer);
      call.cancel?.removeEventListener('abort', abort);
      resolve(attempt);
    };
    xhr.open('POST', call.url);
    for (const [name, value] of Object.entries(headers)) {
      xhr.setRequestHeader(name, value);
    }
    xhr.upload.onprogress = (event): void => {
      rearm();
      if (event.lengthComputable && event.total > 0) {
        onProgress?.(event.loaded / event.total);
      }
    };
    xhr.onload = (): void => settle(answered(xhr.status, (name) => xhr.getResponseHeader(name), xhr.responseText));
    xhr.onerror = (): void => settle(unanswered(false));
    xhr.onabort = (): void => settle(unanswered(timedOut));
    call.cancel?.addEventListener('abort', abort, { once: true });
    rearm();
    xhr.send(file);
  });
}
