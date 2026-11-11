/**
 * Public API Surface of @tellma/core/config-check — the check of an app's
 * configuration against the client metadata.
 *
 * The build runs it in Node after the metadata emit, and the app runs it at
 * startup in development; a production app never loads it.
 *
 * @packageDocumentation
 */

// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

export { tmCheckAppConfig } from './src/tm-check-app-config';
export type { TmConfigProblem, TmConfigProblemCode } from './src/tm-config-problem';
