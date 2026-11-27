/**
 * Public API Surface of @tellma/core/support — the support tools that load
 * on demand: the support dump's builder and its download, the Diagnostics
 * panel the About dialog shows, and the support dialog's content. The
 * support facade, `TmSupport`, is the primary's.
 *
 * @packageDocumentation
 */

// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

// --- The dump, its size cap, and the diagnostic file ---
export {
  TM_SUPPORT_DUMP_MAX_BYTES,
  tmBuildSupportDump,
  tmDownloadSupportDump,
  tmSupportDumpFileName,
  type TmSupportDump,
  type TmSupportDumpContexts,
  type TmSupportDumpOptions,
} from './src/tm-support-dump';

// --- The Diagnostics panel and the support dialog's content ---
export { TmDiagnosticsPanel } from './src/tm-diagnostics-panel';
export { TmSupportDialog } from './src/tm-support-dialog';
