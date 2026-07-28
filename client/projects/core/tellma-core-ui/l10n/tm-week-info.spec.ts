// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import { tmFirstDayOfWeek } from './tm-week-info';

describe('tmFirstDayOfWeek', () => {
  it('resolves the conventional starts (ISO numbering, 1 = Monday)', () => {
    expect(tmFirstDayOfWeek('en-US')).toBe(7); // Sunday
    expect(tmFirstDayOfWeek('de-DE')).toBe(1); // Monday
    // Saudi Arabia's week starts Sunday in current CLDR (the 2013 weekend
    // reform); Egypt keeps the Saturday start.
    expect(tmFirstDayOfWeek('ar-SA')).toBe(7);
    expect(tmFirstDayOfWeek('ar-EG')).toBe(6);
    expect(tmFirstDayOfWeek('en-GB')).toBe(1); // Monday
  });

  it('falls back to the region table for bare language tags', () => {
    // 'ar' maximizes to ar-EG territory; either engine week info or the
    // table must land on a valid ISO day.
    const day = tmFirstDayOfWeek('ar');
    expect(day).toBeGreaterThanOrEqual(1);
    expect(day).toBeLessThanOrEqual(7);
  });

  it('an unparsable tag defaults to Monday', () => {
    expect(tmFirstDayOfWeek('not a locale !!')).toBe(1);
  });
});
