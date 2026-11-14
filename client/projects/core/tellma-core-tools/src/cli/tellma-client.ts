// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import { createRequire } from 'node:module';

import { tmBuild } from '../commands/build.js';
import type { TmCliStreams, TmCommandContext } from '../commands/context.js';
import { tmServe } from '../commands/serve.js';
import { tmNodeProcessRunner, type TmProcessRunner } from '../common/process.js';
import { TmBuildFailure } from '../common/problems.js';
import { tmRunEmit } from '../emit/emit.js';
import { tmReadAppProject } from '../project/app-project.js';

export type { TmCliStreams } from '../commands/context.js';

// The `tellma-client` command line (spec 0024 §2.6): `emit`, `serve` and
// `build`, run in a distribution's client project. The exit code is 0 for
// success, 1 for a usage error, 2 for a build failure (a step that could not
// run) and 3 for a build problem (a check that found one, printed one per
// line as `code key feature - message`).

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

/** What the command runs with besides its arguments; the real process's by default. */
export interface TmCliEnvironment {
  /** The client project's folder; the working directory by default. */
  readonly cwd?: string;
  /** Starts the programs the commands run. */
  readonly runner?: TmProcessRunner;
}

/** One command: its summary, options and body. */
interface Command {
  /** The one-line summary the help shows. */
  readonly summary: string;
  /** The options, as the help shows them. */
  readonly options: readonly (readonly [string, string])[];
  /** The options that take a value, by every spelling. */
  readonly valued: readonly string[];
  /** The options that take none, by every spelling. */
  readonly flags: readonly string[];
  /** Runs the command and resolves with its exit code. */
  readonly run: (context: TmCommandContext, options: ReadonlyMap<string, string | true>) => Promise<number>;
}

/** The emit's options as a command line gives them. */
function emitOptions(options: ReadonlyMap<string, string | true>): { configuration?: string; noDotnetBuild: boolean } {
  const configuration = options.get('--configuration');
  return {
    ...(typeof configuration === 'string' ? { configuration } : {}),
    noDotnetBuild: options.has('--no-dotnet-build'),
  };
}

