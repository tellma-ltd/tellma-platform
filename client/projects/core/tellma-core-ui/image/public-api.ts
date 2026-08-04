/**
 * Public API Surface of @tellma/core-ui/image — `tm-image`: record images
 * in a fixed box with a cached, deferred fetch pipeline, plus an edit mode
 * (replace / re-fit / delete) that emits normalized fits for server-side
 * cropping.
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
export { TmImage, TmImagePlaceholder } from './tm-image';
export type { TmImageEdit, TmImageFit } from './tm-image-types';
