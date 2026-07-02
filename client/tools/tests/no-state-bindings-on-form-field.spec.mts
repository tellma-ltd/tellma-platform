import { RuleTester } from 'eslint';
import angular from 'angular-eslint';
import { describe, it } from 'vitest';

import rule from '../eslint/no-state-bindings-on-form-field.mjs';

const tester = new RuleTester({
  languageOptions: {
    parser: angular.templateParser as never,
  },
});

describe('tm/no-state-bindings-on-form-field', () => {
  it('forbids disabled/readonly/required on [formField]-bound controls only', () => {
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
