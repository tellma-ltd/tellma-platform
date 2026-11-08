// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using System.Globalization;
using System.Text.Json;
using Tellma.Core.Abstractions.Api;

namespace Tellma.Core.Abstractions.Tests.Api
{
    /// <summary>
    ///     Every scalar the wire carries, written in its one form and read back bit for bit, with the
    ///     forms each refuses.
    /// </summary>
    public class ScalarEncodingTests
    {
        // --- Integers ---

        [Fact]
        public void Small_integers_are_exact_json_numbers()
        {
            Assert.Equal("2147483647", Write(int.MaxValue));
            Assert.Equal("-32768", Write(short.MinValue));
            Assert.Equal("255", Write(byte.MaxValue));
            Assert.Equal(int.MinValue, Read<int>("-2147483648"));
        }

        [Theory]
        [InlineData("\"7\"")]
        [InlineData("7.0")]
        public void A_small_integer_is_never_quoted_or_fractional(string json)
        {
            _ = Assert.Throws<JsonException>(() => Read<int>(json));
        }

        [Theory]
        [InlineData(9_007_199_254_740_993L, "\"9007199254740993\"")]
        [InlineData(long.MaxValue, "\"9223372036854775807\"")]
        [InlineData(long.MinValue, "\"-9223372036854775808\"")]
        [InlineData(0L, "\"0\"")]
        public void A_long_is_a_string_of_every_digit_and_round_trips(long value, string json)
        {
            Assert.Equal(json, Write(value));
            Assert.Equal(value, Read<long>(json));
        }

        [Fact]
        public void A_long_reads_from_a_number_too()
        {
            Assert.Equal(9_007_199_254_740_993L, Read<long>("9007199254740993"));
            Assert.Equal(-5L, Read<long>("-5"));
        }

        [Theory]
        [InlineData("5.0")]
        [InlineData("1e3")]
        [InlineData("\"1e3\"")]
        [InlineData("\"5.0\"")]
        [InlineData("\" 5\"")]
        [InlineData("\"+5\"")]
        [InlineData("\"\"")]
        [InlineData("\"-\"")]
        [InlineData("\"abc\"")]
        [InlineData("\"9223372036854775808\"")]
        [InlineData("9223372036854775808")]
        [InlineData("null")]
        [InlineData("true")]
        public void A_long_refuses_anything_but_an_integer_in_range(string json)
        {
            _ = Assert.Throws<JsonException>(() => Read<long>(json));
        }

        [Fact]
        public void A_nullable_long_carries_null()
        {
            Assert.Equal("null", Write<long?>(null));
            Assert.Null(Read<long?>("null"));
            Assert.Equal(12L, Read<long?>("\"12\""));
        }

        [Fact]
        public void An_escaped_string_is_read_by_its_characters()
        {
            Assert.Equal(12L, Read<long>("\"\\u0031\\u0032\""));
        }

        [Fact]
        public void A_long_list_writes_every_element_as_a_string()
        {
            Assert.Equal("[\"1\",\"9007199254740993\"]", Write<IReadOnlyList<long>>([1, 9_007_199_254_740_993]));
            Assert.Equal([5L, 6L], Read<long[]>("[5,\"6\"]"));
        }

        [Fact]
        public void A_long_dictionary_key_round_trips()
        {
            Dictionary<long, int> map = new() { [9_007_199_254_740_993] = 1 };

            Assert.Equal(/*lang=json,strict*/ """{"9007199254740993":1}""", Write(map));
            Assert.Equal(1, Read<Dictionary<long, int>>(/*lang=json,strict*/ """{"9007199254740993":1}""")[9_007_199_254_740_993]);
        }

        [Fact]
        public void A_decimal_key_round_trips_at_full_scale()
        {
            Dictionary<decimal, int> map = new() { [1234.5000m] = 1 };

            Assert.Equal(/*lang=json,strict*/ """{"1234.5000":1}""", Write(map));
            Assert.Equal(4, Read<Dictionary<decimal, int>>(/*lang=json,strict*/ """{"1234.5000":1}""").Keys.Single().Scale);
        }

