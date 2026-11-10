// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

/**
 * Regenerates the committed formatting golden
 * (`projects/core/tellma-core-ui/l10n/format-golden.json`): rows of
 * (locale, options, input → expected) for the client's formatters — the
 * number codec, the date engine with its period styles, the month and
 * weekday names, the instant formats (date-time, time, relative time) and
 * the zone-label decision — asserted row by row by the client's suite.
 *
 *   pnpm run build && pnpm run golden:approve
 *
 * The golden pins the CLIENT's formatters only: no server-side test reads
 * it (.NET formats through NLS on Windows and ICU on Linux, so
 * client-equal strings are no server contract).
 *
 * The script imports the BUILT l10n bundle (dist), so it exercises exactly
 * what ships. It runs on Node's ICU; the asserting suite runs on
 * Chromium's — both draw on the same CLDR releases, and any drift fails
 * the suite loudly on the exact row, making regeneration an explicit,
 * reviewed act (the API-golden posture).
 */
import { writeFileSync } from 'node:fs';
import { dirname, join, resolve } from 'node:path';
import { fileURLToPath, pathToFileURL } from 'node:url';

const clientDir = resolve(dirname(fileURLToPath(import.meta.url)), '..', '..');

/** A display calendar, as far as this script reads one. */
interface Calendar {
  readonly id: string;
}

/** The members of the built l10n bundle this script calls. */
interface L10nModule {
  tmFormatNumber(value: unknown, locale: string, options?: object): string;
  tmFormatDate(iso: string, locale: string, options?: object): string;
  tmFormatDateTime(instant: string, locale: string, options: object): string;
  tmFormatTime(instant: string, locale: string, options: object): string;
  tmFormatRelativeTime(instant: string, locale: string, options: object): string;
  tmFormatZoneOffset(instant: string | number, locale: string, timeZone: string): string;
  tmFormatMonthName(month: number, locale: string, options?: object): string;
  tmFormatWeekdayName(weekday: number, locale: string): string;
  tmGregorianCalendar(): Calendar;
  ɵtmParseInstant(text: string): number | null;
  ɵtmShowsZoneLabel(
    at: number,
    timeZone: string,
    deviceTimeZone: string,
    mode: string,
    headerAt: number,
  ): boolean;
}

const l10n = (await import(
  pathToFileURL(join(clientDir, 'dist/core-ui/fesm2022/tellma-core-ui-l10n.mjs')).href
)) as L10nModule;

const calendars = new Map<string, Calendar>([['gregory', l10n.tmGregorianCalendar()]]);
// The opt-in calendar entry points join the golden once built.
for (const [id, file] of [
  ['islamic-umalqura', 'tellma-core-ui-calendar-umalqura.mjs'],
  ['ethiopic', 'tellma-core-ui-calendar-ethiopic.mjs'],
] as const) {
  try {
    const mod = (await import(
      pathToFileURL(join(clientDir, 'dist/core-ui/fesm2022', file)).href
    )) as Record<string, () => Calendar>;
    const factory = Object.values(mod).find((entry) => typeof entry === 'function');
    if (factory !== undefined) {
      calendars.set(id, factory());
    }
  } catch {
    // Entry point not built yet — its rows join when it exists.
  }
}
const calendarIds = [...calendars.keys()];

/** The epoch milliseconds of an ISO instant the script itself wrote. */
function at(instant: string): number {
  const millis = l10n.ɵtmParseInstant(instant);
  if (millis === null) {
    throw new Error(`generate-format-golden: '${instant}' is not an instant`);
  }
  return millis;
}

// ---- Numbers ----

const NUMBER_LOCALES = [
  'en-US',
  'en-GB',
  'de-DE',
  'fr-FR',
  'ar-SA',
  'ar-EG',
  'hi-IN',
  'tr-TR',
  // The Arabic variants whose minus and percent signs ICU wraps in
  // direction marks (spec 0010 §D.14): plain `ar` and Latin digits.
  'ar',
  'ar-SA-u-nu-latn',
];
const NUMBER_CASES: readonly {
  minDecimals: number;
  maxDecimals: number;
  percent: boolean;
  value: number;
}[] = [
  { minDecimals: 2, maxDecimals: 2, percent: false, value: 1234.5 },
  { minDecimals: 0, maxDecimals: 20, percent: false, value: 0 },
  { minDecimals: 0, maxDecimals: 2, percent: false, value: -9876543.21 },
  { minDecimals: 0, maxDecimals: 20, percent: false, value: 0.5 },
  { minDecimals: 0, maxDecimals: 2, percent: false, value: 1.005 },
  { minDecimals: 0, maxDecimals: 0, percent: false, value: 123456789012345 },
  { minDecimals: 0, maxDecimals: 1, percent: true, value: 0.155 },
  { minDecimals: 0, maxDecimals: 20, percent: true, value: 0.75 },
  { minDecimals: 0, maxDecimals: 1, percent: true, value: -0.155 },
];

