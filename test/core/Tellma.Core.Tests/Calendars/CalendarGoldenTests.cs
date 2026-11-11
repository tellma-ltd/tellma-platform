// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using System.Globalization;
using Tellma.Core.Abstractions.Calendars;
using Tellma.Core.Calendars;
using Tellma.Core.Localization;

namespace Tellma.Core.Tests.Calendars
{
    /// <summary>
    ///     Golden renderings of every calendar in every style, in English, Arabic and Amharic over
    ///     the cultures requests are negotiated to (<see cref="NegotiatedCultures" />): the order and
    ///     punctuation of each culture's patterns, its weekday names, the Gregorian month names it
    ///     carries and Core's month names for the other two calendars.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         A negotiated culture carries the platform's pinned date formats, so these strings
    ///         are the same on every host, whatever ICU release it runs.
    ///     </para>
    ///     <para>
    ///         The dates are a Sunday in Ramadan, an Ethiopian leap day (Pagume 6, 2015), an
    ///         Ethiopian New Year (Meskerem 1, 2017) and an Islamic New Year (Muharram 1, 1447).
    ///         Every string was checked by hand against the conversions and the cultures' patterns.
    ///     </para>
    /// </remarks>
    public class CalendarGoldenTests
    {
        private static readonly Dictionary<string, ICalendarSystem> Systems = new()
        {
            [CalendarCodes.Gregorian] = new GregorianCalendarSystem(),
            [CalendarCodes.UmAlQura] = new UmAlQuraCalendarSystem(),
            [CalendarCodes.Ethiopian] = new EthiopianCalendarSystem(),
        };

