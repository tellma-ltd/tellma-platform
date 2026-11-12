// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using System.Collections.Frozen;
using System.Globalization;
using Tellma.Core.Queryex;
using Tellma.Queryex.Testing.Schema;

namespace Tellma.Queryex.Testing.Semantics
{
    /// <summary>
    ///     Every day of two centuries, one row each, for checking every calendar day by day.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         Far too many rows to write out as statements, so the server generates them from a
    ///         sequence of numbers and the rows in memory are generated from the same numbers by the
    ///         same rules. A row holds the day, the same day with a time of day, and a second day
    ///         some distance before or after it, for the differences.
    ///     </para>
    ///     <para>
    ///         The span runs past both ends of the Umm al-Qura month table, so the days it does not
    ///         cover are checked to answer with the absent value.
    ///     </para>
    /// </remarks>
    public static class CalendarDays
    {
        /// <summary>The table the days are kept in.</summary>
        public const string Source = "[qxcal].[Days]";

        /// <summary>The seconds after midnight of every row's time of day: 13:45:30.</summary>
        private const int TimeOfDay = 49530;

        /// <summary>The most days a row's second day lies before or after its first.</summary>
        private const int Reach = 380;

        /// <summary>The number the spread of second days cycles through, prime so it visits every distance.</summary>
        private const int Spread = 761;

        /// <summary>The first day.</summary>
        public static DateOnly First { get; } = new(1900, 1, 1);

        /// <summary>The last day.</summary>
        public static DateOnly Last { get; } = new(2100, 12, 31);

        /// <summary>The schema the days are queried through: one entity, and the Umm al-Qura month table.</summary>
        public static QueryexSchema Schema { get; } = BuildSchema();

        /// <summary>The entity a row is.</summary>
        public static EntityDescriptor Day { get; } = Schema.FindEntity("Day")!;

        /// <summary>Every row, in key order.</summary>
        public static IReadOnlyList<LedgerRow> Rows => LazyRows.Value;

        /// <summary>The rows, built on first use.</summary>
        private static readonly Lazy<IReadOnlyList<LedgerRow>> LazyRows = new(BuildRows);

        /// <summary>The number of days.</summary>
        private static int Count => Last.DayNumber - First.DayNumber + 1;

        /// <summary>The statements that create the table and fill it, replacing any earlier copy.</summary>
        /// <returns>The script, one batch.</returns>
        public static string Create()
        {
            string first = First.ToString("yyyyMMdd", CultureInfo.InvariantCulture);
            string count = Count.ToString(CultureInfo.InvariantCulture);
            string reach = Reach.ToString(CultureInfo.InvariantCulture);
            string spread = Spread.ToString(CultureInfo.InvariantCulture);
            string time = TimeOfDay.ToString(CultureInfo.InvariantCulture);

            // The day numbers come from a cross join of a catalogue view with itself, which holds
            // far more rows than are needed; the same expressions generate the rows in memory.
            return "IF SCHEMA_ID('qxcal') IS NULL EXEC('CREATE SCHEMA [qxcal]');\n"
                + "DROP TABLE IF EXISTS " + Source + ";\n"
                + "CREATE TABLE " + Source + " (\n"
                + "    [DayId] int NOT NULL PRIMARY KEY,\n"
                + "    [DayDate] date NOT NULL,\n"
                + "    [DayMoment] datetime2(7) NOT NULL,\n"
                + "    [OtherDate] date NOT NULL\n"
                + ");\n"
                + "WITH [N] AS (SELECT TOP (" + count + ") ROW_NUMBER() OVER (ORDER BY (SELECT NULL)) - 1 AS [n]"
                + " FROM sys.all_objects AS [a] CROSS JOIN sys.all_objects AS [b])\n"
                + "INSERT INTO " + Source + " ([DayId], [DayDate], [DayMoment], [OtherDate])\n"
                + "SELECT [n], DATEADD(DAY, [n], CONVERT(date, '" + first + "')),"
                + " DATEADD(SECOND, " + time + ", CAST(DATEADD(DAY, [n], CONVERT(date, '" + first + "')) AS datetime2(7))),"
                + " DATEADD(DAY, [n] + ([n] * 37) % " + spread + " - " + reach + ", CONVERT(date, '" + first + "'))"
                + " FROM [N];\n";
        }

        /// <summary>Builds the schema.</summary>
        /// <returns>The schema.</returns>
        private static QueryexSchema BuildSchema()
        {
            QueryexSchemaBuilder builder = new();
            builder.CalendarMap(CalendarMonths.UmAlQura, LedgerFixture.CalendarMonthsSource);

            QueryexSchemaBuilder.EntityBuilder day = builder.Entity("Day", Source);
            day.Key("Id", QueryexType.QxNumeric, "DayId", QueryexStoreType.QxInt);
            day.Property("Date", QueryexType.QxDate, "DayDate", isNotNull: true, storeType: QueryexStoreType.QxDate);
            day.Property("Moment", QueryexType.QxDateTime, "DayMoment", isNotNull: true, storeType: QueryexStoreType.QxDateTime2(7));
            day.Property("Other", QueryexType.QxDate, "OtherDate", isNotNull: true, storeType: QueryexStoreType.QxDate);
            return builder.Build();
        }

        /// <summary>Builds the rows by the rules the server's statement follows.</summary>
        /// <returns>The rows.</returns>
        private static List<LedgerRow> BuildRows()
        {
            EntityDescriptor day = Day;
            var time = TimeOnly.FromTimeSpan(TimeSpan.FromSeconds(TimeOfDay));
            List<LedgerRow> rows = new(Count);

            for (int n = 0; n < Count; n++)
            {
                DateOnly date = First.AddDays(n);
                Dictionary<string, QxValue> values = new(StringComparer.OrdinalIgnoreCase)
                {
                    ["Id"] = QxValue.Number(n),
                    ["Date"] = QxValue.Date(date),
                    ["Moment"] = QxValue.Moment(new DateTime(date, time, DateTimeKind.Unspecified)),
                    ["Other"] = QxValue.Date(date.AddDays((n * 37 % Spread) - Reach)),
                };

                rows.Add(new LedgerRow(day, values.ToFrozenDictionary(StringComparer.OrdinalIgnoreCase)));
            }

            return rows;
        }
    }
}
