// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import { mkdirSync, mkdtempSync, rmSync, writeFileSync } from 'node:fs';
import { tmpdir } from 'node:os';
import { join } from 'node:path';

import { afterEach, describe, expect, it } from 'vitest';

import { tmCompressedSize, tmContentHash, tmListFiles, tmRelativePosix } from './files.js';
import { tmNodeProcessRunner, tmProcessLaunch } from './process.js';
import { tmFormatProblem, tmHasErrors, type TmBuildProblem } from './problems.js';

const problem: TmBuildProblem = {
  code: 'manifest-invalid',
  severity: 'error',
  key: 'public/manifest.webmanifest',
  feature: 'acme',
  message: "The member 'id' is missing.",
};

describe('build problems', () => {
  it('print as one code key feature line, then the message', () => {
    expect(tmFormatProblem(problem)).toBe("manifest-invalid public/manifest.webmanifest acme - The member 'id' is missing.");
  });

  it('keep each field one word, so the line splits on spaces', () => {
    expect(tmFormatProblem({ ...problem, key: 'public/my brand/mark.svg', feature: '' })).toBe(
      "manifest-invalid public/my_brand/mark.svg - - The member 'id' is missing.",
    );
  });

  it('fail the build only when one is an error', () => {
    expect(tmHasErrors([{ ...problem, severity: 'warning' }])).toBe(false);
    expect(tmHasErrors([{ ...problem, severity: 'warning' }, problem])).toBe(true);
  });
});

describe('file helpers', () => {
  let dir: string | undefined;
  afterEach(() => {
    if (dir !== undefined) {
      rmSync(dir, { recursive: true, force: true });
    }
  });

  it("hash content as the Migrator names files: SHA-256, Crockford's base32, 12 characters", () => {
    expect(tmContentHash(new Uint8Array())).toBe('WERC8GMRZGE1');
    expect(tmContentHash(Buffer.from('{"a":1}\n'))).toBe('WD346811P10Q');
  });

  it('list files recursively with forward slashes, in ordinal order', () => {
    dir = mkdtempSync(join(tmpdir(), 'tm-files-'));
    mkdirSync(join(dir, 'media', 'deep'), { recursive: true });
    for (const file of ['b.js', 'a.css', join('media', 'f.woff2'), join('media', 'deep', 'x.txt')]) {
      writeFileSync(join(dir, file), 'x');
    }
    expect(tmListFiles(dir)).toEqual(['a.css', 'b.js', 'media/deep/x.txt', 'media/f.woff2']);
    expect(tmRelativePosix(dir, join(dir, 'media', 'f.woff2'))).toBe('media/f.woff2');
  });

  it('measure gzip and Brotli sizes', () => {
    const size = tmCompressedSize('a'.repeat(10_000));
    expect(size.gzip).toBeGreaterThan(0);
    expect(size.gzip).toBeLessThan(100);
    expect(size.brotli).toBeLessThan(size.gzip);
  });
});

describe('how the process runner starts a program', () => {
  const env = { ComSpec: 'C:\\Windows\\system32\\cmd.exe' };

  it("starts a package manager's shim on Windows through the command interpreter, the command and its arguments after /c", () => {
    expect(tmProcessLaunch('pnpm', ['run', 'assets:generate'], 'win32', env)).toEqual({
      program: env.ComSpec,
      args: ['/d', '/s', '/c', 'pnpm', 'run', 'assets:generate'],
    });
    for (const shim of ['npm', 'npx']) {
      expect(tmProcessLaunch(shim, ['--version'], 'win32', env), shim).toEqual({ program: env.ComSpec, args: ['/d', '/s', '/c', shim, '--version'] });
    }
  });

  it('names cmd.exe when ComSpec is unset', () => {
    expect(tmProcessLaunch('pnpm', ['--version'], 'win32', {})).toEqual({ program: 'cmd.exe', args: ['/d', '/s', '/c', 'pnpm', '--version'] });
  });

  it('starts any other program on Windows, and every program elsewhere, as it is', () => {
    expect(tmProcessLaunch('dotnet', ['run', '--project', 'x'], 'win32', env)).toEqual({ program: 'dotnet', args: ['run', '--project', 'x'] });
    expect(tmProcessLaunch(process.execPath, ['-e', ''], 'win32', env)).toEqual({ program: process.execPath, args: ['-e', ''] });
    for (const platform of ['linux', 'darwin'] as const) {
      expect(tmProcessLaunch('pnpm', ['--version'], platform, env), platform).toEqual({ program: 'pnpm', args: ['--version'] });
    }
  });
});

// These start real programs. On a busy Windows machine pnpm takes 3-13 s to
// answer --version from a shell, and 15 s or more beside the other spec files,
// so vitest's 5 s default would fail them on load alone. How each program is
// started is covered above without starting one.
describe('the process runner', { timeout: 120_000 }, () => {
  it("starts a package manager's shim with no deprecated shell option", async () => {
    const warnings: string[] = [];
    const listener = (warning: Error & { code?: string }): void => {
      warnings.push(warning.code ?? warning.name);
    };
    process.on('warning', listener);
    try {
      expect(await tmNodeProcessRunner.run('pnpm', ['--version'], { cwd: process.cwd() })).toBe(0);
    } finally {
      process.off('warning', listener);
    }
    expect(warnings).not.toContain('DEP0190');
  });

  it("resolves with the program's exit code", async () => {
    expect(await tmNodeProcessRunner.run(process.execPath, ['-e', 'process.exit(3)'], { cwd: process.cwd() })).toBe(3);
  });

  it('fails a program that cannot be started', async () => {
    await expect(tmNodeProcessRunner.run('tellma-no-such-program', [], { cwd: process.cwd() })).rejects.toThrow(/could not be started/);
  });
});
