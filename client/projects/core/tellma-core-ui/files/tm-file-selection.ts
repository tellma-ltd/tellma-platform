// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

/** Why a file was rejected by the selection guardrails. */
export type TmFileRejectionReason = 'size' | 'type' | 'count' | 'folder';

/**
 * The outcome of one selection (dialog pick, drop, or paste) — native
 * `File` objects, fully decoupled from the backend; upload is the
 * consumer's job.
 *
 * Client-side checks are UX guardrails only (fail fast, honest error
 * copy): `File.type` is extension-derived and spoofable, so real
 * validation — magic bytes, antivirus, limits — belongs to the server.
 */
export interface TmFileSelection {
  /** The files that passed every guardrail, in selection order. */
  accepted: File[];
  /** The files that did not, each with its reason. */
  rejected: { file: File; reason: TmFileRejectionReason }[];
}
