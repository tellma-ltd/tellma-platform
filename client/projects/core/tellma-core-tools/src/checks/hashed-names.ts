// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import type { TmBuildProblem } from '../common/problems.js';

// The hashed-name check (spec 0024 §6). The server serves JavaScript, CSS,
// fonts and every tm-metadata/ file as immutable for a year, which is safe
// only when the name changes with the content: a `-` and at least eight
// [A-Z0-9] before the extension. The worker's scripts and tm-boot.js are
// served `no-cache` instead, so they keep their plain names.

/** The build problem code of a file that needs a hashed name and lacks one. */
export const TM_UNHASHED_PROBLEM = 'unhashed-asset';

/** The scripts served `no-cache`, which keep their plain names. */
export const TM_UNHASHED_EXEMPT: readonly string[] = Object.freeze(['ngsw-worker.js', 'safety-worker.js', 'tm-boot.js']);

/** The extensions of files served immutable. */
const IMMUTABLE = /\.(?:m?js|css|woff2?|ttf|otf|eot)$/i;

/** A hashed name: a `-` and eight or more [A-Z0-9] before the extension. */
const HASHED = /-[A-Z0-9]{8,}\.[^./]+$/;

/**
 * Checks the deployable output's file names.
 *
 * @param files - Every deployable file, relative to the output root, source maps already moved out.
 * @param feature - The distribution's slug.
 * @returns One `unhashed-asset` problem per file.
 */
export function tmCheckHashedNames(files: readonly string[], feature: string): TmBuildProblem[] {
  return files
    .filter((file) => !TM_UNHASHED_EXEMPT.includes(file))
    .filter((file) => IMMUTABLE.test(file) || file.startsWith('tm-metadata/'))
    .filter((file) => !HASHED.test(file.split('/').pop()!))
    .map((file) => ({
      code: TM_UNHASHED_PROBLEM,
      severity: 'error',
      key: file,
      feature,
      message: 'The file is served immutable, so its name needs a content hash: a - and eight or more [A-Z0-9] before the extension.',
    }));
}
