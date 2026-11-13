// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import { existsSync, readFileSync, rmSync, writeFileSync } from 'node:fs';
import { join } from 'node:path';

import { afterAll, describe, expect, it } from 'vitest';

import { copyFixtureApp, fakeEmitData, fakeRunner, type FakeEmitData } from '../../fixtures/fake-emit.js';
import { TM_CLI_EXIT, tmRunCli } from '../cli/tellma-client.js';

// The build's first step on synthetic sources: each source problem exits 3
// before ng build runs, and a step that cannot run exits 2. The config check
// runs for real where the case is about it, and is answered with no problems
// elsewhere, which keeps the cases fast; the real build is the fixture
// spec's.

const copies: string[] = [];
afterAll(() => {
  for (const copy of copies) {
    rmSync(copy, { recursive: true, force: true });
  }
});

/** What a build run printed and started. */
interface Run {
  readonly code: number;
  readonly out: string;
  readonly err: string;
  /** The programs started, as `command first-argument`. */
  readonly started: readonly string[];
  /** The programs started, each with all its arguments. */
  readonly calls: readonly (readonly string[])[];
  /** The copy's folder. */
  readonly app: string;
  /** Whether ng build ran. */
  readonly ngBuilt: boolean;
}

/**
 * Builds a copy of the fixture app.
 *
 * @param name - The copy's name.
 * @param change - Changes the copy before the build.
 * @param options - The emitted data, and whether the config check runs for real.
 */
async function build(
  name: string,
  change: (app: string) => void = () => undefined,
  options: {
    readonly data?: () => FakeEmitData;
    readonly realConfigCheck?: boolean;
    readonly configProblems?: readonly unknown[];
    readonly args?: readonly string[];
  } = {},
): Promise<Run> {
  const app = copyFixtureApp(`build-${name}`);
  copies.push(app);
  change(app);
  let ngBuilt = false;
  const runner = fakeRunner({
    ...(options.data === undefined ? {} : { data: options.data }),
    node: (args) => {
      if (/ng\.js$/.test(args[0] ?? '')) {
        ngBuilt = true;
        return 1;
      }
      if (/config-check-runner\.js$/.test(args[0] ?? '') && options.realConfigCheck !== true) {
        const request = JSON.parse(readFileSync(args[1]!, 'utf8')) as { readonly result: string };
        writeFileSync(request.result, JSON.stringify({ problems: options.configProblems ?? [] }));
        return 0;
      }
      return undefined;
    },
  });
  let out = '';
  let err = '';
  const code = await tmRunCli(['build', ...(options.args ?? [])], { out: (text) => (out += text), err: (text) => (err += text) }, { cwd: app, runner });
  return {
    code,
    out,
    err,
    started: runner.calls.map((call) => `${call.command} ${call.args[0] ?? ''}`),
    calls: runner.calls.map((call) => [call.command, ...call.args]),
    app,
    ngBuilt,
  };
}

/** Changes a JSON file in place. */
function editJson(file: string, change: (value: Record<string, unknown>) => void): void {
  const value = JSON.parse(readFileSync(file, 'utf8')) as Record<string, unknown>;
  change(value);
  writeFileSync(file, JSON.stringify(value, null, 2));
}

/** The problem lines of a run. */
function lines(run: Run): string[] {
  return run.err.split('\n').filter((line) => line !== '');
}

