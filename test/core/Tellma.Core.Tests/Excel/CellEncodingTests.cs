// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Tellma.Core.Abstractions.Errors;
using Tellma.Core.Abstractions.Excel;
using Tellma.Core.Excel;
using Tellma.Core.Queryex;
using Tellma.Testing.Entities;
using static Tellma.Core.Tests.Excel.ExcelTestKit;

namespace Tellma.Core.Tests.Excel
{
    /// <summary>
    ///     Encoding by type: each type's cell, number format, width and validation list in the
    ///     display and the editable shapes, the 15-digit boundary of an editable decimal, and the
    ///     dates a serial cannot carry.
    /// </summary>
    public class CellEncodingTests
    {
        [Fact]
        public void A_null_is_a_blank_cell_whatever_the_type()
        {
            Assert.Equal(EncodedCell.Blank, CellCodec.Encode(null, Encoding(Typed(typeof(int), QueryexType.QxNumeric), ExportKind.Display)));
            Assert.Equal(EncodedCell.Blank, CellCodec.Encode(DBNull.Value, Encoding(Typed(typeof(string), QueryexType.QxString), ExportKind.ForImport)));
        }

        [Fact]
        public void A_boolean_is_a_boolean_cell_with_a_true_false_list_when_editable()
        {
            ColumnEncoding display = Encoding(Typed(typeof(bool), QueryexType.QxBool), ExportKind.Display);
            ColumnEncoding editable = Encoding(Typed(typeof(bool), QueryexType.QxBool), ExportKind.ForImport);

            Assert.Equal(new EncodedCell(EncodedCellKind.Boolean, "1"), CellCodec.Encode(true, display));
            Assert.Equal(new EncodedCell(EncodedCellKind.Boolean, "0"), CellCodec.Encode(false, editable));
            Assert.Null(display.ListValues);
            Assert.Equal(["TRUE", "FALSE"], editable.ListValues);
        }

        [Fact]
        public void An_int_is_a_number_grouped_in_display_and_plain_when_editable()
        {
            ColumnEncoding display = Encoding(Typed(typeof(int), QueryexType.QxNumeric), ExportKind.Display);
            ColumnEncoding editable = Encoding(Typed(typeof(int?), QueryexType.QxNumeric), ExportKind.ForImport);

            Assert.Equal(EncodedCell.OfNumber("-42"), CellCodec.Encode(-42, display));
            Assert.Equal("#,##0", display.NumberFormat);
            Assert.Equal("0", editable.NumberFormat);
            Assert.Equal(EncodedCell.OfNumber("7"), CellCodec.Encode((short)7, editable));
        }

        [Fact]
        public void A_long_is_a_number_in_display_and_text_when_editable()
        {
            ColumnEncoding display = Encoding(Typed(typeof(long), QueryexType.QxNumeric), ExportKind.Display);
            ColumnEncoding editable = Encoding(Typed(typeof(long), QueryexType.QxNumeric), ExportKind.ForImport);

            Assert.Equal(EncodedCell.OfNumber("1234567890123456789"), CellCodec.Encode(1_234_567_890_123_456_789L, display));
            Assert.Equal(EncodedCell.OfText("1234567890123456789"), CellCodec.Encode(1_234_567_890_123_456_789L, editable));
            Assert.Equal("@", editable.NumberFormat);
        }

        [Theory]
        [InlineData("123456789012345", false)]
        [InlineData("1234567890.12345", false)]
        [InlineData("0.000000000000001", false)]
        [InlineData("1234567890123.4500", false)]
        [InlineData("1234567890123456", true)]
        [InlineData("1234567890.123456", true)]
        [InlineData("-99999999999999.99", true)]
        public void An_editable_decimal_becomes_text_past_fifteen_significant_digits(string text, bool asText)
        {
            decimal value = decimal.Parse(text, System.Globalization.CultureInfo.InvariantCulture);
            ColumnEncoding editable = Encoding(Typed(typeof(decimal), QueryexType.QxNumeric, scale: 4), ExportKind.ForImport);

            EncodedCell cell = CellCodec.Encode(value, editable);

            // Trailing zeros of the scale are no digits: the cell carries the normalised value either way.
            Assert.Equal(asText ? EncodedCellKind.Text : EncodedCellKind.Number, cell.Kind);
            Assert.Equal(CellCodec.Normalize(value).ToString(System.Globalization.CultureInfo.InvariantCulture), cell.Text);
        }

