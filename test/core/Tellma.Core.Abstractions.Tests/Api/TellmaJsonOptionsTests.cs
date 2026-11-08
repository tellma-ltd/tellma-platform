// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;
using Tellma.Core.Abstractions.Api;
using Tellma.Core.Abstractions.Errors;

namespace Tellma.Core.Abstractions.Tests.Api
{
    /// <summary>
    ///     The platform's one set of JSON rules: the options instance is pinned, a host's options get
    ///     the same rules, and the member-level rules behave as the wire contract states.
    /// </summary>
    public class TellmaJsonOptionsTests
    {
        private static JsonSerializerOptions Options => TellmaJson.Options;

        [Fact]
        public void The_options_are_read_only()
        {
            Assert.True(Options.IsReadOnly);
            _ = Assert.Throws<InvalidOperationException>(() => Options.MaxDepth = 64);
        }

        [Fact]
        public void The_options_pin_the_platform_rules()
        {
            Assert.Same(JsonNamingPolicy.CamelCase, Options.PropertyNamingPolicy);
            Assert.True(Options.PropertyNameCaseInsensitive);
            Assert.Equal(JsonIgnoreCondition.WhenWritingNull, Options.DefaultIgnoreCondition);
            Assert.Equal(JsonNumberHandling.Strict, Options.NumberHandling);
            Assert.False(Options.AllowDuplicateProperties);
            Assert.Equal(JsonUnmappedMemberHandling.Skip, Options.UnmappedMemberHandling);
            Assert.Equal(16, Options.MaxDepth);
            Assert.Null(Options.DictionaryKeyPolicy);
        }

        [Fact]
        public void The_platform_records_resolve_first_and_everything_else_by_reflection()
        {
            IList<IJsonTypeInfoResolver> chain = Options.TypeInfoResolverChain;

            Assert.Same(TellmaApiJsonContext.Default, chain[0]);
            _ = Assert.IsType<DefaultJsonTypeInfoResolver>(chain[^1]);
        }

        [Fact]
        public void The_platform_converters_come_first_in_a_fixed_order()
        {
            string[] names = [.. Options.Converters.Select(static converter => converter.GetType().Name)];

            Assert.Equal(
                [
                    "StrictEnumJsonConverter",
                    "Int64JsonConverter",
                    "DecimalJsonConverter",
                    "DateTimeJsonConverter",
                    "DateTimeOffsetJsonConverter",
                    "TimeOnlyJsonConverter",
                ],
                names);
        }

        [Theory]
        [InlineData(typeof(QueryRequest))]
        [InlineData(typeof(GetRequest))]
        [InlineData(typeof(AllRequest))]
        [InlineData(typeof(IdsRequest))]
        [InlineData(typeof(IdRequest))]
        [InlineData(typeof(GetByIdsRequest))]
        [InlineData(typeof(EntityActionRequest))]
        [InlineData(typeof(ParentIdsRequest))]
        [InlineData(typeof(DeleteByQueryRequest))]
        [InlineData(typeof(AccessHoldersRequest))]
        [InlineData(typeof(AffectedResult))]
        [InlineData(typeof(JobAccepted))]
        [InlineData(typeof(SecurableSummary))]
        [InlineData(typeof(QueryDiagnostic))]
        [InlineData(typeof(ConcurrencyConflict))]
        [InlineData(typeof(ConfirmationRequest))]
        [InlineData(typeof(long))]
        [InlineData(typeof(decimal))]
        [InlineData(typeof(DateTimeOffset))]
        [InlineData(typeof(byte[]))]
        public void The_wire_records_and_scalars_have_generated_metadata(Type type)
        {
            Assert.NotNull(TellmaApiJsonContext.Default.GetTypeInfo(type));
        }

        [Fact]
        public void Configure_gives_a_hosts_options_the_same_rules()
        {
            // The web host's options start from the framework's web defaults, which read quoted numbers.
            JsonSerializerOptions host = new(JsonSerializerDefaults.Web);

            TellmaJson.Configure(host);

            Assert.Equal(JsonNumberHandling.Strict, host.NumberHandling);
            Assert.False(host.AllowDuplicateProperties);
            Assert.Equal(16, host.MaxDepth);
            Assert.Same(TellmaApiJsonContext.Default, host.TypeInfoResolverChain[0]);

            Probe probe = new() { Id = 9_007_199_254_740_993, Amount = 1234.5000m, Name = null };
            Assert.Equal(JsonSerializer.Serialize(probe, Options), JsonSerializer.Serialize(probe, host));
        }

        [Fact]
        public void The_platform_converters_win_over_a_converter_the_host_added()
        {
            JsonSerializerOptions host = new();
            host.Converters.Add(new NumericInt64Converter());

            TellmaJson.Configure(host);

            Assert.Equal("\"5\"", JsonSerializer.Serialize(5L, host));
        }

        [Fact]
        public void Configuring_twice_changes_nothing()
        {
            JsonSerializerOptions host = new();
            TellmaJson.Configure(host);
            int converters = host.Converters.Count;
            int resolvers = host.TypeInfoResolverChain.Count;

            TellmaJson.Configure(host);

            Assert.Equal(converters, host.Converters.Count);
            Assert.Equal(resolvers, host.TypeInfoResolverChain.Count);
        }

