// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import { readFileSync, rmSync, writeFileSync } from 'node:fs';
import { join, resolve } from 'node:path';

import { afterAll, beforeAll, describe, expect, it } from 'vitest';

import { copyFixtureApp, FIXTURES_DIR } from '../../fixtures/fake-emit.js';
import { tmFindPackageDir, tmReadAppProject, tmRequirePackageDir } from './app-project.js';

const clientDir = resolve(FIXTURES_DIR, '..', '..', '..', '..');

let app: string;
beforeAll(() => {
  app = copyFixtureApp('project');
});
afterAll(() => {
  rmSync(app, { recursive: true, force: true });
});

describe('a client project', () => {
  it('reads the Migrator, the one application and its fixed layout', () => {
    const project = tmReadAppProject(app);
    expect(project).toMatchObject({
      dir: app,
      packageName: 'tellma-client-fixture',
      migratorDir: join(FIXTURES_DIR, 'migrator'),
      angularProject: 'fixture',
      hasServeTarget: true,
      outputDir: join(app, 'dist', 'fixture'),
      browserDir: join(app, 'dist', 'fixture', 'browser'),
      sourceMapsDir: join(app, 'dist', 'fixture', 'sourcemaps'),
      statsFile: join(app, 'dist', 'fixture', 'stats.json'),
      indexHtml: join(app, 'src', 'index.html'),
      tsconfig: join(app, 'tsconfig.app.json'),
      appConfig: join(app, 'src', 'app.config.ts'),
      publicDir: join(app, 'public'),
      generatedDir: join(app, 'generated'),
      rootAssetsDir: join(app, 'generated', 'root'),
      copiesRootAssets: true,
    });
  });

  it("knows whether the build copies generated/root/ to the output root, an object asset's default", () => {
    const file = join(app, 'angular.json');
    const original = readFileSync(file, 'utf8');
    const withAsset = (asset: unknown): boolean => {
      const workspace = JSON.parse(original) as { projects: { fixture: { architect: { build: { options: { assets: unknown[] } } } } } };
      workspace.projects.fixture.architect.build.options.assets = [{ glob: '**/*', input: 'public' }, asset];
      writeFileSync(file, JSON.stringify(workspace));
      return tmReadAppProject(app).copiesRootAssets;
    };
    try {
      expect(withAsset({ glob: '**/*', input: 'generated/root/' })).toBe(true);
      expect(withAsset({ glob: '**/*', input: './generated/root', output: '' })).toBe(true);
      expect(withAsset({ glob: '**/*', input: 'generated/root', output: 'boot' })).toBe(false);
      expect(withAsset({ glob: '*.js', input: 'generated/root' })).toBe(false);
      expect(withAsset('generated/root')).toBe(false);
    } finally {
      writeFileSync(file, original);
    }
  });

  it('fails without exactly one application', () => {
    const file = join(app, 'angular.json');
    const original = readFileSync(file, 'utf8');
    try {
      const workspace = JSON.parse(original) as { projects: Record<string, unknown> };
      workspace.projects['second'] = workspace.projects['fixture'];
      writeFileSync(file, JSON.stringify(workspace));
      expect(() => tmReadAppProject(app)).toThrow(/exactly one application; it holds 2/);
    } finally {
      writeFileSync(file, original);
    }
    expect(() => tmReadAppProject(join(app, 'src'))).toThrow(/package\.json does not exist/);
  });

  it('refuses a browser output that is the output folder itself, which would deploy the source maps', () => {
    const file = join(app, 'angular.json');
    const original = readFileSync(file, 'utf8');
    try {
      const workspace = JSON.parse(original) as { projects: { fixture: { architect: { build: { options: Record<string, unknown> } } } } };
      workspace.projects.fixture.architect.build.options['outputPath'] = { base: 'dist/fixture', browser: '' };
      writeFileSync(file, JSON.stringify(workspace));
      expect(() => tmReadAppProject(app)).toThrow(/"outputPath\.browser" cannot be empty/);
      workspace.projects.fixture.architect.build.options['outputPath'] = { base: 'dist/fixture', browser: 'web' };
      writeFileSync(file, JSON.stringify(workspace));
      expect(tmReadAppProject(app).browserDir).toBe(join(app, 'dist', 'fixture', 'web'));
    } finally {
      writeFileSync(file, original);
    }
  });

  it('finds a package in node_modules, or at the sources the tsconfig paths name', () => {
    const project = tmReadAppProject(app);
    expect(tmFindPackageDir(project, '@angular/cli')).toBe(join(clientDir, 'node_modules', '@angular', 'cli'));
    expect(tmFindPackageDir(project, '@tellma/core')).toBe(join(clientDir, 'projects', 'core', 'tellma-core'));
    expect(tmFindPackageDir(project, '@tellma/nothing')).toBeNull();
    expect(() => tmRequirePackageDir(project, '@tellma/nothing')).toThrow(/is not installed/);
  });
});
