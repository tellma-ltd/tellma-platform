import { RuleTester } from 'eslint';
import tseslint from 'typescript-eslint';
import { describe, it } from 'vitest';

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
        { code: 'export interface FormFieldControl {}', errors: [{ messageId: 'badName' }] },
        {
          code: 'const y = 1; export { y as helper };',
          errors: [{ messageId: 'badName' }],
        },
      ],
    });
  });
});
