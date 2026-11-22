// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using System.Globalization;
using System.Text.Json;
using Tellma.Core.Localization;

namespace Tellma.Core.Tests.Localization
{
    /// <summary>
    ///     The numeric dates the client's day-first languages write are the server's short dates of
    ///     the same pairs.
    /// </summary>
    public class DayFirstClientVectorsTests
    {
        [Fact]
        public void Each_vector_is_the_short_date_of_its_negotiated_culture()
        {
            string path = Path.Combine(AppContext.BaseDirectory, "Localization", "day-first-vectors.json");
            using var file = JsonDocument.Parse(File.ReadAllText(path));
            int count = 0;
            foreach (JsonElement row in file.RootElement.GetProperty("rows").EnumerateArray())
            {
                string[] pair = row.GetProperty("locale").GetString()!.Split('-');
                CultureInfo culture = NegotiatedCultures.Get(pair[0], pair[1]);
                var date = DateOnly.ParseExact(row.GetProperty("date").GetString()!, "yyyy-MM-dd", CultureInfo.InvariantCulture);

                Assert.Equal(row.GetProperty("expected").GetString(), date.ToString("d", culture));
                count++;
            }

            Assert.Equal(12, count);
        }
    }
}
