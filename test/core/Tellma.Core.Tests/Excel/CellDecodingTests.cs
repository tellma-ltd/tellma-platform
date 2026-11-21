// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using DocumentFormat.OpenXml.Spreadsheet;
using System.Globalization;
using Tellma.Core.Abstractions.Excel;
using Tellma.Core.Excel;
using Tellma.Core.Queryex;
using Tellma.Testing.Entities;
using static Tellma.Core.Tests.Excel.ExcelTestKit;

namespace Tellma.Core.Tests.Excel
{
    /// <summary>
    ///     Decoding by the mapped type, never by the cell's format: what each type accepts, what it
    ///     refuses with the expected type and the cell's text, the 1900 and 1904 date systems, and
    ///     every kind of stored cell as the reader sees it. Round trips run each value through the
    ///     encoder and back.
    /// </summary>
    public class CellDecodingTests
    {
        [Theory]
        [InlineData(typeof(bool), true)]
        [InlineData(typeof(int), -42)]
        [InlineData(typeof(long), 1_234_567_890_123_456_789L)]
        [InlineData(typeof(double), 0.1d)]
        [InlineData(typeof(string), "  =SUM(A1) ")]
        public void A_value_round_trips_through_an_editable_cell(Type type, object value)
        {
            QueryexType qx = type == typeof(bool) ? QueryexType.QxBool : type == typeof(string) ? QueryexType.QxString : QueryexType.QxNumeric;
            ColumnEncoding column = Encoding(Typed(type, qx), ExportKind.ForImport);

            object? back = RoundTrip(value, column, new CellDecodeTarget(type) { PreserveWhitespace = true });

            Assert.Equal(value, back);
        }

        [Theory]
        [InlineData("123456789012345")]
        [InlineData("1234567890.12345")]
        [InlineData("1234567890123456")]
        [InlineData("1234567890.123456")]
        [InlineData("-99999999999999.99")]
        [InlineData("79228162514264337593543950335")]
        [InlineData("0.1")]
        public void A_decimal_round_trips_exactly_through_an_editable_cell_on_either_side_of_fifteen_digits(string text)
        {
            decimal value = decimal.Parse(text, CultureInfo.InvariantCulture);
            ColumnEncoding column = Encoding(Typed(typeof(decimal), QueryexType.QxNumeric), ExportKind.ForImport);

            Assert.Equal(value, RoundTrip(value, column, new CellDecodeTarget(typeof(decimal))));
        }

        [Fact]
        public void A_number_cell_reads_as_its_fifteen_significant_digits_so_binary_noise_never_reaches_a_decimal()
        {
            CellDecodeResult result = CellCodec.Decode(new ExcelRawCell(ExcelCellKind.Number, "0.30000000000000004"), new CellDecodeTarget(typeof(decimal)), Decoding());

            Assert.Equal(0.3m, result.Value);
        }

        [Fact]
        public void A_decimal_with_more_places_than_its_scale_is_refused()
        {
            CellDecodeTarget target = new(typeof(decimal)) { Scale = 2 };

            Assert.Equal(1.25m, Decode(Text("1.250"), target).Value);
            CellDecodeResult refused = Decode(Text("1.255"), target);

            Assert.False(refused.Success);
            Assert.Equal(ExpectedValues.Numeric, refused.Expected);
            Assert.Equal("1.255", refused.ErrorArguments()["value"]);
        }

        [Fact]
        public void Decimal_text_is_read_invariant_without_thousands_separators()
        {
            Assert.Equal(-1234.5m, Decode(Text(" -1234.50 "), new CellDecodeTarget(typeof(decimal))).Value);
            Assert.False(Decode(Text("1,234.5"), new CellDecodeTarget(typeof(decimal))).Success);
            Assert.False(Decode(Text("1234,5"), new CellDecodeTarget(typeof(decimal))).Success);
        }

        [Theory]
        [InlineData("TRUE", true)]
        [InlineData("false", false)]
        [InlineData("Yes", true)]
        [InlineData("NO", false)]
        [InlineData("1", true)]
        [InlineData("0", false)]
        [InlineData("نعم", true)]
        public void A_boolean_reads_the_invariant_words_and_the_localized_labels(string text, bool expected)
        {
            CellDecodeTarget target = new(typeof(bool)) { BooleanLabels = new Dictionary<string, bool> { ["نعم"] = true, ["لا"] = false } };

            Assert.Equal(expected, Decode(Text(text), target).Value);
        }

