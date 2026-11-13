// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import { mkdirSync, renameSync, rmSync } from 'node:fs';
import { dirname, join } from 'node:path';

import { tmListFiles } from '../common/files.js';

// The hidden source maps (spec 0024 §2.6, step 5). The application builder
// writes each map beside its file without a sourceMappingURL comment; the
// build moves them out of the deployable output, keeping their relative
// paths, so the server never serves them and CI archives them per UI version.

/**
 * Moves every `.map` file from the deployable output to the source-map folder.
 *
 * @param browserDir - The deployable output.
 * @param sourceMapsDir - The folder outside it; emptied first.
 * @returns The moved files, relative to both folders.
 */
export function tmMoveSourceMaps(browserDir: string, sourceMapsDir: string): string[] {
  rmSync(sourceMapsDir, { recursive: true, force: true });
  const maps = tmListFiles(browserDir).filter((file) => file.endsWith('.map'));
  for (const file of maps) {
    const target = join(sourceMapsDir, ...file.split('/'));
    mkdirSync(dirname(target), { recursive: true });
    renameSync(join(browserDir, ...file.split('/')), target);
  }
  return maps;
}