/** The commands, by name. */
const COMMANDS: ReadonlyMap<string, Command> = new Map<string, Command>([
  [
    'emit',
    {
      summary: "Writes the client metadata through the distribution's Migrator.",
      options: [
        ['--configuration, -c <c>', "The Migrator's .NET build configuration."],
        ['--no-dotnet-build', 'Runs the Migrator without building it first.'],
      ],
      valued: ['--configuration', '-c'],
      flags: ['--no-dotnet-build'],
      run: async (context, options) => {
        await tmRunEmit(tmReadAppProject(context.cwd), emitOptions(options), context.runner);
        return TM_CLI_EXIT.success;
      },
    },
  ],
  [
    'serve',
    {
      summary: 'Runs the development server.',
      options: [['--port <n>', "The port to serve on; the worktree's <SLUG>_CLIENT port, else 4200, by default."]],
      valued: ['--port'],
      flags: [],
      run: async (context, options) => {
        const port = options.get('--port');
        if (port !== undefined && (typeof port !== 'string' || !/^\d+$/.test(port) || Number(port) < 1 || Number(port) > 65535)) {
          context.streams.err(`tellma-client: --port needs a port number, not '${String(port)}'.\n`);
          return TM_CLI_EXIT.usage;
        }
        return tmServe(context, port === undefined ? {} : { port: Number(port) });
      },
    },
  ],
  [
    'build',
    {
      summary: 'Runs the production build and its checks.',
      options: [
        ['--configuration, -c <c>', "The Migrator's .NET build configuration."],
        ['--no-dotnet-build', 'Runs the Migrator without building it first.'],
      ],
      valued: ['--configuration', '-c'],
      flags: ['--no-dotnet-build'],
      run: async (context, options) => {
        const result = await tmBuild(context, emitOptions(options));
        const errors = result.problems.filter((problem) => problem.severity === 'error').length;
        const warnings = result.problems.length - errors;
        context.streams.out(`[tellma-client] ${result.passed ? 'Built' : 'Failed'} with ${errors} error(s) and ${warnings} warning(s).\n`);
        return result.passed ? TM_CLI_EXIT.success : TM_CLI_EXIT.problem;
      },
    },
  ],
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
  const lines = [...COMMANDS].map(([name, command]) => `  ${name.padEnd(width)}  ${command.summary}`);
  return ['Usage: tellma-client <command> [options]', '', 'Commands:', ...lines, '', 'Options:',
    '  --help     Shows this help.', '  --version  Shows the version.', ''].join('\n');
}

/** One command's help text. */
function commandUsage(name: string, command: Command): string {
  const width = Math.max(...command.options.map(([option]) => option.length), '--help'.length);
  const lines = [...command.options, ['--help', 'Shows this help.'] as const].map(([option, text]) => `  ${option.padEnd(width)}  ${text}`);
  return [`Usage: tellma-client ${name} [options]`, '', command.summary, '', 'Options:', ...lines, ''].join('\n');
}

/**
 * Parses a command's arguments into options by their long spelling.
 *
 * @returns The options, or the reason the arguments are wrong.
 */
function parseOptions(command: Command, args: readonly string[]): ReadonlyMap<string, string | true> | string {
  const options = new Map<string, string | true>();
  const long = (name: string): string => (name === '-c' ? '--configuration' : name);
  for (let index = 0; index < args.length; index++) {
    const arg = args[index]!;
    const [name, inline] = arg.startsWith('--') && arg.includes('=') ? [arg.slice(0, arg.indexOf('=')), arg.slice(arg.indexOf('=') + 1)] : [arg, undefined];
    if (command.flags.includes(name) && inline === undefined) {
      options.set(long(name), true);
    } else if (command.valued.includes(name)) {
      const value = inline ?? args[++index];
      if (value === undefined || value === '') {
        return `${name} needs a value.`;
      }
      options.set(long(name), value);
    } else {
      return `unknown option '${arg}'.`;
    }
  }
  return options;
}

/**
 * Runs `tellma-client` with the arguments after the program name, and
 * resolves with the exit code.
 *
 * @param argv - The command-line arguments, without the Node executable and script.
 * @param streams - Where to write; the process's own streams by default.
 * @param environment - The project folder and process runner; the real process's by default.
 */
export async function tmRunCli(
  argv: readonly string[],
  streams: TmCliStreams = {
    out: (text) => process.stdout.write(text),
    err: (text) => process.stderr.write(text),
  },
  environment: TmCliEnvironment = {},
): Promise<number> {
  const [name, ...rest] = argv;

  // --- Options that stand in for a command ---
  if (name === undefined || name === '--help' || name === '-h') {
    (name === undefined ? streams.err : streams.out)(usage());
    return name === undefined ? TM_CLI_EXIT.usage : TM_CLI_EXIT.success;
  }
  if (name === '--version' || name === '-v') {
    streams.out(`${version()}\n`);
    return TM_CLI_EXIT.success;
  }
  const command = COMMANDS.get(name);
  if (command === undefined) {
    streams.err(`tellma-client: unknown command '${name}'.\n\n${usage()}`);
    return TM_CLI_EXIT.usage;
  }

  // --- The command's own help and options ---
  if (rest.includes('--help') || rest.includes('-h')) {
    streams.out(commandUsage(name, command));
    return TM_CLI_EXIT.success;
  }
  const options = parseOptions(command, rest);
  if (typeof options === 'string') {
    streams.err(`tellma-client ${name}: ${options}\n\n${commandUsage(name, command)}`);
    return TM_CLI_EXIT.usage;
  }

  // --- The command; a step that cannot run is a build failure ---
  const context: TmCommandContext = {
    cwd: environment.cwd ?? process.cwd(),
    runner: environment.runner ?? tmNodeProcessRunner,
    streams,
  };
  try {
    return await command.run(context, options);
  } catch (error) {
    const message = error instanceof TmBuildFailure ? error.message : error instanceof Error ? (error.stack ?? error.message) : String(error);
    streams.err(`tellma-client ${name}: ${message}\n`);
    return TM_CLI_EXIT.failure;
  }
}
