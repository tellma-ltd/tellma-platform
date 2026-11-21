// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using System.Globalization;
using Tellma.Core.Abstractions.Calendars;
using Tellma.Core.Abstractions.Excel;
using Tellma.Core.Calendars;
using Tellma.Core.Excel;
using Tellma.Core.Queryex;
using static Tellma.Core.Tests.Excel.ExcelTestKit;

namespace Tellma.Core.Tests.Excel
{
    /// <summary>
    ///     Number formats from the request context: numbers by scale and shape, dates as the culture's
    ///     short pattern behind the locale tag of its LCID and calendar, and the calendars Excel cannot
    ///     render, whose dates are text with the pattern the manifest records.
    /// </summary>
    public class NumberFormatTests
    {
        [Theory]
        [InlineData(0, false, "#,##0")]
        [InlineData(0, true, "0")]
        [InlineData(2, false, "#,##0.00")]
        [InlineData(4, true, "0.0000")]
        public void A_decimal_takes_its_format_from_its_scale_grouped_only_in_display(int scale, bool editable, string expected)
        {
            Assert.Equal(expected, NumberFormatBuilder.Decimal(scale, editable));
        }

        [Fact]
        public void English_dates_carry_the_us_lcid_and_no_calendar()
        {
            Assert.Equal("[$-0409]", NumberFormatBuilder.LocaleTag(EnUs, CalendarCodes.Gregorian));
            Assert.Equal("[$-0409]m/d/yyyy", NumberFormatBuilder.DateFormat(EnUs, CalendarCodes.Gregorian));
            Assert.Equal("[$-0409]m/d/yyyy h:mm:ss AM/PM", NumberFormatBuilder.DateTimeFormat(EnUs, CalendarCodes.Gregorian));
        }

        [Fact]
        public void Arabic_dates_carry_the_saudi_lcid_and_the_umm_al_qura_calendar_when_it_is_the_request_calendar()
        {
            // The culture's raw pattern carries right-to-left marks and an era; the format carries neither.
            Assert.Equal("[$-0401]d/m/yyyy", NumberFormatBuilder.DateFormat(ArSa, CalendarCodes.Gregorian));
            Assert.Equal("[$-170401]d/m/yyyy", NumberFormatBuilder.DateFormat(ArSa, CalendarCodes.UmAlQura));
            Assert.Equal("[$-170401]d/m/yyyy h:mm:ss AM/PM", NumberFormatBuilder.DateTimeFormat(ArSa, CalendarCodes.UmAlQura));
        }

        [Fact]
        public void An_umm_al_qura_date_is_still_its_gregorian_serial()
        {
            ColumnEncoding column = Encoding(Typed(typeof(DateOnly), QueryexType.QxDate), ExportKind.Display, Context(ArSa, UmAlQura));

            Assert.Equal(EncodedCell.OfNumber("46342"), CellCodec.Encode(new DateOnly(2026, 11, 16), column));
            Assert.Equal("[$-170401]d/m/yyyy", column.NumberFormat);
            Assert.Equal(ExcelDateEncoding.Serial, column.DateEncoding);
            Assert.Equal("d/m/yyyy", column.DatePattern);
        }

        [Fact]
        public void An_ethiopian_date_is_calendar_text_and_the_manifest_records_its_pattern()
        {
            ExcelContext context = Context(AmEt, Ethiopian);
            ColumnEncoding date = Encoding(Typed(typeof(DateOnly), QueryexType.QxDate), ExportKind.ForImport, context);
            ColumnEncoding dateTime = Encoding(Typed(typeof(DateTime), QueryexType.QxDateTime), ExportKind.ForImport, context);
            DateOnly value = new(2025, 6, 26);

            Assert.False(NumberFormatBuilder.HasWindowsCalendar(CalendarCodes.Ethiopian));
            Assert.Equal(EncodedCell.OfText(Ethiopian.Format(value, AmEt, DateStyle.Short)), CellCodec.Encode(value, date));
            Assert.Equal("@", date.NumberFormat);
            Assert.Equal(ExcelDateEncoding.Text, date.DateEncoding);
            Assert.Equal("dd/MM/yyyy", date.DatePattern);
            _ = Assert.Throws<ArgumentException>(() => NumberFormatBuilder.DateFormat(AmEt, CalendarCodes.Ethiopian));

            // An editable date and time in a text calendar is ISO 8601, exact whatever the calendar.
            Assert.Equal(EncodedCell.OfText("2025-06-26T14:30:00"), CellCodec.Encode(new DateTime(2025, 6, 26, 14, 30, 0), dateTime));
            Assert.Equal(CellCodec.IsoDateTimePattern, dateTime.DatePattern);
        }

