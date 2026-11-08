// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import { ESLint, RuleTester } from 'eslint';
import tseslint from 'typescript-eslint';
import { describe, expect, it } from 'vitest';

import rule from '../eslint/tm-prefix-exports.mjs';

const tester = new RuleTester({
  languageOptions: {
    parser: tseslint.parser as never,
    ecmaVersion: 2022,
    sourceType: 'module',
  },
});

describe('tm/prefix-exports', () => {
  it('accepts prefixed and allowlisted exports, rejects the rest', () => {
    tester.run('tm/prefix-exports', rule as never, {
      valid: [
        { code: 'export class TmSelect {}' },
        { code: 'export interface TmFormFieldControl { x: number }' },
        { code: 'export type TmFieldError = { kind: string };' },
        { code: 'export const TM_UI_TRANSLATE = 1;' },
        { code: 'export function provideTellmaForms() {}' },
        { code: 'export function tmValueToKey() {}' },
        // Context accessors carry the brand after `inject`.
        { code: 'export function injectTmDetails() {}' },
        { code: 'export const injectTmSearch = () => 1;' },
        { code: 'export function ɵinjectTmPage() {}' },
        { code: 'const x = 1; export { x as TM_X };' },
        {
          code: 'export type SignalLike<T> = () => T;',
          options: [{ allow: ['SignalLike'] }],
        },
        {
          code: 'export function fontPreloadLinks() {}',
          options: [{ allow: ['fontPreloadLinks'] }],
        },
      ],
      invalid: [
        { code: 'export class Select {}', errors: [{ messageId: 'badName' }] },
        { code: 'export const UI_TRANSLATE = 1;', errors: [{ messageId: 'badName' }] },
        { code: 'export function provideForms() {}', errors: [{ messageId: 'badName' }] },
        { code: 'export function injectDetails() {}', errors: [{ messageId: 'badName' }] },
        { code: 'export function injectTmdetails() {}', errors: [{ messageId: 'badName' }] },
        { code: 'export interface FormFieldControl {}', errors: [{ messageId: 'badName' }] },
        {
          code: 'const y = 1; export { y as helper };',
          errors: [{ messageId: 'badName' }],
        },
      ],
    });
  });
});

describe('tm/prefix-exports scope', () => {
  const eslint = new ESLint({ cwd: process.cwd() });
  const UNPREFIXED = 'export const helper = 1;\n';
  const ACCESSOR = 'export function injectTmSales(): number {\n  return 1;\n}\n';

  /** The prefix-rule messages the workspace config reports for `code` at `filePath`. */
  async function prefixMessages(filePath: string, code: string): Promise<string[]> {
    const [result] = await eslint.lintText(code, { filePath });
    return result.messages
      .filter((message) => message.ruleId === 'tm/prefix-exports')
      .map((message) => message.message);
  }

  it('covers feature packs under projects/module', async () => {
    const pack = 'projects/module/acme-sales/public-api.ts';
    expect(await prefixMessages(pack, UNPREFIXED)).toHaveLength(1);
    expect(await prefixMessages(pack, ACCESSOR)).toEqual([]);
  });

  it('covers core and locale packages, but not the Node tooling packages', async () => {
    const covered = [
      'projects/core/tellma-core-ui/x/public-api.ts',
      'projects/locale/tellma-locale-xx/public-api.ts',
    ];
    for (const filePath of covered) {
      expect(await prefixMessages(filePath, UNPREFIXED), filePath).toHaveLength(1);
    }
    const exempt = [
      'projects/core/tellma-core-tools/src/cli.ts',
      'projects/core/tellma-core-ui-mcp/src/server.ts',
    ];
    for (const filePath of exempt) {
      expect(await prefixMessages(filePath, UNPREFIXED), filePath).toEqual([]);
    }
  });
});