        [Theory]
        [InlineData(/*lang=json,strict*/ """{"1e3":1}""")]
        [InlineData(/*lang=json,strict*/ """{"1,5":1}""")]
        [InlineData(/*lang=json,strict*/ """{"0.00000000000000000000000000001":1}""")]
        public void A_decimal_key_refuses_what_a_decimal_value_refuses(string json)
        {
            _ = Assert.Throws<JsonException>(() => Read<Dictionary<decimal, int>>(json));
        }

        [Fact]
        public void A_date_time_key_round_trips_and_refuses_an_offset()
        {
            Dictionary<DateTime, int> map = new() { [new DateTime(2026, 8, 30, 12, 2, 10)] = 1 };

            Assert.Equal(/*lang=json,strict*/ """{"2026-08-30T12:02:10.0000000":1}""", Write(map));
            Assert.Equal(map.Keys.Single(), Read<Dictionary<DateTime, int>>(Write(map)).Keys.Single());
            _ = Assert.Throws<JsonException>(() => Read<Dictionary<DateTime, int>>(/*lang=json,strict*/ """{"2026-08-30T12:02:10Z":1}"""));
        }

        [Fact]
        public void An_instant_key_keeps_its_offset_through_the_encoders_escaping()
        {
            DateTimeOffset key = new(2026, 8, 30, 12, 2, 10, TimeSpan.FromHours(3));
            Dictionary<DateTimeOffset, int> map = new() { [key] = 1 };

            // A key goes through the encoder, which escapes the offset's plus sign.
            string json = Write(map);
            Assert.StartsWith("{\"2026-08-30T12:02:10.0000000", json, StringComparison.Ordinal);
            Assert.Contains("\\u002B03:00", json, StringComparison.Ordinal);
            Assert.DoesNotContain("+", json, StringComparison.Ordinal);

            DateTimeOffset read = Read<Dictionary<DateTimeOffset, int>>(json).Keys.Single();
            Assert.Equal((key.Ticks, key.Offset), (read.Ticks, read.Offset));
            _ = Assert.Throws<JsonException>(() => Read<Dictionary<DateTimeOffset, int>>(/*lang=json,strict*/ """{"2026-08-30T12:02:10":1}"""));
        }

        [Fact]
        public void A_time_of_day_key_round_trips_and_refuses_another_form()
        {
            Dictionary<TimeOnly, int> map = new() { [new TimeOnly(12, 2, 10)] = 1 };

            Assert.Equal(/*lang=json,strict*/ """{"12:02:10.0000000":1}""", Write(map));
            Assert.Equal(map.Keys.Single(), Read<Dictionary<TimeOnly, int>>(Write(map)).Keys.Single());
            _ = Assert.Throws<JsonException>(() => Read<Dictionary<TimeOnly, int>>(/*lang=json,strict*/ """{"8:00":1}"""));
        }

        [Fact]
        public void A_token_longer_than_any_scalar_is_refused_unread()
        {
            _ = Assert.Throws<JsonException>(() => Read<long>("\"" + new string('1', 200) + "\""));
        }

        // --- Decimals ---

        [Theory]
        [InlineData("1234.5000", "\"1234.5000\"")]
        [InlineData("-0.0001", "\"-0.0001\"")]
        [InlineData("0.00", "\"0.00\"")]
        [InlineData("0.0000000000000000000000000001", "\"0.0000000000000000000000000001\"")]
        [InlineData("79228162514264337593543950335", "\"79228162514264337593543950335\"")]
        [InlineData("-79228162514264337593543950335", "\"-79228162514264337593543950335\"")]
        [InlineData("7.9228162514264337593543950335", "\"7.9228162514264337593543950335\"")]
        public void A_decimal_is_a_string_at_full_scale_and_round_trips(string text, string json)
        {
            decimal value = decimal.Parse(text, CultureInfo.InvariantCulture);

            Assert.Equal(json, Write(value));

            decimal read = Read<decimal>(json);
            Assert.Equal(value, read);
            Assert.Equal(value.Scale, read.Scale);
        }

