// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import { TM_PARSE_ERROR, type TmParseError } from '@tellma/core-ui/contracts';
import { tmFormatTime } from '@tellma/core-ui/l10n';

/** A time of day on the wire: 24-hour `HH:mm` with Latin digits. */
const TIME_OF_DAY = /^([01]\d|2[0-3]):([0-5]\d)$/;

/** The minutes in a day — the modulus of every step. */
const MINUTES_PER_DAY = 24 * 60;

/** Unicode format and bidirectional controls (the `Cf` category) — invisible noise in typed text. */
const FORMAT_CONTROLS = /\p{Cf}/gu;

/** A run of letters (with combining marks), the unit day periods and locale words are matched in. */
const LETTER_RUN = /\p{L}[\p{L}\p{M}]*/gu;

/**
 * Whether `text` is a time of day on the wire (`HH:mm`, 24-hour, Latin
 * digits).
 */
export function ɵtmIsTimeOfDay(text: string): boolean {
  return TIME_OF_DAY.test(text);
}

/** The minutes since midnight of a wire time of day. */
export function ɵtmMinutesOfDay(time: string): number {
  return Number(time.slice(0, 2)) * 60 + Number(time.slice(3, 5));
}

/** The wire time of day of a minute count, wrapped into one day. */
export function ɵtmTimeOfMinutes(minutes: number): string {
  const wrapped = ((minutes % MINUTES_PER_DAY) + MINUTES_PER_DAY) % MINUTES_PER_DAY;
  const hour = Math.floor(wrapped / 60);
  const minute = wrapped % 60;
  return `${String(hour).padStart(2, '0')}:${String(minute).padStart(2, '0')}`;
}

/**
 * Adds `minutes` (negative subtracts) to a wire time of day, wrapping at
 * midnight in both directions: `23:50` plus 15 is `00:05`.
 */
export function ɵtmStepTimeOfDay(time: string, minutes: number): string {
  return ɵtmTimeOfMinutes(ɵtmMinutesOfDay(time) + minutes);
}

/**
 * Formats a wire time of day in the locale's hour cycle and digits:
 * "14:30" in en-GB, "2:30 PM" in en-US, "٢:٣٠ م" in ar-SA. It is the
 * instant formatter's text for that time on a fixed UTC day, so a time of
 * day and the time part of an instant always read alike.
 */
export function ɵtmFormatTimeOfDay(time: string, locale: string): string {
  return tmFormatTime(`1970-01-01T${time}:00Z`, locale, { timeZone: 'UTC' });
}

/**
 * The locale's day-period words and the other words its time format
 * writes (French Canadian "14 h 30", Bulgarian "14:30 ч."), each folded
 * (see `fold`), cached per locale.
 */
interface LocaleTimeWords {
  /** The folded forenoon marker ("am", "ص"). */
  readonly am: string;
  /** The folded afternoon marker ("pm", "م"). */
  readonly pm: string;
  /** Folded words the format writes that carry no meaning to a parser. */
  readonly literals: ReadonlySet<string>;
}

const wordsCache = new Map<string, LocaleTimeWords>();

/**
 * Folds a word for comparison: lower case, with the dots and spaces some
 * locales put inside a day period ("p. m.", "a.m.") removed.
 */
function fold(word: string): string {
  return word.replace(/[.\s]/g, '').toLowerCase();
}

/** The day-period part of a 12-hour format at a UTC hour, folded. */
function dayPeriodAt(locale: string, hour: number): string {
  const parts = new Intl.DateTimeFormat(locale, {
    hour: 'numeric',
    hourCycle: 'h12',
    timeZone: 'UTC',
  }).formatToParts(Date.UTC(2000, 0, 1, hour));
  return fold(parts.find((part) => part.type === 'dayPeriod')?.value ?? '');
}

/** The locale's time words, read once from the engine's own formats. */
function localeTimeWords(locale: string): LocaleTimeWords {
  let words = wordsCache.get(locale);
  if (words === undefined) {
    const literals = new Set<string>();
    // The words the locale's short time format writes between its numbers,
    // in both hour cycles, so text the field itself wrote always parses.
    for (const hourCycle of ['h12', 'h23'] as const) {
      const parts = new Intl.DateTimeFormat(locale, {
        hour: 'numeric',
        minute: '2-digit',
        hourCycle,
        timeZone: 'UTC',
      }).formatToParts(Date.UTC(2000, 0, 1, 14, 30));
      for (const part of parts) {
        if (part.type === 'literal') {
          for (const word of part.value.match(LETTER_RUN) ?? []) {
            literals.add(fold(word));
          }
        }
      }
    }
    words = { am: dayPeriodAt(locale, 1), pm: dayPeriodAt(locale, 13), literals };
    wordsCache.set(locale, words);
  }
  return words;
}