        [Fact]
        public void A_boolean_reads_a_boolean_cell_or_zero_and_one_and_refuses_anything_else()
        {
            CellDecodeTarget target = new(typeof(bool));

            Assert.Equal(true, Decode(new ExcelRawCell(ExcelCellKind.Boolean, "1"), target).Value);
            Assert.Equal(false, Decode(new ExcelRawCell(ExcelCellKind.Number, "0"), target).Value);
            Assert.Equal(ExpectedValues.Bool, Decode(new ExcelRawCell(ExcelCellKind.Number, "2"), target).Expected);
            Assert.False(Decode(Text("maybe"), target).Success);
        }

        [Fact]
        public void An_integer_reads_an_integral_number_or_text_and_refuses_a_fraction_or_an_overflow()
        {
            CellDecodeTarget target = new(typeof(int));

            Assert.Equal(12, Decode(new ExcelRawCell(ExcelCellKind.Number, "12"), target).Value);
            Assert.Equal(-7, Decode(Text(" -7 "), target).Value);
            Assert.Equal(ExpectedValues.Integer, Decode(new ExcelRawCell(ExcelCellKind.Number, "12.5"), target).Expected);
            Assert.False(Decode(new ExcelRawCell(ExcelCellKind.Number, "3000000000"), target).Success);
            Assert.False(Decode(new ExcelRawCell(ExcelCellKind.Number, "12345678901234567890"), new CellDecodeTarget(typeof(long))).Success);
            Assert.Equal((byte)200, Decode(Text("200"), new CellDecodeTarget(typeof(byte?))).Value);
        }

        [Fact]
        public void A_string_reads_any_cell_numbers_rendered_invariant_trimmed_unless_preserved()
        {
            CellDecodeTarget target = new(typeof(string));

            Assert.Equal("12", Decode(new ExcelRawCell(ExcelCellKind.Number, "12"), target).Value);
            Assert.Equal("0.1", Decode(new ExcelRawCell(ExcelCellKind.Number, "0.1"), target).Value);
            Assert.Equal("TRUE", Decode(new ExcelRawCell(ExcelCellKind.Boolean, "1"), target).Value);
            Assert.Equal("a b", Decode(Text("  a b "), target).Value);
            Assert.Null(Decode(Text("   "), target).Value);
            Assert.Equal("  a b ", Decode(Text("  a b "), target with { PreserveWhitespace = true }).Value);
        }

        [Fact]
        public void A_choice_reads_its_stored_value_ignoring_case_or_any_label()
        {
            CellDecodeTarget target = new(typeof(WidgetKind)) { ChoiceLabels = new Dictionary<string, string> { ["فاخر"] = "Fancy" } };

            Assert.Equal(WidgetKind.Fancy, Decode(Text("fancy"), target).Value);
            Assert.Equal(WidgetKind.Fancy, Decode(Text("فاخر"), target).Value);
            Assert.Equal(ExpectedValues.Choice, Decode(Text("Plainest"), target).Expected);
            Assert.Equal("en", Decode(Text("EN"), new CellDecodeTarget(typeof(string)) { Choices = ["en", "ar"] }).Value);
        }

        [Fact]
        public void A_date_reads_a_whole_serial_in_either_date_system()
        {
            CellDecodeTarget target = new(typeof(DateOnly));

            Assert.Equal(new DateOnly(2026, 11, 16), Decode(new ExcelRawCell(ExcelCellKind.Number, "46342"), target).Value);
            Assert.Equal(new DateOnly(2026, 11, 16), CellCodec.Decode(new ExcelRawCell(ExcelCellKind.Number, "44880"), target, Decoding(date1904: true)).Value);
            Assert.Equal(new DateOnly(1904, 1, 1), CellCodec.Decode(new ExcelRawCell(ExcelCellKind.Number, "0"), target, Decoding(date1904: true)).Value);
        }

        [Theory]
        [InlineData("60")]
        [InlineData("59")]
        [InlineData("1")]
        [InlineData("0")]
        [InlineData("-3")]
        [InlineData("46342.5")]
        [InlineData("2958466")]
        public void A_suspect_or_fractional_serial_is_refused_as_a_date(string serial)
        {
            CellDecodeResult result = Decode(new ExcelRawCell(ExcelCellKind.Number, serial), new CellDecodeTarget(typeof(DateOnly)));

            Assert.False(result.Success);
            Assert.Equal(ExpectedValues.Date, result.Expected);
            Assert.Equal(serial, result.Text);
        }

