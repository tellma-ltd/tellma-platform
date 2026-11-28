/**
 * Public API Surface of @tellma/core/uploads — a form's uploads: the paths
 * with an upload in flight and the blob each path's last upload staged,
 * which a form's host provides and the image and file editors list their
 * uploads in. The details page's record provides one in every mode, and the
 * edit side's value editor reports to the nearest one. It is an entry point
 * of its own so that neither the view side nor the search page carries it:
 * it loads with the details page or the edit side, whichever imports it
 * first.
 *
 * @packageDocumentation
 */

// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

// A form's uploads.
export { TmUploads } from './src/tm-uploads';
// What a failed upload says on its field.
export { tmUploadFailure, type TmUploadFailure } from './src/tm-upload-failure';