// Grouping rows (spec 0010 §D.15): a four-digit and a seven-digit value,
// grouped and not. Spanish groups from five digits, so its grouped 1234
// stays "1234".
const GROUPING_LOCALES = ['en', 'ar-SA', 'es'];
const GROUPING_VALUES = [1234, 1234567];

const number = [
  ...NUMBER_LOCALES.flatMap((locale) =>
    NUMBER_CASES.map((options) => ({
      locale,
      minDecimals: options.minDecimals,
      maxDecimals: options.maxDecimals,
      percent: options.percent,
      grouping: true,
      value: options.value,
      expected: l10n.tmFormatNumber(options.value, locale, options),
    })),
  ),
  ...GROUPING_LOCALES.flatMap((locale) =>
    GROUPING_VALUES.flatMap((value) =>
      [true, false].map((grouping) => ({
        locale,
        minDecimals: 0,
        maxDecimals: 20,
        percent: false,
        grouping,
        value,
        expected: l10n.tmFormatNumber(value, locale, { grouping }),
      })),
    ),
  ),
];

// ---- Dates ----

const DATE_LOCALES = [
  'en-US',
  'en-GB',
  'de-DE',
  'ar-SA',
  'am-ET',
  // The Arabic variants of spec 0010 §D.14.
  'ar',
  'ar-SA-u-nu-latn',
  'ar-EG',
];
const DATE_STYLES = ['numeric', 'medium', 'long', 'weekday', 'month', 'quarter', 'year'] as const;
// 2026-09-08 is in Ethiopic's thirteenth month (3 Pagumen 2018), which
// falls in the fourth quarter (spec 0010 §D.7).
const DATE_ISOS = ['2026-03-05', '2024-02-29', '1999-12-31', '2026-09-08'];

/** The translated quarter pattern a locale's rows are written through. */
const quarterPatternOf = (locale: string): string =>
  locale.startsWith('ar') ? 'الربع {quarter} {year}' : 'Q{quarter} {year}';

const date = calendarIds.flatMap((calendarId) =>
  DATE_LOCALES.flatMap((locale) =>
    DATE_STYLES.flatMap((dateStyle) =>
      DATE_ISOS.map((iso) => {
        const quarterPattern = dateStyle === 'quarter' ? quarterPatternOf(locale) : undefined;
        return {
          locale,
          calendar: calendarId,
          dateStyle,
          ...(quarterPattern === undefined ? {} : { quarterPattern }),
          iso,
          expected: l10n.tmFormatDate(iso, locale, {
            calendar: calendars.get(calendarId),
            dateStyle,
            quarterPattern,
          }),
        };
      }),
    ),
  ),
);

// ---- Month and weekday names ----

const NAME_LOCALES = ['en-GB', 'ar-SA', 'am-ET', 'de-DE'];
// 0 and 14 are a month of no calendar, and 13 of Ethiopic alone; 0 and 8
// are no weekday. Those format as numbers.
const MONTH_NUMBERS = [0, 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13, 14];
const WEEKDAY_NUMBERS = [0, 1, 2, 3, 4, 5, 6, 7, 8];

const monthName = calendarIds.flatMap((calendarId) =>
  NAME_LOCALES.flatMap((locale) =>
    MONTH_NUMBERS.map((month) => ({
      locale,
      calendar: calendarId,
      month,
      expected: l10n.tmFormatMonthName(month, locale, { calendar: calendars.get(calendarId) }),
    })),
  ),
);

const weekdayName = NAME_LOCALES.flatMap((locale) =>
  WEEKDAY_NUMBERS.map((weekday) => ({
    locale,
    weekday,
    expected: l10n.tmFormatWeekdayName(weekday, locale),
  })),
);

// ---- Instants ----

