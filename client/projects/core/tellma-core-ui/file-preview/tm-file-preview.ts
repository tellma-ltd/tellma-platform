// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import { inject, Injectable } from '@angular/core';

import { TmModal, type TmModalRef } from '@tellma/core-ui/modal';

import { ɵTmFilePreviewContent } from './internal/tm-file-preview-content';

/** One file to preview. */
export interface TmPreviewFile {
  /** Drives kind detection (extension fallback) and the title bar. */
  readonly name: string;
  /** MIME type, when known (takes precedence over the extension). */
  readonly type?: string;
  /** Size in bytes, for the footer. */
  readonly size?: number;
  /**
   * The bytes, decoupled from the backend: a `Blob` already in memory
   * (the file-selection output), a lazy loader (the consumer fetches with
   * its own auth — interceptors/BFF cookies apply there), or `{ url }`
   * for STREAMABLE MEDIA — video/audio use the URL directly so the
   * browser range-requests instead of buffering whole blobs. URL sources
   * require ambient auth (cookies under a BFF, or presigned URLs under
   * bearer tokens). Every other kind needs bytes, so a `{ url }` text,
   * image-less or PDF source falls back to the download-only card — the
   * PDF viewer's frame is not sandboxed and may only ever render bytes
   * this component fetched and re-typed itself.
   */
  readonly source: Blob | (() => Promise<Blob>) | { readonly url: string };
}

/**
 * A modal file viewer (hosted on `tm-modal`, size `lg`): raster images
 * and SVG render via `<img>` (scripts never execute there), PDF through
 * the browser's own viewer when it has one, video/audio through native
 * elements with controls and no autoplay, and the plain-text allowlist in
 * an escaped `<pre>`. HTML and everything undetected are NEVER rendered —
 * a blob URL inherits the app origin, so user-authored active content is
 * a same-origin XSS vector; those kinds get the download-only card by
 * policy, not limitation.
 *
 * @example
 * ```ts
 * const ref = this.preview.open({
 *   name: attachment.fileName,
 *   type: attachment.mimeType,
 *   size: attachment.sizeBytes,
 *   source: () => this.api.downloadAttachment(attachment.id),
 * });
 * await ref.closed;
 * ```
 */
@Injectable({ providedIn: 'root' })
export class TmFilePreview {
  private readonly modal = inject(TmModal);

  /** Opens the viewer; the returned ref resolves when it closes. */
  open(file: TmPreviewFile): TmModalRef<void> {
    return this.modal.open<void>(ɵTmFilePreviewContent, {
      size: 'lg',
      title: file.name,
      data: file,
      // Full-bleed: every pixel the chrome does not need belongs to the
      // file being looked at.
      panelClass: 'tm-modal-panel--flush',
    });
  }
}
