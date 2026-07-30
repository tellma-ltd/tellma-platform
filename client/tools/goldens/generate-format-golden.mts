// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

/**
 * Regenerates the committed cross-platform formatting golden
 * (`projects/core/tellma-core-ui/l10n/format-golden.json`): rows of
 * (locale, options, input → expected) for the number codec and the date
 * engine, consumed row-by-row by the client's asserting suite and
 * verbatim by the server-side implementation's tests.
 *
 *   pnpm run build && pnpm run golden:approve
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

interface L10nModule {
  tmFormatNumber(value: unknown, locale: string, options?: object): string;
  tmFormatDate(iso: string, locale: string, options?: object): string;
  tmGregorianCalendar(): { id: string };
}

const l10n = (await import(
  pathToFileURL(join(clientDir, 'dist/core-ui/fesm2022/tellma-core-ui-l10n.mjs')).href
)) as L10nModule;

const calendars = new Map<string, { id: string }>([['gregory', l10n.tmGregorianCalendar()]]);
// The opt-in calendar entry points join the golden once built.
for (const [id, file] of [
  ['islamic-umalqura', 'tellma-core-ui-calendar-umalqura.mjs'],
  ['ethiopic', 'tellma-core-ui-calendar-ethiopic.mjs'],
] as const) {
  try {
    const mod = (await import(
      pathToFileURL(join(clientDir, 'dist/core-ui/fesm2022', file)).href
    )) as Record<string, () => { id: string }>;
    const factory = Object.values(mod).find((entry) => typeof entry === 'function');
    if (factory !== undefined) {
      calendars.set(id, factory());
    }
  } catch {
    // Entry point not built yet — its rows join when it exists.
  }
}

const NUMBER_LOCALES = ['en-US', 'en-GB', 'de-DE', 'fr-FR', 'ar-SA', 'ar-EG', 'hi-IN', 'tr-TR'];
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
];

const DATE_LOCALES = ['en-US', 'en-GB', 'de-DE', 'ar-SA', 'am-ET'];
const DATE_STYLES = ['numeric', 'medium', 'long'] as const;
const DATE_ISOS = ['2026-03-05', '2024-02-29', '1999-12-31'];

const number = NUMBER_LOCALES.flatMap((locale) =>
  NUMBER_CASES.map((options) => ({
    locale,
    minDecimals: options.minDecimals,
    maxDecimals: options.maxDecimals,
    percent: options.percent,
    value: options.value,
    expected: l10n.tmFormatNumber(options.value, locale, options),
  })),
);

const date = [...calendars.keys()].flatMap((calendarId) =>
  DATE_LOCALES.flatMap((locale) =>
    DATE_STYLES.flatMap((dateStyle) =>
      DATE_ISOS.map((iso) => ({
        locale,
        calendar: calendarId,
        dateStyle,
        iso,
        expected: l10n.tmFormatDate(iso, locale, {
          calendar: calendars.get(calendarId),
          dateStyle,
        }),
      })),
    ),
  ),
);

const golden = {
  $comment:
    'Cross-platform formatting golden: (params -> expected) rows asserted by the client suite ' +
    'and consumed verbatim by the server-side implementation tests. Regenerate via ' +
    '`pnpm run build && pnpm run golden:approve`; review the diff like an API golden.',
  number,
  date,
};

const target = join(clientDir, 'projects/core/tellma-core-ui/l10n/format-golden.json');
writeFileSync(target, `${JSON.stringify(golden, null, 2)}\n`);
console.log(
  `format-golden OK -> ${target} (${number.length} number rows, ${date.length} date rows, ` +
    `calendars: ${[...calendars.keys()].join(', ')})`,
);
