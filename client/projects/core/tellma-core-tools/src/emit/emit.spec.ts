// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import { readdirSync, renameSync, rmSync, writeFileSync } from 'node:fs';
import { join } from 'node:path';

import { afterAll, beforeAll, describe, expect, it } from 'vitest';

import { copyFixtureApp, FIXTURES_DIR, fakeEmitData, fakeRunner, writeFakeEmit } from '../../fixtures/fake-emit.js';
import { TmBuildFailure } from '../common/problems.js';
import { tmReadAppProject } from '../project/app-project.js';
import { tmRunEmit } from './emit.js';
import { tmReadEmittedMetadata } from './emitted-metadata.js';

let app: string;
beforeAll(() => {
  app = copyFixtureApp('emit');
});
afterAll(() => {
  rmSync(app, { recursive: true, force: true });
});

describe('the emit', () => {
  it("runs the Migrator's emit-client-assets into generated/, from the Migrator's folder", async () => {
    const runner = fakeRunner();
    await tmRunEmit(tmReadAppProject(app), {}, runner);
    expect(runner.calls).toEqual([
      {
        command: 'dotnet',
        args: ['run', '--project', join(FIXTURES_DIR, 'migrator'), '--no-launch-profile', '--', 'emit-client-assets', join(app, 'generated')],
        cwd: join(FIXTURES_DIR, 'migrator'),
      },
    ]);
  });

  it('passes the configuration and skips the .NET build when asked', async () => {
    const runner = fakeRunner();
    await tmRunEmit(tmReadAppProject(app), { configuration: 'Release', noDotnetBuild: true }, runner);
    expect(runner.calls[0]!.args).toEqual([
      'run',
      '--project',
      join(FIXTURES_DIR, 'migrator'),
      '--no-launch-profile',
      '--configuration',
      'Release',
      '--no-build',
      '--',
      'emit-client-assets',
      join(app, 'generated'),
    ]);
  });

  it('fails when the Migrator fails, or the project names none', async () => {
    const failing = fakeRunner();
    const project = tmReadAppProject(app);
    await expect(tmRunEmit(project, {}, { run: () => Promise.resolve(4) })).rejects.toThrow(/exited with code 4/);
    await expect(tmRunEmit({ ...project, migratorDir: null }, {}, failing)).rejects.toThrow(TmBuildFailure);
    await expect(tmRunEmit({ ...project, migratorDir: join(app, 'missing') }, {}, failing)).rejects.toThrow(/does not exist/);
    expect(failing.calls).toEqual([]);
  });
});

describe('the emitted metadata', () => {
  it('reads the chunks and string packs, by language', () => {
    writeFakeEmit(join(app, 'generated'));
    const metadata = tmReadEmittedMetadata(join(app, 'generated'));
    expect(metadata.core.value.distribution.slug).toBe('fixture');
    expect(metadata.core.name).toMatch(/^core-[0-9A-Z]{12}\.json$/);
    expect([...metadata.strings.keys()]).toEqual(['ar', 'en']);
    expect(metadata.strings.get('en')!.value['Boot_TryAgain']).toBe('Try again');
  });

  it('refuses a file whose name does not carry the hash of its bytes', () => {
    writeFakeEmit(join(app, 'generated'));
    const assets = join(app, 'generated', 'assets');
    const core = readdirSync(assets).find((name) => name.startsWith('core-'))!;
    writeFileSync(join(assets, core), '{}\n');
    expect(() => tmReadEmittedMetadata(join(app, 'generated'))).toThrow(/is not the hash of its content/);
  });

  it('refuses a missing chunk, or a chunk present twice', () => {
    writeFakeEmit(join(app, 'generated'));
    const assets = join(app, 'generated', 'assets');
    const entities = readdirSync(assets).find((name) => name.startsWith('entities-'))!;
    renameSync(join(assets, entities), join(app, 'entities.json'));
    expect(() => tmReadEmittedMetadata(join(app, 'generated'))).toThrow(/the entities chunk is missing/);

    writeFakeEmit(join(app, 'generated'));
    const second = `${JSON.stringify({ entities: [1], enums: [] })}\n`;
    const data = fakeEmitData();
    writeFakeEmit(join(app, 'stale'), { ...data, entities: JSON.parse(second) as Record<string, unknown> });
    const stale = readdirSync(join(app, 'stale', 'assets')).find((name) => name.startsWith('entities-'))!;
    renameSync(join(app, 'stale', 'assets', stale), join(assets, stale));
    expect(() => tmReadEmittedMetadata(join(app, 'generated'))).toThrow(/two files hold the entities chunk/);
  });

  it('refuses a Core chunk without the members the build reads', () => {
    const data = fakeEmitData();
    writeFakeEmit(join(app, 'generated'), { ...data, core: { ...data.core, languages: [] } });
    expect(() => tmReadEmittedMetadata(join(app, 'generated'))).toThrow(/offers no languages/);
    writeFakeEmit(join(app, 'generated'), { ...data, core: { ...data.core, routing: { serverFirstSegments: ['a/b'], serverTenantSegments: [] } } });
    expect(() => tmReadEmittedMetadata(join(app, 'generated'))).toThrow(/routing words/);
  });
});
