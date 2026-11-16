// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using System.Globalization;
using Tellma.Core.Abstractions.Calendars;
using Tellma.Core.Abstractions.Data;

namespace Tellma.Core.Calendars
{
    /// <summary>
    ///     A row of <c>core.CalendarMonths</c>: one month of a calendar the query language cannot
    ///     compute, with its first Gregorian day and the day after its last.
    /// </summary>
    /// <remarks>
    ///     Not an entity: no surrogate key, no history, no audit columns, no table type; absent from
    ///     the query language except as its calendar map. Only the migrations write it.
    /// </remarks>
    internal sealed class CalendarMonthRow
    {
        /// <summary>The calendar's code, <c>varchar(2)</c>: <c>uq</c>.</summary>
        public string Calendar { get; set; } = null!;

        /// <summary>The year in the calendar.</summary>
        public int Year { get; set; }

        /// <summary>The month in the calendar, 1 to 12.</summary>
        public int Month { get; set; }

        /// <summary>The month's first Gregorian day.</summary>
        public DateOnly StartDate { get; set; }

        /// <summary>The Gregorian day after the month's last: the range is exclusive.</summary>
        public DateOnly EndDate { get; set; }
    }

    // The table is spec 0013 §8.2's; the query language seeks it by (Calendar, StartDate) (spec 0010 §F.11).
    /// <summary>
    ///     The model of <c>core.CalendarMonths</c>, seeded with every Umm al-Qura month from AH 1318
    ///     to 1500 as .NET's <see cref="UmAlQuraCalendar" /> computes them.
    /// </summary>
    internal sealed class CalendarMonthRowConfiguration : IEntityTypeConfiguration<CalendarMonthRow>
    {
        /// <summary>The table.</summary>
        public static readonly TableName Table = new("core", "CalendarMonths");

        /// <summary>The first Umm al-Qura year seeded: the first .NET supports.</summary>
        public const int FirstUmAlQuraYear = 1318;

        /// <summary>The last Umm al-Qura year seeded: the last .NET supports.</summary>
        public const int LastUmAlQuraYear = 1500;

        /// <inheritdoc />
        public void Configure(EntityTypeBuilder<CalendarMonthRow> builder)
        {
            ArgumentNullException.ThrowIfNull(builder);

            // --- The table: keyed by calendar, year and month; sought by calendar and first day ---
            _ = builder.ToTable(Table.Name, Table.Schema);
            _ = builder.HasKey(static row => new { row.Calendar, row.Year, row.Month });
            _ = builder.Property(static row => row.Calendar).HasColumnType("varchar(2)").HasMaxLength(2).IsUnicode(false);
            _ = builder.HasIndex(static row => new { row.Calendar, row.StartDate })
                .HasDatabaseName("UX_CalendarMonths_Calendar_StartDate")
                .IsUnique();

            // --- The Umm al-Qura months, computed when the migration is written ---
            _ = builder.HasData(UmAlQuraMonths());
        }

        /// <summary>Every Umm al-Qura month of the seeded years, in order: contiguous, and never overlapping.</summary>
        /// <returns>The rows.</returns>
        public static IReadOnlyList<CalendarMonthRow> UmAlQuraMonths()
        {
            UmAlQuraCalendar calendar = new();
            List<CalendarMonthRow> rows = [];
            for (int year = FirstUmAlQuraYear; year <= LastUmAlQuraYear; year++)
            {
                for (int month = 1; month <= 12; month++)
                {
                    // The last month ends a day past the calendar's range, so its end is counted, not converted.
                    var start = DateOnly.FromDateTime(calendar.ToDateTime(year, month, 1, 0, 0, 0, 0));
                    rows.Add(new CalendarMonthRow
                    {
                        Calendar = CalendarCodes.UmAlQura,
                        Year = year,
                        Month = month,
                        StartDate = start,
                        EndDate = start.AddDays(calendar.GetDaysInMonth(year, month)),
                    });
                }
            }

            return rows;
        }
    }
}
