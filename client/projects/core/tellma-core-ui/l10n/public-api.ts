/**
 * Public API Surface of @tellma/core-ui/l10n — locale-sensitive number,
 * date and instant formatting/parsing as pure functions (no DOM, no
 * dependency injection, no components): the number codec and the numeric
 * precision helpers, the date engine and the calendar seam, the instant
 * formats (date-time, time of day, relative time, zone offsets and names)
 * and the month and weekday names.
 *
 * Everything here is deterministic per (input, locale, options) — the
 * display time zone and "now" are arguments, never read from the device:
 * the standalone path for read-only display formatting, and the machinery
 * the form controls and the data grid build their format/parse loops on.
 * Formatted text never carries a bidirectional control character; markup
 * sets direction.
 *
 * @packageDocumentation
 */

// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.
export { TM_BIDI_CONTROLS, tmStripBidiControls } from './tm-bidi';
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
  ɵtmFormatWithoutEra,
  type TmDateFormatOptions,
  type TmDateParseOptions,
  type TmDateStyle,
} from './tm-date-engine';
export { tmFormatMonthName, tmFormatWeekdayName } from './tm-date-names';
export {
  tmFormatDateTime,
  tmFormatRelativeTime,
  tmFormatTime,
  tmFormatZoneName,
  tmFormatZoneOffset,
  ɵtmParseInstant,
  ɵtmShowsZoneLabel,
  ɵtmZoneOffset,
  type TmDateTimeFormatOptions,
  type TmRelativeTimeFormatOptions,
  type TmTimeFormatOptions,
  type TmTimeStyle,
} from './tm-instant';
export { tmFirstDayOfWeek } from './tm-week-info';