        [Fact]
        public void A_display_decimal_is_always_a_number_and_takes_its_format_from_the_scale()
        {
            ColumnEncoding display = Encoding(Typed(typeof(decimal), QueryexType.QxNumeric, scale: 4), ExportKind.Display);
            ColumnEncoding editable = Encoding(Typed(typeof(decimal), QueryexType.QxNumeric, scale: 2), ExportKind.ForImport);
            ColumnEncoding computed = Encoding(Computed(QueryexType.QxNumeric), ExportKind.Display);
            ColumnEncoding whole = Encoding(Typed(typeof(decimal), QueryexType.QxNumeric, scale: 0), ExportKind.Display);

            Assert.Equal(EncodedCell.OfNumber("1234567890.123456"), CellCodec.Encode(1234567890.123456m, display));
            Assert.Equal("#,##0.0000", display.NumberFormat);
            Assert.Equal("0.00", editable.NumberFormat);
            Assert.Equal("General", computed.NumberFormat);
            Assert.Equal("#,##0", whole.NumberFormat);
        }

        [Fact]
        public void A_computed_numeric_column_writes_an_integer_reader_value_as_an_integer()
        {
            ColumnEncoding computed = Encoding(Computed(QueryexType.QxNumeric), ExportKind.Display);

            Assert.Equal(EncodedCell.OfNumber("12"), CellCodec.Encode(12, computed));
            Assert.Equal(EncodedCell.OfNumber("12.5"), CellCodec.Encode(12.50m, computed));
        }

        [Fact]
        public void A_double_is_a_general_number_and_a_non_finite_one_is_text()
        {
            ColumnEncoding column = Encoding(Typed(typeof(double), QueryexType.QxNumeric), ExportKind.ForImport);

            Assert.Equal(EncodedCell.OfNumber("0.1"), CellCodec.Encode(0.1d, column));
            Assert.Equal(EncodedCell.OfNumber("3.4028234663852886E+38"), CellCodec.Encode(float.MaxValue, column));
            Assert.Equal(EncodedCellKind.Text, CellCodec.Encode(double.NaN, column).Kind);
            Assert.Equal("General", column.NumberFormat);
        }

        [Fact]
        public void A_string_is_an_inline_string_with_a_length_check_when_editable_and_declared()
        {
            ColumnEncoding display = Encoding(Typed(typeof(string), QueryexType.QxString, maxLength: 50), ExportKind.Display);
            ColumnEncoding editable = Encoding(Typed(typeof(string), QueryexType.QxString, maxLength: 50), ExportKind.ForImport);
            ColumnEncoding undeclared = Encoding(Typed(typeof(string), QueryexType.QxString), ExportKind.ForImport);

            Assert.Equal(EncodedCell.OfText("=SUM(A1)"), CellCodec.Encode("=SUM(A1)", display));
            Assert.Equal("@", display.NumberFormat);
            Assert.Null(display.MaxLength);
            Assert.Equal(50, editable.MaxLength);
            Assert.Null(undeclared.MaxLength);
        }

        [Fact]
        public void Text_longer_than_a_cell_is_cut_in_display_and_refused_when_editable()
        {
            string text = new('x', CellCodec.MaxCellLength + 1);

            EncodedCell cut = CellCodec.Encode(text, Encoding(Typed(typeof(string), QueryexType.QxString), ExportKind.Display));
            LimitExceededException refused = Assert.Throws<LimitExceededException>(
                () => CellCodec.Encode(text, Encoding(Typed(typeof(string), QueryexType.QxString), ExportKind.ForImport)));

            Assert.Equal(CellCodec.MaxCellLength, cut.Text.Length);
            Assert.Equal(ExcelErrorCodes.ExportCellTooLong, refused.Limit);
        }

        [Fact]
        public void An_enum_is_its_label_in_display_and_its_stored_value_with_a_list_when_editable()
        {
            string[] members = Enum.GetNames<WidgetKind>();
            ColumnEncoding display = Encoding(Typed(typeof(WidgetKind), QueryexType.QxString, maxLength: 8, enumValues: members), ExportKind.Display);
            ColumnEncoding editable = Encoding(Typed(typeof(WidgetKind?), QueryexType.QxString, maxLength: 8, enumValues: members), ExportKind.ForImport);

            Assert.Equal(EncodedCell.OfText("[Fancy]"), CellCodec.Encode(WidgetKind.Fancy, display));
            Assert.Equal(EncodedCell.OfText("Fancy"), CellCodec.Encode(WidgetKind.Fancy, editable));
            Assert.Equal(["Plain", "Fancy"], editable.ListValues);
            Assert.Null(display.ListValues);
        }