        [Fact]
        public void A_decimal_never_uses_exponent_form()
        {
            Assert.Equal("\"0.0000001\"", Write(0.0000001m));
            Assert.Equal("\"10000000000000000000000000000\"", Write(1E28m));
        }

        [Theory]
        [InlineData("1234.5", "1234.5")]
        [InlineData("1.5e3", "1500")]
        [InlineData("15E-1", "1.5")]
        [InlineData("1E+28", "10000000000000000000000000000")]
        [InlineData("0e5", "0")]
        [InlineData("-0.5", "-0.5")]
        public void A_decimal_reads_from_a_json_number_exactly(string json, string expected)
        {
            decimal read = Read<decimal>(json);

            Assert.Equal(expected, read.ToString(CultureInfo.InvariantCulture));
        }

        [Fact]
        public void A_decimal_drops_only_trailing_zeros_it_cannot_hold()
        {
            // Scale 29 with a trailing zero holds exactly at scale 28.
            decimal read = Read<decimal>("\"0.10000000000000000000000000000\"");

            Assert.Equal(0.1m, read);
            Assert.Equal(28, read.Scale);
        }

        [Theory]
        [InlineData("\"1.5e3\"")]
        [InlineData("\"1,5\"")]
        [InlineData("\" 1.5\"")]
        [InlineData("\"1.\"")]
        [InlineData("\".5\"")]
        [InlineData("\"+1\"")]
        [InlineData("\"NaN\"")]
        [InlineData("\"\"")]
        [InlineData("\"0.00000000000000000000000000001\"")]
        [InlineData("0.00000000000000000000000000001")]
        [InlineData("1e-30")]
        [InlineData("\"79228162514264337593543950336\"")]
        [InlineData("\"123456789012345678901234567890\"")]
        [InlineData("1e29")]
        [InlineData("1e100000000")]
        [InlineData("null")]
        [InlineData("false")]
        public void A_decimal_refuses_a_value_it_cannot_hold_exactly_or_a_string_that_is_not_plain(string json)
        {
            _ = Assert.Throws<JsonException>(() => Read<decimal>(json));
        }

        // --- Doubles ---

        [Theory]
        [InlineData(0.1, "0.1")]
        [InlineData(1.7976931348623157E+308, "1.7976931348623157E+308")]
        [InlineData(-2.5, "-2.5")]
        public void A_double_is_its_shortest_round_trip_number(double value, string json)
        {
            Assert.Equal(json, Write(value));
            Assert.Equal(value, Read<double>(json));
        }

        [Fact]
        public void Negative_zero_round_trips_bit_for_bit()
        {
            Assert.Equal(BitConverter.DoubleToInt64Bits(-0.0), BitConverter.DoubleToInt64Bits(Read<double>(Write(-0.0))));
        }

        [Theory]
        [InlineData("\"NaN\"")]
        [InlineData("\"Infinity\"")]
        [InlineData("\"1.5\"")]
        public void A_double_refuses_quoted_and_non_finite_values(string json)
        {
            _ = Assert.Throws<JsonException>(() => Read<double>(json));
        }

        [Fact]
        public void A_non_finite_double_is_never_written()
        {
            _ = Assert.ThrowsAny<ArgumentException>(() => Write(double.NaN));
        }

        // --- Dates and times ---

        [Fact]
        public void A_date_is_yyyy_mm_dd()
        {
            Assert.Equal("\"2026-08-30\"", Write(new DateOnly(2026, 8, 30)));
            Assert.Equal(new DateOnly(1, 1, 1), Read<DateOnly>("\"0001-01-01\""));
            _ = Assert.Throws<JsonException>(() => Read<DateOnly>("\"2026-8-30\""));
        }