        [Fact]
        public void Amharic_dates_in_the_gregorian_calendar_are_serials_behind_the_ethiopian_lcid()
        {
            Assert.Equal("[$-045E]dd/mm/yyyy", NumberFormatBuilder.DateFormat(AmEt, CalendarCodes.Gregorian));
        }

        [Fact]
        public void A_culture_without_an_lcid_omits_it_and_keeps_the_calendar()
        {
            CultureInfo custom = CultureWithoutLcid();

            Assert.Equal(string.Empty, NumberFormatBuilder.LocaleTag(custom, CalendarCodes.Gregorian));
            Assert.Equal("[$-170000]", NumberFormatBuilder.LocaleTag(custom, CalendarCodes.UmAlQura));
            Assert.Equal(string.Empty, NumberFormatBuilder.LocaleTag(CultureInfo.InvariantCulture, CalendarCodes.Gregorian));
        }

        [Theory]
        [InlineData("dd/MM/yyyy", "/", "dd/mm/yyyy")]
        [InlineData("yyyy-MM-dd", "-", "yyyy-mm-dd")]
        [InlineData("d.M.yyyy", ".", "d\".\"m\".\"yyyy")]
        [InlineData("dd MMM yyyy", "/", "dd mmm yyyy")]
        [InlineData("dddd, MMMM d, yyyy", "/", "dddd\",\" mmmm d\",\" yyyy")]
        [InlineData("yyyy'年'M'月'd'日'", "/", "yyyy\"年\"m\"月\"d\"日\"")]
        public void A_date_pattern_translates_token_by_token_with_literals_quoted(string pattern, string separator, string expected)
        {
            Assert.Equal(expected, NumberFormatBuilder.TranslateDate(DatePattern.Parse(pattern), separator));
        }

        [Theory]
        [InlineData("HH:mm:ss", "hh:mm:ss")]
        [InlineData("H:mm", "h:mm")]
        [InlineData("h:mm:ss tt", "h:mm:ss AM/PM")]
        [InlineData("hh:mm tt", "hh:mm AM/PM")]
        public void A_time_pattern_translates_to_hours_minutes_seconds_and_the_meridiem(string pattern, string expected)
        {
            Assert.Equal(expected, NumberFormatBuilder.TranslateTime(pattern));
        }

        [Theory]
        [InlineData("General", 0)]
        [InlineData("0", 1)]
        [InlineData("0.00", 2)]
        [InlineData("#,##0", 3)]
        [InlineData("#,##0.00", 4)]
        [InlineData("@", 49)]
        public void A_built_in_format_takes_its_id(string code, int id)
        {
            Assert.Equal(id, NumberFormatBuilder.BuiltInId(code));
        }

        [Fact]
        public void A_custom_format_has_no_built_in_id()
        {
            Assert.Null(NumberFormatBuilder.BuiltInId("#,##0.000"));
            Assert.Null(NumberFormatBuilder.BuiltInId("[$-0409]m/d/yyyy"));
        }

        /// <summary>A culture .NET has no LCID for, as a deployment's own culture data would be.</summary>
        /// <returns>The culture, its LCID the custom 0x1000.</returns>
        private static CultureInfo CultureWithoutLcid()
        {
            // Cultures without a Windows identifier report LOCALE_CUSTOM_UNSPECIFIED; a regional
            // variant .NET carries no LCID for is one, whatever the platform's culture data.
            var culture = CultureInfo.GetCultureInfo("en-150");
            Assert.Equal(0x1000, culture.LCID);
            return culture;
        }
    }
}
