// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import { createHash } from 'node:crypto';
import { cpSync, existsSync, readdirSync, readFileSync, rmSync, writeFileSync } from 'node:fs';
import { join } from 'node:path';

import { afterAll, beforeAll, describe, expect, it } from 'vitest';

import { copyFixtureApp, fakeRunner, FIXTURES_DIR } from '../../fixtures/fake-emit.js';
import { TM_CLI_EXIT, tmRunCli } from '../cli/tellma-client.js';
import { tmNodeProcessRunner, type TmProcessOptions } from '../common/process.js';
import { tmListFiles } from '../common/files.js';

// The real build of the fixture app: one `ng build`, then the build's later
// steps on its output. The output checks then run again on copies of that
// one ng build's output with a fault injected, so each output problem is
// shown to fail the build without building again.

/** The fixture copy the spec builds. */
let app: string;
/** ng build's own output, kept before the build's later steps change it. */
const snapshot = join(FIXTURES_DIR, 'run-fixture-ng-output');
/** The first build's outcome. */
let first: { code: number; out: string; err: string };

const output = (): string => join(app, 'dist', 'fixture');
const browser = (): string => join(output(), 'browser');
const read = (file: string): string => readFileSync(join(browser(), ...file.split('/')), 'utf8');

/** Whether a Node program is ng build. */
const isNgBuild = (args: readonly string[]): boolean => /ng\.js$/.test(args[0] ?? '') && args[1] === 'build';

/**
 * Builds the copy again with ng build's output restored from the snapshot
 * and changed, and the config check answered with no problems.
 */
async function rebuild(change: (browserDir: string) => void): Promise<{ code: number; err: string }> {
  const runner = fakeRunner({
    node: (args, options: TmProcessOptions) => {
      if (isNgBuild(args)) {
        rmSync(output(), { recursive: true, force: true });
        cpSync(snapshot, output(), { recursive: true });
        change(browser());
        return 0;
      }
      if (/config-check-runner\.js$/.test(args[0] ?? '')) {
        const request = JSON.parse(readFileSync(args[1]!, 'utf8')) as { readonly result: string };
        writeFileSync(request.result, JSON.stringify({ problems: [] }));
        return 0;
      }
      return tmNodeProcessRunner.run(process.execPath, args, options);
    },
  });
  let err = '';
  const code = await tmRunCli(['build'], { out: () => undefined, err: (text) => (err += text) }, { cwd: app, runner });
  return { code, err };
}

beforeAll(async () => {
  app = copyFixtureApp('fixture');
  rmSync(snapshot, { recursive: true, force: true });
  const runner = fakeRunner({
    node: async (args, options) => {
      if (!isNgBuild(args)) {
        return undefined;
      }
      const code = await tmNodeProcessRunner.run(process.execPath, args, options);
      cpSync(output(), snapshot, { recursive: true });
      return code;
    },
  });
  let out = '';
  let err = '';
  const code = await tmRunCli(['build'], { out: (text) => (out += text), err: (text) => (err += text) }, { cwd: app, runner });
  first = { code, out, err };
  // A cold ng build of the fixture takes minutes on a busy machine.
}, 1_200_000);

afterAll(() => {
  rmSync(app, { recursive: true, force: true });
  rmSync(snapshot, { recursive: true, force: true });
});