        [Fact]
        public void A_value_set_string_is_its_label_in_display_and_its_value_when_editable()
        {
            ColumnEncoding display = Encoding(Typed(typeof(string), QueryexType.QxString, valueSet: "languages"), ExportKind.Display);
            ColumnEncoding editable = Encoding(Typed(typeof(string), QueryexType.QxString, valueSet: "languages"), ExportKind.ForImport);

            Assert.Equal(EncodedCell.OfText("languages:ar"), CellCodec.Encode("ar", display));
            Assert.Equal(EncodedCell.OfText("ar"), CellCodec.Encode("ar", editable));
        }

        [Fact]
        public void A_choice_list_too_long_or_holding_a_comma_or_a_quote_is_left_off()
        {
            ExcelColumnSpec column = Computed(QueryexType.QxString) with { Choices = [.. Enumerable.Range(0, 60).Select(static i => $"Choice{i}")] };
            ExcelColumnSpec comma = Computed(QueryexType.QxString) with { Choices = ["a,b", "c"] };
            ExcelColumnSpec quote = Computed(QueryexType.QxString) with { Choices = ["a\"b", "c"] };

            Assert.Null(Encoding(column, ExportKind.ForImport).ListValues);
            Assert.Null(Encoding(comma, ExportKind.ForImport).ListValues);
            Assert.Null(Encoding(quote, ExportKind.ForImport).ListValues);
            Assert.Equal(["c", "d"], Encoding(Computed(QueryexType.QxString) with { Choices = ["c", "d"] }, ExportKind.ForImport).ListValues);
        }

        [Fact]
        public void A_date_is_its_serial_from_the_1900_epoch()
        {
            ColumnEncoding column = Encoding(Typed(typeof(DateOnly), QueryexType.QxDate), ExportKind.ForImport);

            Assert.Equal(EncodedCell.OfNumber("61"), CellCodec.Encode(new DateOnly(1900, 3, 1), column));
            Assert.Equal(EncodedCell.OfNumber("46342"), CellCodec.Encode(new DateOnly(2026, 11, 16), column));
            Assert.Equal(EncodedCell.OfNumber("2958465"), CellCodec.Encode(DateOnly.MaxValue, column));
            Assert.Equal(ExcelDateEncoding.Serial, column.DateEncoding);
            Assert.Equal("m/d/yyyy", column.DatePattern);
            Assert.Equal("[$-0409]m/d/yyyy", column.NumberFormat);
        }

        [Fact]
        public void A_date_before_1_march_1900_has_no_reliable_serial_and_is_iso_text()
        {
            ColumnEncoding column = Encoding(Typed(typeof(DateOnly), QueryexType.QxDate), ExportKind.ForImport);

            Assert.Equal(EncodedCell.OfText("1900-02-28"), CellCodec.Encode(new DateOnly(1900, 2, 28), column));
            Assert.Equal(EncodedCell.OfText("0001-01-01"), CellCodec.Encode(DateOnly.MinValue, column));
        }

        [Fact]
        public void A_date_and_time_is_its_serial_with_the_time_as_the_fraction()
        {
            ColumnEncoding column = Encoding(Typed(typeof(DateTime), QueryexType.QxDateTime), ExportKind.ForImport);

            Assert.Equal(EncodedCell.OfNumber("46342.75"), CellCodec.Encode(new DateTime(2026, 11, 16, 18, 0, 0), column));
            Assert.Equal("[$-0409]m/d/yyyy h:mm:ss AM/PM", column.NumberFormat);
            Assert.Equal("m/d/yyyy h:mm:ss AM/PM", column.DatePattern);
        }

        [Fact]
        public void An_instant_is_a_serial_in_the_display_zone_in_display_and_iso_text_with_its_offset_when_editable()
        {
            var riyadh = TimeZoneInfo.CreateCustomTimeZone("Test/Riyadh", TimeSpan.FromHours(3), "Riyadh", "Riyadh");
            ExcelColumnSpec spec = Typed(typeof(DateTimeOffset), QueryexType.QxDateTimeOffset);
            ColumnEncoding display = Encoding(spec, ExportKind.Display, Context(zone: riyadh));
            ColumnEncoding editable = Encoding(spec, ExportKind.ForImport, Context(zone: riyadh));
            DateTimeOffset instant = new(2026, 11, 16, 15, 0, 0, TimeSpan.Zero);

            Assert.Equal(EncodedCell.OfNumber("46342.75"), CellCodec.Encode(instant, display));
            Assert.Equal(EncodedCell.OfText("2026-11-16T15:00:00+00:00"), CellCodec.Encode(instant, editable));
            Assert.Equal(EncodedCell.OfText("2026-11-16T18:00:00.1234567+03:00"), CellCodec.Encode(new DateTimeOffset(2026, 11, 16, 18, 0, 0, TimeSpan.FromHours(3)).AddTicks(1_234_567), editable));
            Assert.Equal("@", editable.NumberFormat);
            Assert.Equal(ExcelDateEncoding.Text, editable.DateEncoding);
            Assert.Null(editable.DatePattern);
        }

