// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import { ESLint } from 'eslint';
import { describe, expect, it } from 'vitest';

// The workspace's own ESLint config applies the app framework's rules to the
// @tellma/core packages, the decimal rules with type information. The texts
// below are linted as if they were the named source files, which places them
// in those files' TypeScript projects.

/** The rule ids the workspace config reports for `code` at `filePath`. */
async function ruleIds(eslint: ESLint, code: string, filePath: string): Promise<string[]> {
  const [result] = await eslint.lintText(code, { filePath });
  return (result?.messages ?? []).map((message) => message.ruleId ?? `fatal: ${message.message}`);
}

// The first lint loads the whole workspace config and a TypeScript program,
// which takes several seconds on a busy machine.
describe('the @tellma/core lint scope', { timeout: 120_000 }, () => {
  const eslint = new ESLint({ cwd: process.cwd() });
  const decimals = 'projects/core/tellma-core/src/json/tm-decimals.ts';

  it('refuses text arithmetic on a TmDecimal in the primary entry point', async () => {
    const code =
      "import type { TmDecimal } from './tm-json';\n" +
      'export function total(a: TmDecimal, b: TmDecimal): boolean {\n' +
      '  return a + b > a;\n' +
      '}\n';
    expect(await ruleIds(eslint, code, decimals)).toEqual(
      expect.arrayContaining(['tm/no-decimal-operators', 'tm/no-decimal-operators']),
    );
  });

  it('refuses a string added to a number', async () => {
    const code = "export const label = (count: number): string => 'Rows: ' + count;\n";
    expect(await ruleIds(eslint, code, decimals)).toContain('@typescript-eslint/restrict-plus-operands');
  });

  it('refuses a static import of a lazy entry point from the primary', async () => {
    const code = "export { TmSearchHost } from '@tellma/core/search';\n";
    expect(await ruleIds(eslint, code, decimals)).toContain('tm/core-import-boundary');
  });

  it('applies the decimal rule to @tellma/core-testing as well', async () => {
    const code =
      "import type { TmDecimal } from '@tellma/core';\n" +
      "export const more = (a: TmDecimal): boolean => a > ('1' as TmDecimal);\n";
    expect(await ruleIds(eslint, code, 'projects/core/tellma-core-testing/src/tm-fake-telemetry.ts')).toContain(
      'tm/no-decimal-operators',
    );
  });
});
