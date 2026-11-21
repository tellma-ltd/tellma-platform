// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import {
  ɵtmFormatOffsetInstant,
  ɵtmInstantOfWallTime,
  ɵtmReadInstant,
  ɵtmWallTimeOf,
} from './tm-zone-wall-time';

/** The written instant a wall time names in a zone. */
function instantOf(date: string, time: string, zone: string): string {
  return ɵtmFormatOffsetInstant(ɵtmInstantOfWallTime({ date, time }, zone), zone);
}

describe('wall time in a zone', () => {
  it('reads an ordinary wall time at the zone offset', () => {
    expect(instantOf('2026-11-20', '14:00', 'Asia/Riyadh')).toBe('2026-11-20T14:00:00+03:00');
    expect(instantOf('2026-11-20', '14:00', 'UTC')).toBe('2026-11-20T14:00:00+00:00');
    expect(instantOf('2026-07-01', '09:15', 'America/New_York')).toBe('2026-07-01T09:15:00-04:00');
    expect(instantOf('2026-01-01', '09:15', 'Asia/Kolkata')).toBe('2026-01-01T09:15:00+05:30');
  });

  describe('Asia/Beirut, 2026', () => {
    // Beirut moves from 00:00 to 01:00 on Sunday 29 March, and back from
    // 00:00 to 23:00 on Saturday 24 October.
    it('reads a time inside the forward change as the change itself', () => {
      expect(instantOf('2026-03-29', '00:30', 'Asia/Beirut')).toBe('2026-03-29T01:00:00+03:00');
      expect(instantOf('2026-03-29', '00:00', 'Asia/Beirut')).toBe('2026-03-29T01:00:00+03:00');
      expect(instantOf('2026-03-29', '00:59', 'Asia/Beirut')).toBe('2026-03-29T01:00:00+03:00');
    });

    it('reads the times around the forward change as themselves', () => {
      expect(instantOf('2026-03-28', '23:59', 'Asia/Beirut')).toBe('2026-03-28T23:59:00+02:00');
      expect(instantOf('2026-03-29', '01:00', 'Asia/Beirut')).toBe('2026-03-29T01:00:00+03:00');
    });

    it('reads a time inside the backward change as the earlier instant', () => {
      expect(instantOf('2026-10-24', '23:30', 'Asia/Beirut')).toBe('2026-10-24T23:30:00+03:00');
      expect(instantOf('2026-10-24', '23:00', 'Asia/Beirut')).toBe('2026-10-24T23:00:00+03:00');
    });

    it('reads the times around the backward change as themselves', () => {
      expect(instantOf('2026-10-24', '22:59', 'Asia/Beirut')).toBe('2026-10-24T22:59:00+03:00');
      expect(instantOf('2026-10-25', '00:00', 'Asia/Beirut')).toBe('2026-10-25T00:00:00+02:00');
    });

    it('shows each instant of the repeated hour on the same clock', () => {
      const earlier = ɵtmReadInstant('2026-10-24T23:30:00+03:00')!;
      const later = ɵtmReadInstant('2026-10-24T23:30:00+02:00')!;
      expect(ɵtmWallTimeOf(earlier, 'Asia/Beirut')).toEqual({ date: '2026-10-24', time: '23:30' });
      expect(ɵtmWallTimeOf(later, 'Asia/Beirut')).toEqual({ date: '2026-10-24', time: '23:30' });
    });
  });

  it('shows an instant to the minute, dropping seconds and fractions', () => {
    const at = ɵtmReadInstant('2026-11-20T11:00:59.9999999+00:00')!;
    expect(ɵtmWallTimeOf(at, 'UTC')).toEqual({ date: '2026-11-20', time: '11:00' });
    expect(ɵtmWallTimeOf(at, 'Asia/Riyadh')).toEqual({ date: '2026-11-20', time: '14:00' });
  });

  it('writes no fraction and keeps the seconds', () => {
    const at = ɵtmReadInstant('2026-11-20T11:00:07.5+00:00')!;
    expect(ɵtmFormatOffsetInstant(at, 'Asia/Riyadh')).toBe('2026-11-20T14:00:07+03:00');
  });

  it('reads empty and unreadable values as no instant', () => {
    expect(ɵtmReadInstant(null)).toBeNull();
    expect(ɵtmReadInstant('')).toBeNull();
    expect(ɵtmReadInstant('2026-11-20')).toBeNull();
  });
});
