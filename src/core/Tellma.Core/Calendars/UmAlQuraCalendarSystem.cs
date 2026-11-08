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
    ///         The two meet without a gap or a repeated day: the tabular calendar's 1317-12-29 is the
    ///         day before the table's 1318-01-01; its 1501-01-01 falls on the table's last day, so
    ///         Muharram 1501 begins the day after the table, with 29 days, and from Safar 1501 every
    ///         date is the tabular calendar's. <see cref="Calendar" /> follows the same rule. A date
    ///         before AH 1-01-01 (AD 622-07-18) has no rendering in this calendar and renders in the
    ///         ISO Gregorian form marked <c>gc</c>.
    ///     </para>
    /// </remarks>
    public sealed class UmAlQuraCalendarSystem : ICalendarSystem
    {
        /// <summary>The conversion and naming this system renders with.</summary>
        private static readonly Converter Rules = new();

        /// <inheritdoc />
        public string Code => CalendarCodes.UmAlQura;

        /// <inheritdoc />
        /// <remarks>
        ///     The framework's Umm al-Qura table extended by the tabular calendar exactly as
        ///     <see cref="Decompose" /> extends it, so arithmetic covers every date the system renders
        ///     and reads, from AD 622-07-18 on.
        /// </remarks>
        public Calendar Calendar { get; } = Calendar.ReadOnly(new UmAlQuraExtendedCalendar());

        /// <inheritdoc />
        public int MonthCount => 12;

        /// <inheritdoc />
        /// <remarks>AD 1900-04-30, AH 1318-01-01: the first day of the Umm al-Qura table.</remarks>
        public DateOnly MinSupported => UmAlQuraExtendedCalendar.TableStart;

        /// <inheritdoc />
        /// <remarks>AD 2077-11-16, AH 1500-12-30: the last day of the Umm al-Qura table.</remarks>
        public DateOnly MaxSupported => UmAlQuraExtendedCalendar.TableEnd;

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
            /// <inheritdoc />
            public override string Code => CalendarCodes.UmAlQura;

            /// <inheritdoc />
            public override int MonthCount => 12;

            /// <inheritdoc />
            public override bool TryDecompose(DateOnly date, out int year, out int month, out int day)
            {
                return UmAlQuraExtendedCalendar.TryDecompose(date, out year, out month, out day);
            }

            /// <inheritdoc />
            public override DateOnly? Compose(int year, int month, int day)
            {
                return UmAlQuraExtendedCalendar.Compose(year, month, day);
            }
        }
    }
}
