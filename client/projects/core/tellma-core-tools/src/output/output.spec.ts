// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import { existsSync, mkdirSync, mkdtempSync, readFileSync, rmSync, writeFileSync } from 'node:fs';
import { tmpdir } from 'node:os';
import { join } from 'node:path';

import { afterEach, describe, expect, it } from 'vitest';

import { fakeEmitData } from '../../fixtures/fake-emit.js';
import type { TmEmittedCore, TmEmittedMetadata } from '../emit/emitted-metadata.js';
import { tmBootConfig, tmBuildBootScript } from './boot.js';
import { tmAddPreloads, tmReadInitialFiles } from './preloads.js';
import { tmMoveSourceMaps } from './source-maps.js';
import { TmBuildGraph, tmEchartsModule, tmEntryPointModule } from './stats.js';
import { tmNavigationUrls, tmOnDemandChunks, tmWorkerConfig, tmWorkerGroups } from './worker-config.js';

let dir: string | undefined;
afterEach(() => {
  if (dir !== undefined) {
    rmSync(dir, { recursive: true, force: true });
    dir = undefined;
  }
});

/** A scratch folder, removed after the case. */
function scratch(): string {
  dir = mkdtempSync(join(tmpdir(), 'tm-output-'));
  return dir;
}

/** The fake Migrator's metadata, with the string packs a case gives. */
function metadata(strings: Record<string, Record<string, string>> = fakeEmitData().strings): TmEmittedMetadata {
  return {
    core: { name: 'core-AAAAAAAAAAAA.json', value: fakeEmitData().core as unknown as TmEmittedCore },
    strings: new Map(Object.entries(strings).map(([code, value]) => [code, { name: `strings-${code}-AAAAAAAAAAAA.json`, value }])),
  } as unknown as TmEmittedMetadata;
}

describe('the chunk graph', () => {
  it('reads the JavaScript outputs and their static and dynamic imports from the metafile', () => {
    const file = join(scratch(), 'stats.json');
    writeFileSync(
      file,
      JSON.stringify({
        inputs: {},
        outputs: {
          'main-A.js': {
            inputs: { 'src\\main.ts': {} },
            imports: [
              { path: 'chunk-B.js', kind: 'import-statement' },
              { path: 'chunk-C.js', kind: 'dynamic-import' },
              { path: 'media/x.woff2', kind: 'url-token' },
              { path: 'https://example.com/x.js', kind: 'import-statement', external: true },
            ],
          },
          'main-A.js.map': { inputs: {}, imports: [] },
          'styles-D.css': { inputs: {}, imports: [{ path: 'media/x.woff2', kind: 'url-token' }] },
          'chunk-B.js': { inputs: {}, imports: [] },
          'chunk-C.js': { inputs: {}, imports: [{ path: 'chunk-B.js', kind: 'import-statement' }] },
        },
      }),
    );
    const graph = TmBuildGraph.read(file);
    expect([...graph.outputs.keys()]).toEqual(['main-A.js', 'chunk-B.js', 'chunk-C.js']);
    expect(graph.outputs.get('main-A.js')).toEqual({
      path: 'main-A.js',
      inputs: ['src/main.ts'],
      imports: [
        { path: 'chunk-B.js', kind: 'static' },
        { path: 'chunk-C.js', kind: 'dynamic' },
      ],
    });
    expect([...graph.staticClosure(['main-A.js'])].sort()).toEqual(['chunk-B.js', 'main-A.js']);
    expect([...graph.reach(['main-A.js'])].sort()).toEqual(['chunk-B.js', 'chunk-C.js', 'main-A.js']);
    expect(() => TmBuildGraph.read(join(dir!, 'missing.json'))).toThrow(/does not exist/);
  });

  it('finds an entry point by its built bundle or its workspace sources', () => {
    const shell = tmEntryPointModule('tellma-core', 'shell');
    expect(shell('../../node_modules/@tellma/core/fesm2022/tellma-core-shell.mjs')).toBe(true);
    expect(shell('../../../tellma-core/shell/src/tm-shell.ts')).toBe(true);
    expect(shell('../../../tellma-core/src/shell.ts')).toBe(false);
    expect(shell('../../node_modules/@tellma/core/fesm2022/tellma-core-shell-extra.mjs')).toBe(false);
    expect(tmEchartsModule('../node_modules/.pnpm/zrender@6.0.0/node_modules/zrender/lib/zrender.js')).toBe(true);
    expect(tmEchartsModule('src/echarts-like.ts')).toBe(false);
  });
});

