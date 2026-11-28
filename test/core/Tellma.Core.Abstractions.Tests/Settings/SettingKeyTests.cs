// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using System.Collections.Immutable;
using System.Text.Json;
using System.Text.Json.Serialization;
using Tellma.Core.Abstractions.Settings;

namespace Tellma.Core.Abstractions.Tests.Settings
{
    /// <summary>
    ///     Setting keys: the default serialized once under the platform's JSON rules (or the key's own
    ///     contract), the label key, the category and group rules, and reading a stored value back.
    /// </summary>
    public class SettingKeyTests
    {
        [Fact]
        public void A_key_derives_its_category_label_key_and_type_from_its_declaration()
        {
            SettingKey<bool> key = new("gl.posting.autoNumberOnPost", true, visibility: SettingVisibility.Client);

            Assert.Equal("gl.posting.autoNumberOnPost", key.Name);
            Assert.Equal("gl", key.Category);
            Assert.Null(key.Group);
            Assert.Equal(SettingVisibility.Client, key.Visibility);
            Assert.Equal(typeof(bool), key.ValueType);
            Assert.Null(key.References);
            Assert.Equal("Setting_gl_posting_autoNumberOnPost", key.LabelKey);
            Assert.True(key.DefaultValue);
            Assert.Equal(JsonValueKind.True, key.DefaultJson.ValueKind);
        }

        [Fact]
        public void A_stated_category_group_and_reference_are_kept()
        {
            SettingKey<int?> key = new("gl.defaultCenterId", null, category: "accounting", group: "posting", references: typeof(Center));

            Assert.Equal("accounting", key.Category);
            Assert.Equal("posting", key.Group);
            Assert.Equal(typeof(Center), key.References);
            Assert.Equal(typeof(int?), key.ValueType);
            Assert.Equal(JsonValueKind.Null, key.DefaultJson.ValueKind);
        }

        [Theory]
        [InlineData("gl.posting.autoNumberOnPost", "gl", "posting")]
        [InlineData("acme.invoicing.prefix", "invoicing", null)]
        [InlineData("acme.invoicing.prefix", "acme", "invoicing")]
        [InlineData("gl.defaultCenterId", "gl", null)]
        [InlineData("gl.posting.numbers.prefix", "gl", "posting.numbers")]
        [InlineData("acme.invoicing.numbers.prefix", "invoicing", "numbers")]
        [InlineData("gl.gl.prefix", "gl", null)]
        public void The_group_derives_from_the_middle_segments_less_a_leading_one_equal_to_the_category(string name, string category, string? expected)
        {
            Assert.Equal(expected, SettingKey.DeriveGroup(name, category));
        }

        [Fact]
        public void A_blank_name_is_refused()
        {
            Assert.ThrowsAny<ArgumentException>(() => new SettingKey<bool>(" ", false));
            Assert.ThrowsAny<ArgumentException>(() => new SettingKey<bool>(null!, false));
            Assert.Throws<ArgumentNullException>(() => SettingKey.DeriveGroup(null!, "gl"));
            Assert.Throws<ArgumentNullException>(() => SettingKey.DeriveGroup("gl.a", null!));
        }

        [Fact]
        public void Defaults_serialize_under_the_platforms_json_rules()
        {
            Assert.Equal("\"9007199254740993\"", Json(new SettingKey<long>("a.b", 9007199254740993L)));
            Assert.Equal("\"12.50\"", Json(new SettingKey<decimal>("a.b", 12.50m)));
            Assert.Equal("7", Json(new SettingKey<int>("a.b", 7)));
            Assert.Equal("\"2026-03-15\"", Json(new SettingKey<DateOnly>("a.b", new DateOnly(2026, 3, 15))));
            Assert.Equal("\"08:30:00.0000000\"", Json(new SettingKey<TimeOnly>("a.b", new TimeOnly(8, 30))));
            Assert.Equal("\"Quarterly\"", Json(new SettingKey<Period>("a.b", Period.Quarterly)));
            Assert.Equal("[1,2]", Json(new SettingKey<ImmutableArray<int>>("a.b", [1, 2])));
            Assert.Equal("\"فاتورة\"", Json(new SettingKey<string>("a.b", "فاتورة")));
            Assert.Equal("null", Json(new SettingKey<string?>("a.b", null)));
        }

