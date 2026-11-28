// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import { tmDefaultUiArgumentFormatter, type TmUiArgumentFormatter } from '@tellma/core-ui';
import {
  tmFormatDate,
  tmFormatDateTime,
  tmFormatTime,
  type TmCalendar,
  type TmDateStyle,
  type TmTimeStyle,
} from '@tellma/core-ui/l10n';

// The calendar-aware formatter of messages' typed arguments (spec 0024 §7.5):
// `{d, date, …}` and `{t, time, …}` render in the active calendar and display
// zone, as the server's message formatter renders them, so a message reads
// alike whichever end formatted it. A calendar date and a wall-clock time
// format as they are; an instant formats on the display zone's wall clock.
// The styles map as the server maps them: none or `medium` → medium, `short`
// → numeric, `long` → long, `full` → with the weekday; any other style, and
// every number, goes to the library's default formatter.

/** What the formatter formats in: the active calendar and display zone, read at each call. */
export interface TmArgumentFormatterInputs {
  /** The active display calendar. */
  readonly calendar: () => TmCalendar;
  /** The display time zone. */
  readonly timeZone: () => string;
}

/** The date styles of ICU's date arguments, as the server maps them. */
const DATE_STYLES: Readonly<Record<string, TmDateStyle>> = {
  '': 'medium',
  medium: 'medium',
  short: 'numeric',
  long: 'long',
  full: 'weekday',
};

/** The styles of ICU's time arguments: `short` writes no seconds, every other style does. */
const TIME_STYLES: Readonly<Record<string, TmTimeStyle>> = {
  '': 'medium',
  medium: 'medium',
  short: 'short',
  long: 'medium',
  full: 'medium',
};

/** A calendar date: `YYYY-MM-DD`. */
const DATE_ONLY = /^\d{4}-\d{2}-\d{2}$/;

/** A wall-clock date and time, with no zone: `YYYY-MM-DDThh:mm[:ss[.fff]]`. */
const WALL_CLOCK = /^(\d{4}-\d{2}-\d{2})T(\d{2}:\d{2}(?::\d{2}(?:\.\d+)?)?)$/;

/** A time of day: `hh:mm[:ss[.fff]]`. */
const TIME_ONLY = /^\d{2}:\d{2}(?::\d{2}(?:\.\d+)?)?$/;

/**
 * The message argument formatter of a Tellma app: dates and times in the
 * active calendar and display zone, numbers as the library formats them.
 *
 * @param inputs - The active calendar and display zone.
 * @returns The formatter, for `TM_UI_ARGUMENT_FORMATTER`.
 *
 * @example
 * ```ts
 * const format = tmArgumentFormatter({ calendar: () => l10n.calendar(), timeZone: () => l10n.timeZone() });
 * format('date', '2026-03-05', 'long', 'ar-SA'); // in the active calendar
 * ```
 */
export function tmArgumentFormatter(inputs: TmArgumentFormatterInputs): TmUiArgumentFormatter {
  return (type, value, style, locale) => {
    const text = value instanceof Date ? (Number.isNaN(value.getTime()) ? null : value.toISOString()) : typeof value === 'string' ? value : null;
    if (type === 'number' || text === null) {
      return tmDefaultUiArgumentFormatter(type, value, style, locale);
    }
    if (type === 'date') {
      const dateStyle = DATE_STYLES[style];
      if (dateStyle === undefined) {
        return tmDefaultUiArgumentFormatter(type, value, style, locale);
      }
      // A calendar date and a wall-clock time keep their own date; an instant takes its zone's.
      const wallClock = WALL_CLOCK.exec(text);
      const date = DATE_ONLY.test(text) ? text : wallClock?.[1];
      return date !== undefined
        ? tmFormatDate(date, locale, { calendar: inputs.calendar(), dateStyle })
        : tmFormatDateTime(text, locale, { calendar: inputs.calendar(), timeZone: inputs.timeZone(), dateStyle, timeStyle: 'none' });
    }
    const timeStyle = TIME_STYLES[style];
    if (timeStyle === undefined) {
      return tmDefaultUiArgumentFormatter(type, value, style, locale);
    }
    // A time of day and a wall-clock time format as they are: as that time in UTC, read in UTC.
    const wallClock = WALL_CLOCK.exec(text)?.[2] ?? (TIME_ONLY.test(text) ? text : undefined);
    return wallClock !== undefined
      ? tmFormatTime(`1970-01-01T${wallClock}Z`, locale, { timeZone: 'UTC', timeStyle })
      : tmFormatTime(text, locale, { timeZone: inputs.timeZone(), timeStyle });
  };
}