describe('the worker config', () => {
  it('derives the navigation URLs from the routing words', () => {
    expect(tmNavigationUrls({ serverFirstSegments: ['api', '.well-known'], serverTenantSegments: ['hub'] })).toEqual([
      '/**',
      '!/**/*.*',
      '!/**/*__*',
      '!/**/*__*/**',
      '!/api',
      '!/api/**',
      '!/.well-known',
      '!/.well-known/**',
      '!/*/hub',
      '!/*/hub/**',
    ]);
  });

  it('puts only what the on-demand entry points alone reach in the on-demand group', () => {
    const graph = new TmBuildGraph([
      { path: 'main.js', inputs: ['src/main.ts'], imports: [{ path: 'page.js', kind: 'dynamic' }, { path: 'chart.js', kind: 'dynamic' }, { path: 'studio.js', kind: 'dynamic' }] },
      { path: 'page.js', inputs: ['src/page.ts'], imports: [{ path: 'shared.js', kind: 'static' }] },
      { path: 'chart.js', inputs: ['node_modules/echarts/index.js'], imports: [{ path: 'shared.js', kind: 'static' }, { path: 'theme.js', kind: 'static' }] },
      { path: 'studio.js', inputs: ['node_modules/@tellma/core/fesm2022/tellma-core-report-studio.mjs'], imports: [{ path: 'studio-lazy.js', kind: 'dynamic' }] },
      { path: 'studio-lazy.js', inputs: ['x.ts'], imports: [] },
      { path: 'shared.js', inputs: ['y.ts'], imports: [] },
      { path: 'theme.js', inputs: ['z.ts'], imports: [] },
      { path: 'orphan.js', inputs: ['w.ts'], imports: [] },
    ]);
    const onDemand = tmOnDemandChunks(graph, ['main.js']);
    expect([...onDemand].sort()).toEqual(['chart.js', 'studio-lazy.js', 'studio.js', 'theme.js']);
    // Without the on-demand entry points there is nothing on demand.
    expect(tmOnDemandChunks(new TmBuildGraph([{ path: 'main.js', inputs: [], imports: [] }]), ['main.js']).size).toBe(0);

    const files = [
      'chart.js',
      'chart.js.map',
      'icons/icon-192.png',
      'index.html',
      'main.js',
      'manifest.webmanifest',
      'media/noto-sans-arabic-arabic-wght-normal-AAAAAAAA.woff2',
      'media/noto-sans-cyrillic-wght-normal-AAAAAAAA.woff2',
      'media/noto-sans-latin-ext-wght-normal-AAAAAAAA.woff2',
      'media/noto-sans-latin-wght-normal-AAAAAAAA.woff2',
      'ngsw-worker.js',
      'ngsw.json',
      'orphan.js',
      'safety-worker.js',
      'tm-boot.js',
      'tm-metadata/core-AAAAAAAAAAAA.json',
    ];
    expect(tmWorkerGroups(files, onDemand, ['latin', 'arabic'])).toEqual({
      shell: [
        'index.html',
        'main.js',
        'media/noto-sans-arabic-arabic-wght-normal-AAAAAAAA.woff2',
        'media/noto-sans-latin-wght-normal-AAAAAAAA.woff2',
        'orphan.js',
        'tm-boot.js',
        'tm-metadata/core-AAAAAAAAAAAA.json',
      ],
      onDemand: ['chart.js', 'icons/icon-192.png', 'manifest.webmanifest'],
    });
  });

  it('writes the versions, the strategy, the navigation URLs and the two groups', () => {
    const core = fakeEmitData().core as unknown as TmEmittedCore;
    const config = tmWorkerConfig(core, { shell: ['index.html', 'main-A.js'], onDemand: ['manifest.webmanifest'] });
    expect(config).toEqual({
      index: '/index.html',
      appData: { version: '1.0.0+fixture', platformVersion: '1.0.0+fixture' },
      navigationRequestStrategy: 'performance',
      navigationUrls: tmNavigationUrls(core.routing),
      assetGroups: [
        { name: 'shell', installMode: 'prefetch', updateMode: 'prefetch', resources: { files: ['/index.html', '/main-A.js'] } },
        { name: 'on-demand', installMode: 'lazy', updateMode: 'lazy', resources: { files: ['/manifest.webmanifest'] } },
      ],
    });
    expect(config).not.toHaveProperty('dataGroups');
  });
});