describe('tellma-client build on the fixture app', () => {
  it('exits 0 with no problem', () => {
    expect(first.err).toBe('');
    expect(first.code).toBe(TM_CLI_EXIT.success);
    expect(first.out).toContain('Built with 0 error(s) and 0 warning(s).');
    expect(first.out).toMatch(/^initialJs +\d+\.\d\d +\d+\.\d\d +230 +260 +\d+$/m);
  });

  it('writes the brand files, tm-boot.js and the localized manifest names', () => {
    for (const name of ['wordmark-on-dark.svg', 'wordmark-on-light.svg', 'mark-on-dark.svg', 'mark-on-light.svg']) {
      expect(existsSync(join(browser(), 'brand', name)), name).toBe(true);
    }
    const boot = read('tm-boot.js');
    expect(boot).toContain('"defaultLanguage":"en"');
    expect(boot).toContain('"Distribution_DisplayName":"فكسشر"');
    expect(boot).not.toContain('/*TM_BOOT*/');
    const manifest = JSON.parse(read('manifest.webmanifest')) as Record<string, unknown>;
    expect(manifest['name_localized']).toEqual({ ar: 'فكسشر' });
    expect(manifest['short_name_localized']).toEqual({ ar: 'فكسشر' });
    expect(manifest['name']).toBe('Fixture');
  });

  it('preloads the Latin and Arabic fonts, the Core chunk and the default string pack', () => {
    const html = read('index.html');
    const assets = readdirSync(join(browser(), 'tm-metadata'));
    const core = assets.find((name) => name.startsWith('core-'))!;
    const english = assets.find((name) => name.startsWith('strings-en-'))!;
    expect(html).toContain(`<link rel="preload" href="tm-metadata/${core}" as="fetch" crossorigin>`);
    expect(html).toContain(`<link rel="preload" href="tm-metadata/${english}" as="fetch" crossorigin>`);
    expect(html).not.toContain('strings-ar-');
    expect(html).toMatch(/<link rel="preload" href="media\/noto-sans-latin-wght-normal-[A-Z0-9]{8}\.woff2" as="font" type="font\/woff2" crossorigin>/);
    expect(html).toMatch(/<link rel="preload" href="media\/noto-sans-arabic-arabic-wght-normal-[A-Z0-9]{8}\.woff2"/);
    expect(html).not.toMatch(/href="media\/noto-sans-latin-ext-/);
  });

  it('copies the worker scripts, and ngsw.json hashes every file it caches as it is', () => {
    expect(existsSync(join(browser(), 'ngsw-worker.js'))).toBe(true);
    expect(existsSync(join(browser(), 'safety-worker.js'))).toBe(true);
    const ngsw = JSON.parse(read('ngsw.json')) as { hashTable: Record<string, string>; appData: unknown; assetGroups: { name: string; urls: string[] }[] };
    expect(Object.keys(ngsw.hashTable).length).toBeGreaterThan(10);
    for (const [url, hash] of Object.entries(ngsw.hashTable)) {
      const bytes = readFileSync(join(browser(), ...url.slice(1).split('/')));
      expect(createHash('sha1').update(bytes).digest('hex'), url).toBe(hash);
    }
    expect(ngsw.appData).toEqual({ version: '1.0.0+fixture', platformVersion: '1.0.0+fixture' });
    const [shell, onDemand] = ngsw.assetGroups;
    expect(shell!.name).toBe('shell');
    expect(shell!.urls).toContain('/index.html');
    expect(shell!.urls).toContain('/tm-boot.js');
    expect(shell!.urls.some((url) => url.startsWith('/tm-metadata/strings-ar-'))).toBe(true);
    expect(shell!.urls.some((url) => url.includes('latin-ext'))).toBe(false);
    expect(shell!.urls.some((url) => url.endsWith('.map') || url.includes('worker'))).toBe(false);
    expect(onDemand).toMatchObject({ name: 'on-demand' });
    expect(onDemand!.urls).toEqual(['/icons/favicon.svg', '/icons/icon-192.png', '/icons/icon-512.png', '/icons/icon-maskable-512.png', '/manifest.webmanifest']);
  });

  it("derives the navigation URLs from the routing words", () => {
    const ngsw = JSON.parse(read('ngsw.json')) as { navigationUrls: { positive: boolean; regex: string }[] };
    const navigates = (path: string): boolean =>
      ngsw.navigationUrls.some((url) => url.positive && new RegExp(url.regex).test(path)) &&
      !ngsw.navigationUrls.some((url) => !url.positive && new RegExp(url.regex).test(path));
    for (const path of ['/', '/tenants', '/signed-out', '/12/settings', '/12/list/centers', '/12/apiary']) {
      expect(navigates(path), path).toBe(true);
    }
    for (const path of ['/api', '/api/users', '/bff/login', '/.well-known/openid-configuration', '/signin-oidc', '/12/hub', '/12/blobs/x', '/main-A.js', '/a__b']) {
      expect(navigates(path), path).toBe(false);
    }
  });

  it('moves the source maps out of the deployable output', () => {
    expect(tmListFiles(browser()).filter((file) => file.endsWith('.map'))).toEqual([]);
    expect(tmListFiles(join(output(), 'sourcemaps')).some((file) => /^main-[A-Z0-9]{8}\.js\.map$/.test(file))).toBe(true);
  });
});

describe('the output checks on the fixture app', () => {
  it('fail an event-handler attribute in the built index.html', { timeout: 300_000 }, async () => {
    const run = await rebuild((dir) => {
      const file = join(dir, 'index.html');
      writeFileSync(file, readFileSync(file, 'utf8').replace('<body>', '<body onload="start()">'));
    });
    expect(run.code).toBe(TM_CLI_EXIT.problem);
    expect(run.err).toBe(
      'index-html-invalid dist/fixture/browser/index.html fixture - No element may carry an event handler attribute (on*), which the Content Security Policy refuses.\n',
    );
  });

  it('fail a script without a hashed name', { timeout: 300_000 }, async () => {
    const run = await rebuild((dir) => writeFileSync(join(dir, 'legacy.js'), 'void 0;'));
    expect(run.code).toBe(TM_CLI_EXIT.problem);
    expect(run.err).toMatch(/^unhashed-asset legacy\.js fixture - /);
  });

  it('fail a figure over its error level, and pass one over its warn level', { timeout: 300_000 }, async () => {
    const budgets = (value: unknown): void => {
      const file = join(app, 'package.json');
      const manifest = JSON.parse(readFileSync(file, 'utf8')) as { tellma: Record<string, unknown> };
      manifest.tellma['budgets'] = value;
      writeFileSync(file, JSON.stringify(manifest, null, 2));
    };
    try {
      budgets({ initialCss: { error: 0.1 } });
      const exceeded = await rebuild(() => undefined);
      expect(exceeded.code).toBe(TM_CLI_EXIT.problem);
      expect(exceeded.err).toMatch(/^budget-exceeded budget:initialCss fixture - [\d.]+ KB gzip is over the budget's error level of 0\.1 KB\.\n$/);

      budgets({ initialJs: { warn: 1 } });
      const warned = await rebuild(() => undefined);
      expect(warned.code).toBe(TM_CLI_EXIT.success);
      expect(warned.err).toMatch(/^budget-warning budget:initialJs fixture - /);
    } finally {
      budgets(undefined);
    }
  });
});
