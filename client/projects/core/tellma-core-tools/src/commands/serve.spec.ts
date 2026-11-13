// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import { mkdtempSync, readFileSync, rmSync, writeFileSync } from 'node:fs';
import { tmpdir } from 'node:os';
import { join } from 'node:path';

import { afterAll, beforeAll, describe, expect, it } from 'vitest';

import { copyFixtureApp, fakeRunner, FIXTURES_DIR } from '../../fixtures/fake-emit.js';
import type { TmProcessOptions } from '../common/process.js';
import { tmDevPort, tmProxyConfig, tmServe, TM_DEFAULT_CLIENT_PORT, TM_DEFAULT_WEB_PORT } from './serve.js';

let app: string;
beforeAll(() => {
  app = copyFixtureApp('serve');
});
afterAll(() => {
  rmSync(app, { recursive: true, force: true });
});

const ROUTING = {
  serverFirstSegments: ['api', 'bff', '.well-known'],
  serverTenantSegments: ['api', 'hub'],
};

describe('the proxy', () => {
  const config = tmProxyConfig(ROUTING, 7100);
  const match = (url: string): boolean => Object.keys(config).some((key) => new RegExp(key).test(url));

  it("sends the server's paths to the web host, websockets included", () => {
    expect(Object.values(config)).toEqual([
      { target: 'https://localhost:7100', secure: false, ws: true },
      { target: 'https://localhost:7100', secure: false, ws: true },
    ]);
    for (const url of ['/api', '/api/x?y=1', '/bff/login?returnUrl=%2F', '/.well-known/openid-configuration', '/12/api/users', '/12/hub', '/12/hub?id=1']) {
      expect(match(url), url).toBe(true);
    }
  });

  it("keeps the app's own paths, including words that only begin like a server word", () => {
    for (const url of ['/', '/apiary', '/tenants', '/12/settings', '/12/hubs', '/x/api', '/main-ABCD1234.js', '/xwell-known']) {
      expect(match(url), url).toBe(false);
    }
  });
});

describe('the dev ports', () => {
  it('come from the nearest .dev-ports.local', () => {
    const file = join(app, '.dev-ports.local');
    writeFileSync(file, 'FIXTURE_CLIENT=4321\nFIXTURE_WEB=not-a-port\n');
    try {
      expect(tmDevPort(join(app, 'src'), 'FIXTURE_CLIENT')).toBe(4321);
      expect(tmDevPort(app, 'FIXTURE_WEB')).toBeNull();
      expect(tmDevPort(app, 'OTHER_CLIENT')).toBeNull();
    } finally {
      rmSync(file);
    }
  });
});