        [Fact]
        public void A_date_reads_iso_text_a_date_cell_and_the_request_calendars_short_pattern()
        {
            CellDecodeTarget target = new(typeof(DateOnly));

            Assert.Equal(new DateOnly(1890, 5, 4), Decode(Text("1890-05-04"), target).Value);
            Assert.Equal(new DateOnly(2026, 11, 16), Decode(new ExcelRawCell(ExcelCellKind.Date, "2026-11-16T00:00:00"), target).Value);
            Assert.Equal(new DateOnly(2026, 11, 16), Decode(Text("11/16/2026"), target).Value);
            Assert.False(Decode(new ExcelRawCell(ExcelCellKind.Date, "2026-11-16T10:00:00"), target).Success);
        }

        [Fact]
        public void A_text_date_reads_in_the_manifest_calendar_before_the_request_calendar()
        {
            DateOnly date = new(2025, 6, 26);
            string ethiopian = Ethiopian.Format(date, AmEt, Abstractions.Calendars.DateStyle.Short);
            CellDecodeTarget target = new(typeof(DateOnly)) { TextCalendar = Ethiopian, TextCulture = AmEt };

            Assert.Equal(date, Decode(Text(ethiopian), target).Value);
        }

        [Fact]
        public void A_date_round_trips_through_an_ethiopian_text_cell()
        {
            ExcelContext context = Context(AmEt, Ethiopian);
            ColumnEncoding column = Encoding(Typed(typeof(DateOnly), QueryexType.QxDate), ExportKind.ForImport, context);
            DateOnly date = new(2025, 6, 26);

            object? back = RoundTrip(date, column, new CellDecodeTarget(typeof(DateOnly)), Decoding(culture: AmEt, calendar: Ethiopian));

            Assert.Equal(date, back);
        }

        [Fact]
        public void A_date_and_time_reads_a_serial_rounded_to_the_millisecond_iso_text_or_a_calendar_date()
        {
            CellDecodeTarget target = new(typeof(DateTime));
            DateTime value = new(2026, 11, 16, 18, 45, 30, 125);

            Assert.Equal(value, RoundTrip(value, Encoding(Typed(typeof(DateTime), QueryexType.QxDateTime), ExportKind.ForImport), target));
            Assert.Equal(new DateTime(2026, 11, 16, 18, 0, 0), Decode(new ExcelRawCell(ExcelCellKind.Number, "46342.75"), target).Value);
            Assert.Equal(new DateTime(2026, 11, 16, 18, 0, 0), CellCodec.Decode(new ExcelRawCell(ExcelCellKind.Number, "44880.75"), target, Decoding(date1904: true)).Value);
            Assert.Equal(new DateTime(2026, 11, 16, 7, 5, 0), Decode(Text("2026-11-16T07:05"), target).Value);
            Assert.Equal(new DateTime(2026, 11, 16), Decode(Text("11/16/2026"), target).Value);
            Assert.False(Decode(new ExcelRawCell(ExcelCellKind.Number, "60.5"), target).Success);
        }

        [Theory]
        [InlineData("2026-11-16T15:00:00+03:00", 15, 3)]
        [InlineData("2026-11-16T15:00:00.5-05:30", 15, -5.5)]
        [InlineData("2026-11-16T15:00:00Z", 15, 0)]
        [InlineData("2026-11-16T15:00+01:00", 15, 1)]
        public void An_instant_reads_iso_8601_text_with_its_offset(string text, int hour, double offset)
        {
            var value = (DateTimeOffset)Decode(Text(text), new CellDecodeTarget(typeof(DateTimeOffset))).Value!;

            Assert.Equal(hour, value.Hour);
            Assert.Equal(TimeSpan.FromHours(offset), value.Offset);
        }

        [Fact]
        public void An_instant_round_trips_through_an_editable_cell_with_its_offset_and_ticks()
        {
            DateTimeOffset value = new DateTimeOffset(2026, 11, 16, 18, 0, 0, TimeSpan.FromHours(3)).AddTicks(1_234_567);
            ColumnEncoding column = Encoding(Typed(typeof(DateTimeOffset), QueryexType.QxDateTimeOffset), ExportKind.ForImport);

            var back = (DateTimeOffset)RoundTrip(value, column, new CellDecodeTarget(typeof(DateTimeOffset)))!;

            Assert.Equal(value, back);
            Assert.Equal(value.Offset, back.Offset);
        }

