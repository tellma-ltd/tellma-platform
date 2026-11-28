// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

namespace Tellma.Core.Excel
{
    /// <summary>
    ///     Excel's serial dates: Gregorian day numbers from the 1899-12-30 epoch of the 1900 date
    ///     system, the time of day as the fraction. A serial is the same in every calendar; only the
    ///     number format decides how Excel renders it.
    /// </summary>
    /// <remarks>
    ///     Serials below 61 are never written or read: the 1900 system counts a 29 February 1900
    ///     that never was (serial 60), so every serial before 1 March 1900 is a day off. A workbook
    ///     in the 1904 date system counts from 1904-01-01, which reading shifts by 1,462 days.
    /// </remarks>
    internal static class ExcelSerial
    {
        /// <summary>The first serial written or read: 1 March 1900.</summary>
        public const int FirstReliable = 61;

        /// <summary>The days a 1904-system serial is behind the 1900 system's.</summary>
        public const int Date1904Shift = 1_462;

        /// <summary>The milliseconds of a day.</summary>
        private const double MillisecondsPerDay = 86_400_000d;

        /// <summary>Day zero of the 1900 date system as Excel computes it.</summary>
        private static readonly DateOnly Epoch = new(1899, 12, 30);

        /// <summary>The first date written as a serial.</summary>
        private static readonly DateOnly FirstDate = new(1900, 3, 1);

        /// <summary>The last serial Excel renders: 9999-12-31.</summary>
        private static readonly int LastSerial = DateOnly.MaxValue.DayNumber - Epoch.DayNumber;

        /// <summary>A date's serial.</summary>
        /// <param name="date">The date.</param>
        /// <param name="serial">The serial.</param>
        /// <returns>False for a date before 1 March 1900, which has no reliable serial.</returns>
        public static bool TryFrom(DateOnly date, out int serial)
        {
            serial = date.DayNumber - Epoch.DayNumber;
            return date >= FirstDate;
        }

        /// <summary>A date and time's serial, the time as the fraction.</summary>
        /// <param name="value">The date and time; its kind is ignored.</param>
        /// <param name="serial">The serial.</param>
        /// <returns>False for a date before 1 March 1900.</returns>
        public static bool TryFrom(DateTime value, out double serial)
        {
            var date = DateOnly.FromDateTime(value);
            serial = date.DayNumber - Epoch.DayNumber + (value.TimeOfDay.Ticks / (double)TimeSpan.TicksPerDay);
            return date >= FirstDate;
        }

        /// <summary>A time of day's serial: the fraction of the day.</summary>
        /// <param name="time">The time.</param>
        /// <returns>The fraction, from 0 up to but not including 1.</returns>
        public static double From(TimeOnly time)
        {
            return time.Ticks / (double)TimeSpan.TicksPerDay;
        }

        /// <summary>Reads a serial as a date.</summary>
        /// <param name="serial">The serial, as the workbook stores it.</param>
        /// <param name="date1904">Whether the workbook uses the 1904 date system.</param>
        /// <param name="date">The date.</param>
        /// <returns>False for a fraction, a serial before 1 March 1900, or one beyond 9999-12-31.</returns>
        public static bool TryReadDate(double serial, bool date1904, out DateOnly date)
        {
            date = default;
            double shifted = Shift(serial, date1904);
            if (shifted != Math.Floor(shifted) || !InRange(shifted))
            {
                return false;
            }

            date = DateOnly.FromDayNumber(Epoch.DayNumber + (int)shifted);
            return true;
        }

        /// <summary>Reads a serial as a date and time, rounded to the millisecond.</summary>
        /// <param name="serial">The serial, as the workbook stores it.</param>
        /// <param name="date1904">Whether the workbook uses the 1904 date system.</param>
        /// <param name="value">The date and time, of unspecified kind.</param>
        /// <returns>False for a serial before 1 March 1900, or one beyond 9999-12-31.</returns>
        public static bool TryReadDateTime(double serial, bool date1904, out DateTime value)
        {
            value = default;
            double shifted = Shift(serial, date1904);
            if (!InRange(Math.Floor(shifted)))
            {
                return false;
            }

            // A double holds a serial to about a microsecond; Excel itself keeps milliseconds.
            double milliseconds = Math.Round(shifted * MillisecondsPerDay, MidpointRounding.AwayFromZero);
            var epoch = Epoch.ToDateTime(TimeOnly.MinValue);
            long ticks = (long)milliseconds * TimeSpan.TicksPerMillisecond;
            if (ticks > DateTime.MaxValue.Ticks - epoch.Ticks)
            {
                return false;
            }

            value = epoch.AddTicks(ticks);
            return true;
        }

        /// <summary>Reads a serial as a time of day, rounded to the millisecond.</summary>
        /// <param name="serial">The serial: a fraction of a day.</param>
        /// <param name="time">The time.</param>
        /// <returns>False for a serial that is not a fraction of one day.</returns>
        public static bool TryReadTime(double serial, out TimeOnly time)
        {
            time = default;
            if (serial is not (>= 0 and < 1))
            {
                return false;
            }

            double milliseconds = Math.Round(serial * MillisecondsPerDay, MidpointRounding.AwayFromZero);
            if (milliseconds >= MillisecondsPerDay)
            {
                return false;
            }

            time = new TimeOnly((long)milliseconds * TimeSpan.TicksPerMillisecond);
            return true;
        }

        /// <summary>Moves a 1904-system serial to the 1900 system.</summary>
        /// <param name="serial">The serial.</param>
        /// <param name="date1904">Whether it counts from 1904.</param>
        /// <returns>The 1900-system serial.</returns>
        private static double Shift(double serial, bool date1904)
        {
            return date1904 ? serial + Date1904Shift : serial;
        }

        /// <summary>Whether a whole serial lies within the dates read.</summary>
        /// <param name="day">The serial's whole days.</param>
        /// <returns>True from 1 March 1900 to 9999-12-31.</returns>
        private static bool InRange(double day)
        {
            return !double.IsNaN(day) && day >= FirstReliable && day <= LastSerial;
        }
    }
}
