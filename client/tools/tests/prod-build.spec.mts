// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import {
  mkdirSync,
  mkdtempSync,
  readdirSync,
  rmSync,
  statSync,
  utimesSync,
  writeFileSync,
} from 'node:fs';
import { tmpdir } from 'node:os';
import { dirname, join } from 'node:path';

import { afterEach, beforeEach, describe, expect, it } from 'vitest';

import { isProdBuildFresh, markBuildStart, prodBuildDir } from '../../scripts/prod-build.mjs';

// The freshness rule behind the e2e production bundle: a bundle is reused
// only while it is newer than every build input, so the e2e run never tests
// stale code and never repeats a build for bytes that already exist.

/** A whole second, so the times below round-trip through every file system exactly. */
const NOW = Math.floor(Date.now() / 1000) * 1000;

/** When every build input was last written: two hours ago. */
const SOURCES_TIME = new Date(NOW - 2 * 60 * 60 * 1000);

/** When the bundle was built: one hour ago, after the sources. */
const BUILT_TIME = new Date(NOW - 60 * 60 * 1000);

/** The fixture's repository root; the Angular workspace is its client/ folder. */
let repo: string;

/** The fixture's Angular workspace. */
let client: string;

/** Writes a file, creating its folders. */
function write(path: string, text = ''): void {
  mkdirSync(dirname(path), { recursive: true });
  writeFileSync(path, text);
}

/** Sets the mtime of a path and of everything under it. */
function age(path: string, time: Date): void {
  for (const entry of readdirSync(path, { withFileTypes: true })) {
    const child = join(path, entry.name);
    if (entry.isDirectory()) {
      age(child, time);
    } else {
      utimesSync(child, time, time);
    }
  }
  utimesSync(path, time, time);
}

beforeEach(() => {
  // A workspace shaped like client/: sources, configs, the lockfile one level
  // up, and a bundle built after all of them.
  repo = mkdtempSync(join(tmpdir(), 'tellma-prod-build-'));
  client = join(repo, 'client');
  write(join(repo, 'pnpm-lock.yaml'), 'lockfileVersion: 9.0');
  write(join(client, 'projects', 'core', 'src', 'index.ts'), 'export {};');
  write(join(client, 'angular.json'), '{}');
  write(join(client, 'package.json'), '{}');
  write(join(client, 'tsconfig.json'), '{}');
  write(join(prodBuildDir('showcase', client), 'index.html'), '<!doctype html>');
  age(repo, SOURCES_TIME);
  age(join(client, 'dist'), BUILT_TIME);
});

afterEach(() => {
  rmSync(repo, { recursive: true, force: true });
});

describe('isProdBuildFresh', () => {
  it('reuses a bundle that is newer than every build input', () => {
    expect(isProdBuildFresh('showcase', client)).toBe(true);
  });

  it('rebuilds when there is no bundle', () => {
    rmSync(join(client, 'dist'), { recursive: true });

    expect(isProdBuildFresh('showcase', client)).toBe(false);
  });

  it('rebuilds when a source file changed after the build', () => {
    write(join(client, 'projects', 'core', 'src', 'index.ts'), 'export const changed = 1;');

    expect(isProdBuildFresh('showcase', client)).toBe(false);
  });

  it('rebuilds when a source file was deleted after the build', () => {
    write(join(client, 'projects', 'core', 'src', 'gone.ts'), 'export {};');
    age(join(client, 'projects'), SOURCES_TIME);
    expect(isProdBuildFresh('showcase', client)).toBe(true);

    // Deleting a file moves only its folder's mtime.
    rmSync(join(client, 'projects', 'core', 'src', 'gone.ts'));

    expect(isProdBuildFresh('showcase', client)).toBe(false);
  });

  it('rebuilds when the workspace lockfile changed after the build', () => {
    write(join(repo, 'pnpm-lock.yaml'), 'lockfileVersion: 9.0\n# changed');

    expect(isProdBuildFresh('showcase', client)).toBe(false);
  });

  it('rebuilds when a workspace config changed after the build', () => {
    write(join(client, 'angular.json'), '{ "changed": true }');

    expect(isProdBuildFresh('showcase', client)).toBe(false);
  });

  it('ignores build output written inside the source tree', () => {
    write(join(client, 'projects', 'core', 'node_modules', 'dep', 'index.js'));
    write(join(client, 'projects', 'core', 'dist', 'index.js'));
    write(join(client, 'projects', 'core', '.angular', 'cache', 'entry'));
    write(join(client, 'projects', 'core', 'tsconfig.lib.tsbuildinfo'));
    // Creating them also moved their parents' mtimes, where the rule errs
    // toward rebuilding; reset those so only the entries themselves count.
    for (const folder of ['projects', join('projects', 'core')]) {
      utimesSync(join(client, folder), SOURCES_TIME, SOURCES_TIME);
    }

    expect(isProdBuildFresh('showcase', client)).toBe(true);
  });

  it('ignores a fixture copy that a test creates and removes inside the source tree', () => {
    const fixtures = join(client, 'projects', 'tools', 'fixtures');
    write(join(fixtures, 'app', 'main.ts'), 'export {};');
    age(join(client, 'projects'), SOURCES_TIME);

    // A spec copies the fixture app beside it and removes the copy again,
    // which moves the fixtures folder's own mtime past the build.
    write(join(fixtures, 'run-1', 'main.ts'), 'export {};');
    rmSync(join(fixtures, 'run-1'), { recursive: true });

    expect(isProdBuildFresh('showcase', client)).toBe(true);
  });

  it('rebuilds when a source was saved while the build that the marker dates ran', () => {
    const indexHtml = join(prodBuildDir('showcase', client), 'index.html');
    const buildStarted = new Date(BUILT_TIME.getTime() - 10 * 60 * 1000);
    const savedDuringBuild = new Date(BUILT_TIME.getTime() - 5 * 60 * 1000);
    const source = join(client, 'projects', 'core', 'src', 'index.ts');
    utimesSync(source, savedDuringBuild, savedDuringBuild);
    // Dated by when the build wrote index.html, the save looks older.
    expect(isProdBuildFresh('showcase', client)).toBe(true);

    markBuildStart('showcase', buildStarted.getTime(), client);

    expect(statSync(indexHtml).mtimeMs).toBe(buildStarted.getTime());
    expect(isProdBuildFresh('showcase', client)).toBe(false);
  });

  it('still reuses a build dated by its start when every source predates it', () => {
    markBuildStart('showcase', BUILT_TIME.getTime() - 10 * 60 * 1000, client);

    expect(isProdBuildFresh('showcase', client)).toBe(true);
  });

  it('skips a build input the workspace does not have', () => {
    rmSync(join(client, 'tsconfig.json'));

    expect(isProdBuildFresh('showcase', client)).toBe(true);
  });
});
