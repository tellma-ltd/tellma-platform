// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import { ɵtmParseInstant, ɵtmZoneOffset } from '@tellma/core-ui/l10n';

/** Milliseconds in a minute and in a day. */
const MINUTE = 60_000;
const DAY = 86_400_000;

/** A wall time in a zone: an ISO date and a wire time of day (`HH:mm`). */
export interface ɵTmWallTime {
  /** The calendar date, ISO `YYYY-MM-DD`. */
  readonly date: string;
  /** The time of day, `HH:mm`, 24-hour. */
  readonly time: string;
}

/**
 * The epoch milliseconds a wall time would have if it were UTC. `Date.UTC`
 * maps the years 0–99 into 1900–1999, so the year is set afterwards, on a
 * leap year that keeps 29 February intact until then.
 */
function asUtc(wall: ɵTmWallTime): number {
  const date = new Date(
    Date.UTC(
      2000,
      Number(wall.date.slice(5, 7)) - 1,
      Number(wall.date.slice(8, 10)),
      Number(wall.time.slice(0, 2)),
      Number(wall.time.slice(3, 5)),
    ),
  );
  date.setUTCFullYear(Number(wall.date.slice(0, 4)));
  return date.getTime();
}

/** Two-digit zero padding. */
function pad(value: number, width = 2): string {
  return String(value).padStart(width, '0');
}

/**
 * The wall time an instant shows in a zone, to the minute (seconds and
 * fractions are dropped, never rounded).
 */
export function ɵtmWallTimeOf(at: number, timeZone: string): ɵTmWallTime {
  const local = new Date(at + ɵtmZoneOffset(at, timeZone) * MINUTE);
  return {
    date: `${pad(local.getUTCFullYear(), 4)}-${pad(local.getUTCMonth() + 1)}-${pad(local.getUTCDate())}`,
    time: `${pad(local.getUTCHours())}:${pad(local.getUTCMinutes())}`,
  };
}

/** Whether an instant shows exactly this wall time in the zone. */
function shows(at: number, wall: ɵTmWallTime, timeZone: string): boolean {
  const shown = ɵtmWallTimeOf(at, timeZone);
  return shown.date === wall.date && shown.time === wall.time;
}

/**
 * The instant a wall time names in a zone, in epoch milliseconds.
 *
 * - A wall time that occurs once is that instant.
 * - One that occurs twice (inside a backward daylight-saving change) takes
 *   the EARLIER instant.
 * - One that does not exist (inside a forward change) becomes the earliest
 *   valid instant after it — the change itself: 00:30 on the night Beirut
 *   moves from midnight to 01:00 reads as 01:00 at the new offset.
 *
 * The two candidates are the wall time read at the offset in force a day
 * before and a day after it; zones change offset at most once in a day,
 * so they bracket every case.
 */
export function ɵtmInstantOfWallTime(wall: ɵTmWallTime, timeZone: string): number {
  const utc = asUtc(wall);
  const early = utc - ɵtmZoneOffset(utc - DAY, timeZone) * MINUTE;
  const late = utc - ɵtmZoneOffset(utc + DAY, timeZone) * MINUTE;
  const valid = [early, late].filter((at) => shows(at, wall, timeZone));
  if (valid.length > 0) {
    return Math.min(...valid);
  }
  // A gap: the change lies between the two readings (the later offset is
  // the larger one, so its reading is the earlier instant). Search the
  // minute where the offset changes — the first instant on the far side.
  let before = Math.min(early, late);
  let after = Math.max(early, late);
  const offsetBefore = ɵtmZoneOffset(before, timeZone);
  while (after - before > MINUTE) {
    const middle = before + Math.floor((after - before) / 2 / MINUTE) * MINUTE;
    if (ɵtmZoneOffset(middle, timeZone) === offsetBefore) {
      before = middle;
    } else {
      after = middle;
    }
  }
  return after;
}

/**
 * Writes an instant as `yyyy-MM-ddTHH:mm:ss±HH:mm`: the wall time in the
 * zone with the zone's offset at the instant (`+00:00` for UTC, never `Z`),
 * and no fraction.
 */
export function ɵtmFormatOffsetInstant(at: number, timeZone: string): string {
  const offset = ɵtmZoneOffset(at, timeZone);
  const local = new Date(Math.floor(at / 1000) * 1000 + offset * MINUTE);
  const sign = offset < 0 ? '-' : '+';
  const magnitude = Math.abs(offset);
  return (
    `${pad(local.getUTCFullYear(), 4)}-${pad(local.getUTCMonth() + 1)}-${pad(local.getUTCDate())}` +
    `T${pad(local.getUTCHours())}:${pad(local.getUTCMinutes())}:${pad(local.getUTCSeconds())}` +
    `${sign}${pad(Math.floor(magnitude / 60))}:${pad(magnitude % 60)}`
  );
}

/**
 * The epoch milliseconds of an instant on the wire, or `null` for an empty
 * value or text that is not an instant.
 */
export function ɵtmReadInstant(instant: string | null | undefined): number | null {
  return instant === null || instant === undefined || instant === ''
    ? null
    : ɵtmParseInstant(instant);
}
