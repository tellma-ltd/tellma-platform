// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import type { ValidationError } from '@angular/forms/signals';
import { TmDecimals, type TmDecimal } from '@tellma/core';
import { TM_NUMBER_MAX_DIGITS, tmFormatNumber, tmNumberDigitCount, tmParseNumber } from '@tellma/core-ui/l10n';

// Decimals and longs edited as numbers (spec 0029 §12.2: "the tmNumber
// control converts at the binding"). The text a user types is read into
// decimal text at the column's scale, held to the digits a double carries;
// text that reads as no number, or as more digits than that, is refused
// with the same errors tmNumber raises, and never becomes the value.

/** What a decimal's edit text reads as: its decimal text, or the error that refuses it. */
export type ɵTmExactParse = { readonly value: TmDecimal | null } | { readonly error: ValidationError.WithoutFieldTree };

/**
 * Reads a decimal's or a long's edit text, rounded to its scale. Internal:
 * the value editor, the grid columns and the column filter parse through it.
 *
 * @param text - The edit text.
 * @param locale - The locale it is written in.
 * @param scale - The fraction digits the value keeps.
 * @param sourceLocale - The locale of pasted text, when it differs.
 * @returns The decimal text, `null` for empty text, or the error.
 */
export function ɵtmParseExact(text: string, locale: string, scale: number, sourceLocale?: string): ɵTmExactParse {
  if (text.trim() === '') {
    return { value: null };
  }
  const options = { minDecimals: scale, maxDecimals: scale };
  const number = tmParseNumber(text, locale, { ...options, ...(sourceLocale !== undefined && { sourceLocale }) });
  if (typeof number !== 'number') {
    return { error: { kind: 'parse', example: tmFormatNumber(1234.5, locale, options) } as ValidationError.WithoutFieldTree };
  }
  const rounded = Number(number.toFixed(scale));
  return tmNumberDigitCount(rounded) > TM_NUMBER_MAX_DIGITS
    ? { error: { kind: 'numberPrecision', maxDigits: TM_NUMBER_MAX_DIGITS } as ValidationError.WithoutFieldTree }
    : { value: TmDecimals.fromNumber(rounded) };
}