        [Fact]
        public void An_instant_without_an_offset_is_the_wall_clock_of_the_display_zone()
        {
            var riyadh = TimeZoneInfo.CreateCustomTimeZone("Test/Riyadh", TimeSpan.FromHours(3), "Riyadh", "Riyadh");
            CellDecodeContext context = Decoding(zone: riyadh);
            CellDecodeTarget target = new(typeof(DateTimeOffset));

            Assert.Equal(new DateTimeOffset(2026, 11, 16, 18, 0, 0, TimeSpan.FromHours(3)), CellCodec.Decode(new ExcelRawCell(ExcelCellKind.Number, "46342.75"), target, context).Value);
            Assert.Equal(new DateTimeOffset(2026, 11, 16, 7, 5, 0, TimeSpan.FromHours(3)), CellCodec.Decode(Text("2026-11-16 07:05"), target, context).Value);
        }

        [Fact]
        public void A_wall_clock_time_the_zone_skips_is_refused()
        {
            var rule = TimeZoneInfo.AdjustmentRule.CreateAdjustmentRule(
                new DateTime(2000, 1, 1),
                new DateTime(2099, 12, 31),
                TimeSpan.FromHours(1),
                TimeZoneInfo.TransitionTime.CreateFixedDateRule(new DateTime(1, 1, 1, 2, 0, 0), 3, 29),
                TimeZoneInfo.TransitionTime.CreateFixedDateRule(new DateTime(1, 1, 1, 3, 0, 0), 10, 25));
            var zone = TimeZoneInfo.CreateCustomTimeZone("Test/Shifting", TimeSpan.Zero, "Shifting", "Shifting", "Shifting summer", [rule]);

            Assert.False(CellCodec.Decode(Text("2026-03-29 02:30"), new CellDecodeTarget(typeof(DateTimeOffset)), Decoding(zone: zone)).Success);
        }

        [Fact]
        public void A_time_reads_a_fraction_or_hours_and_minutes_text()
        {
            CellDecodeTarget target = new(typeof(TimeOnly));

            Assert.Equal(new TimeOnly(18, 0), Decode(new ExcelRawCell(ExcelCellKind.Number, "0.75"), target).Value);
            Assert.Equal(new TimeOnly(7, 5), Decode(Text("7:05"), target).Value);
            Assert.Equal(new TimeOnly(17, 5, 9), Decode(Text("17:05:09"), target).Value);
            Assert.False(Decode(new ExcelRawCell(ExcelCellKind.Number, "1.25"), target).Success);
            Assert.Equal(TimeSpan.FromHours(6), Decode(new ExcelRawCell(ExcelCellKind.Number, "0.25"), new CellDecodeTarget(typeof(TimeSpan))).Value);
            Assert.Equal(new TimeOnly(23, 59, 59, 999), RoundTrip(new TimeOnly(23, 59, 59, 999), Encoding(Typed(typeof(TimeOnly), QueryexType.QxString), ExportKind.ForImport), target));
        }

        [Fact]
        public void A_guid_reads_its_hyphenated_or_braced_text()
        {
            var id = Guid.Parse("0f8fad5b-d9cb-469f-a165-70867728950e");
            CellDecodeTarget target = new(typeof(Guid));

            Assert.Equal(id, Decode(Text("0F8FAD5B-D9CB-469F-A165-70867728950E"), target).Value);
            Assert.Equal(id, Decode(Text("{0f8fad5b-d9cb-469f-a165-70867728950e}"), target).Value);
            Assert.Equal(ExpectedValues.Guid, Decode(Text("0f8fad5bd9cb469fa16570867728950e"), target).Expected);
        }

        [Fact]
        public void A_blank_cell_is_null_and_an_error_cell_is_refused_with_its_text()
        {
            Assert.Equal(CellDecodeResult.Of(null), Decode(ExcelRawCell.Blank, new CellDecodeTarget(typeof(int))));
            Assert.Null(Decode(Text("  "), new CellDecodeTarget(typeof(DateOnly))).Value);

            CellDecodeResult error = Decode(new ExcelRawCell(ExcelCellKind.Error, "#N/A"), new CellDecodeTarget(typeof(decimal)));

            Assert.False(error.Success);
            Assert.Equal(new Dictionary<string, object?> { ["expected"] = ExpectedValues.Numeric, ["value"] = "#N/A" }, error.ErrorArguments());
        }

        [Fact]
        public void A_refused_cells_text_is_cut_to_a_hundred_characters()
        {
            CellDecodeResult result = Decode(Text(new string('x', 300)), new CellDecodeTarget(typeof(int)));

            Assert.Equal(CellDecodeResult.MaxErrorTextLength, result.Text!.Length);
        }

