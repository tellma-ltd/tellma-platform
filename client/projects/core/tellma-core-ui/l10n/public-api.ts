/**
 * Public API Surface of @tellma/core-ui/l10n — locale-sensitive number and
 * date formatting/parsing as pure functions (no DOM, no dependency
 * injection, no components): the number codec, the numeric precision
 * helpers, and — as they land — the date engine and the calendar seam.
 *
 * Everything here is deterministic per (input, locale, options): the
 * standalone path for read-only display formatting, and the machinery the
 * form controls and the data grid build their format/parse loops on.
 *
 * @packageDocumentation
 */

// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.
export {
  TM_NUMBER_MAX_DIGITS,
  tmFormatNumber,
  tmNumberDigitCount,
  tmParseNumber,
  type TmNumberFormatOptions,
  type TmNumberParseOptions,
} from './tm-number-codec';
export {
  tmGregorianCalendar,
  ɵtmAdaptCalendar,
  ɵtmParseIsoDate,
  ɵtmToIsoDate,
  type TmCalendar,
  type TmCalendarParts,
} from './tm-calendar';
export {
  tmDatePlaceholder,
  tmFormatDate,
  tmParseDate,
  type TmDateFormatOptions,
  type TmDateParseOptions,
  type TmDateStyle,
} from './tm-date-engine';
export { tmFirstDayOfWeek } from './tm-week-info';
