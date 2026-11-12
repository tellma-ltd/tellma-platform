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

/** Every ordering of a list. */
function permutations(items: readonly string[]): string[][] {
  return items.length <= 1
    ? [[...items]]
    : items.flatMap((item, index) =>
        permutations([...items.slice(0, index), ...items.slice(index + 1)]).map((rest) => [
          item,
          ...rest,
        ]),
      );
}

/** The distinct texts `sortUnionMembers` gives for every ordering of a union's members. */
function printedForEveryOrder(members: readonly string[], layout: (order: string[]) => string) {
  return [...new Set(permutations(members).map((order) => sortUnionMembers(layout(order))))];
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
    const printed = printedForEveryOrder(
      ['"info"', '"success"', '"warning"', '"error"'],
      (order) => `export type Kind = ${order.join(' | ')};`,
    );

    expect(printed).toEqual(['export type Kind = "error" | "info" | "success" | "warning";']);
  });

  it('gives every permutation of a multi-line union the same text, keeping its layout', () => {
    const printed = printedForEveryOrder(
      ['"a"', '"b"', '"c"'],
      (order) => `export type Kind =\n    | ${order.join('\n    | ')};`,
    );

    expect(printed).toEqual(['export type Kind =\n    | "a"\n    | "b"\n    | "c";']);
  });

  it('keeps each written separator in its position when the members move', () => {
    expect(sortUnionMembers('export type Kind = "c" | "b"\n    | "a";')).toBe(
      'export type Kind = "a" | "b"\n    | "c";',
    );
  });

  it('orders number and bigint literals together by value', () => {
    const printed = printedForEveryOrder(
      ['2', '10', '1n', '-3n', '0x10'],
      (order) => `export type N = ${order.join(' | ')};`,
    );

    expect(printed).toEqual(['export type N = -3n | 1n | 2 | 10 | 0x10;']);
  });

  it('orders string literals, then numeric literals, then the other literals', () => {
    // Five members keep the permutations (120 parses) quick.
    const printed = printedForEveryOrder(
      ['true', '1n', '1', '"a"', '`t`'],
      (order) => `export type L = ${order.join(' | ')};`,
    );

    // Equal values (1 and 1n) fall back to their text.
    expect(printed).toEqual(['export type L = "a" | 1 | 1n | `t` | true;']);
    expect(sortUnionMembers('export type L = true | false | `t` | 1n | 1 | "b" | "a";')).toBe(
      'export type L = "a" | "b" | 1 | 1n | `t` | false | true;',
    );
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

  it.each([
    [
      'doc comments on their own lines',
      "export type TmTimeStyle =\n/** The hour and the minute. */\n'short'\n/** The hour, the minute and the second. */\n| 'medium';",
    ],
    ['a block comment before a `|`', 'export type T = "b" /* c */ | "a";'],
    ['a block comment after a `|`', 'export type T = "b" | /* c */ "a";'],
    ['a line comment after a `|`', 'export type T = "b" | // c\n"a";'],
    ['a line comment before a `|`', 'export type T = "b" // c\n| "a";'],
    ['a line comment after a leading-layout `|`', 'export type T =\n| "b"\n| // c\n"a";'],
    ['same-line doc comments', "export type T = /** x */ 'short' | /** y */ 'medium' | 'long';"],
    ['a comment before the first member', 'export type T = /* c */ "b" | "a";'],
    ['a comment after a leading `|`', 'export type T =\n| /* c */ "b"\n| "a";'],
  ])('leaves a union with %s in its written order', (_layout, source) => {
    expect(sortUnionMembers(source)).toBe(source);
  });

  it.each([
    ['a parenthesized union in an array', 'type T = ("b" | "a")[];', 'type T = ("a" | "b")[];'],
    [
      'an array of a union inside a union',
      'type T = ("d" | "c")[] | "b" | "a";',
      'type T = "a" | "b" | ("c" | "d")[];',
    ],
    [
      'a conditional type',
      'type T<X> = X extends "b" | "a" ? "d" | "c" : "f" | "e";',
      'type T<X> = X extends "a" | "b" ? "c" | "d" : "e" | "f";',
    ],
    [
      'a mapped type with an `as` clause',
      'type T = { [K in "b" | "a" as K | "z"]: "d" | "c" };',
      'type T = { [K in "a" | "b" as "z" | K]: "c" | "d" };',
    ],
    ['a template literal type', 'type T = `x-${"b" | "a"}`;', 'type T = `x-${"a" | "b"}`;'],
    ['a tuple', 'type T = ["b" | "a", 2 | 1];', 'type T = ["a" | "b", 1 | 2];'],
    ['an indexed access type', 'type T = Foo["b" | "a"];', 'type T = Foo["a" | "b"];'],
    [
      'an `infer … extends` constraint',
      'type T<X> = X extends [infer U extends "b" | "a"] ? U : never;',
      'type T<X> = X extends [infer U extends "a" | "b"] ? U : never;',
    ],
    [
      'an import type argument',
      'type T = import("x").Foo<"b" | "a">;',
      'type T = import("x").Foo<"a" | "b">;',
    ],
    [
      'the operands of an intersection, keeping the `&` order',
      'type T = (Z | Y) & ("b" | "a");',
      'type T = (Y | Z) & ("a" | "b");',
    ],
    [
      '`undefined | null`',
      'type T = string | undefined | null;',
      'type T = string | null | undefined;',
    ],
    [
      'a multi-line union with a leading `|`',
      'type T =\n    | "b"\n    | "a";',
      'type T =\n    | "a"\n    | "b";',
    ],
  ])('sorts the unions of %s', (_shape, source, expected) => {
    expect(sortUnionMembers(source)).toBe(expected);
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

  it('sorts a CRLF report, returning it with LF line endings', () => {
    const crlf = report('export type A = "b" | "a";\nexport type B = 2 | 1;').replaceAll(
      '\n',
      '\r\n',
    );

    expect(normalizeUnionOrder(crlf)).toBe(
      report('export type A = "a" | "b";\nexport type B = 1 | 2;'),
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
    // Parsing every golden takes a few seconds; allow for a loaded machine.
  }, 60_000);
});
