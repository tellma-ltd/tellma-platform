// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import { join } from 'node:path';

import { RuleTester } from 'eslint';
import tseslint from 'typescript-eslint';
import { describe, it } from 'vitest';

import { tmNoDecimalOperators } from './no-decimal-operators.js';

// The code under test imports the real TmDecimal type from @tellma/core's
// sources, so the rule is tested against the brand it must recognise.
const fixture = join(import.meta.dirname, '..', 'fixtures', 'decimals');
const filename = join(fixture, 'file.ts');
const header = "import type { TmDecimal, TmId } from '../../../../../tellma-core/src/json/tm-json';\n";

const tester = new RuleTester({
  languageOptions: {
    parser: tseslint.parser as never,
    parserOptions: {
      projectService: { allowDefaultProject: ['*.ts'], defaultProject: 'tsconfig.json' },
      tsconfigRootDir: fixture,
    },
  },
});

/** A valid case: the header, then `body`. */
const valid = (body: string) => ({ code: header + body, filename });

/** An invalid case reporting `operator` once. */
const invalid = (body: string, operator: string, instead: string) => ({
  code: header + body,
  filename,
  errors: [{ messageId: 'decimalOperator', data: { operator, instead } }],
});

// The first case builds the TypeScript program, which takes seconds on a busy
// or small machine.
describe('tm/no-decimal-operators', { timeout: 120_000 }, () => {
  it('refuses each operator TypeScript allows on strings when an operand is a TmDecimal', () => {
    tester.run('tm/no-decimal-operators', tmNoDecimalOperators as never, {
      valid: [],
      invalid: [
        invalid('declare const a: TmDecimal, b: TmDecimal; const c = a + b;', '+', 'TmDecimals.add'),
        invalid('declare const a: TmDecimal; const c = a + 1;', '+', 'TmDecimals.add'),
        invalid("declare const a: TmDecimal; const c = 'Total: ' + a;", '+', 'TmDecimals.add'),
        invalid('declare let a: TmDecimal; declare const b: TmDecimal; a += b;', '+=', 'TmDecimals.add'),
        invalid('declare let s: string; declare const a: TmDecimal; s += a;', '+=', 'TmDecimals.add'),
        invalid('declare const a: TmDecimal, b: TmDecimal; const c = a < b;', '<', 'TmDecimals.compare'),
        invalid('declare const a: TmDecimal, b: TmDecimal; const c = a <= b;', '<=', 'TmDecimals.compare'),
        invalid('declare const a: TmDecimal, b: TmDecimal; const c = a > b;', '>', 'TmDecimals.compare'),
        invalid('declare const a: TmDecimal, b: TmDecimal; const c = a >= b;', '>=', 'TmDecimals.compare'),
      ],
    });
  });

  it('follows the brand through unions, ids, type parameters and aliases', () => {
    tester.run('tm/no-decimal-operators', tmNoDecimalOperators as never, {
      valid: [],
      invalid: [
        invalid('declare const a: TmDecimal | undefined; const c = a! + 1;', '+', 'TmDecimals.add'),
        invalid('declare const id: TmId; const next = id + 1;', '+', 'TmDecimals.add'),
        invalid('function sum<T extends TmDecimal>(a: T, b: T) { return a + b; }', '+', 'TmDecimals.add'),
        invalid('type Money = TmDecimal; declare const a: Money, b: Money; const c = a > b;', '>', 'TmDecimals.compare'),
        invalid(
          "interface Row { readonly Total: TmDecimal } declare const row: Row; const c = row.Total < '10';",
          '<',
          'TmDecimals.compare',
        ),
      ],
    });
  });

  it('passes the operators on numbers and plain strings, and the other operators on decimals', () => {
    tester.run('tm/no-decimal-operators', tmNoDecimalOperators as never, {
      valid: [
        valid('declare const a: number, b: number; const c = a + b; const d = a < b;'),
        valid("declare const s: string; const t = s + '!'; const u = s < 'm';"),
        valid('declare let n: number; n += 1;'),
        valid("declare let s: string; s += 'x';"),
        valid('declare const a: bigint, b: bigint; const c = a + b;'),
        valid('declare const a: TmDecimal, b: TmDecimal; const c = a === b; const d = a !== b;'),
        valid('declare const a: TmDecimal; const text = `Total: ${a}`;'),
        valid('declare const a: any; const c = a + 1;'),
      ],
      invalid: [],
    });
  });
});