const ZONES = ['UTC', 'Asia/Riyadh', 'Africa/Addis_Ababa', 'Asia/Beirut'];
const INSTANTS = [
  // 10:42 in Riyadh (spec 0010 §D.4's example).
  '2026-10-20T07:42:00Z',
  // A seven-digit fraction (truncated to milliseconds) and an offset; in
  // Beirut it is 01:30 on the day daylight saving begins (the clocks skip
  // 00:00–01:00 on 29 March 2026).
  '2026-03-28T22:30:15.1234567+00:00',
  // 22:30 UTC on New Year's Eve, written with a negative offset: already
  // 1 January 2027 in Riyadh, Addis Ababa and Beirut.
  '2026-12-31T21:30:00-01:00',
];

/** The date and time style pairs every locale × zone × instant is written in. */
const DATE_TIME_STYLES = [
  { dateStyle: 'numeric', timeStyle: 'short' },
  { dateStyle: 'medium', timeStyle: 'medium' },
  { dateStyle: 'weekday', timeStyle: 'short' },
] as const;

/** One `dateTime` row, formatted. */
function dateTimeRow(
  locale: string,
  timeZone: string,
  calendarId: string,
  dateStyle: string,
  timeStyle: string,
  zoneLabel: boolean,
  instant: string,
) {
  return {
    locale,
    timeZone,
    calendar: calendarId,
    dateStyle,
    timeStyle,
    ...(zoneLabel ? { zoneLabel } : {}),
    instant,
    expected: l10n.tmFormatDateTime(instant, locale, {
      timeZone,
      calendar: calendars.get(calendarId),
      dateStyle,
      timeStyle,
      zoneLabel,
    }),
  };
}

// The locales and zones whose rows carry the offset label: the three
// spellings of Arabic and English in Riyadh, and a half-hour offset.
const LABEL_LOCALES = ['en-GB', 'ar-SA', 'ar-SA-u-nu-latn'];
const LABEL_ZONES = ['Asia/Riyadh', 'Asia/Kolkata'];

// A day boundary in Riyadh (spec 0010 §D.8): one second before midnight
// of 20 October 2026 there, and midnight itself.
const DAY_BOUNDARY = ['2026-10-19T20:59:59Z', '2026-10-19T21:00:00Z'];

const dateTime = [
  // Gregorian, every locale × zone × instant × style pair.
  ...DATE_LOCALES.flatMap((locale) =>
    ZONES.flatMap((timeZone) =>
      INSTANTS.flatMap((instant) =>
        DATE_TIME_STYLES.map(({ dateStyle, timeStyle }) =>
          dateTimeRow(locale, timeZone, 'gregory', dateStyle, timeStyle, false, instant),
        ),
      ),
    ),
  ),
  // The other calendars, in the zones their users sit in: the era goes,
  // and the separator between the year and the time stays.
  ...calendarIds
    .filter((calendarId) => calendarId !== 'gregory')
    .flatMap((calendarId) =>
      DATE_LOCALES.flatMap((locale) =>
        ['Asia/Riyadh', 'Africa/Addis_Ababa'].flatMap((timeZone) =>
          INSTANTS.flatMap((instant) =>
            DATE_TIME_STYLES.map(({ dateStyle, timeStyle }) =>
              dateTimeRow(locale, timeZone, calendarId, dateStyle, timeStyle, false, instant),
            ),
          ),
        ),
      ),
    ),
  // The zone label.
  ...LABEL_LOCALES.flatMap((locale) =>
    LABEL_ZONES.flatMap((timeZone) =>
      INSTANTS.map((instant) =>
        dateTimeRow(locale, timeZone, 'gregory', 'numeric', 'short', true, instant),
      ),
    ),
  ),
  // No time: the instant's date in the zone, per calendar, across the day
  // boundary. A label asked for is never written.
  ...calendarIds.flatMap((calendarId) =>
    DATE_LOCALES.flatMap((locale) =>
      DAY_BOUNDARY.flatMap((instant) =>
        (['numeric', 'weekday'] as const).map((dateStyle) =>
          dateTimeRow(locale, 'Asia/Riyadh', calendarId, dateStyle, 'none', false, instant),
        ),
      ),
    ),
  ),
  ...DAY_BOUNDARY.map((instant) =>
    dateTimeRow('en-GB', 'Asia/Riyadh', 'gregory', 'weekday', 'none', true, instant),
  ),
];

