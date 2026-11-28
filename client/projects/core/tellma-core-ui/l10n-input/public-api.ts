/**
 * Public API Surface of @tellma/core-ui/l10n-input — the input side of
 * localization, as pure functions (no DOM, no dependency injection, no
 * components): typed and pasted numbers and dates parsed back from the
 * locale's own formats, the numeric precision helpers, a date field's
 * placeholder, the week's first day, and wall-clock times in an explicit
 * time zone.
 *
 * It is an entry point of its own, apart from `@tellma/core-ui/l10n`'s
 * formats, so a page that only displays values never loads the parsers:
 * an app's bundler places the code a lazily loaded form uses from a file
 * in the first frame's chunks whenever the first frame reaches that file.
 * Everything here is deterministic per (input, locale, options).
 *
 * @packageDocumentation
 */

// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.
export {
  TM_NUMBER_MAX_DIGITS,
  tmNumberDigitCount,
  tmParseNumber,
  type TmNumberParseOptions,
} from './tm-number-parse';
export { tmDatePlaceholder, tmParseDate, type TmDateParseOptions } from './tm-date-parse';
export { tmFirstDayOfWeek } from './tm-week-info';
export {
  ɵtmFormatOffsetInstant,
  ɵtmInstantOfWallTime,
  ɵtmReadInstant,
  ɵtmWallTimeOf,
  type ɵTmWallTime,
} from './tm-zone-wall-time';