        [Fact]
        public void A_time_of_day_is_its_fraction_formatted_as_hours_minutes_and_seconds()
        {
            ColumnEncoding column = Encoding(Typed(typeof(TimeOnly), QueryexType.QxString), ExportKind.ForImport);

            Assert.Equal(EncodedCell.OfNumber("0.75"), CellCodec.Encode(new TimeOnly(18, 0), column));
            Assert.Equal(EncodedCell.OfNumber("0"), CellCodec.Encode(TimeOnly.MinValue, column));
            Assert.Equal("hh:mm:ss", column.NumberFormat);
        }

        [Fact]
        public void A_guid_is_text_in_its_hyphenated_form()
        {
            var id = Guid.Parse("0f8fad5b-d9cb-469f-a165-70867728950e");

            Assert.Equal(EncodedCell.OfText("0f8fad5b-d9cb-469f-a165-70867728950e"), CellCodec.Encode(id, Encoding(Typed(typeof(Guid), QueryexType.QxGuid), ExportKind.ForImport)));
        }

        [Fact]
        public void Binary_data_is_blank_and_a_hierarchy_path_or_json_is_its_text()
        {
            Assert.Equal(EncodedCell.Blank, CellCodec.Encode(new byte[] { 1, 2 }, Encoding(Typed(typeof(byte[]), QueryexType.QxString), ExportKind.Display)));
            Assert.Equal(EncodedCell.OfText("/1/3/"), CellCodec.Encode("/1/3/", Encoding(Computed(QueryexType.QxHierarchyId), ExportKind.Display)));
            Assert.Equal(EncodedCell.OfText(/*lang=json,strict*/ "{\"a\":1}"), CellCodec.Encode(/*lang=json,strict*/ "{\"a\":1}", Encoding(Typed(typeof(string), QueryexType.QxString), ExportKind.Display)));
        }

        [Fact]
        public void A_stamp_is_its_wire_string_in_a_hidden_text_column()
        {
            ColumnEncoding column = Encoding(Computed(QueryexType.QxDateTimeOffset, "Stamp") with { Role = ExcelColumnRole.Stamp }, ExportKind.ForImport);
            DateTimeOffset stamp = new(2026, 11, 16, 9, 30, 0, TimeSpan.Zero);

            Assert.Equal(EncodedCell.OfText("2026-11-16T09:30:00.0000000+00:00"), CellCodec.Encode(stamp, column));
            Assert.True(column.Hidden);
            Assert.Equal("@", column.NumberFormat);
        }

        [Fact]
        public void A_column_is_as_wide_as_its_type_and_never_narrower_than_its_header()
        {
            Assert.Equal(12, Encoding(Typed(typeof(DateOnly), QueryexType.QxDate), ExportKind.Display).Width);
            Assert.Equal(25, Encoding(Typed(typeof(string), QueryexType.QxString, maxLength: 50), ExportKind.Display).Width);
            Assert.Equal(12, Encoding(Typed(typeof(string), QueryexType.QxString, maxLength: 8), ExportKind.Display).Width);
            Assert.Equal(60, Encoding(Typed(typeof(string), QueryexType.QxString, maxLength: 4000), ExportKind.Display).Width);
            Assert.Equal(42, Encoding(Computed(QueryexType.QxNumeric, new string('h', 40)), ExportKind.Display).Width);
            Assert.Equal(60, Encoding(Computed(QueryexType.QxNumeric, new string('h', 200)), ExportKind.Display).Width);
        }

        [Fact]
        public void A_column_format_the_plan_gives_wins_over_the_types_own()
        {
            var column = ColumnEncoding.For(Computed(QueryexType.QxNumeric), ExportKind.Report, Context(), "0.0%", 5);

            Assert.Equal("0.0%", column.NumberFormat);
        }
    }
}
