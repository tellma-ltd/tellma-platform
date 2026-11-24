/**
 * Public API Surface of @tellma/core-ui/blob-fetcher — `TM_BLOB_FETCHER`,
 * the seam through which `tm-image` fetches every blob, with its default
 * over `HttpClient`. It is an entry point of its own so an app's startup
 * can provide the seam without reaching `tm-image`, whose code would
 * otherwise join the first frame as soon as any lazily loaded page shows an
 * image. `@tellma/core-ui/image` re-exports it.
 *
 * @packageDocumentation
 */

// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.
export {
  TM_BLOB_FETCHER,
  type TmBlobFetcher,
  type TmBlobFetchOptions,
  type TmBlobFetchResult,
} from './tm-blob-fetcher';
