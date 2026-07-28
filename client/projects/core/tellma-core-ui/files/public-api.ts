/**
 * Public API Surface of @tellma/core-ui/files — file selection: the
 * `tmFilePicker` button directive and the `tm-dropzone` drop target, two
 * surfaces over one guardrail engine emitting native `File` selections
 * (upload is the consumer's job).
 *
 * ## Transport guidance
 *
 * The components stop at `File`/`Blob` outputs, which feed either
 * sanctioned transport unchanged:
 *
 * - **Inline multipart save** — `multipart/form-data` with a JSON DTO part
 *   plus one binary part per blob (native `FormData`: streaming, real
 *   progress events, no base64 +33%/memory penalty). The default for
 *   record images and small attachment sets: the save stays one atomic
 *   request and an abandoned draft needs no server cleanup.
 *   Base64-in-JSON is reserved for reliably-tiny payloads.
 * - **Staged upload** — files upload on selection to a staging area and
 *   the record save references staged ids (per-file progress and retry,
 *   early server-side validation, a lean final save). Staged blobs carry
 *   a TTL comfortably above any in-memory draft's realistic lifetime, a
 *   server job garbage-collects expired ones, and the client best-effort
 *   deletes its staged uploads on explicit draft abandonment. The
 *   sanctioned path where file sizes or counts make inline saves slow.
 *
 * A distribution picks per screen; both shapes consume this entry point's
 * output as-is.
 *
 * @packageDocumentation
 */

// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.
export { TmFilePicker } from './tm-file-picker';
export { TmDropzone } from './tm-dropzone';
export type { TmFileRejectionReason, TmFileSelection } from './tm-file-selection';
