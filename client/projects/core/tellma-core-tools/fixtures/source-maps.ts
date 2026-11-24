// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

// The specs' source maps: version 3 mappings written from plain runs, so a
// spec states which source each run of generated code came from and the
// attribution is checked against that, not against another decoder.

/** The Base64 digits a VLQ is written in, by value. */
const BASE64 = 'ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789+/';

/** One run of generated code: the column it starts at, and the index of its source or null for none. */
export type FixtureRun = readonly [column: number, source: number | null];

/** Writes one signed value as a Base64 VLQ. */
function vlq(value: number): string {
  let rest = value < 0 ? (-value << 1) | 1 : value << 1;
  let text = '';
  do {
    let digit = rest & 31;
    rest >>>= 5;
    if (rest > 0) {
      digit |= 32;
    }
    text += BASE64[digit];
  } while (rest > 0);
  return text;
}

/**
 * Encodes runs as a map's mappings: the generated column relative within its
 * line, the source index relative across the map, and an original position
 * of line 0, column 0 for each mapped run.
 *
 * @param lines - The runs of each generated line, in column order.
 * @returns The mappings.
 */
export function fixtureMappings(lines: readonly (readonly FixtureRun[])[]): string {
  let previousSource = 0;
  let previousLine = 0;
  let previousColumn = 0;
  return lines
    .map((runs) => {
      let previous = 0;
      return runs
        .map(([column, source]) => {
          const generated = vlq(column - previous);
          previous = column;
          if (source === null) {
            return generated;
          }
          // The original line and column move too, so the fields after the source are exercised.
          const line = source;
          const original = column;
          const text = generated + vlq(source - previousSource) + vlq(line - previousLine) + vlq(original - previousColumn);
          previousSource = source;
          previousLine = line;
          previousColumn = original;
          return text;
        })
        .join(',');
    })
    .join(';');
}

/**
 * A version 3 source map's JSON over the runs.
 *
 * @param sources - The source paths.
 * @param lines - The runs of each generated line.
 * @param sourceRoot - A prefix of every source path, when the map has one.
 * @returns The map's text.
 */
export function fixtureSourceMap(sources: readonly string[], lines: readonly (readonly FixtureRun[])[], sourceRoot?: string): string {
  return JSON.stringify({ version: 3, sources, ...(sourceRoot === undefined ? {} : { sourceRoot }), names: [], mappings: fixtureMappings(lines) });
}
