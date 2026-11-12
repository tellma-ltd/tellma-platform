// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import { readFileSync, readdirSync } from 'node:fs';
import { dirname, join } from 'node:path';
import { fileURLToPath } from 'node:url';

import { describe, expect, it } from 'vitest';

import { normalizeUnionOrder, sortUnionMembers } from '../api/union-order.mjs';

// The canonical union order of the API reports: the same public API must
// yield the same report whichever order the compiler printed its unions in.

const apiDir = join(dirname(fileURLToPath(import.meta.url)), '..', '..', 'api');

/** Wraps declaration source in the fenced shape of an API Extractor report. */
function report(body: string): string {
  return ['## API Report File for "tellma-client"', '', '```ts', body, '```', ''].join('\n');
}

describe('sortUnionMembers', () => {
  it('prints a union the same whichever order the compiler emitted it in', () => {
    // The order a clean build prints, and the order a warm build cache printed.
    const clean = 'export declare const rowActivation: InputSignal<"row" | "none">;';
    const warm = 'export declare const rowActivation: InputSignal<"none" | "row">;';

    expect(sortUnionMembers(clean)).toBe(sortUnionMembers(warm));
    expect(sortUnionMembers(clean)).toBe(
      'export declare const rowActivation: InputSignal<"none" | "row">;',
    );
  });

  it('gives every permutation of a union the same text', () => {
    const members = ['"info"', '"success"', '"warning"', '"error"'];
    const permutations = (items: string[]): string[][] =>
      items.length <= 1
        ? [items]
        : items.flatMap((item, index) =>
            permutations([...items.slice(0, index), ...items.slice(index + 1)]).map((rest) => [
              item,
              ...rest,
            ]),
          );

    const printed = new Set(
      permutations(members).map((order) =>
        sortUnionMembers(`export type Kind = ${order.join(' | ')};`),
      ),
    );

    expect([...printed]).toEqual(['export type Kind = "error" | "info" | "success" | "warning";']);
  });

  it('orders literals, keywords, named types and structural types, then null and undefined', () => {
    const source =
      'export type T = undefined | (() => void) | null | Blob | string | "a" | readonly string[];';

    expect(sortUnionMembers(source)).toBe(
      'export type T = "a" | string | Blob | (() => void) | readonly string[] | null | undefined;',
    );
  });

  it('orders number literals by value, not by text', () => {
    expect(sortUnionMembers('export type Space = 10 | 2 | -1 | 0 | 24 | 3;')).toBe(
      'export type Space = -1 | 0 | 2 | 3 | 10 | 24;',
    );
  });

  it('sorts nested unions and orders the outer union by their sorted text', () => {
    const a =
      'export declare const parse: InputSignal<((text: string) => V | TmParseError) | undefined | "auto">;';
    const b =
      'export declare const parse: InputSignal<"auto" | undefined | ((text: string) => TmParseError | V)>;';

    expect(sortUnionMembers(a)).toBe(sortUnionMembers(b));
    expect(sortUnionMembers(a)).toBe(
      'export declare const parse: InputSignal<"auto" | ((text: string) => TmParseError | V) | undefined>;',
    );
  });

  it('keeps a union already in canonical order as written, line breaks included', () => {
    const source = 'export type Kind =\n    | "a"\n    | "b"\n    | null;';

    expect(sortUnionMembers(source)).toBe(source);
  });

  it('leaves a union with comments between its members in its written order', () => {
    const source = [
      'export type TmTimeStyle =',
      '/** The hour and the minute. */',
      "'short'",
      '/** The hour, the minute and the second. */',
      "| 'medium';",
    ].join('\n');

    expect(sortUnionMembers(source)).toBe(source);
  });

  it('never reorders an intersection', () => {
    const source = 'export type T = TmB & TmA;';

    expect(sortUnionMembers(source)).toBe(source);
  });

  it('leaves union-like text inside string literals alone', () => {
    const source = 'export declare const selector: "b | a";';

    expect(sortUnionMembers(source)).toBe(source);
  });

  it('keeps the comments and blank lines between declarations', () => {
    const source =
      '// @public\nexport type A = "b" | "a";\n\n// @public (undocumented)\nexport type B = 2 | 1;\n';

    expect(sortUnionMembers(source)).toBe(
      '// @public\nexport type A = "a" | "b";\n\n// @public (undocumented)\nexport type B = 1 | 2;\n',
    );
  });

  it('refuses source that does not parse rather than rewriting it', () => {
    expect(() => sortUnionMembers('export type T = "b" | ;')).toThrow(
      /does not parse as TypeScript/,
    );
  });
});

describe('normalizeUnionOrder', () => {
  it('sorts the unions inside the TypeScript fence', () => {
    expect(normalizeUnionOrder(report('export type A = "b" | "a";'))).toBe(
      report('export type A = "a" | "b";'),
    );
  });

  it('leaves the markdown outside the fence untouched', () => {
    const text = 'Kinds: "b" | "a"\n\n```ts\nexport type A = "b" | "a";\n```\n\nAfter: "d" | "c"\n';

    expect(normalizeUnionOrder(text)).toBe(
      'Kinds: "b" | "a"\n\n```ts\nexport type A = "a" | "b";\n```\n\nAfter: "d" | "c"\n',
    );
  });

  it('returns a report without a TypeScript fence unchanged', () => {
    const text = '## Report\n\nNo declarations.\n';

    expect(normalizeUnionOrder(text)).toBe(text);
  });

  it('is idempotent', () => {
    const once = normalizeUnionOrder(
      report('export type A = InputSignal<"row" | "none" | undefined> | (() => 2 | 1);'),
    );

    expect(normalizeUnionOrder(once)).toBe(once);
  });

  it('holds every committed golden in canonical order', () => {
    // A golden approved without the sort (or edited by hand) would make
    // api:approve rewrite it; catch that here, without needing a build.
    const goldens = readdirSync(apiDir).filter((file) => file.endsWith('.api.md'));

    expect(goldens.length).toBeGreaterThan(0);
    for (const file of goldens) {
      const golden = readFileSync(join(apiDir, file), 'utf8').replaceAll('\r\n', '\n');
      expect(normalizeUnionOrder(golden), file).toBe(golden);
    }
  });
});
