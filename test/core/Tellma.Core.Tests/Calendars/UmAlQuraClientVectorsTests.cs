// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using System.Globalization;
using System.Text.Json;
using Tellma.Core.Calendars;

namespace Tellma.Core.Tests.Calendars
{
    /// <summary>
    ///     The Umm al-Qura vectors the client's calendar is checked against are the server calendar's
    ///     dates: every month start and length they list, and every single day.
    /// </summary>
    public class UmAlQuraClientVectorsTests
    {
        [Fact]
        public void Every_month_starts_and_ends_where_the_server_calendar_puts_it()
        {
            using JsonDocument vectors = Load();
            int count = 0;
            foreach (JsonElement row in vectors.RootElement.GetProperty("months").EnumerateArray())
            {
                int year = row[0].GetInt32();
                int month = row[1].GetInt32();
                DateOnly start = ParseIso(row[2].GetString()!);
                int length = row[3].GetInt32();

                Assert.Equal(start, UmAlQuraExtendedCalendar.Compose(year, month, 1));
                Assert.Equal(length, UmAlQuraExtendedCalendar.DaysIn(year, month));
                count++;
            }

            Assert.True(count > 2196);
        }

        [Fact]
        public void The_vectors_list_every_month_of_the_table_and_the_tabular_years_beside_it()
        {
            using JsonDocument vectors = Load();
            HashSet<(int Year, int Month)> listed = [.. vectors.RootElement.GetProperty("months").EnumerateArray()
                .Select(static row => (row[0].GetInt32(), row[1].GetInt32()))];

            DateOnly first = UmAlQuraExtendedCalendar.TableStart;
            DateOnly last = UmAlQuraExtendedCalendar.TableEnd;
            Assert.True(UmAlQuraExtendedCalendar.TryDecompose(first, out int firstYear, out _, out _));
            Assert.True(UmAlQuraExtendedCalendar.TryDecompose(last, out int lastYear, out _, out _));
            for (int year = firstYear - 10; year <= lastYear + 10; year++)
            {
                for (int month = 1; month <= 12; month++)
                {
                    Assert.Contains((year, month), listed);
                }
            }
        }

        [Fact]
        public void Every_listed_day_decomposes_as_the_server_calendar_decomposes_it()
        {
            using JsonDocument vectors = Load();
            foreach (JsonElement row in vectors.RootElement.GetProperty("days").EnumerateArray())
            {
                DateOnly date = ParseIso(row[0].GetString()!);
                bool converts = UmAlQuraExtendedCalendar.TryDecompose(date, out int year, out int month, out int day);
                if (row[1].ValueKind == JsonValueKind.Null)
                {
                    Assert.False(converts, $"{date:yyyy-MM-dd}");
                }
                else
                {
                    Assert.True(converts, $"{date:yyyy-MM-dd}");
                    Assert.Equal((row[1][0].GetInt32(), row[1][1].GetInt32(), row[1][2].GetInt32()), (year, month, day));
                }
            }
        }

        /// <summary>Parses an ISO 8601 date.</summary>
        /// <param name="text">The text.</param>
        /// <returns>The date.</returns>
        private static DateOnly ParseIso(string text)
        {
            return DateOnly.ParseExact(text, "yyyy-MM-dd", CultureInfo.InvariantCulture);
        }

        /// <summary>Reads the shared vector file copied beside the suite.</summary>
        /// <returns>The parsed file.</returns>
        private static JsonDocument Load()
        {
            string path = Path.Combine(AppContext.BaseDirectory, "Calendars", "umalqura-vectors.json");
            return JsonDocument.Parse(File.ReadAllText(path));
        }
    }
}