const time = [
  ...DATE_LOCALES.flatMap((locale) =>
    ZONES.flatMap((timeZone) =>
      INSTANTS.flatMap((instant) =>
        (['short', 'medium'] as const).map((timeStyle) => ({
          locale,
          timeZone,
          timeStyle,
          instant,
          expected: l10n.tmFormatTime(instant, locale, { timeZone, timeStyle }),
        })),
      ),
    ),
  ),
  ...LABEL_LOCALES.flatMap((locale) =>
    LABEL_ZONES.flatMap((timeZone) =>
      INSTANTS.map((instant) => ({
        locale,
        timeZone,
        timeStyle: 'short',
        zoneLabel: true,
        instant,
        expected: l10n.tmFormatTime(instant, locale, { timeZone, zoneLabel: true }),
      })),
    ),
  ),
];

// ---- Relative time ----

const SECOND = 1000;
const MINUTE = 60 * SECOND;
const HOUR = 60 * MINUTE;
const DAY = 24 * HOUR;

/** "Now" for the threshold rows: 10:42 in Riyadh on 20 October 2026. */
const RELATIVE_NOW = '2026-10-20T07:42:00Z';

/**
 * How long before `now` each threshold row's instant lies (negative: after
 * it). Each boundary appears on both of its sides, since distances are
 * truncated: 59.999 s is still "now", 119 s is one minute.
 */
const RELATIVE_AGES = [
  0,
  59 * SECOND + 999,
  MINUTE,
  119 * SECOND,
  2 * MINUTE,
  59 * MINUTE + 59 * SECOND,
  HOUR,
  3 * HOUR + 59 * MINUTE,
  23 * HOUR + 59 * MINUTE,
  // 24 hours back is the previous date at 10:42: "yesterday".
  DAY,
  // 34 h 43 min back is 23:59 two dates before: "2 days ago", where
  // rounding the hours would say one.
  34 * HOUR + 43 * MINUTE,
  3 * DAY,
  6 * DAY + 10 * HOUR,
  // Seven calendar days: the absolute date.
  6 * DAY + 11 * HOUR,
  40 * DAY,
  -30 * SECOND,
  -5 * MINUTE,
  -3 * HOUR,
  -DAY,
  -3 * DAY,
  -7 * DAY,
];

/** One `relative` row, formatted. */
function relativeRow(locale: string, timeZone: string, now: number, instant: string, style: string) {
  return {
    locale,
    timeZone,
    now,
    instant,
    style,
    expected: l10n.tmFormatRelativeTime(instant, locale, { now, timeZone, style }),
  };
}

const relative = [
  ...DATE_LOCALES.flatMap((locale) =>
    (['short', 'long', 'narrow'] as const).flatMap((style) =>
      RELATIVE_AGES.map((age) =>
        relativeRow(
          locale,
          'Asia/Riyadh',
          at(RELATIVE_NOW),
          new Date(at(RELATIVE_NOW) - age).toISOString(),
          style,
        ),
      ),
    ),
  ),
  // The same pair of instants, 26 hours apart, is "yesterday" in UTC and
  // "2 days ago" in Riyadh, Addis Ababa and Beirut, where two midnights
  // fell between them: days are the zone's calendar days.
  ...['en-GB', 'ar-SA'].flatMap((locale) =>
    ZONES.map((timeZone) =>
      relativeRow(locale, timeZone, at('2026-10-20T22:30:00Z'), '2026-10-19T20:30:00Z', 'short'),
    ),
  ),
  // Beirut's daylight-saving days. 29 March 2026 has 23 hours: 24 hours
  // after 23:30 on the 28th is 00:30 on the 30th, two dates on. 24 October
  // has 25 hours: 24 hours after 00:10 is 23:10 the same date.
  ...['en-GB', 'ar-SA'].flatMap((locale) => [
    relativeRow(locale, 'Asia/Beirut', at('2026-03-29T21:30:00Z'), '2026-03-28T21:30:00Z', 'short'),
    relativeRow(locale, 'Asia/Beirut', at('2026-10-24T21:10:00Z'), '2026-10-23T21:10:00Z', 'short'),
  ]),
];

// ---- The zone label ----

/**
 * The label a facade shows for an instant: the display zone's offset when
 * the mode's rule says the instant is labelled, else ''.
 */
function zoneLabelRow(
  locale: string,
  timeZone: string,
  deviceTimeZone: string,
  instant: string,
  mode: 'auto' | 'column' | 'none',
  header: string | null,
) {
  const shows = l10n.ɵtmShowsZoneLabel(
    at(instant),
    timeZone,
    deviceTimeZone,
    mode,
    header === null ? at(instant) : at(header),
  );
  return {
    locale,
    timeZone,
    deviceTimeZone,
    instant,
    mode,
    // The instant a column's header was labelled at; null outside 'column'.
    header,
    expected: shows ? l10n.tmFormatZoneOffset(instant, locale, timeZone) : '',
  };
}