        [Theory]
        [InlineData("EnGb", "gc", "2026-03-15", DateStyle.Short, "15/03/2026")]
        [InlineData("EnGb", "gc", "2026-03-15", DateStyle.Medium, "15 Mar 2026")]
        [InlineData("EnGb", "gc", "2026-03-15", DateStyle.Long, "15 March 2026")]
        [InlineData("EnGb", "gc", "2026-03-15", DateStyle.Full, "Sunday, 15 March 2026")]
        [InlineData("EnGb", "gc", "2023-09-11", DateStyle.Short, "11/09/2023")]
        [InlineData("EnGb", "gc", "2023-09-11", DateStyle.Medium, "11 Sept 2023")]
        [InlineData("EnGb", "gc", "2023-09-11", DateStyle.Long, "11 September 2023")]
        [InlineData("EnGb", "gc", "2023-09-11", DateStyle.Full, "Monday, 11 September 2023")]
        [InlineData("EnGb", "gc", "2024-09-11", DateStyle.Short, "11/09/2024")]
        [InlineData("EnGb", "gc", "2024-09-11", DateStyle.Medium, "11 Sept 2024")]
        [InlineData("EnGb", "gc", "2024-09-11", DateStyle.Long, "11 September 2024")]
        [InlineData("EnGb", "gc", "2024-09-11", DateStyle.Full, "Wednesday, 11 September 2024")]
        [InlineData("EnGb", "gc", "2025-06-26", DateStyle.Short, "26/06/2025")]
        [InlineData("EnGb", "gc", "2025-06-26", DateStyle.Medium, "26 Jun 2025")]
        [InlineData("EnGb", "gc", "2025-06-26", DateStyle.Long, "26 June 2025")]
        [InlineData("EnGb", "gc", "2025-06-26", DateStyle.Full, "Thursday, 26 June 2025")]
        [InlineData("EnGb", "uq", "2026-03-15", DateStyle.Short, "26/09/1447")]
        [InlineData("EnGb", "uq", "2026-03-15", DateStyle.Medium, "26 Ramadan 1447")]
        [InlineData("EnGb", "uq", "2026-03-15", DateStyle.Long, "26 Ramadan 1447")]
        [InlineData("EnGb", "uq", "2026-03-15", DateStyle.Full, "Sunday, 26 Ramadan 1447")]
        [InlineData("EnGb", "uq", "2023-09-11", DateStyle.Short, "26/02/1445")]
        [InlineData("EnGb", "uq", "2023-09-11", DateStyle.Medium, "26 Safar 1445")]
        [InlineData("EnGb", "uq", "2023-09-11", DateStyle.Long, "26 Safar 1445")]
        [InlineData("EnGb", "uq", "2023-09-11", DateStyle.Full, "Monday, 26 Safar 1445")]
        [InlineData("EnGb", "uq", "2024-09-11", DateStyle.Short, "08/03/1446")]
        [InlineData("EnGb", "uq", "2024-09-11", DateStyle.Medium, "8 Rabiʻ I 1446")]
        [InlineData("EnGb", "uq", "2024-09-11", DateStyle.Long, "8 Rabiʻ I 1446")]
        [InlineData("EnGb", "uq", "2024-09-11", DateStyle.Full, "Wednesday, 8 Rabiʻ I 1446")]
        [InlineData("EnGb", "uq", "2025-06-26", DateStyle.Short, "01/01/1447")]
        [InlineData("EnGb", "uq", "2025-06-26", DateStyle.Medium, "1 Muharram 1447")]
        [InlineData("EnGb", "uq", "2025-06-26", DateStyle.Long, "1 Muharram 1447")]
        [InlineData("EnGb", "uq", "2025-06-26", DateStyle.Full, "Thursday, 1 Muharram 1447")]
        [InlineData("EnGb", "et", "2026-03-15", DateStyle.Short, "06/07/2018")]
        [InlineData("EnGb", "et", "2026-03-15", DateStyle.Medium, "6 Megabit 2018")]
        [InlineData("EnGb", "et", "2026-03-15", DateStyle.Long, "6 Megabit 2018")]
        [InlineData("EnGb", "et", "2026-03-15", DateStyle.Full, "Sunday, 6 Megabit 2018")]
        [InlineData("EnGb", "et", "2023-09-11", DateStyle.Short, "06/13/2015")]
        [InlineData("EnGb", "et", "2023-09-11", DateStyle.Medium, "6 Pagumen 2015")]
        [InlineData("EnGb", "et", "2023-09-11", DateStyle.Long, "6 Pagumen 2015")]
        [InlineData("EnGb", "et", "2023-09-11", DateStyle.Full, "Monday, 6 Pagumen 2015")]
        [InlineData("EnGb", "et", "2024-09-11", DateStyle.Short, "01/01/2017")]
        [InlineData("EnGb", "et", "2024-09-11", DateStyle.Medium, "1 Meskerem 2017")]
        [InlineData("EnGb", "et", "2024-09-11", DateStyle.Long, "1 Meskerem 2017")]
        [InlineData("EnGb", "et", "2024-09-11", DateStyle.Full, "Wednesday, 1 Meskerem 2017")]
        [InlineData("EnGb", "et", "2025-06-26", DateStyle.Short, "19/10/2017")]
        [InlineData("EnGb", "et", "2025-06-26", DateStyle.Medium, "19 Sene 2017")]
        [InlineData("EnGb", "et", "2025-06-26", DateStyle.Long, "19 Sene 2017")]
        [InlineData("EnGb", "et", "2025-06-26", DateStyle.Full, "Thursday, 19 Sene 2017")]
        [InlineData("ArSa", "gc", "2026-03-15", DateStyle.Short, "15/3/2026")]
        [InlineData("ArSa", "gc", "2026-03-15", DateStyle.Medium, "15 مارس 2026")]
        [InlineData("ArSa", "gc", "2026-03-15", DateStyle.Long, "15 مارس 2026")]
        [InlineData("ArSa", "gc", "2026-03-15", DateStyle.Full, "الأحد، 15 مارس 2026")]
        [InlineData("ArSa", "gc", "2023-09-11", DateStyle.Short, "11/9/2023")]
        [InlineData("ArSa", "gc", "2023-09-11", DateStyle.Medium, "11 سبتمبر 2023")]
        [InlineData("ArSa", "gc", "2023-09-11", DateStyle.Long, "11 سبتمبر 2023")]
        [InlineData("ArSa", "gc", "2023-09-11", DateStyle.Full, "الاثنين، 11 سبتمبر 2023")]
        [InlineData("ArSa", "gc", "2024-09-11", DateStyle.Short, "11/9/2024")]
        [InlineData("ArSa", "gc", "2024-09-11", DateStyle.Medium, "11 سبتمبر 2024")]
        [InlineData("ArSa", "gc", "2024-09-11", DateStyle.Long, "11 سبتمبر 2024")]
        [InlineData("ArSa", "gc", "2024-09-11", DateStyle.Full, "الأربعاء، 11 سبتمبر 2024")]
        [InlineData("ArSa", "gc", "2025-06-26", DateStyle.Short, "26/6/2025")]
        [InlineData("ArSa", "gc", "2025-06-26", DateStyle.Medium, "26 يونيو 2025")]
        [InlineData("ArSa", "gc", "2025-06-26", DateStyle.Long, "26 يونيو 2025")]
        [InlineData("ArSa", "gc", "2025-06-26", DateStyle.Full, "الخميس، 26 يونيو 2025")]
        [InlineData("ArSa", "uq", "2026-03-15", DateStyle.Short, "26/9/1447")]
        [InlineData("ArSa", "uq", "2026-03-15", DateStyle.Medium, "26 رمضان 1447")]
        [InlineData("ArSa", "uq", "2026-03-15", DateStyle.Long, "26 رمضان 1447")]
        [InlineData("ArSa", "uq", "2026-03-15", DateStyle.Full, "الأحد، 26 رمضان 1447")]
        [InlineData("ArSa", "uq", "2023-09-11", DateStyle.Short, "26/2/1445")]
        [InlineData("ArSa", "uq", "2023-09-11", DateStyle.Medium, "26 صفر 1445")]
        [InlineData("ArSa", "uq", "2023-09-11", DateStyle.Long, "26 صفر 1445")]
        [InlineData("ArSa", "uq", "2023-09-11", DateStyle.Full, "الاثنين، 26 صفر 1445")]
        [InlineData("ArSa", "uq", "2024-09-11", DateStyle.Short, "8/3/1446")]
        [InlineData("ArSa", "uq", "2024-09-11", DateStyle.Medium, "8 ربيع الأول 1446")]
        [InlineData("ArSa", "uq", "2024-09-11", DateStyle.Long, "8 ربيع الأول 1446")]
        [InlineData("ArSa", "uq", "2024-09-11", DateStyle.Full, "الأربعاء، 8 ربيع الأول 1446")]
        [InlineData("ArSa", "uq", "2025-06-26", DateStyle.Short, "1/1/1447")]
        [InlineData("ArSa", "uq", "2025-06-26", DateStyle.Medium, "1 محرم 1447")]
        [InlineData("ArSa", "uq", "2025-06-26", DateStyle.Long, "1 محرم 1447")]
        [InlineData("ArSa", "uq", "2025-06-26", DateStyle.Full, "الخميس، 1 محرم 1447")]
        [InlineData("ArSa", "et", "2026-03-15", DateStyle.Short, "6/7/2018")]
        [InlineData("ArSa", "et", "2026-03-15", DateStyle.Medium, "6 مجابيت 2018")]
        [InlineData("ArSa", "et", "2026-03-15", DateStyle.Long, "6 مجابيت 2018")]
        [InlineData("ArSa", "et", "2026-03-15", DateStyle.Full, "الأحد، 6 مجابيت 2018")]
        [InlineData("ArSa", "et", "2023-09-11", DateStyle.Short, "6/13/2015")]
        [InlineData("ArSa", "et", "2023-09-11", DateStyle.Medium, "6 باجمن 2015")]
        [InlineData("ArSa", "et", "2023-09-11", DateStyle.Long, "6 باجمن 2015")]
        [InlineData("ArSa", "et", "2023-09-11", DateStyle.Full, "الاثنين، 6 باجمن 2015")]
        [InlineData("ArSa", "et", "2024-09-11", DateStyle.Short, "1/1/2017")]
        [InlineData("ArSa", "et", "2024-09-11", DateStyle.Medium, "1 مسكريم 2017")]
        [InlineData("ArSa", "et", "2024-09-11", DateStyle.Long, "1 مسكريم 2017")]
        [InlineData("ArSa", "et", "2024-09-11", DateStyle.Full, "الأربعاء، 1 مسكريم 2017")]
        [InlineData("ArSa", "et", "2025-06-26", DateStyle.Short, "19/10/2017")]
        [InlineData("ArSa", "et", "2025-06-26", DateStyle.Medium, "19 سين 2017")]
        [InlineData("ArSa", "et", "2025-06-26", DateStyle.Long, "19 سين 2017")]
        [InlineData("ArSa", "et", "2025-06-26", DateStyle.Full, "الخميس، 19 سين 2017")]
        [InlineData("AmEt", "gc", "2026-03-15", DateStyle.Short, "15/03/2026")]
        [InlineData("AmEt", "gc", "2026-03-15", DateStyle.Medium, "15 ማርች 2026")]
        [InlineData("AmEt", "gc", "2026-03-15", DateStyle.Long, "15 ማርች 2026")]
        [InlineData("AmEt", "gc", "2026-03-15", DateStyle.Full, "እሑድ 15 ማርች 2026")]
        [InlineData("AmEt", "gc", "2023-09-11", DateStyle.Short, "11/09/2023")]
        [InlineData("AmEt", "gc", "2023-09-11", DateStyle.Medium, "11 ሴፕቴ 2023")]
        [InlineData("AmEt", "gc", "2023-09-11", DateStyle.Long, "11 ሴፕቴምበር 2023")]
        [InlineData("AmEt", "gc", "2023-09-11", DateStyle.Full, "ሰኞ 11 ሴፕቴምበር 2023")]
        [InlineData("AmEt", "gc", "2024-09-11", DateStyle.Short, "11/09/2024")]
        [InlineData("AmEt", "gc", "2024-09-11", DateStyle.Medium, "11 ሴፕቴ 2024")]
        [InlineData("AmEt", "gc", "2024-09-11", DateStyle.Long, "11 ሴፕቴምበር 2024")]
        [InlineData("AmEt", "gc", "2024-09-11", DateStyle.Full, "ረቡዕ 11 ሴፕቴምበር 2024")]
        [InlineData("AmEt", "gc", "2025-06-26", DateStyle.Short, "26/06/2025")]
        [InlineData("AmEt", "gc", "2025-06-26", DateStyle.Medium, "26 ጁን 2025")]
        [InlineData("AmEt", "gc", "2025-06-26", DateStyle.Long, "26 ጁን 2025")]
        [InlineData("AmEt", "gc", "2025-06-26", DateStyle.Full, "ሐሙስ 26 ጁን 2025")]
        [InlineData("AmEt", "uq", "2026-03-15", DateStyle.Short, "26/09/1447")]
        [InlineData("AmEt", "uq", "2026-03-15", DateStyle.Medium, "26 ረመዳን 1447")]
        [InlineData("AmEt", "uq", "2026-03-15", DateStyle.Long, "26 ረመዳን 1447")]
        [InlineData("AmEt", "uq", "2026-03-15", DateStyle.Full, "እሑድ 26 ረመዳን 1447")]
        [InlineData("AmEt", "uq", "2023-09-11", DateStyle.Short, "26/02/1445")]
        [InlineData("AmEt", "uq", "2023-09-11", DateStyle.Medium, "26 ሳፈር 1445")]
        [InlineData("AmEt", "uq", "2023-09-11", DateStyle.Long, "26 ሳፈር 1445")]
        [InlineData("AmEt", "uq", "2023-09-11", DateStyle.Full, "ሰኞ 26 ሳፈር 1445")]
        [InlineData("AmEt", "uq", "2024-09-11", DateStyle.Short, "08/03/1446")]
        [InlineData("AmEt", "uq", "2024-09-11", DateStyle.Medium, "8 ረቢዑል አወል 1446")]
        [InlineData("AmEt", "uq", "2024-09-11", DateStyle.Long, "8 ረቢዑል አወል 1446")]
        [InlineData("AmEt", "uq", "2024-09-11", DateStyle.Full, "ረቡዕ 8 ረቢዑል አወል 1446")]
        [InlineData("AmEt", "uq", "2025-06-26", DateStyle.Short, "01/01/1447")]
        [InlineData("AmEt", "uq", "2025-06-26", DateStyle.Medium, "1 ሙሀረም 1447")]
        [InlineData("AmEt", "uq", "2025-06-26", DateStyle.Long, "1 ሙሀረም 1447")]
        [InlineData("AmEt", "uq", "2025-06-26", DateStyle.Full, "ሐሙስ 1 ሙሀረም 1447")]
        [InlineData("AmEt", "et", "2026-03-15", DateStyle.Short, "06/07/2018")]
        [InlineData("AmEt", "et", "2026-03-15", DateStyle.Medium, "6 መጋቢት 2018")]
        [InlineData("AmEt", "et", "2026-03-15", DateStyle.Long, "6 መጋቢት 2018")]
        [InlineData("AmEt", "et", "2026-03-15", DateStyle.Full, "እሑድ 6 መጋቢት 2018")]
        [InlineData("AmEt", "et", "2023-09-11", DateStyle.Short, "06/13/2015")]
        [InlineData("AmEt", "et", "2023-09-11", DateStyle.Medium, "6 ጳጉሜን 2015")]
        [InlineData("AmEt", "et", "2023-09-11", DateStyle.Long, "6 ጳጉሜን 2015")]
        [InlineData("AmEt", "et", "2023-09-11", DateStyle.Full, "ሰኞ 6 ጳጉሜን 2015")]
        [InlineData("AmEt", "et", "2024-09-11", DateStyle.Short, "01/01/2017")]
        [InlineData("AmEt", "et", "2024-09-11", DateStyle.Medium, "1 መስከረም 2017")]
        [InlineData("AmEt", "et", "2024-09-11", DateStyle.Long, "1 መስከረም 2017")]
        [InlineData("AmEt", "et", "2024-09-11", DateStyle.Full, "ረቡዕ 1 መስከረም 2017")]
        [InlineData("AmEt", "et", "2025-06-26", DateStyle.Short, "19/10/2017")]
        [InlineData("AmEt", "et", "2025-06-26", DateStyle.Medium, "19 ሰኔ 2017")]
        [InlineData("AmEt", "et", "2025-06-26", DateStyle.Long, "19 ሰኔ 2017")]
        [InlineData("AmEt", "et", "2025-06-26", DateStyle.Full, "ሐሙስ 19 ሰኔ 2017")]
        public void A_date_renders_as_approved(string culture, string calendar, string date, DateStyle style, string expected)
        {
            string actual = Systems[calendar].Format(DateOnly.ParseExact(date, "yyyy-MM-dd", CultureInfo.InvariantCulture), Culture(culture), style);

            Assert.Equal(expected, actual);
        }

