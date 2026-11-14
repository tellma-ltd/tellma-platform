// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import {
  validate,
  type PathKind,
  type SchemaPath,
  type SchemaPathRules,
  type ValidationError,
} from '@angular/forms/signals';

/** A from–to pair of ISO `YYYY-MM-DD` dates; either end may be empty. */
export interface TmDateRangeValue {
  /** The first day of the range, or `null` while open. */
  readonly from: string | null;
  /** The last day of the range, or `null` while open. */
  readonly to: string | null;
}

/**
 * Validates that a date range's end is on or after its start, reporting the
 * `dateRangeOrder` kind ("The end date must be on or after the start
 * date"), which `tm-date-range` shows on its "to" field. A range with an
 * empty end passes: an open range is in order.
 */
export function tmDateRangeOrder<TPathKind extends PathKind = PathKind.Root>(
  path: SchemaPath<TmDateRangeValue, SchemaPathRules.Supported, TPathKind>,
): void {
  validate(path, (ctx) => {
    const { from, to } = ctx.value();
    // ISO dates compare correctly as text.
    if (from === null || to === null || from <= to) {
      return undefined;
    }
    return { kind: 'dateRangeOrder' } as ValidationError.WithoutFieldTree;
  });
}