        [Fact]
        public void A_date_time_has_seven_fractional_digits_and_no_offset_whatever_its_kind()
        {
            DateTime utc = new(2026, 8, 30, 12, 2, 10, DateTimeKind.Utc);

            Assert.Equal("\"2026-08-30T12:02:10.0000000\"", Write(utc));
            Assert.Equal("\"2026-08-30T12:02:10.0000000\"", Write(DateTime.SpecifyKind(utc, DateTimeKind.Local)));
        }

        [Fact]
        public void A_date_time_round_trips_every_tick_and_reads_as_unspecified()
        {
            DateTime value = new DateTime(2026, 8, 30, 12, 2, 10, DateTimeKind.Unspecified).AddTicks(1_234_567);

            DateTime read = Read<DateTime>(Write(value));

            Assert.Equal(value.Ticks, read.Ticks);
            Assert.Equal(DateTimeKind.Unspecified, read.Kind);
            Assert.Equal(DateTime.MaxValue, Read<DateTime>(Write(DateTime.MaxValue)));
        }

        [Theory]
        [InlineData("\"2026-08-30T12:02:10\"", 0L)]
        [InlineData("\"2026-08-30T12:02:10.5\"", 5_000_000L)]
        [InlineData("\"2026-08-30t12:02:10.0000001\"", 1L)]
        public void A_date_time_reads_up_to_seven_fractional_digits(string json, long fractionTicks)
        {
            DateTime read = Read<DateTime>(json);

            Assert.Equal(new DateTime(2026, 8, 30, 12, 2, 10).Ticks + fractionTicks, read.Ticks);
        }

        [Theory]
        [InlineData("\"2026-08-30T12:02:10Z\"")]
        [InlineData("\"2026-08-30T12:02:10+03:00\"")]
        [InlineData("\"2026-08-30\"")]
        [InlineData("\"2026-08-30 12:02:10\"")]
        [InlineData("\"2026-02-29T00:00:00\"")]
        [InlineData("\"2026-13-01T00:00:00\"")]
        [InlineData("\"2026-08-30T24:00:00\"")]
        [InlineData("\"2026-08-30T12:02\"")]
        [InlineData("\"2026-08-30T12:02:10.\"")]
        [InlineData("\"2026-08-30T12:02:10.12345678\"")]
        [InlineData("\"0000-01-01T00:00:00\"")]
        [InlineData("1756555330")]
        public void A_date_time_refuses_an_offset_and_every_other_form(string json)
        {
            _ = Assert.Throws<JsonException>(() => Read<DateTime>(json));
        }

        [Fact]
        public void A_leap_day_is_a_date()
        {
            Assert.Equal(new DateTime(2028, 2, 29), Read<DateTime>("\"2028-02-29T00:00:00\""));
        }

        [Theory]
        [InlineData(0, "\"2026-08-30T12:02:10.1234567+00:00\"")]
        [InlineData(180, "\"2026-08-30T12:02:10.1234567+03:00\"")]
        [InlineData(-570, "\"2026-08-30T12:02:10.1234567-09:30\"")]
        public void An_instant_has_seven_fractional_digits_and_its_offset(int offsetMinutes, string json)
        {
            DateTimeOffset value = new DateTimeOffset(2026, 8, 30, 12, 2, 10, TimeSpan.FromMinutes(offsetMinutes)).AddTicks(1_234_567);

            Assert.Equal(json, Write(value));
        }

        [Fact]
        public void A_stamp_round_trips_bit_for_bit()
        {
            DateTimeOffset stamp = new DateTimeOffset(2026, 8, 30, 12, 2, 10, TimeSpan.Zero).AddTicks(9_999_999);

            DateTimeOffset read = Read<DateTimeOffset>(Write(stamp));

            Assert.Equal((stamp.Ticks, stamp.Offset), (read.Ticks, read.Offset));
        }

        [Theory]
        [InlineData("\"2026-08-30T12:02:10Z\"", 0)]
        [InlineData("\"2026-08-30T12:02:10z\"", 0)]
        [InlineData("\"2026-08-30T12:02:10+14:00\"", 840)]
        [InlineData("\"2026-08-30T12:02:10-14:00\"", -840)]
        public void An_instant_reads_z_or_an_offset(string json, int offsetMinutes)
        {
            DateTimeOffset read = Read<DateTimeOffset>(json);

            Assert.Equal(TimeSpan.FromMinutes(offsetMinutes), read.Offset);
            Assert.Equal(new DateTime(2026, 8, 30, 12, 2, 10), read.DateTime);
        }