describe('tellma-client build, before ng build', () => {
  it('stops with exit 3 on an index.html problem', async () => {
    const run = await build('index', (app) => {
      const file = join(app, 'src', 'index.html');
      writeFileSync(file, readFileSync(file, 'utf8').replace('<tm-app></tm-app>', ''));
    });
    expect(run.code).toBe(TM_CLI_EXIT.problem);
    expect(lines(run)).toEqual(['index-html-invalid src/index.html fixture - The body must hold the <tm-app> element.']);
    expect(run.ngBuilt).toBe(false);
    expect(run.out).toContain('Failed with 1 error(s) and 0 warning(s).');
  });

  it('stops with exit 3 on a manifest problem', async () => {
    const run = await build('manifest', (app) =>
      editJson(join(app, 'public', 'manifest.webmanifest'), (manifest) => {
        manifest['short_name'] = 'Fixture Accounts';
        manifest['prefer_related_applications'] = true;
      }),
    );
    expect(run.code).toBe(TM_CLI_EXIT.problem);
    expect(lines(run).map((line) => line.split(' - ')[0])).toEqual([
      'manifest-invalid public/manifest.webmanifest fixture',
      'manifest-invalid public/manifest.webmanifest fixture',
    ]);
    expect(run.ngBuilt).toBe(false);
  });

  it('stops with exit 3 on a brand file problem', async () => {
    const run = await build('brand', (app) => {
      const file = join(app, 'public', 'brand', 'mark-on-dark.svg');
      writeFileSync(file, readFileSync(file, 'utf8').replace('<svg ', '<svg onload="alert(1)" '));
    });
    expect(run.code).toBe(TM_CLI_EXIT.problem);
    expect(lines(run)).toEqual(["brand-file-invalid public/brand/mark-on-dark.svg fixture - It carries the event-handler attribute 'onload'."]);
    expect(run.ngBuilt).toBe(false);
  });

  it('stops with exit 3 on a budget raised without a note', async () => {
    const run = await build('budget-note', (app) =>
      editJson(join(app, 'package.json'), (manifest) => {
        (manifest['tellma'] as Record<string, unknown>)['budgets'] = { initialJs: { error: 300 } };
      }),
    );
    expect(run.code).toBe(TM_CLI_EXIT.problem);
    expect(lines(run)).toEqual([
      "budget-note-missing budget:initialJs fixture - The budget 'initialJs' is raised above the platform's level without a \"note\" saying why.",
    ]);
    expect(run.ngBuilt).toBe(false);
  });

  it('stops with exit 3 on a configuration that disagrees with the emitted metadata', { timeout: 120_000 }, async () => {
    const run = await build('config', undefined, {
      realConfigCheck: true,
      data: () => {
        const data = fakeEmitData();
        return { ...data, core: { ...data.core, features: [{ key: 'core', version: '1.0.0' }] } };
      },
    });
    expect(run.code).toBe(TM_CLI_EXIT.problem);
    expect(lines(run)).toHaveLength(1);
    expect(lines(run)[0]).toMatch(/^feature-not-composed \S+ fixture - /);
    expect(run.ngBuilt).toBe(false);
  });

  it('passes the string packs to the config check, which reports a reserved key', { timeout: 120_000 }, async () => {
    const run = await build('strings', undefined, {
      realConfigCheck: true,
      data: () => {
        const data = fakeEmitData();
        return { ...data, strings: { ...data.strings, ar: { ...data.strings['ar']!, 'tm.shell.home': 'x' } } };
      },
    });
    expect(run.code).toBe(TM_CLI_EXIT.problem);
    expect(lines(run).map((line) => line.split(' ')[0])).toEqual(['string-key-collision']);
    expect(run.ngBuilt).toBe(false);
  });

  it("prints the config check's warnings and still reaches ng build", async () => {
    const warning = { code: 'label-missing', severity: 'warning', key: 'entry:fixture.Review', feature: 'fixture', message: 'It has no label.' };
    const run = await build('warning', undefined, { configProblems: [warning] });
    expect(lines(run)[0]).toBe('label-missing entry:fixture.Review fixture - It has no label.');
    expect(run.ngBuilt).toBe(true);
    expect(run.err).toContain('ng build exited with code 1.');
  });

  it('reaches ng build when the sources pass, and exits 2 when it fails', async () => {
    const run = await build('passes', undefined, { args: ['-c', 'Release', '--no-dotnet-build'] });
    expect(run.ngBuilt).toBe(true);
    expect(run.code).toBe(TM_CLI_EXIT.failure);
    expect(run.err).toContain('ng build exited with code 1.');
    // The emit's options reach the Migrator's run.
    // Inside this repository the platform's generated assets come first.
    expect(run.calls[0]).toEqual(['pnpm', 'run', 'assets:generate']);
    expect(run.calls[1]).toEqual([
      'dotnet',
      'run',
      '--project',
      join(run.app, '..', 'migrator'),
      '--no-launch-profile',
      '--configuration',
      'Release',
      '--no-build',
      '--',
      'emit-client-assets',
      join(run.app, 'generated'),
    ]);
    // generated/root/ is fresh before ng build: the boot script, and no override of public/brand/.
    expect(readFileSync(join(run.app, 'generated', 'root', 'tm-boot.js'), 'utf8')).toContain('"defaultLanguage":"en"');
    expect(existsSync(join(run.app, 'generated', 'root', 'brand'))).toBe(false);
  });
});

describe('tellma-client build, failures', () => {
  it('exits 2 when the English string pack lacks a boot string', async () => {
    const run = await build('english', undefined, {
      data: () => {
        const data = fakeEmitData();
        const en = { ...data.strings['en']! };
        delete en['Boot_Title'];
        return { ...data, strings: { ...data.strings, en } };
      },
    });
    expect(run.code).toBe(TM_CLI_EXIT.failure);
    expect(run.err).toContain('The English string pack lacks Boot_Title');
    expect(run.ngBuilt).toBe(false);
  });

  it('exits 2 when the Migrator is missing', async () => {
    const run = await build(
      'migrator',
      (app) =>
        editJson(join(app, 'package.json'), (manifest) => {
          (manifest['tellma'] as Record<string, unknown>)['migrator'] = '../nowhere';
        }),
      { args: ['--no-dotnet-build', '-c', 'Release'] },
    );
    expect(run.code).toBe(TM_CLI_EXIT.failure);
    expect(run.err).toMatch(/nowhere, named by "tellma\.migrator" in package\.json, does not exist/);
    expect(run.started).toEqual(['pnpm run']);
  });

  it('exits 1 on an unknown option', async () => {
    const run = await build('usage', undefined, { args: ['--watch'] });
    expect(run.code).toBe(TM_CLI_EXIT.usage);
    expect(run.err).toContain("unknown option '--watch'");
    expect(run.started).toEqual([]);
  });
});
