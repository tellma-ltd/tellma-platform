// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import { copyFileSync, mkdirSync, rmSync, writeFileSync } from 'node:fs';
import { join } from 'node:path';

import type { TmBrandFile } from '../checks/brand.js';
import { TM_BOOT_SCRIPT_FILE } from '../build/index-html-rules.js';

// The files the commands write at the root of what the browser loads: the
// rendered tm-boot.js and the brand files (spec 0024 §4.2, §7.6). `build`
// writes them into its output; `serve` writes the boot script and the default
// brand files to generated/root/, which the application's angular.json copies
// to the output root, so the dev server serves them as the built app does
// (the overrides already come from public/brand/). `build` refreshes that
// folder too, so a production build never copies a stale one.

/**
 * Writes `tm-boot.js` and the brand files a folder serves at its root.
 *
 * @param dir - The folder.
 * @param bootScript - The rendered boot script.
 * @param brand - The resolved brand files; a name with no source is left out.
 */
export function tmWriteRootFiles(dir: string, bootScript: string, brand: readonly TmBrandFile[]): void {
  mkdirSync(join(dir, 'brand'), { recursive: true });
  for (const file of brand) {
    if (file.source !== null) {
      copyFileSync(file.source, join(dir, 'brand', file.name));
    }
  }
  writeFileSync(join(dir, TM_BOOT_SCRIPT_FILE), bootScript);
}

/**
 * Replaces the generated root folder's files with these. The overriding
 * brand files are left out: they sit in `public/brand/`, which the
 * application already copies, and two assets must not claim one output path.
 *
 * @param dir - The generated root folder, `generated/root`.
 * @param bootScript - The rendered boot script.
 * @param brand - The resolved brand files.
 */
export function tmRefreshRootFiles(dir: string, bootScript: string, brand: readonly TmBrandFile[]): void {
  rmSync(dir, { recursive: true, force: true });
  tmWriteRootFiles(
    dir,
    bootScript,
    brand.filter((file) => !file.override),
  );
}