        [Fact]
        public void An_instant_whose_offset_sign_a_client_escaped_reads_the_same()
        {
            // Encoders that escape HTML-sensitive characters write '+' as +.
            DateTimeOffset read = Read<DateTimeOffset>("\"2026-08-30T12:02:10\\u002B03:00\"");

            Assert.Equal(TimeSpan.FromHours(3), read.Offset);
            Assert.DoesNotContain("\\u", Write(read), StringComparison.Ordinal);
        }

        [Theory]
        [InlineData("\"2026-08-30T12:02:10\"")]
        [InlineData("\"2026-08-30T12:02:10+14:01\"")]
        [InlineData("\"2026-08-30T12:02:10+15:00\"")]
        [InlineData("\"2026-08-30T12:02:10+0300\"")]
        [InlineData("\"2026-08-30T12:02:10+03:60\"")]
        [InlineData("\"0001-01-01T00:00:00+01:00\"")]
        [InlineData("\"9999-12-31T23:59:59-01:00\"")]
        public void An_instant_requires_a_valid_offset_inside_the_range(string json)
        {
            _ = Assert.Throws<JsonException>(() => Read<DateTimeOffset>(json));
        }

        [Fact]
        public void A_time_of_day_has_seven_fractional_digits()
        {
            TimeOnly value = new TimeOnly(12, 2, 10).Add(TimeSpan.FromTicks(1_234_567));

            Assert.Equal("\"12:02:10.1234567\"", Write(value));
            Assert.Equal(value, Read<TimeOnly>(Write(value)));
            Assert.Equal(new TimeOnly(8, 0), Read<TimeOnly>("\"08:00:00\""));
        }

        [Theory]
        [InlineData("\"8:00:00\"")]
        [InlineData("\"24:00:00\"")]
        [InlineData("\"08:00\"")]
        [InlineData("\"08:00:00Z\"")]
        public void A_time_of_day_refuses_every_other_form(string json)
        {
            _ = Assert.Throws<JsonException>(() => Read<TimeOnly>(json));
        }

        // --- Other scalars ---

        [Fact]
        public void A_guid_is_its_canonical_lowercase_form()
        {
            var value = Guid.Parse("4BF92F35-77B3-4DA6-A3CE-929D0E0E4736");

            Assert.Equal("\"4bf92f35-77b3-4da6-a3ce-929d0e0e4736\"", Write(value));
            Assert.Equal(value, Read<Guid>("\"4BF92F35-77B3-4DA6-A3CE-929D0E0E4736\""));
        }

        [Fact]
        public void Bytes_are_base64()
        {
            Assert.Equal("\"AQID\"", Write<byte[]>([1, 2, 3]));
            Assert.Equal([1, 2, 3], Read<byte[]>("\"AQID\""));
        }

        [Fact]
        public void Booleans_and_strings_are_plain()
        {
            Assert.Equal("true", Write(true));
            Assert.Equal("\"East region\"", Write("East region"));
            _ = Assert.Throws<JsonException>(() => Read<bool>("\"true\""));
        }

        /// <summary>Serializes a value with the platform's options.</summary>
        /// <typeparam name="T">The value's type.</typeparam>
        /// <param name="value">The value.</param>
        /// <returns>The JSON text.</returns>
        private static string Write<T>(T value)
        {
            return JsonSerializer.Serialize(value, TellmaJson.Options);
        }

        /// <summary>Deserializes a value with the platform's options.</summary>
        /// <typeparam name="T">The value's type.</typeparam>
        /// <param name="json">The JSON text.</param>
        /// <returns>The value.</returns>
        private static T Read<T>(string json)
        {
            return JsonSerializer.Deserialize<T>(json, TellmaJson.Options)!;
        }
    }
}