        [Theory]
        [InlineData("EnGb", "gc", "16 Mar 2026 01:30", "16/03/2026")]
        [InlineData("EnGb", "uq", "27 Ramadan 1447 01:30", "27/09/1447")]
        [InlineData("EnGb", "et", "7 Megabit 2018 01:30", "07/07/2018")]
        [InlineData("ArSa", "gc", "16 مارس 2026 1:30 ص", "16/3/2026")]
        [InlineData("ArSa", "uq", "27 رمضان 1447 1:30 ص", "27/9/1447")]
        [InlineData("ArSa", "et", "7 مجابيت 2018 1:30 ص", "7/7/2018")]
        [InlineData("AmEt", "gc", "16 ማርች 2026 1:30 ጥዋት", "16/03/2026")]
        [InlineData("AmEt", "uq", "27 ረመዳን 1447 1:30 ጥዋት", "27/09/1447")]
        [InlineData("AmEt", "et", "7 መጋቢት 2018 1:30 ጥዋት", "07/07/2018")]
        public void An_instant_renders_on_the_zones_wall_clock_as_approved(string culture, string calendar, string withTime, string dateOnly)
        {
            // 22:30 UTC is 01:30 the next day in Riyadh, so the date itself moves with the zone.
            DateTimeOffset instant = new(2026, 3, 15, 22, 30, 0, TimeSpan.Zero);
            var riyadh = TimeZoneInfo.FindSystemTimeZoneById("Asia/Riyadh");
            ICalendarSystem system = Systems[calendar];

            Assert.Equal(withTime, system.Format(instant, riyadh, Culture(culture), DateStyle.Medium));
            Assert.Equal(dateOnly, system.Format(instant, riyadh, Culture(culture), DateStyle.Short, includeTime: false));
        }

        /// <summary>The pinned culture a row names.</summary>
        /// <param name="name">The row's culture key.</param>
        /// <returns>The culture.</returns>
        private static CultureInfo Culture(string name)
        {
            return name switch
            {
                "EnGb" => NegotiatedCultures.Get("en", "GB"),
                "ArSa" => NegotiatedCultures.Get("ar", "SA"),
                "AmEt" => NegotiatedCultures.Get("am", "ET"),
                _ => throw new ArgumentOutOfRangeException(nameof(name), name, "No such culture."),
            };
        }
    }
}
