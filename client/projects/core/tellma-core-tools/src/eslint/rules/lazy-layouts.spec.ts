// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import { RuleTester } from 'eslint';
import tseslint from 'typescript-eslint';
import { describe, it } from 'vitest';

import { tmLazyLayouts } from './lazy-layouts.js';

const tester = new RuleTester({
  languageOptions: { parser: tseslint.parser as never, ecmaVersion: 2022, sourceType: 'module' },
});

describe('tm/lazy-layouts', () => {
  it('accepts inline lazy loaders in every layout member', () => {
    tester.run('tm/lazy-layouts', tmLazyLayouts as never, {
      valid: [
        {
          code: `export const customers = tmEntity('acme.Customer', {
            section: 'sales',
            details: () => import('./customer-details'),
            search: () => import('./customer-search').then((m) => m.CustomerSearch),
            create: async () => import('./customer-create'),
            display: () => import(\`./customer-display\`),
            dialog: () => import('./customer-dialog'),
          });`,
        },
        { code: "tmPage('credit-review', { component: () => import('./credit-review'), titleKey: 'Acme_CreditReview' });" },
        {
          code: `tmFeature({
            key: 'acme',
            home: () => import('./home'),
            settingsTabs: [{ category: 'sales', component: () => import('./sales-settings') }],
            appBar: [{ id: 'acme-approvals', panel: () => import('./approvals') }],
          });`,
        },
        { code: "tmWidget('acme.cash-position', { component: () => import('./cash-position'), settingsEditor: () => import('./settings') });" },
        // Outside a declaration call the members mean nothing to the rule.
        { code: 'const route = { component: StaticPage };' },
        { code: 'register({ component: StaticPage });' },
        // A spread's members are checked where they are written.
        { code: 'tmPage("x", { ...shared, titleKey: "X" });' },
      ],
      invalid: [
        {
          code: "import { CustomerDetails } from './customer-details'; tmEntity('acme.Customer', { details: CustomerDetails });",
          errors: [{ messageId: 'notLazy', data: { member: 'details' } }],
        },
        {
          code: "tmPage('credit-review', { component: () => CreditReview, titleKey: 'X' });",
          errors: [{ messageId: 'notLazy', data: { member: 'component' } }],
        },
        {
          code: "tmPage('credit-review', { component, titleKey: 'X' });",
          errors: [{ messageId: 'notLazy', data: { member: 'component' } }],
        },
        {
          code: "tmPage('x', { component: () => import(path), titleKey: 'X' });",
          errors: [{ messageId: 'notLazy' }],
        },
        {
          code: "tmPage('x', { component: (name) => import('./x'), titleKey: 'X' });",
          errors: [{ messageId: 'notLazy' }],
        },
        {
          code: "tmPage('x', { component: function () { return import('./x'); }, titleKey: 'X' });",
          errors: [{ messageId: 'notLazy' }],
        },
        {
          code: "tmFeature({ key: 'acme', entries: [{ id: 'acme.x', page: loadPage }] });",
          errors: [{ messageId: 'notLazy', data: { member: 'page' } }],
        },
        {
          code: "tmFeature({ key: 'acme', settingsTabs: [{ category: 'sales', settingsEditor: SalesEditor }] });",
          errors: [{ messageId: 'notLazy', data: { member: 'settingsEditor' } }],
        },
        {
          code: "tmFeature({ key: 'acme', appBar: [{ id: 'acme-approvals', panel: ApprovalsPanel }] });",
          errors: [{ messageId: 'notLazy', data: { member: 'panel' } }],
        },
        {
          code: "tmFeature({ key: 'acme', home: Home });",
          errors: [{ messageId: 'notLazy', data: { member: 'home' } }],
        },
      ],
    });
  });
});