describe('serve', () => {
  /** A runner that fakes the Migrator, the certificate export and the dev server, recording ng serve's arguments. */
  function serveRunner(served: string[][]): ReturnType<typeof fakeRunner> {
    return fakeRunner({
      node: (args) => {
        if (args.includes('serve')) {
          served.push([...args]);
          return 0;
        }
        return undefined;
      },
      other: (command, args) => {
        // Writes the exported files, as dotnet dev-certs does.
        if (command === 'dotnet' && args[0] === 'dev-certs') {
          const pem = args[args.indexOf('--export-path') + 1]!;
          writeFileSync(pem, 'cert');
          writeFileSync(pem.replace(/\.pem$/, '.key'), 'key');
        }
        return 0;
      },
    });
  }

  it('emits, exports the certificate, then runs ng serve over HTTPS with the proxy', async () => {
    const certificates = mkdtempSync(join(tmpdir(), 'tm-certs-'));
    writeFileSync(join(app, '.dev-ports.local'), 'FIXTURE_CLIENT=4321\n');
    const served: string[][] = [];
    let proxy: unknown;
    const runner = serveRunner(served);
    const recording = {
      ...runner,
      run: async (command: string, args: readonly string[], options: TmProcessOptions) => {
        // The proxy file lives only while ng serve runs.
        if (args.includes('--proxy-config')) {
          proxy = JSON.parse(readFileSync(args[args.indexOf('--proxy-config') + 1]!, 'utf8'));
        }
        return runner.run(command, args, options);
      },
    };
    let out = '';
    try {
      const code = await tmServe({ cwd: app, runner: recording, streams: { out: (text) => (out += text), err: () => undefined } }, { certificateDir: certificates });
      expect(code).toBe(0);
    } finally {
      rmSync(join(app, '.dev-ports.local'));
      rmSync(certificates, { recursive: true, force: true });
    }

    // In this repository the platform's generated assets come first.
    const commands = runner.calls.map((call) => [call.command, ...call.args.slice(0, 2)].join(' '));
    expect(commands.slice(0, 3)).toEqual(['pnpm run assets:generate', 'dotnet run --project', 'dotnet dev-certs https']);
    expect(runner.calls[2]!.args).toEqual(['dev-certs', 'https', '--export-path', join(certificates, 'fixture.pem'), '--format', 'Pem', '--no-password']);
    expect(served).toHaveLength(1);
    expect(served[0]!.slice(1, 10)).toEqual([
      'serve',
      '--port',
      '4321',
      '--ssl',
      '--ssl-cert',
      join(certificates, 'fixture.pem'),
      '--ssl-key',
      join(certificates, 'fixture.key'),
      '--proxy-config',
    ]);
    expect(runner.calls.at(-1)!.cwd).toBe(app);
    const routing = (JSON.parse(readFileSync(join(FIXTURES_DIR, 'migrator', 'core.json'), 'utf8')) as { routing: typeof ROUTING }).routing;
    expect(proxy).toEqual(tmProxyConfig(routing, TM_DEFAULT_WEB_PORT));
    expect(out).toContain('Serving on https://localhost:4321/');
    // The boot script the page loads first is there for the dev server to serve.
    const boot = readFileSync(join(app, 'generated', 'root', 'tm-boot.js'), 'utf8');
    expect(boot).toContain('"defaultLanguage":"en"');
    expect(boot).not.toContain('/*TM_BOOT*/');
  });

  it('serves on the --port given, else on the default port, and exports the certificate once', async () => {
    const certificates = mkdtempSync(join(tmpdir(), 'tm-certs-'));
    const served: string[][] = [];
    const runner = serveRunner(served);
    const context = { cwd: app, runner, streams: { out: () => undefined, err: () => undefined } };
    try {
      await tmServe(context, { port: 5000, certificateDir: certificates });
      await tmServe(context, { certificateDir: certificates });
    } finally {
      rmSync(certificates, { recursive: true, force: true });
    }
    expect(served.map((args) => args[args.indexOf('--port') + 1])).toEqual(['5000', String(TM_DEFAULT_CLIENT_PORT)]);
    expect(runner.calls.filter((call) => call.args[0] === 'dev-certs')).toHaveLength(1);
  });

  it('fails before the emit when angular.json does not copy generated/root/ to the output root', async () => {
    const copy = copyFixtureApp('serve-no-root');
    const file = join(copy, 'angular.json');
    writeFileSync(file, readFileSync(file, 'utf8').replace('"input": "generated/root"', '"input": "generated/other"'));
    const runner = fakeRunner();
    try {
      await expect(tmServe({ cwd: copy, runner, streams: { out: () => undefined, err: () => undefined } }, {})).rejects.toThrow(
        /must copy generated\/root\/ to the output root/,
      );
      expect(runner.calls).toEqual([]);
    } finally {
      rmSync(copy, { recursive: true, force: true });
    }
  });

  it('fails when the certificate cannot be exported', async () => {
    const certificates = mkdtempSync(join(tmpdir(), 'tm-certs-'));
    const runner = fakeRunner({ other: (command, args) => (args[0] === 'dev-certs' ? 1 : 0) });
    try {
      await expect(
        tmServe({ cwd: app, runner, streams: { out: () => undefined, err: () => undefined } }, { certificateDir: certificates }),
      ).rejects.toThrow(/development certificate could not be exported \(code 1\)/);
    } finally {
      rmSync(certificates, { recursive: true, force: true });
    }
  });
});
