// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using System.Globalization;
using Tellma.Core.Abstractions.Calendars;

namespace Tellma.Core.Calendars
{
    /// <summary>
    ///     The Umm al-Qura calendar (<c>uq</c>), over the framework's <see cref="UmAlQuraCalendar" />:
    ///     exact from AD 1900-04-30 to 2077-11-16, which is AH 1318-01-01 to 1500-12-30. Month names
    ///     come from Core's resources.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         Umm al-Qura is a published table, not a rule, so no date outside the table converts
    ///         exactly. A date outside it decomposes, renders and reads back through the tabular
    ///         Islamic calendar (<see cref="HijriCalendar" /> with <see cref="HijriCalendar.HijriAdjustment" />
    ///         0, never a host's registry setting): an approximation that can differ from the
    ///         observed calendar by a day or two. <see cref="MinSupported" /> and
    ///         <see cref="MaxSupported" /> bound the exact range, and validators warn beyond it.
    ///     </para>
    ///     <para>
    ///         The two meet without a repeated day: the tabular calendar continues AH 1317-12-29 into
    ///         the table's 1318-01-01, and after the table's 1500-12-30 continues at 1501-01-02, so
    ///         no date renders as 1501-01-01. A date before AH 1-01-01 (AD 622-07-18) has no
    ///         rendering in this calendar and renders in the ISO Gregorian form.
    ///     </para>
    /// </remarks>
    public sealed class UmAlQuraCalendarSystem : ICalendarSystem
    {
        /// <summary>The conversion and naming this system renders with.</summary>
        private static readonly Converter Rules = new();

        /// <inheritdoc />
        public string Code => CalendarCodes.UmAlQura;

        /// <inheritdoc />
        public Calendar Calendar { get; } = Calendar.ReadOnly(new UmAlQuraCalendar());

        /// <inheritdoc />
        public int MonthCount => 12;

        /// <inheritdoc />
        /// <remarks>AD 1900-04-30, AH 1318-01-01: the first day of the Umm al-Qura table.</remarks>
        public DateOnly MinSupported => Converter.TableStart;

        /// <inheritdoc />
        /// <remarks>AD 2077-11-16, AH 1500-12-30: the last day of the Umm al-Qura table.</remarks>
        public DateOnly MaxSupported => Converter.TableEnd;

        /// <inheritdoc />
        public (int Year, int Month, int Day) Decompose(DateOnly date)
        {
            return Rules.Decompose(date);
        }

        /// <inheritdoc />
        public string Format(DateOnly date, CultureInfo culture, DateStyle style)
        {
            return CalendarFormatter.Format(Rules, date, culture, style);
        }

        /// <inheritdoc />
        public string Format(DateTimeOffset instant, TimeZoneInfo zone, CultureInfo culture, DateStyle style, bool includeTime = true)
        {
            return CalendarFormatter.Format(Rules, instant, zone, culture, style, includeTime);
        }

        /// <inheritdoc />
        public string MonthName(int month, CultureInfo culture, bool abbreviated)
        {
            return CalendarFormatter.MonthName(Rules, month, culture, abbreviated);
        }

        /// <inheritdoc />
        public DateOnly? TryParse(string text, CultureInfo culture)
        {
            return CalendarFormatter.TryParse(Rules, text, culture);
        }

        /// <summary>The Umm al-Qura table, with the tabular Islamic calendar outside it.</summary>
        private sealed class Converter : CalendarConverter
        {
            /// <summary>The exact table, used for arithmetic only.</summary>
            private static readonly UmAlQuraCalendar Table = new();

            /// <summary>
            ///     The tabular Islamic calendar beyond the table. Its adjustment is fixed at 0: the
            ///     framework's default reads a per-host setting on Windows.
            /// </summary>
            private static readonly Calendar Tabular = Calendar.ReadOnly(new HijriCalendar { HijriAdjustment = 0 });

            /// <summary>The first Gregorian day of the table.</summary>
            public static DateOnly TableStart { get; } = DateOnly.FromDateTime(Table.MinSupportedDateTime);

            /// <summary>The last Gregorian day of the table.</summary>
            public static DateOnly TableEnd { get; } = DateOnly.FromDateTime(Table.MaxSupportedDateTime);

            /// <summary>The first Gregorian day the tabular calendar converts.</summary>
            private static DateOnly TabularStart { get; } = DateOnly.FromDateTime(Tabular.MinSupportedDateTime);

            /// <summary>The first year of the table.</summary>
            private static int TableFirstYear { get; } = Table.GetYear(Table.MinSupportedDateTime);

            /// <summary>The last year of the table.</summary>
            private static int TableLastYear { get; } = Table.GetYear(Table.MaxSupportedDateTime);

            /// <inheritdoc />
            public override string Code => CalendarCodes.UmAlQura;

            /// <inheritdoc />
            public override int MonthCount => 12;

            /// <inheritdoc />
            public override bool TryDecompose(DateOnly date, out int year, out int month, out int day)
            {
                Calendar calendar = date >= TableStart && date <= TableEnd ? Table : Tabular;
                if (date < TabularStart)
                {
                    (year, month, day) = (0, 0, 0);
                    return false;
                }

                var time = date.ToDateTime(TimeOnly.MinValue);
                (year, month, day) = (calendar.GetYear(time), calendar.GetMonth(time), calendar.GetDayOfMonth(time));
                return true;
            }

            /// <inheritdoc />
            public override DateOnly? Compose(int year, int month, int day)
            {
                // A year of the table converts through it; every other year through the tabular
                // calendar. The caller's round trip refuses the tabular 1501-01-01, which falls on the
                // table's last day.
                Calendar calendar = year >= TableFirstYear && year <= TableLastYear ? Table : Tabular;
                if (year < 1 || month is < 1 or > 12 || day < 1)
                {
                    return null;
                }

                try
                {
                    return day <= calendar.GetDaysInMonth(year, month)
                        ? DateOnly.FromDateTime(calendar.ToDateTime(year, month, day, 0, 0, 0, 0))
                        : null;
                }
                catch (ArgumentOutOfRangeException)
                {
                    // Beyond the last year the tabular calendar converts (AH 9666).
                    return null;
                }
            }
        }
    }
}
