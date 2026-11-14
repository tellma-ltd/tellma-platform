// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import { execFileSync } from 'node:child_process';
import { readFileSync } from 'node:fs';
import { join } from 'node:path';

import { describe, expect, it } from 'vitest';

import { TM_CLI_EXIT, tmRunCli } from './tellma-client.js';

/** Runs the command and collects what it wrote. */
async function run(...argv: string[]): Promise<{ code: number; out: string; err: string }> {
  let out = '';
  let err = '';
  const code = await tmRunCli(argv, { out: (text) => (out += text), err: (text) => (err += text) });
  return { code, out, err };
}

const packageDir = join(import.meta.dirname, '..', '..');
const { version } = JSON.parse(readFileSync(join(packageDir, 'package.json'), 'utf8')) as { version: string };

describe('tellma-client', () => {
  it('shows its help with exit code 0', async () => {
    const { code, out } = await run('--help');
    expect(code).toBe(TM_CLI_EXIT.success);
    for (const command of ['emit', 'serve', 'build']) {
      expect(out).toContain(`  ${command} `);
    }
  });

  it('shows its version', async () => {
    expect(await run('--version')).toEqual({ code: TM_CLI_EXIT.success, out: `${version}\n`, err: '' });
  });

  it('answers a missing or unknown command with a usage error', async () => {
    expect((await run()).code).toBe(TM_CLI_EXIT.usage);
    const unknown = await run('deploy');
    expect(unknown.code).toBe(TM_CLI_EXIT.usage);
    expect(unknown.err).toContain("unknown command 'deploy'");
  });

  it('shows the help of each command, with its options', async () => {
    const help = new Map((await Promise.all(['emit', 'serve', 'build'].map(async (command) => [command, await run(command, '--help')] as const))));
    for (const [command, result] of help) {
      expect(result.code, command).toBe(TM_CLI_EXIT.success);
      expect(result.out, command).toContain(`Usage: tellma-client ${command} [options]`);
    }
    expect(help.get('emit')!.out).toContain('--no-dotnet-build');
    expect(help.get('build')!.out).toContain('--configuration, -c <c>');
    expect(help.get('serve')!.out).toContain('--port <n>');
  });

  it('answers an unknown option or a missing value with a usage error, before running anything', async () => {
    const runner = { run: () => Promise.reject(new Error('nothing may run')) };
    const usage = async (...argv: string[]): Promise<string> => {
      let err = '';
      const code = await tmRunCli(argv, { out: () => undefined, err: (text) => (err += text) }, { runner });
      expect(code, argv.join(' ')).toBe(TM_CLI_EXIT.usage);
      return err;
    };
    expect(await usage('emit', '--prod')).toContain("tellma-client emit: unknown option '--prod'.");
    expect(await usage('build', '--configuration')).toContain('--configuration needs a value.');
    expect(await usage('serve', '--port', 'eighty')).toContain("--port needs a port number, not 'eighty'.");
    expect(await usage('serve', '--no-dotnet-build')).toContain("unknown option '--no-dotnet-build'");
  });

  it('reports a step that cannot run as a build failure', async () => {
    let err = '';
    const code = await tmRunCli(['emit', '--configuration=Release'], { out: () => undefined, err: (text) => (err += text) }, { cwd: packageDir });
    expect(code).toBe(TM_CLI_EXIT.failure);
    expect(err).toMatch(/^tellma-client emit: .*angular\.json does not exist/);
  });

  // Starts two Node processes, which takes seconds on a busy or small machine.
  it('runs through its bin script with the same exit codes', { timeout: 60_000 }, () => {
    const bin = join(packageDir, 'bin', 'tellma-client.js');
    expect(execFileSync(process.execPath, [bin, '--version'], { encoding: 'utf8' })).toBe(`${version}\n`);
    expect(() => execFileSync(process.execPath, [bin, 'deploy'], { stdio: 'pipe' })).toThrow(
      expect.objectContaining({ status: TM_CLI_EXIT.usage }),
    );
  });
});