        [Fact]
        public void The_reader_sees_every_kind_of_stored_cell()
        {
            static Cell Stored(CellValues? type, string? value)
            {
                Cell cell = new() { CellReference = "A1" };
                if (type is CellValues t)
                {
                    cell.DataType = t;
                }

                if (value is not null)
                {
                    cell.CellValue = new CellValue(value);
                }

                return cell;
            }

            static string? Shared(int index)
            {
                return index == 3 ? "shared" : null;
            }

            Assert.Equal(new ExcelRawCell(ExcelCellKind.Number, "12.5"), CellReader.Read(Stored(null, "12.5"), Shared));
            Assert.Equal(new ExcelRawCell(ExcelCellKind.Text, "shared"), CellReader.Read(Stored(CellValues.SharedString, "3"), Shared));
            Assert.Equal(new ExcelRawCell(ExcelCellKind.Text, "cached\u0001"), CellReader.Read(Stored(CellValues.String, "cached_x0001_"), Shared));
            Assert.Equal(new ExcelRawCell(ExcelCellKind.Boolean, "1"), CellReader.Read(Stored(CellValues.Boolean, "1"), Shared));
            Assert.Equal(new ExcelRawCell(ExcelCellKind.Date, "2026-11-16T00:00:00"), CellReader.Read(Stored(CellValues.Date, "2026-11-16T00:00:00"), Shared));
            Assert.Equal(new ExcelRawCell(ExcelCellKind.Error, "#DIV/0!"), CellReader.Read(Stored(CellValues.Error, "#DIV/0!"), Shared));
            Assert.Equal(ExcelRawCell.Blank, CellReader.Read(Stored(CellValues.Number, null), Shared));
            Assert.Equal(new ExcelRawCell(ExcelCellKind.Text, string.Empty), CellReader.Read(Stored(CellValues.String, string.Empty), Shared));
            _ = Assert.Throws<FormatException>(() => CellReader.Read(Stored(CellValues.SharedString, "4"), Shared));
        }

        [Fact]
        public void The_reader_joins_rich_text_runs_and_skips_phonetic_readings()
        {
            Cell inline = new(new InlineString(new Run(new Text("Bold ")), new Run(new Text("plain")), new PhoneticRun(new Text("reading"))))
            {
                DataType = CellValues.InlineString,
            };
            Cell plain = new(new InlineString(new Text("a_x005F_x0041_b"))) { DataType = CellValues.InlineString };

            Assert.Equal(new ExcelRawCell(ExcelCellKind.Text, "Bold plain"), CellReader.Read(inline, static _ => null));
            Assert.Equal(new ExcelRawCell(ExcelCellKind.Text, "a_x0041_b"), CellReader.Read(plain, static _ => null));
        }

        /// <summary>A text cell.</summary>
        /// <param name="text">The text.</param>
        /// <returns>The cell.</returns>
        private static ExcelRawCell Text(string text)
        {
            return new ExcelRawCell(ExcelCellKind.Text, text);
        }

        /// <summary>Decodes a cell in the default request context.</summary>
        /// <param name="cell">The cell.</param>
        /// <param name="target">The target.</param>
        /// <returns>The result.</returns>
        private static CellDecodeResult Decode(ExcelRawCell cell, CellDecodeTarget target)
        {
            return CellCodec.Decode(cell, target, Decoding());
        }

        /// <summary>Encodes a value, stores it as the writer does, and decodes it back.</summary>
        /// <param name="value">The value.</param>
        /// <param name="column">The column's encoding.</param>
        /// <param name="target">What the cell decodes into.</param>
        /// <param name="context">The decoding context; the default one when null.</param>
        /// <returns>The value read back.</returns>
        private static object? RoundTrip(object value, ColumnEncoding column, CellDecodeTarget target, CellDecodeContext? context = null)
        {
            EncodedCell encoded = CellCodec.Encode(value, column);

            // The stored kinds the writer emits: a number, a boolean, an inline string.
            ExcelRawCell stored = encoded.Kind switch
            {
                EncodedCellKind.Number => new ExcelRawCell(ExcelCellKind.Number, encoded.Text),
                EncodedCellKind.Boolean => new ExcelRawCell(ExcelCellKind.Boolean, encoded.Text),
                EncodedCellKind.Text => new ExcelRawCell(ExcelCellKind.Text, encoded.Text),
                EncodedCellKind.Blank => ExcelRawCell.Blank,
                _ => throw new InvalidOperationException(),
            };

            CellDecodeResult result = CellCodec.Decode(stored, target, context ?? Decoding());
            Assert.True(result.Success, $"{encoded} did not decode: expected {result.Expected}.");
            return result.Value;
        }
    }
}
