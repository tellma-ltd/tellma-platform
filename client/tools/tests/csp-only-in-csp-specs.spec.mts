// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import { ESLint, RuleTester } from 'eslint';
import tseslint from 'typescript-eslint';
import { describe, expect, it } from 'vitest';

import rule from '../eslint/csp-only-in-csp-specs.mjs';

const tester = new RuleTester({
  languageOptions: {
    parser: tseslint.parser as never,
    ecmaVersion: 2022,
    sourceType: 'module',
  },
});

describe('tm/csp-only-in-csp-specs', () => {
  it('allows a Content-Security-Policy only in *.csp.spec.ts files', () => {
    tester.run('tm/csp-only-in-csp-specs', rule as never, {
      valid: [
        { code: "meta.httpEquiv = 'Content-Security-Policy';", filename: 'a/tm-icu.csp.spec.ts' },
        { code: 'const p = `content-security-policy`;', filename: 'a/b.csp.spec.ts' },
        { code: "const header = 'Content-Type';", filename: 'a/tm-icu.spec.ts' },
      ],
      invalid: [
        {
          code: "meta.httpEquiv = 'Content-Security-Policy';",
          filename: 'a/tm-icu.spec.ts',
          errors: [{ messageId: 'cspOutsideCspSpec' }],
        },
        {
          code: 'const name = `content-security-policy`;',
          filename: 'a/tm-other.spec.ts',
          errors: [{ messageId: 'cspOutsideCspSpec' }],
        },
        {
          code: "meta.setAttribute('http-equiv', 'CONTENT-SECURITY-POLICY');",
          filename: 'a/csp.spec.ts',
          errors: [{ messageId: 'cspOutsideCspSpec' }],
        },
      ],
    });
  });
});

// The first lint loads the whole workspace config, which takes several seconds on a busy machine.
describe('tm/csp-only-in-csp-specs scope', { timeout: 60_000 }, () => {
  const eslint = new ESLint({ cwd: process.cwd() });
  const CODE = "const meta = { httpEquiv: 'Content-Security-Policy' };\nvoid meta;\n";

  /** The guard's messages the workspace config reports for `CODE` at `filePath`. */
  async function guardMessages(filePath: string): Promise<number> {
    const [result] = await eslint.lintText(CODE, { filePath });
    return result.messages.filter((message) => message.ruleId === 'tm/csp-only-in-csp-specs')
      .length;
  }

  it('applies to every unit spec, entry-point boundary folders included', async () => {
    expect(await guardMessages('projects/core/tellma-core-ui/i18n/x.spec.ts')).toBe(1);
    expect(await guardMessages('projects/core/tellma-core-ui/grid-engine/x.spec.ts')).toBe(1);
    expect(await guardMessages('projects/core/tellma-core-ui/l10n/x.spec.ts')).toBe(1);
    expect(await guardMessages('projects/locale/tellma-locale-ar/x.spec.ts')).toBe(1);
  });

  it('exempts *.csp.spec.ts files and non-spec sources', async () => {
    expect(await guardMessages('projects/core/tellma-core-ui/i18n/x.csp.spec.ts')).toBe(0);
    expect(await guardMessages('projects/core/tellma-core-ui/i18n/x.ts')).toBe(0);
  });
});
