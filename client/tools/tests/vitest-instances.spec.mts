// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import { mkdirSync, mkdtempSync, realpathSync, rmSync, writeFileSync } from 'node:fs';
import { tmpdir } from 'node:os';
import { dirname, join, resolve } from 'node:path';

import { afterEach, beforeEach, describe, expect, it } from 'vitest';

import {
  discoverUnitTestWorkspaces,
  readWorkspaceGlobs,
  resolvePackageDir,
  type TmUnitRunInstances,
  unitRunInstances,
  unitRunProblems,
  UNIT_TEST_BUILDER,
} from '../vitest-instances.mjs';

// Angular's unit-test builder and a workspace's browser provider must run on
// one copy of Vitest and one of Vite: first over the installed repository,
// every workspace that runs unit tests; then over small node_modules trees
// laid out by hand, so the check is seen to fail on a split.

/** The repository root, which holds pnpm-workspace.yaml. */
const repoRoot = resolve(import.meta.dirname, '..', '..', '..');

describe('the installed workspaces', () => {
  const workspaces = discoverUnitTestWorkspaces(repoRoot);

  it('find the client workspace and at least one distribution', () => {
    const names = workspaces.map((workspace) => workspace.name);
    expect(names).toContain('client');
    // A distribution moved out of the workspace's globs would leave the check nothing to look at.
    expect(names.some((name) => name.startsWith('distributions/'))).toBe(true);
  });

  it.each(workspaces.map((workspace) => [workspace.name, workspace.dir]))(
    'run %s on one copy of vitest and one of vite, and the builder on the sass it pins',
    (name, dir) => {
      expect(unitRunProblems(name, unitRunInstances(dir), repoRoot)).toEqual([]);
    },
  );
});

