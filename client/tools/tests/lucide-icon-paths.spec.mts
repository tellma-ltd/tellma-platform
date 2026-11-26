// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import { existsSync, readdirSync, readFileSync, statSync } from 'node:fs';
import { createRequire } from 'node:module';
import { dirname, join, relative } from 'node:path';
import { describe, expect, it } from 'vitest';

/**
 * The workspace imports each Lucide icon from its own module, which
 * `@tellma/core-tools/lucide-icons` types with one wildcard declaration. Any
 * path under that prefix type-checks, so a misspelt icon name or a module a
 * `lucide` release no longer ships would surface only when something bundles
 * the file. This node-side guard reads every icon import in the workspace's
 * packages and checks it against the installed `lucide` instead.
 */

// Section: the installed lucide and its index's re-export table.
const clientDir = process.cwd();
const lucideDir = dirname(createRequire(join(clientDir, 'package.json')).resolve('lucide/package.json'));
const indexText = readFileSync(join(lucideDir, 'dist', 'esm', 'lucide.mjs'), 'utf8');
/** The icon modules the index re-exports, by file name without extension. */
const indexed = new Set([...indexText.matchAll(/from '\.\/icons\/([^']+)\.mjs';/g)].map((match) => match[1]));

/** Every TypeScript source file under a folder, skipping installed and built output. */
function sources(folder: string): string[] {
  const files: string[] = [];
  for (const name of readdirSync(folder)) {
    if (name === 'node_modules' || name === 'dist') {
      continue;
    }
    const path = join(folder, name);
    if (statSync(path).isDirectory()) {
      files.push(...sources(path));
    } else if (name.endsWith('.ts') && !name.endsWith('.d.ts')) {
      files.push(path);
    }
  }
  return files;
}

// Section: every `lucide/…` specifier the workspace's packages import, with its file.
const imports = sources(join(clientDir, 'projects')).flatMap((file) =>
  [...readFileSync(file, 'utf8').matchAll(/^import\s[^;]*?\sfrom\s'(lucide\/[^']*)';/gm)].map((match) => ({
    file: relative(clientDir, file).split('\\').join('/'),
    specifier: match[1]!,
  })),
);

describe('the per-icon lucide imports', () => {
  it('are found, so the checks below look at something', () => {
    expect(indexed.size).toBeGreaterThan(1000);
    expect(imports.length).toBeGreaterThan(100);
  });

  it('each name an icon module of the installed lucide that its index exports', () => {
    for (const { file, specifier } of imports) {
      const name = /^lucide\/dist\/esm\/icons\/([a-z0-9-]+)\.mjs$/.exec(specifier)?.[1];
      expect(name, `${file} imports '${specifier}', which is not lucide/dist/esm/icons/<name>.mjs`).toBeDefined();
      expect(existsSync(join(lucideDir, 'dist', 'esm', 'icons', `${name}.mjs`)), `${file}: '${specifier}' does not exist`).toBe(true);
      expect(indexed.has(name!), `${file}: '${specifier}' is not an icon the lucide index exports`).toBe(true);
    }
  });
});
