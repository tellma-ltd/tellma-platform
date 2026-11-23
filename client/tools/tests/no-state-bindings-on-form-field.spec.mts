// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import angular from 'angular-eslint';
import { describe } from 'vitest';

import { createRuleTester } from '../../projects/core/tellma-core-tools/fixtures/rule-tester.js';
import rule from '../eslint/no-state-bindings-on-form-field.mjs';

// Each case below runs as a test of its own (see createRuleTester).
const tester = createRuleTester({
  languageOptions: {
    parser: angular.templateParser as never,
  },
});

describe('tm/no-state-bindings-on-form-field', () => {
  describe('forbids disabled/readonly/required on [formField]-bound controls only', () => {
    tester.run('tm/no-state-bindings-on-form-field', rule as never, {
      valid: [
        // Unbound control: the inputs are legitimate (non-form usage).
        { code: '<input tmInput [disabled]="true" />', filename: 'a.html' },
        { code: '<input tmInput required />', filename: 'a.html' },
        // Bound control without the conflicting inputs.
        { code: '<input tmInput [formField]="form.email" />', filename: 'a.html' },
        {
          code: '<tm-checkbox [formField]="form.ok" [indeterminate]="x()" />',
          filename: 'a.html',
        },
      ],
      invalid: [
        {
          code: '<input tmInput [formField]="form.email" [disabled]="true" />',
          filename: 'a.html',
          errors: [{ messageId: 'forbidden' }],
        },
        {
          code: '<input tmInput [formField]="form.email" required />',
          filename: 'a.html',
          errors: [{ messageId: 'forbidden' }],
        },
        {
          code: '<tm-select [formField]="form.country" [readonly]="r()" />',
          filename: 'a.html',
          errors: [{ messageId: 'forbidden' }],
        },
      ],
    });
  });
});