        [Fact]
        public void Configure_refuses_read_only_and_missing_options()
        {
            JsonSerializerOptions frozen = new();
            frozen.MakeReadOnly(populateMissingResolver: true);

            _ = Assert.Throws<InvalidOperationException>(() => TellmaJson.Configure(frozen));
            _ = Assert.Throws<ArgumentNullException>(() => TellmaJson.Configure(null!));
        }

        [Fact]
        public void Members_are_camel_case_out_and_matched_without_case_in()
        {
            Assert.Equal(/*lang=json,strict*/ """{"id":"7","amount":"1.5","name":"East"}""", JsonSerializer.Serialize(new Probe { Id = 7, Amount = 1.5m, Name = "East" }, Options));

            Probe read = JsonSerializer.Deserialize<Probe>(/*lang=json,strict*/ """{"ID":"7","AMOUNT":"1.5","Name":"East"}""", Options)!;

            Assert.Equal((7L, 1.5m, "East"), (read.Id, read.Amount, read.Name));
        }

        [Fact]
        public void A_null_member_is_omitted()
        {
            Assert.Equal(/*lang=json,strict*/ """{"id":"1","amount":"0"}""", JsonSerializer.Serialize(new Probe { Id = 1 }, Options));
        }

        [Fact]
        public void An_unknown_member_is_skipped()
        {
            Probe read = JsonSerializer.Deserialize<Probe>(/*lang=json,strict*/ """{"id":"3","renamedLongAgo":{"deep":[1,2]}}""", Options)!;

            Assert.Equal(3L, read.Id);
        }

        [Fact]
        public void A_duplicate_member_is_refused()
        {
            _ = Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<Probe>(/*lang=json*/ """{"id":"1","id":"2"}""", Options));
        }

        [Fact]
        public void Nesting_is_bounded_at_sixteen()
        {
            // A JsonElement reads any depth up to the bound: sixteen nested arrays pass, seventeen fail.
            static string Nested(int depth)
            {
                return new string('[', depth) + new string(']', depth);
            }

            _ = JsonSerializer.Deserialize<JsonElement>(Nested(16), Options);
            _ = Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<JsonElement>(Nested(17), Options));
        }

        [Fact]
        public void Enums_are_member_names_and_an_integer_is_refused()
        {
            Assert.Equal("\"ReadOnly\"", JsonSerializer.Serialize(McpExposure.ReadOnly, Options));
            Assert.Equal(McpExposure.Hidden, JsonSerializer.Deserialize<McpExposure>("\"Hidden\"", Options));

            _ = Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<McpExposure>("1", Options));
        }

        [Theory]
        [InlineData("مركز الرياض")]
        [InlineData("ማዕከል")]
        [InlineData("Zürich – Genève")]
        public void Text_in_any_script_is_written_as_itself(string text)
        {
            string json = JsonSerializer.Serialize(text, Options);

            Assert.Equal("\"" + text + "\"", json);
            Assert.Equal(text, JsonSerializer.Deserialize<string>(json, Options));
        }

        [Theory]
        [InlineData("<script>", "\"\\u003Cscript\\u003E\"")]
        [InlineData("a&b", "\"a\\u0026b\"")]
        [InlineData("it's", "\"it\\u0027s\"")]
        [InlineData("say \"hi\"", "\"say \\u0022hi\\u0022\"")]
        [InlineData("a+b", "\"a\\u002Bb\"")]
        public void Characters_html_gives_meaning_to_stay_escaped(string text, string json)
        {
            Assert.Equal(json, JsonSerializer.Serialize(text, Options));
            Assert.Equal(text, JsonSerializer.Deserialize<string>(json, Options));
        }

        [Fact]
        public void A_hosts_options_write_text_the_same_way()
        {
            JsonSerializerOptions host = new(JsonSerializerDefaults.Web);

            TellmaJson.Configure(host);

            Assert.Equal("\"مركز \\u003Cb\\u003E\"", JsonSerializer.Serialize("مركز <b>", host));
        }

        [Fact]
        public void An_enum_member_may_declare_its_wire_name()
        {
            Assert.Equal("\"outside-filter\"", JsonSerializer.Serialize(ForbiddenReason.OutsideFilter, Options));
            Assert.Equal(ForbiddenReason.RestrictedPath, JsonSerializer.Deserialize<ForbiddenReason>("\"restricted-path\"", Options));
        }

        /// <summary>A small entity-like class resolved by reflection.</summary>
        private sealed class Probe
        {
            /// <summary>A 64-bit id.</summary>
            public long Id { get; set; }

            /// <summary>A decimal amount.</summary>
            public decimal Amount { get; set; }

            /// <summary>An optional name.</summary>
            public string? Name { get; set; }
        }

        /// <summary>A host converter that would write a long as a number.</summary>
        private sealed class NumericInt64Converter : JsonConverter<long>
        {
            /// <inheritdoc />
            public override long Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
            {
                return reader.GetInt64();
            }

            /// <inheritdoc />
            public override void Write(Utf8JsonWriter writer, long value, JsonSerializerOptions options)
            {
                writer.WriteNumberValue(value);
            }
        }
    }
}
