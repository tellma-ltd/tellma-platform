// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import { existsSync, readFileSync } from 'node:fs';

import { TmBuildFailure } from '../common/problems.js';

// Which bytes of a built file came from which source modules, read from the
// file's source map (version 3). A map's mappings mark, line by line, the
// column where each run of generated code starts and the source it came
// from; a run lasts until the next one starts or the line ends. A run with
// no source (glue the bundler wrote) belongs to no module. Columns count
// UTF-16 code units, as JavaScript strings do, so the file is sliced as a
// string. The budgets use this to weigh one package's share of chunks it
// shares with other code.

/** The parts of a version 3 source map the attribution reads. */
export interface TmSourceMap {
  /** The source paths, which the mappings index. */
  readonly sources: readonly (string | null)[];
  /** A prefix of every source path, when the map has one. */
  readonly sourceRoot?: string;
  /** The Base64 VLQ mappings. */
  readonly mappings: string;
}

/** The Base64 digits a VLQ is written in, by value. */
const BASE64 = 'ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789+/';

/** The value of each Base64 digit, by character code; -1 for a character that is not one. */
const DIGITS = (() => {
  const digits = new Int8Array(128).fill(-1);
  for (let index = 0; index < BASE64.length; index++) {
    digits[BASE64.charCodeAt(index)] = index;
  }
  return digits;
})();

/** One run of generated code: the column it starts at and the index of its source, or -1 for none. */
interface Run {
  /** The generated column the run starts at. */
  readonly column: number;
  /** The index into `sources`, or -1 when the run has no source. */
  readonly source: number;
}

/**
 * Reads a file's source map.
 *
 * @param file - The map's path.
 * @returns The map.
 * @throws TmBuildFailure when the file is missing, is not JSON, or is not a
 * version 3 map with sources and mappings (an indexed map with sections
 * included).
 */
export function tmReadSourceMap(file: string): TmSourceMap {
  if (!existsSync(file)) {
    throw new TmBuildFailure(`${file} does not exist; the build reads the hidden script source maps the application builder writes.`);
  }
  let map: unknown;
  try {
    map = JSON.parse(readFileSync(file, 'utf8'));
  } catch (error) {
    throw new TmBuildFailure(`${file} is not valid JSON.`, { cause: error });
  }
  const { version, sources, mappings, sourceRoot } = (typeof map === 'object' && map !== null ? map : {}) as Record<string, unknown>;
  if (
    version !== 3 ||
    !Array.isArray(sources) ||
    !sources.every((source) => source === null || typeof source === 'string') ||
    typeof mappings !== 'string' ||
    (sourceRoot !== undefined && typeof sourceRoot !== 'string')
  ) {
    throw new TmBuildFailure(`${file} is not a version 3 source map with sources and mappings.`);
  }
  return { sources: sources as (string | null)[], mappings, ...(sourceRoot === undefined ? {} : { sourceRoot }) };
}

/**
 * Decodes a map's mappings into the runs of each generated line. Only the
 * generated column and the source index are kept: the attribution needs no
 * original position or name.
 *
 * @param mappings - The Base64 VLQ mappings.
 * @param sources - The number of sources, which a source index must stay under.
 * @returns The runs of each line, in column order.
 * @throws TmBuildFailure when the mappings are malformed.
 */
function decodeRuns(mappings: string, sources: number): Run[][] {
  const malformed = (): TmBuildFailure => new TmBuildFailure('A source map holds malformed mappings.');
  const lines: Run[][] = [];
  // The source index is relative to the previous segment's across the whole
  // map; the generated column is relative within its line.
  let source = 0;
  for (const line of mappings.split(';')) {
    const runs: Run[] = [];
    let column = 0;
    for (const segment of line.split(',')) {
      if (segment === '') {
        continue;
      }
      // --- One segment: 1, 4 or 5 VLQ fields ---
      const fields: number[] = [];
      let value = 0;
      let shift = 0;
      for (let index = 0; index < segment.length; index++) {
        const code = segment.charCodeAt(index);
        const digit = code < 128 ? DIGITS[code]! : -1;
        if (digit < 0 || shift > 30) {
          throw malformed();
        }
        value += (digit & 31) * 2 ** shift;
        if ((digit & 32) !== 0) {
          shift += 5;
        } else {
          fields.push(value % 2 === 1 ? -(value - 1) / 2 : value / 2);
          value = 0;
          shift = 0;
        }
      }
      if (shift !== 0 || ![1, 4, 5].includes(fields.length)) {
        throw malformed();
      }
      column += fields[0]!;
      if (fields.length === 1) {
        runs.push({ column, source: -1 });
      } else {
        source += fields[1]!;
        if (source < 0 || source >= sources) {
          throw malformed();
        }
        runs.push({ column, source });
      }
    }
    lines.push(runs.sort((a, b) => a.column - b.column));
  }
  return lines;
}

/**
 * Takes the generated code that came from the sources a test accepts, in
 * file order.
 *
 * @param code - The built file's text.
 * @param map - Its source map.
 * @param test - Tests a source's path, the map's `sourceRoot` prefixed and
 * backslashes turned into forward slashes.
 * @returns The runs of code from accepted sources, joined.
 * @throws TmBuildFailure when the mappings are malformed.
 */
export function tmCodeFromSources(code: string, map: TmSourceMap, test: (source: string) => boolean): string {
  // --- Which sources the test accepts, by index ---
  const root = map.sourceRoot === undefined || map.sourceRoot === '' ? '' : map.sourceRoot.replace(/\/?$/, '/');
  const accepted = map.sources.map((source) => source !== null && test(`${root}${source}`.replace(/\\/g, '/')));
  if (!accepted.includes(true)) {
    return '';
  }
  // --- The accepted runs, line by line; a run ends where the next starts ---
  const lines = code.split('\n');
  const runs = decodeRuns(map.mappings, map.sources.length);
  const pieces: string[] = [];
  runs.forEach((line, index) => {
    const text = lines[index] ?? '';
    line.forEach((run, position) => {
      if (run.source >= 0 && accepted[run.source]) {
        pieces.push(text.slice(run.column, line[position + 1]?.column ?? text.length));
      }
    });
  });
  return pieces.join('');
}