/**
 * The numeric value of a decimal digit in any script (`٣` is 3, `३` is 3):
 * every script's digits are a contiguous run of ten starting at its zero,
 * and adjacent runs (the mathematical digit styles) are each ten long, so
 * the offset from the start of the contiguous block, modulo ten, is the
 * digit.
 */
function digitValue(codePoint: number): number {
  let start = codePoint;
  while (start > 0 && /\p{Nd}/u.test(String.fromCodePoint(start - 1))) {
    start--;
  }
  return (codePoint - start) % 10;
}

/** Maps every decimal digit, in any script, to its Latin digit; other characters pass through. */
function latinDigits(text: string): string {
  let out = '';
  for (const char of text) {
    out += /\p{Nd}/u.test(char) ? String(digitValue(char.codePointAt(0)!)) : char;
  }
  return out;
}

/**
 * Parses typed time-of-day text to the wire form (`HH:mm`), `null` for
 * blank text, or `TM_PARSE_ERROR`.
 *
 * Accepted: digits in any script; `14`, `930`, `1430`, `14:30` (any
 * non-letter separator — `14.30`, `14 30`); and the locale's day-period
 * words ("pm", "م", "午後") or the Latin `am`/`pm`/`a`/`p`, before or after
 * the numbers and with or without dots. With a day period the hour is 1–12
 * (0 reads as 12); without one it is 0–23. Seconds are refused: the field
 * holds minutes.
 */
export function ɵtmParseTimeOfDay(text: string, locale: string): string | null | TmParseError {
  let rest = latinDigits(text.replace(FORMAT_CONTROLS, '')).trim();
  if (rest === '') {
    return null;
  }
  // Fold dots and spaces that sit INSIDE a day period ("p. m." → "pm.")
  // before splitting the text into words.
  rest = rest.replace(/(?<=\p{L})[.\s]+(?=\p{L})/gu, '');
  const words = localeTimeWords(locale);
  let period: 'am' | 'pm' | null = null;
  for (const run of rest.match(LETTER_RUN) ?? []) {
    const word = fold(run);
    const isAm = word === words.am || word === 'am' || word === 'a';
    const isPm = word === words.pm || word === 'pm' || word === 'p';
    if (isAm || isPm) {
      if (period !== null) {
        return TM_PARSE_ERROR; // two day periods
      }
      period = isAm ? 'am' : 'pm';
    } else if (!words.literals.has(word)) {
      return TM_PARSE_ERROR; // a word the locale's times never carry
    }
  }
  const groups = rest.replace(LETTER_RUN, ' ').match(/\d+/g) ?? [];
  let hour: number;
  let minute: number;
  if (groups.length === 1) {
    // One run of digits: H, HH, HMM or HHMM.
    const digits = groups[0];
    if (digits.length <= 2) {
      hour = Number(digits);
      minute = 0;
    } else if (digits.length <= 4) {
      hour = Number(digits.slice(0, -2));
      minute = Number(digits.slice(-2));
    } else {
      return TM_PARSE_ERROR;
    }
  } else if (groups.length === 2 && groups[0].length <= 2 && groups[1].length === 2) {
    hour = Number(groups[0]);
    minute = Number(groups[1]);
  } else {
    return TM_PARSE_ERROR; // no digits, seconds, or a stray number
  }
  if (minute > 59) {
    return TM_PARSE_ERROR;
  }
  if (period === null) {
    if (hour > 23) {
      return TM_PARSE_ERROR;
    }
  } else {
    if (hour > 12) {
      return TM_PARSE_ERROR;
    }
    // 12 am is midnight and 12 pm is noon; 0 is read as 12 (the h11 clocks).
    hour = (hour % 12) + (period === 'pm' ? 12 : 0);
  }
  return ɵtmTimeOfMinutes(hour * 60 + minute);
}
