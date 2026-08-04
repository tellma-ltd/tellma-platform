// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import {
  validate,
  type LogicFn,
  type PathKind,
  type SchemaPath,
  type SchemaPathRules,
  type ValidationError,
} from '@angular/forms/signals';

/**
 * Validates that an ISO `YYYY-MM-DD` field value is on or after `minDate`
 * (lexicographic comparison — correct for the fixed ISO shape), reporting
 * the framework's `minDate` kind so the standard message resolver supplies
 * localized defaults. The framework's own `minDate` validator is typed for
 * `Date | null` and does not apply to string-valued date fields; empty and
 * null values pass (`required` is its own concern).
 */
export function tmMinDate<
  TValue extends string | null,
  TPathKind extends PathKind = PathKind.Root,
>(
  path: SchemaPath<TValue, SchemaPathRules.Supported, TPathKind>,
  minDate: string | LogicFn<TValue, string | undefined, TPathKind>,
): void {
  validate(path, (ctx) => {
    const value = ctx.value();
    if (value === null || value === '') {
      return undefined;
    }
    const bound = typeof minDate === 'function' ? minDate(ctx) : minDate;
    if (bound === undefined || value >= bound) {
      return undefined;
    }
    return { kind: 'minDate', minDate: bound } as ValidationError.WithoutFieldTree;
  });
}

/**
 * Validates that an ISO `YYYY-MM-DD` field value is on or before `maxDate`
 * (lexicographic comparison — correct for the fixed ISO shape), reporting
 * the framework's `maxDate` kind so the standard message resolver supplies
 * localized defaults. The framework's own `maxDate` validator is typed for
 * `Date | null` and does not apply to string-valued date fields; empty and
 * null values pass (`required` is its own concern).
 */
export function tmMaxDate<
  TValue extends string | null,
  TPathKind extends PathKind = PathKind.Root,
>(
  path: SchemaPath<TValue, SchemaPathRules.Supported, TPathKind>,
  maxDate: string | LogicFn<TValue, string | undefined, TPathKind>,
): void {
  validate(path, (ctx) => {
    const value = ctx.value();
    if (value === null || value === '') {
      return undefined;
    }
    const bound = typeof maxDate === 'function' ? maxDate(ctx) : maxDate;
    if (bound === undefined || value <= bound) {
      return undefined;
    }
    return { kind: 'maxDate', maxDate: bound } as ValidationError.WithoutFieldTree;
  });
}
