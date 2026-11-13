// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import { existsSync, mkdirSync, readFileSync, rmSync, writeFileSync } from 'node:fs';
import { dirname, join } from 'node:path';

import type { TmBrandFile } from '../checks/brand.js';
import { TM_BOOT_SCRIPT_FILE } from '../build/index-html-rules.js';
import { tmListFiles } from '../common/files.js';

// The files the commands write at the root of what the browser loads: the
// rendered tm-boot.js and the brand files (spec 0024 §4.2, §7.6). `build`
// writes them into its output; `serve` writes the boot script and the default
// brand files to generated/root/, which the application's angular.json copies
// to the output root, so the dev server serves them as the built app does
// (the overrides already come from public/brand/). `build` refreshes that
// folder too, so a production build never copies a stale one. The refresh
// works in place, since a running dev server watches the folder: it writes a
// file only when its bytes change and removes only the files that no longer
// belong.

/** The files a folder serves at its root, by path relative to it with forward slashes. */
function rootFiles(bootScript: string, brand: readonly TmBrandFile[]): Map<string, Buffer> {
  const files = new Map<string, Buffer>();
  for (const file of brand) {
    if (file.source !== null) {
      files.set(`brand/${file.name}`, readFileSync(file.source));
    }
  }
  files.set(TM_BOOT_SCRIPT_FILE, Buffer.from(bootScript, 'utf8'));
  return files;
}

/** Writes a file unless it already holds these bytes. */
function writeIfChanged(file: string, bytes: Buffer): void {
  if (existsSync(file) && readFileSync(file).equals(bytes)) {
    return;
  }
  mkdirSync(dirname(file), { recursive: true });
  writeFileSync(file, bytes);
}

/**
 * Writes `tm-boot.js` and the brand files a folder serves at its root.
 *
 * @param dir - The folder.
 * @param bootScript - The rendered boot script.
 * @param brand - The resolved brand files; a name with no source is left out.
 */
export function tmWriteRootFiles(dir: string, bootScript: string, brand: readonly TmBrandFile[]): void {
  for (const [path, bytes] of rootFiles(bootScript, brand)) {
    writeIfChanged(join(dir, ...path.split('/')), bytes);
  }
}

/**
 * Brings the generated root folder to exactly these files, in place. The
 * overriding brand files are left out: they sit in `public/brand/`, which the
 * application already copies, and two assets must not claim one output path.
 *
 * @param dir - The generated root folder, `generated/root`.
 * @param bootScript - The rendered boot script.
 * @param brand - The resolved brand files.
 */
export function tmRefreshRootFiles(dir: string, bootScript: string, brand: readonly TmBrandFile[]): void {
  const files = rootFiles(
    bootScript,
    brand.filter((file) => !file.override),
  );
  // --- Remove what no longer belongs, then write what changed ---
  if (existsSync(dir)) {
    for (const path of tmListFiles(dir)) {
      if (!files.has(path)) {
        rmSync(join(dir, ...path.split('/')), { force: true });
      }
    }
  }
  for (const [path, bytes] of files) {
    writeIfChanged(join(dir, ...path.split('/')), bytes);
  }
}