// Beirut is at +2 in winter and +3 in summer; Riyadh is at +3 all year.
const WINTER = '2026-01-15T09:00:00Z';
const SUMMER = '2026-07-15T09:00:00Z';

const zoneLabel = ['en-GB', 'ar-SA', 'ar-SA-u-nu-latn'].flatMap((locale) => [
  // Equal offsets under different ids never label.
  zoneLabelRow(locale, 'Asia/Riyadh', 'Asia/Kuwait', SUMMER, 'auto', null),
  zoneLabelRow(locale, 'Asia/Riyadh', 'Asia/Kuwait', SUMMER, 'column', SUMMER),
  // A device in another offset.
  zoneLabelRow(locale, 'Asia/Riyadh', 'Europe/London', SUMMER, 'auto', null),
  zoneLabelRow(locale, 'Asia/Riyadh', 'Europe/London', SUMMER, 'none', null),
  zoneLabelRow(locale, 'Asia/Kolkata', 'Asia/Riyadh', SUMMER, 'auto', null),
  // Under a labelled header a cell is bare...
  zoneLabelRow(locale, 'Asia/Riyadh', 'Europe/London', WINTER, 'column', SUMMER),
  // Beirut shown on a Riyadh device: the offsets differ in winter only.
  zoneLabelRow(locale, 'Asia/Beirut', 'Asia/Riyadh', WINTER, 'auto', null),
  zoneLabelRow(locale, 'Asia/Beirut', 'Asia/Riyadh', SUMMER, 'auto', null),
  // ...unless daylight saving changed between it and the header: a summer
  // row under a winter header labels, though its offset equals the device's.
  zoneLabelRow(locale, 'Asia/Beirut', 'Asia/Riyadh', WINTER, 'column', WINTER),
  zoneLabelRow(locale, 'Asia/Beirut', 'Asia/Riyadh', SUMMER, 'column', WINTER),
  // Under a bare header (summer: both at +3) a cell labels as 'auto' does.
  zoneLabelRow(locale, 'Asia/Beirut', 'Asia/Riyadh', WINTER, 'column', SUMMER),
  zoneLabelRow(locale, 'Asia/Beirut', 'Asia/Riyadh', SUMMER, 'column', SUMMER),
  // UTC: equal to London in winter, an hour behind it in summer.
  zoneLabelRow(locale, 'UTC', 'Europe/London', WINTER, 'auto', null),
  zoneLabelRow(locale, 'UTC', 'Europe/London', SUMMER, 'auto', null),
  zoneLabelRow(locale, 'UTC', 'UTC', SUMMER, 'auto', null),
  zoneLabelRow(locale, 'Asia/Riyadh', 'UTC', SUMMER, 'auto', null),
]);

const golden = {
  $comment:
    "Formatting golden: (params -> expected) rows pinning the client's formatters, asserted by " +
    'the client suite; no server-side test reads it. Regenerate via ' +
    '`pnpm run build && pnpm run golden:approve`; review the diff like an API golden.',
  number,
  date,
  monthName,
  weekdayName,
  dateTime,
  time,
  relative,
  zoneLabel,
};

// No expected string may hold a bidi control: the formatters strip them
// (spec 0010 §D.14), and a golden that pinned one would pin the bug.
const BIDI_CONTROLS = /[\u061C\u200E\u200F\u202A-\u202E\u2066-\u2069]/;
for (const [name, rows] of Object.entries(golden)) {
  if (Array.isArray(rows) && rows.some((row) => BIDI_CONTROLS.test(row.expected))) {
    throw new Error(`generate-format-golden: a '${name}' row holds a bidi control character`);
  }
}

const target = join(clientDir, 'projects/core/tellma-core-ui/l10n/format-golden.json');
writeFileSync(target, `${JSON.stringify(golden, null, 2)}\n`);
console.log(
  `format-golden OK -> ${target} (` +
    Object.entries(golden)
      .filter(([, rows]) => Array.isArray(rows))
      .map(([name, rows]) => `${(rows as unknown[]).length} ${name}`)
      .join(', ') +
    `; calendars: ${calendarIds.join(', ')})`,
);
