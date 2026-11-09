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

  it('shows the help of each command, and reports that this version cannot run it', async () => {
    for (const command of ['emit', 'serve', 'build']) {
      expect((await run(command, '--help')).code).toBe(TM_CLI_EXIT.success);
      const result = await run(command);
      expect(result.code).toBe(TM_CLI_EXIT.failure);
      expect(result.err).toContain(`'${command}' is not available`);
    }
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
