// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

// Writes the Ethiopian calendar vectors the Queryex suites read: Gregorian dates and the Ethiopian
// (year, month, day) that ICU's 'ethiopic' calendar gives them, through Intl.DateTimeFormat, which
// is the client's own source of Ethiopian dates.
//
// Usage: node generate.mjs <output.csv>

import { writeFileSync } from 'node:fs';

// The ICU reading of one Gregorian date, at midnight UTC so no zone can move it to another day.
const formatter = new Intl.DateTimeFormat('en-u-ca-ethiopic-nu-latn', {
  timeZone: 'UTC',
  year: 'numeric',
  month: 'numeric',
  day: 'numeric',
});

/**
 * Reads one date in the Ethiopian calendar.
 * @param {Date} date The Gregorian date, at midnight UTC.
 * @returns {{ year: number, month: number, day: number }} The Ethiopian date.
 */
function ethiopic(date) {
  const parts = Object.fromEntries(
    formatter.formatToParts(date).map((part) => [part.type, part.value]),
  );
  return { year: Number(parts.year), month: Number(parts.month), day: Number(parts.day) };
}

/**
 * Writes a Gregorian date as yyyy-MM-dd.
 * @param {Date} date The date, at midnight UTC.
 * @returns {string} The text.
 */
function iso(date) {
  return date.toISOString().slice(0, 10);
}

/**
 * Whether a date sits on one of the boundaries the suites probe: the first day of a year, of a
 * quarter or of Pagume, the last days of Nehase and of Pagume, and the last day of the third
 * quarter.
 * @param {{ year: number, month: number, day: number }} date The Ethiopian date.
 * @returns {boolean} True when it does.
 */
function isBoundary({ month, day }) {
  return (day === 1 && (month === 1 || month === 4 || month === 7 || month === 10 || month === 13))
    || (month === 9 && day === 30)
    || (month === 12 && day === 30)
    || (month === 13 && day >= 5);
}

const output = process.argv[2];
if (!output) {
  console.error('Usage: node generate.mjs <output.csv>');
  process.exit(2);
}

const lines = [];
const seen = new Set();

/**
 * Adds one date's vector, once.
 * @param {Date} date The date, at midnight UTC.
 */
function add(date) {
  const key = iso(date);
  if (seen.has(key)) {
    return;
  }

  seen.add(key);
  const { year, month, day } = ethiopic(date);
  lines.push(`${key},${year},${month},${day}`);
}

// The pinned conformance dates the server and the client are also held to, some of which reach
// past the range below at both ends.
const pinnedDates = [
  '1752-09-09', '1752-09-14', '1900-01-01', '1971-09-11', '2000-01-01', '2007-09-12', '2011-09-11',
  '2015-09-12', '2019-09-11', '2020-01-01', '2024-09-11', '2026-09-04', '2499-09-15', '2500-01-01',
];
for (const pinned of pinnedDates) {
  add(new Date(`${pinned}T00:00:00Z`));
}

// Every boundary day of every Ethiopian year that overlaps 1900 to 2100.
const first = Date.UTC(1899, 8, 1);
const last = Date.UTC(2101, 8, 30);
for (let time = first; time <= last; time += 86_400_000) {
  const date = new Date(time);
  if (isBoundary(ethiopic(date))) {
    add(date);
  }
}

// In date order, which is also the order of the text, under a header naming the columns.
lines.sort((a, b) => (a < b ? -1 : a > b ? 1 : 0));
writeFileSync(output, `gregorian,year,month,day\n${lines.join('\n')}\n`, { encoding: 'utf8' });
console.log(`Wrote ${lines.length} vectors to ${output}`);
