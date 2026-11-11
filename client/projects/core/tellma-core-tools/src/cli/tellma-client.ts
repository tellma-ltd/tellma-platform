// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import { createRequire } from 'node:module';

// The `tellma-client` command line (spec 0024 §2.6). This version parses the
// command line and answers help and version; the three commands are named
// and reserved, and each reports that this version cannot run it yet.

/** Where the command writes: its standard output and standard error. */
export interface TmCliStreams {
  /** Writes one chunk of normal output. */
  readonly out: (text: string) => void;
  /** Writes one chunk of diagnostic output. */
  readonly err: (text: string) => void;
}

/**
 * The exit codes of `tellma-client`: 0 for success, 1 for a usage error, 2
 * for a build failure and 3 for a build problem.
 */
export const TM_CLI_EXIT = Object.freeze({
  /** The command did what it was asked. */
  success: 0,
  /** The command line was wrong: an unknown command or option. */
  usage: 1,
  /** A step of the command failed to run. */
  failure: 2,
  /** The command ran and found a problem in the sources or the output. */
  problem: 3,
});

/** The commands `tellma-client` reserves, each with its one-line summary. */
const COMMANDS: ReadonlyMap<string, string> = new Map([
  ['emit', "Writes the client metadata through the distribution's Migrator."],
  ['serve', 'Runs the development server.'],
  ['build', 'Runs the production build and its checks.'],
]);

/** The package version, read from the package's own manifest. */
function version(): string {
  // dist/cli and src/cli both sit two levels below the package root.
  const manifest = createRequire(import.meta.url)('../../package.json') as { readonly version: string };
  return manifest.version;
}

/** The help text, listing every command. */
function usage(): string {
  const width = Math.max(...[...COMMANDS.keys()].map((name) => name.length));
  const lines = [...COMMANDS].map(([name, summary]) => `  ${name.padEnd(width)}  ${summary}`);
  return ['Usage: tellma-client <command> [options]', '', 'Commands:', ...lines, '', 'Options:',
    '  --help     Shows this help.', '  --version  Shows the version.', ''].join('\n');
}

/**
 * Runs `tellma-client` with the arguments after the program name, and
 * resolves with the exit code.
 *
 * @param argv - The command-line arguments, without the Node executable and script.
 * @param streams - Where to write; the process's own streams by default.
 */
export async function tmRunCli(
  argv: readonly string[],
  streams: TmCliStreams = {
    out: (text) => process.stdout.write(text),
    err: (text) => process.stderr.write(text),
  },
): Promise<number> {
  const [command, ...rest] = argv;

  // Options that stand in for a command.
  if (command === undefined || command === '--help' || command === '-h') {
    (command === undefined ? streams.err : streams.out)(usage());
    return command === undefined ? TM_CLI_EXIT.usage : TM_CLI_EXIT.success;
  }
  if (command === '--version' || command === '-v') {
    streams.out(`${version()}\n`);
    return TM_CLI_EXIT.success;
  }

  // A known command: help for it, or the report that it cannot run yet.
  if (COMMANDS.has(command)) {
    if (rest.includes('--help') || rest.includes('-h')) {
      streams.out(`Usage: tellma-client ${command} [options]\n\n${COMMANDS.get(command)}\n`);
      return TM_CLI_EXIT.success;
    }
    streams.err(`tellma-client: '${command}' is not available in @tellma/core-tools ${version()}.\n`);
    return TM_CLI_EXIT.failure;
  }

  streams.err(`tellma-client: unknown command '${command}'.\n\n${usage()}`);
  return TM_CLI_EXIT.usage;
}