describe('the check over a hand-made install', () => {
  /** The scratch repository root, its real path. */
  let root: string;

  /**
   * What the workspace `app` loads, by Node's lookup kept inside the scratch
   * root: a node_modules folder above it (the temp folder's, a home folder's)
   * must not stand in for a package the tree leaves out.
   */
  function instances(): TmUnitRunInstances {
    return unitRunInstances(join(root, 'app'), (fromDir, name) => {
      const dir = resolvePackageDir(fromDir, name);
      return dir !== null && dir.startsWith(root) ? dir : null;
    });
  }

  /** Writes a JSON file, creating its folders. */
  function writeJson(path: string, value: unknown): void {
    mkdirSync(dirname(join(root, path)), { recursive: true });
    writeFileSync(join(root, path), JSON.stringify(value), 'utf8');
  }

  /** Writes a package's package.json under a node_modules path. */
  function writePackage(
    path: string,
    version: string,
    dependencies?: Record<string, string>,
  ): void {
    writeJson(join(path, 'package.json'), {
      name: path.split('node_modules/').at(-1),
      version,
      dependencies,
    });
  }

  /**
   * Lays out a workspace `app` whose unit run loads one copy of everything: the
   * builder pins sass 1.99.0 and resolves it, and the provider's packages sit
   * beside the builder's.
   */
  function layOutOneCopy(): void {
    writeFileSync(join(root, 'pnpm-workspace.yaml'), 'packages:\n  - app\n', 'utf8');
    writeJson('app/package.json', { name: 'app' });
    writeJson('app/angular.json', {
      projects: { app: { architect: { test: { builder: UNIT_TEST_BUILDER } } } },
    });
    writePackage('app/node_modules/@angular/build', '22.0.5', { sass: '1.99.0' });
    writePackage('app/node_modules/@angular/build/node_modules/sass', '1.99.0');
    writePackage('app/node_modules/vitest', '4.1.9');
    writePackage('app/node_modules/vite', '7.3.5');
    writePackage('app/node_modules/@vitest/browser-playwright', '4.1.9');
    writePackage('app/node_modules/@vitest/browser', '4.1.9');
    writePackage('app/node_modules/@vitest/mocker', '4.1.9');
  }

  beforeEach(() => {
    root = realpathSync.native(mkdtempSync(join(tmpdir(), 'tm-vitest-instances-')));
    layOutOneCopy();
  });

  afterEach(() => {
    rmSync(root, { recursive: true, force: true });
  });

  it('find the workspace, and pass a run on one copy of each', () => {
    expect(discoverUnitTestWorkspaces(root)).toEqual([{ name: 'app', dir: join(root, 'app') }]);
    const run = instances();
    expect(run.vitest.map((route) => route.route)).toEqual([
      '@angular/build > vitest',
      'vitest',
      '@vitest/browser-playwright > vitest',
      '@vitest/browser-playwright > @vitest/browser > vitest',
    ]);
    expect(run.vite.map((route) => route.route)).toEqual([
      '@angular/build > vite',
      '@angular/build > vitest > vite',
      '@angular/build > vitest > @vitest/mocker > vite',
      '@vitest/browser-playwright > @vitest/browser > vite',
      '@vitest/browser-playwright > @vitest/browser > @vitest/mocker > vite',
      '@vitest/browser-playwright > @vitest/mocker > vite',
    ]);
    expect(unitRunProblems('app', run, root)).toEqual([]);
  });

  it("fail a run whose browser server's own mocker sits on another vite", () => {
    // @vitest/browser imports @vitest/mocker from its own folder, apart from the provider's.
    writePackage('app/node_modules/@vitest/browser/node_modules/@vitest/mocker', '4.1.9');
    writePackage(
      'app/node_modules/@vitest/browser/node_modules/@vitest/mocker/node_modules/vite',
      '7.3.5',
    );
    const problems = unitRunProblems('app', instances(), root);
    expect(problems).toHaveLength(1);
    expect(problems[0]).toMatch(/^app: one unit run loads 2 copies of vite,/);
    expect(problems[0]).toContain(
      'app/node_modules/@vitest/browser/node_modules/@vitest/mocker/node_modules/vite (@vitest/browser-playwright > @vitest/browser > @vitest/mocker > vite)',
    );
  });

  it("fail a run whose builder loads a vitest other than the provider's, naming both copies and their routes", () => {
    // The builder's own copy, as pnpm links a second peer variant beside it.
    writePackage('app/node_modules/@angular/build/node_modules/vitest', '4.1.9');
    const [problem, ...rest] = unitRunProblems('app', instances(), root);
    expect(rest).toEqual([]);
    expect(problem).toMatch(/^app: one unit run loads 2 copies of vitest/);
    expect(problem).toContain(
      '\n  app/node_modules/@angular/build/node_modules/vitest (@angular/build > vitest)',
    );
    expect(problem).toContain(
      '\n  app/node_modules/vitest (vitest; @vitest/browser-playwright > vitest; @vitest/browser-playwright > @vitest/browser > vitest)',
    );
  });

  it('fail a run whose provider brings a vite of its own', () => {
    writePackage('app/node_modules/@vitest/browser/node_modules/vite', '7.3.5');
    const problems = unitRunProblems('app', instances(), root);
    expect(problems).toHaveLength(1);
    expect(problems[0]).toMatch(/^app: one unit run loads 2 copies of vite,/);
    expect(problems[0]).toContain(
      'app/node_modules/@vitest/browser/node_modules/vite (@vitest/browser-playwright > @vitest/browser > vite)',
    );
  });

  it('fail a builder on a sass other than the one it pins: an override left behind by an upgrade', () => {
    writePackage('app/node_modules/@angular/build/node_modules/sass', '1.101.0');
    expect(unitRunProblems('app', instances(), root)).toEqual([
      'app: @angular/build pins sass 1.99.0 but resolves 1.101.0; set the sass override in pnpm-workspace.yaml to 1.99.0.',
    ]);
  });

  it('fail a builder whose sass pin cannot be compared with the override, rather than pass it', () => {
    const recheck =
      'Check whether the sass override in pnpm-workspace.yaml still fits it, and update the override and this check together.';
    // A range in its dependencies.
    writePackage('app/node_modules/@angular/build', '22.0.5', { sass: '^1.99.0' });
    expect(unitRunProblems('app', instances(), root)).toEqual([
      `app: @angular/build names sass as ^1.99.0, not an exact version, and resolves 1.99.0. ${recheck}`,
    ]);
    // No sass at all.
    writePackage('app/node_modules/@angular/build', '22.0.5');
    expect(unitRunProblems('app', instances(), root)).toEqual([
      `app: @angular/build names no sass in its package.json, so its sass cannot be compared with the override. ${recheck}`,
    ]);
    // An exact version, but outside its dependencies.
    writeJson('app/node_modules/@angular/build/package.json', {
      name: '@angular/build',
      version: '22.0.5',
      peerDependencies: { sass: '1.99.0' },
    });
    expect(unitRunProblems('app', instances(), root)).toEqual([
      `app: @angular/build names sass 1.99.0 in its peerDependencies, not its dependencies. ${recheck}`,
    ]);
  });

  it('fail a provider whose own packages do not resolve, and a workspace with no builder', () => {
    rmSync(join(root, 'app/node_modules/@vitest/browser'), { recursive: true });
    expect(unitRunProblems('app', instances(), root)).toEqual([
      'app: @vitest/browser-playwright > @vitest/browser > vitest does not resolve; run pnpm install.',
      'app: @vitest/browser-playwright > @vitest/browser > vite does not resolve; run pnpm install.',
      'app: @vitest/browser-playwright > @vitest/browser > @vitest/mocker > vite does not resolve; run pnpm install.',
    ]);
    rmSync(join(root, 'app/node_modules/@angular/build'), { recursive: true });
    expect(unitRunProblems('app', instances(), root)).toEqual(
      expect.arrayContaining(['app: @angular/build > vitest does not resolve; run pnpm install.']),
    );
  });

  it('leave out a workspace with no unit-test target', () => {
    writeJson('app/angular.json', {
      projects: { app: { architect: { build: { builder: '@angular/build:application' } } } },
    });
    expect(discoverUnitTestWorkspaces(root)).toEqual([]);
  });

  it('leave out a folder a ! glob takes away, and read a glob past its trailing comment', () => {
    // A second folder with a unit-test target and no install of its own.
    writeJson('legacy/angular.json', {
      projects: { legacy: { architect: { test: { builder: UNIT_TEST_BUILDER } } } },
    });
    writeFileSync(
      join(root, 'pnpm-workspace.yaml'),
      "packages:\n  - '*'  # every folder\n  - '!legacy'\n",
      'utf8',
    );
    expect(discoverUnitTestWorkspaces(root)).toEqual([{ name: 'app', dir: join(root, 'app') }]);
  });
});

