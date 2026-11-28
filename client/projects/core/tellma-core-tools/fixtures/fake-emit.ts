// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import { cpSync, mkdirSync, readdirSync, readFileSync, rmSync, writeFileSync } from 'node:fs';
import { dirname, join } from 'node:path';
import { fileURLToPath } from 'node:url';

import { tmContentHash } from '../src/common/files.js';
import { tmNodeProcessRunner, type TmProcessOptions, type TmProcessRunner } from '../src/common/process.js';

// The specs' stand-in for a distribution's Migrator. The fixture app names
// fixtures/migrator/ as its Migrator; a spec's process runner answers the
// `dotnet run … emit-client-assets <dir>` the emit starts by writing the
// files the real emit writes, from the JSON in that folder, and passes
// every other program to the real runner unless the spec intercepts it.

/** The fixtures folder. */
export const FIXTURES_DIR = dirname(fileURLToPath(import.meta.url));

/** The fixture app's folder. */
export const FIXTURE_APP_DIR = join(FIXTURES_DIR, 'app');

/** The emitted data per file kind, as a spec may change it before the emit writes it. */
export interface FakeEmitData {
  /** The Core chunk. */
  core: Record<string, unknown>;
  /** The Entities chunk. */
  entities: Record<string, unknown>;
  /** The Contracts chunk. */
  contracts: Record<string, unknown>;
  /** The string packs per language code. */
  strings: Record<string, Record<string, string>>;
}

/** Reads one JSON file of the fake Migrator. */
function readMigratorJson<T>(name: string): T {
  return JSON.parse(readFileSync(join(FIXTURES_DIR, 'migrator', name), 'utf8')) as T;
}

/** The fake Migrator's data, read fresh for each call so a spec may change it freely. */
export function fakeEmitData(): FakeEmitData {
  const strings: Record<string, Record<string, string>> = {};
  for (const name of readdirSync(join(FIXTURES_DIR, 'migrator'))) {
    const match = /^strings-(.+)\.json$/.exec(name);
    if (match !== null) {
      strings[match[1]!] = readMigratorJson(name);
    }
  }
  return {
    core: readMigratorJson('core.json'),
    entities: readMigratorJson('entities.json'),
    contracts: readMigratorJson('contracts.json'),
    strings,
  };
}

/**
 * Writes what the Migrator's emit writes: hashed chunks and string packs
 * under assets/, and the loaders module naming them.
 *
 * @param generatedDir - The emit's output folder.
 * @param data - The data to write.
 */
export function writeFakeEmit(generatedDir: string, data: FakeEmitData = fakeEmitData()): void {
  rmSync(generatedDir, { recursive: true, force: true });
  mkdirSync(join(generatedDir, 'assets'), { recursive: true });
  const write = (kind: string, value: unknown): string => {
    const text = `${JSON.stringify(value)}\n`;
    const name = `${kind}-${tmContentHash(Buffer.from(text, 'utf8'))}.json`;
    writeFileSync(join(generatedDir, 'assets', name), text);
    return `tm-metadata/${name}`;
  };
  const core = write('core', data.core);
  const entities = write('entities', data.entities);
  const contracts = write('contracts', data.contracts);
  const strings = Object.fromEntries(Object.entries(data.strings).map(([code, pack]) => [code, write(`strings-${code}`, pack)]));
  writeFileSync(
    join(generatedDir, 'tellma-metadata.ts'),
    [
      "import { tmMetadataLoaders } from '@tellma/core';",
      '',
      '/** The loaders of the fixture build\'s client metadata. */',
      'export const tellmaMetadata = tmMetadataLoaders({',
      `  core: '${core}',`,
      `  entities: '${entities}',`,
      `  contracts: '${contracts}',`,
      `  strings: ${JSON.stringify(strings)},`,
      '});',
      '',
    ].join('\n'),
  );
}

/** One program a fake runner was asked to start. */
export interface RunnerCall {
  /** The program. */
  readonly command: string;
  /** Its arguments. */
  readonly args: readonly string[];
  /** Its working directory. */
  readonly cwd: string;
}

/** A process runner that fakes the Migrator and records every call. */
export interface FakeRunner extends TmProcessRunner {
  /** Every program started, in order. */
  readonly calls: RunnerCall[];
}

/** What a fake runner does besides the emit. */
export interface FakeRunnerOptions {
  /** The data the emit writes; the fake Migrator's by default. */
  readonly data?: () => FakeEmitData;
  /** Answers a Node program instead of running it, by returning an exit code; undefined runs it. */
  readonly node?: (args: readonly string[], options: TmProcessOptions) => Promise<number | undefined> | number | undefined;
  /** Answers any other program; by default it exits with 0 and does nothing. */
  readonly other?: (command: string, args: readonly string[], options: TmProcessOptions) => Promise<number> | number;
}

/**
 * Creates a runner that writes the fake emit for `dotnet run … emit-client-assets`.
 *
 * @param options - How it answers other programs.
 * @returns The runner.
 */
export function fakeRunner(options: FakeRunnerOptions = {}): FakeRunner {
  const calls: RunnerCall[] = [];
  return {
    calls,
    async run(command, args, processOptions) {
      calls.push({ command, args, cwd: processOptions.cwd });
      const emit = args.indexOf('emit-client-assets');
      if (command === 'dotnet' && args[0] === 'run' && emit >= 0) {
        writeFakeEmit(args[emit + 1]!, (options.data ?? fakeEmitData)());
        return 0;
      }
      if (command === process.execPath) {
        const answer = await options.node?.(args, processOptions);
        return answer ?? tmNodeProcessRunner.run(command, args, processOptions);
      }
      return (await options.other?.(command, args, processOptions)) ?? 0;
    },
  };
}

/**
 * Copies the fixture app to a sibling folder, at the same depth so its
 * relative paths to the workspace hold, for a spec to change and build.
 *
 * @param name - The copy's name; the folder is `fixtures/run-<name>/`.
 * @returns The copy's folder.
 */
export function copyFixtureApp(name: string): string {
  const target = join(FIXTURES_DIR, `run-${name}`);
  rmSync(target, { recursive: true, force: true });
  cpSync(FIXTURE_APP_DIR, target, {
    recursive: true,
    filter: (source) => !/[\\/](?:dist|generated|\.angular)$/.test(source),
  });
  return target;
}
