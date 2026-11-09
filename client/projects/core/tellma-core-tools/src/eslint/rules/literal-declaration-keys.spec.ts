// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import { RuleTester } from 'eslint';
import tseslint from 'typescript-eslint';
import { describe, it } from 'vitest';

import { tmLiteralDeclarationKeys } from './literal-declaration-keys.js';

const tester = new RuleTester({
  languageOptions: { parser: tseslint.parser as never, ecmaVersion: 2022, sourceType: 'module' },
});

describe('tm/literal-declaration-keys', () => {
  it('accepts literal keys', () => {
    tester.run('tm/literal-declaration-keys', tmLiteralDeclarationKeys as never, {
      valid: [
        { code: "tmEntity('acme.Customer', {});" },
        { code: 'tmPage(`credit-review`, {});' },
        { code: "tmCodedReport('acme-credit-exposure', { section: 'sales' });" },
        { code: "tmWidget('acme.cash-position', {});" },
        { code: "tmOverride('section:administration', { order: 10 });" },
        {
          code: `tmFeature({
            key: 'acme',
            hide: ['entry:core.Role', 'page:reports'],
            sections: [{ id: 'sales', labelKey: 'Acme_Sales', icon: 'cart', order: 100 }, salesSection],
            entries: [{ id: 'acme.settings', labelKey: 'X' }],
            appBar: [{ id: 'acme-approvals' }],
            settingsTabs: [{ category: 'sales' }],
          } as TmFeatureDefinition);`,
        },
        // Other functions, and declarations whose key lives elsewhere, are not the rule's.
        { code: 'register(name, {});' },
        { code: 'tmFeature(definition);' },
      ],
      invalid: [
        {
          code: "const name = 'acme.Customer'; tmEntity(name, {});",
          errors: [{ messageId: 'notLiteral', data: { what: 'key of tmEntity' } }],
        },
        { code: 'tmPage(`${base}/review`, {});', errors: [{ messageId: 'notLiteral' }] },
        { code: "tmCodedReport('acme-' + kind, {});", errors: [{ messageId: 'notLiteral' }] },
        { code: 'tmWidget(KEYS.cash, {});', errors: [{ messageId: 'notLiteral' }] },
        { code: "tmOverride(`section:${id}`, { order: 10 });", errors: [{ messageId: 'notLiteral' }] },
        {
          code: 'tmFeature({ key: FEATURE_KEY });',
          errors: [{ messageId: 'notLiteral', data: { what: "feature's key" } }],
        },
        {
          code: "tmFeature({ key: 'acme', hide: ['entry:core.Role', hiddenEntry] });",
          errors: [{ messageId: 'notLiteral', data: { what: 'hidden key' } }],
        },
        {
          code: "tmFeature({ key: 'acme', hide: HIDDEN });",
          errors: [{ messageId: 'notLiteral', data: { what: "'hide' list" } }],
        },
        {
          code: "tmFeature({ key: 'acme', sections: [{ id: prefix + 'sales' }] });",
          errors: [{ messageId: 'notLiteral', data: { what: "'id' of each of 'sections'" } }],
        },
        {
          code: "tmFeature({ key: 'acme', settingsTabs: [{ category: CATEGORY }] });",
          errors: [{ messageId: 'notLiteral', data: { what: "'category' of each of 'settingsTabs'" } }],
        },
      ],
    });
  });
});