describe('the preloads', () => {
  const BUILT = `<!doctype html><html><head><script src="tm-boot.js"></script><link rel="stylesheet" href="styles-A.css"></head>
<body><tm-app></tm-app><link rel="modulepreload" href="chunk-B.js"><script src="./main-C.js" type="module"></script><script src="https://cdn.example.com/x.js"></script></body></html>`;

  it('read the scripts, module preloads and stylesheets of the built index.html', () => {
    expect(tmReadInitialFiles(BUILT)).toEqual({ scripts: ['main-C.js'], modulePreloads: ['chunk-B.js'], stylesheets: ['styles-A.css'] });
  });

  it('go before the end of the head: fonts, fetches with crossorigin, then modules', () => {
    const html = tmAddPreloads(BUILT, { fonts: ['media/a-AAAAAAAA.woff2'], fetches: ['tm-metadata/core-X.json'], modules: ['chunk-D.js'] });
    expect(html).toContain(
      '<link rel="preload" href="media/a-AAAAAAAA.woff2" as="font" type="font/woff2" crossorigin>' +
        '<link rel="preload" href="tm-metadata/core-X.json" as="fetch" crossorigin>' +
        '<link rel="modulepreload" href="chunk-D.js"></head>',
    );
  });
});

describe('the boot script', () => {
  it('embeds English always, each offered language with boot strings, and fills the display name', () => {
    const config = tmBootConfig(metadata());
    expect(config.languages).toEqual([
      { code: 'en', dir: 'ltr' },
      { code: 'ar', dir: 'rtl' },
    ]);
    expect(config.defaultLanguage).toBe('en');
    expect(config.strings['en']!['Distribution_DisplayName']).toBe('Fixture');
    expect(config.strings['en']).not.toHaveProperty('Fixture_Review');
    expect(config.strings['ar']!['Distribution_DisplayName']).toBe('فكسشر');
    expect(Object.keys(config.strings['ar']!)).not.toContain('Distribution_ShortName');
  });

  it('leaves out a language with no boot string of its own, and a pack for a language not offered', () => {
    const { en } = fakeEmitData().strings;
    const config = tmBootConfig(metadata({ en: en!, ar: { Fixture_Review: 'مراجعة' }, fr: { Boot_TryAgain: 'Réessayer' } }));
    expect(Object.keys(config.strings)).toEqual(['en']);
  });

  it('fails when the English pack lacks a boot string, the fallback of every language', () => {
    const en = { ...fakeEmitData().strings['en']! };
    delete en['Boot_Cookies'];
    expect(() => tmBootConfig(metadata({ en }))).toThrow(/English string pack lacks Boot_Cookies/);
  });

  it("renders @tellma/core's template, and fails without it", () => {
    const core = scratch();
    expect(() => tmBuildBootScript(core, tmBootConfig(metadata()))).toThrow(/tm-boot\.template\.js does not exist/);
    mkdirSync(join(core, 'boot'));
    writeFileSync(join(core, 'boot', 'tm-boot.template.js'), 'var TM_BOOT=/*TM_BOOT*/null;run(TM_BOOT);');
    expect(tmBuildBootScript(core, tmBootConfig(metadata()))).toMatch(/^var TM_BOOT=\{"v":1,"languages":\[\{"code":"en"/);
    writeFileSync(join(core, 'boot', 'tm-boot.template.js'), 'no marker');
    expect(() => tmBuildBootScript(core, tmBootConfig(metadata()))).toThrow(/could not be written/);
  });
});

describe('the source maps', () => {
  it('move out of the deployable output, keeping their paths', () => {
    const root = scratch();
    const browser = join(root, 'browser');
    mkdirSync(join(browser, 'media'), { recursive: true });
    for (const file of ['main-A.js', 'main-A.js.map', join('media', 'x.css.map')]) {
      writeFileSync(join(browser, file), 'x');
    }
    expect(tmMoveSourceMaps(browser, join(root, 'sourcemaps'))).toEqual(['main-A.js.map', 'media/x.css.map']);
    expect(existsSync(join(browser, 'main-A.js.map'))).toBe(false);
    expect(readFileSync(join(root, 'sourcemaps', 'media', 'x.css.map'), 'utf8')).toBe('x');
    expect(existsSync(join(browser, 'main-A.js'))).toBe(true);
  });
});