describe('the workspace globs', () => {
  /** The scratch folder that holds the pnpm-workspace.yaml under test, its real path. */
  let root: string;

  beforeEach(() => {
    root = realpathSync.native(mkdtempSync(join(tmpdir(), 'tm-vitest-globs-')));
  });

  afterEach(() => {
    rmSync(root, { recursive: true, force: true });
  });

  it('read the items of the top-level packages list only, unquoted, past comments', () => {
    writeFileSync(
      join(root, 'pnpm-workspace.yaml'),
      [
        '# The workspace.',
        'packages:  # the packages',
        '  # The platform.',
        '  - client',
        "  - 'distributions/*/src/*.Client'",
        '',
        '  - "tools/*"',
        '  - samples/*  # the samples',
        "  - 'docs/#notes' # a quoted # stays",
        '  - a#b',
        "  - '!distributions/legacy/**'  # taken away",
        'catalog:',
        '  - not-a-package',
        'overrides:',
        '  sass: 1.99.0',
      ].join('\r\n'),
      'utf8',
    );
    expect(readWorkspaceGlobs(root)).toEqual({
      include: [
        'client',
        'distributions/*/src/*.Client',
        'tools/*',
        'samples/*',
        'docs/#notes',
        'a#b',
      ],
      exclude: ['distributions/legacy/**'],
    });
  });
});
