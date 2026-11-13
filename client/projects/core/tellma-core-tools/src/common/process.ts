// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import { spawn } from 'node:child_process';

import { TmBuildFailure } from './problems.js';

// The one seam through which the commands start other programs: the .NET CLI
// for the emit and the certificate, Node for the Angular CLI, the config
// check and the worker config, and pnpm for the platform's generated assets.
// Tests replace it to observe or fake a step without running it.

/** How a child process runs. */
export interface TmProcessOptions {
  /** The working directory. */
  readonly cwd: string;
  /** Variables added to the parent's environment. */
  readonly env?: Readonly<Record<string, string>>;
}

/** Starts a program and resolves with its exit code once it ends. */
export interface TmProcessRunner {
  /**
   * Runs a program to its end, its output passed through to this process's.
   *
   * @param command - The program: an absolute path, or a name the PATH resolves.
   * @param args - Its arguments, passed as they are, with no shell.
   * @param options - Its working directory and added environment.
   * @returns The exit code; a program that dies by a signal counts as 1.
   * @throws TmBuildFailure when the program cannot be started.
   */
  run(command: string, args: readonly string[], options: TmProcessOptions): Promise<number>;
}

/** The package managers' command shims, which Windows starts as `.cmd` files through a shell. */
const WINDOWS_SHIMS: ReadonlySet<string> = new Set(['pnpm', 'npm', 'npx']);

/**
 * The runner of real processes. Arguments never pass through a shell, except
 * that Windows needs `cmd.exe` to start a package manager's `.cmd` shim;
 * those calls take only fixed arguments.
 */
export const tmNodeProcessRunner: TmProcessRunner = {
  run(command, args, options) {
    // A shim starts through cmd.exe named as the program, rather than through
    // spawn's shell option, which Node deprecates when given an argument list.
    const shim = process.platform === 'win32' && WINDOWS_SHIMS.has(command);
    const program = shim ? (process.env['ComSpec'] ?? 'cmd.exe') : command;
    const programArgs = shim ? ['/d', '/s', '/c', command, ...args] : [...args];
    return new Promise<number>((resolve, reject) => {
      const child = spawn(program, programArgs, {
        cwd: options.cwd,
        env: { ...process.env, ...options.env },
        stdio: 'inherit',
      });
      // A program that cannot start, such as one missing from the PATH, fails the step.
      child.once('error', (error) => reject(new TmBuildFailure(`${command} could not be started: ${error.message}`, { cause: error })));
      child.once('close', (code) => resolve(code ?? 1));
    });
  },
};