        [Fact]
        public void A_key_with_its_own_contract_serializes_and_reads_through_it()
        {
            SettingKey<Prefix> key = new("a.b", new Prefix("INV", 6), typeInfo: PrefixContext.Default.Prefix);

            Assert.Same(PrefixContext.Default.Prefix, key.TypeInfo);
            Assert.Equal(/*lang=json,strict*/ """{"Text":"INV","Width":6}""", Json(key));
            Assert.True(key.TryRead(Parse(/*lang=json,strict*/ """{"Text":"PO","Width":4}"""), out Prefix value));
            Assert.Equal(new Prefix("PO", 4), value);
        }

        [Theory]
        [InlineData("\"9007199254740993\"", 9007199254740993L)]
        [InlineData("42", 42L)]
        public void A_long_reads_from_a_string_or_a_number(string json, long expected)
        {
            Assert.True(new SettingKey<long>("a.b", 0).TryRead(Parse(json), out long value));
            Assert.Equal(expected, value);
        }

        [Theory]
        [InlineData("\"7\"")]
        [InlineData("7.5")]
        [InlineData("true")]
        [InlineData("null")]
        [InlineData("[7]")]
        public void A_value_that_is_not_an_int_does_not_read_as_one(string json)
        {
            SettingKey<int> key = new("a.b", 3);

            Assert.False(key.TryRead(Parse(json), out int value));
            Assert.Equal(3, value);
        }

        [Theory]
        [InlineData("\"Yearly\"")]
        [InlineData("1")]
        [InlineData("\"Monthly, Quarterly\"")]
        public void An_enum_reads_only_as_a_defined_member_name(string json)
        {
            Assert.False(new SettingKey<Period>("a.b", Period.Monthly).TryRead(Parse(json), out _));
        }

        [Fact]
        public void Null_reads_only_for_a_key_whose_default_is_null()
        {
            Assert.True(new SettingKey<int?>("a.b", null).TryRead(Parse("null"), out int? nullable));
            Assert.Null(nullable);
            Assert.True(new SettingKey<string?>("a.b", null).TryRead(Parse("null"), out string? text));
            Assert.Null(text);
            Assert.False(new SettingKey<string>("a.b", "x").TryRead(Parse("null"), out string notNull));
            Assert.Equal("x", notNull);
            Assert.False(new SettingKey<int?>("a.b", 5).TryRead(Parse("null"), out _));
        }

        [Fact]
        public void An_array_setting_reads_as_an_immutable_array()
        {
            SettingKey<ImmutableArray<Period>> key = new("a.b", [Period.Monthly]);

            Assert.True(key.TryRead(Parse("[\"Quarterly\",\"Monthly\"]"), out ImmutableArray<Period> value));
            Assert.Equal([Period.Quarterly, Period.Monthly], value);
            Assert.False(key.TryRead(Parse("[\"Yearly\"]"), out _));
            Assert.False(key.TryRead(Parse("null"), out _));
        }

        [Fact]
        public void A_key_without_its_type_reads_and_checks_a_value()
        {
            SettingKey width = new SettingKey<int>("gl.posting.width", 6, validate: static w => w is >= 1 and <= 12 ? null : "Gl.WidthOutOfRange");

            Assert.True(width.TryReadValue(Parse("8"), out object? read, out string? code));
            Assert.Equal(8, read);
            Assert.Null(code);

            Assert.False(width.TryReadValue(Parse("40"), out read, out code));
            Assert.Equal(6, read);
            Assert.Equal("Gl.WidthOutOfRange", code);

            Assert.False(width.TryReadValue(Parse("\"8\""), out read, out code));
            Assert.Equal(6, read);
            Assert.Null(code);
        }

        /// <summary>A key's default as JSON text.</summary>
        /// <param name="key">The key.</param>
        /// <returns>The text.</returns>
        private static string Json(SettingKey key)
        {
            return key.DefaultJson.GetRawText();
        }

        /// <summary>Parses JSON text into a detached element.</summary>
        /// <param name="json">The text.</param>
        /// <returns>The element.</returns>
        private static JsonElement Parse(string json)
        {
            using var document = JsonDocument.Parse(json);
            return document.RootElement.Clone();
        }

        /// <summary>A referenced entity.</summary>
        private sealed class Center;
    }

    /// <summary>An enum setting's values.</summary>
    public enum Period
    {
        /// <summary>Every month.</summary>
        Monthly,

        /// <summary>Every quarter.</summary>
        Quarterly,
    }

    /// <summary>A pack's own setting shape, which the platform's rules do not know.</summary>
    /// <param name="Text">The prefix text.</param>
    /// <param name="Width">The number's width.</param>
    public sealed record Prefix(string Text, int Width);

    /// <summary>The pack's source-generated contract for <see cref="Prefix" />.</summary>
    [JsonSerializable(typeof(Prefix))]
    internal sealed partial class PrefixContext : JsonSerializerContext;
}
