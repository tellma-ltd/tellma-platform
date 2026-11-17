// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using System.Text.Json;
using Tellma.Core.Abstractions.Data;
using Tellma.Core.Abstractions.Errors;
using Tellma.Core.Crud.Query;
using Tellma.Core.Queryex;

namespace Tellma.Core.Tests.Crud.Query
{
    /// <summary>
    ///     The arguments' conversion: each type from its JSON form by the wire's exact grammars, a
    ///     JSON null and a missing value bound to null, a value no clause mentions ignored, and every
    ///     other form refused naming its parameter.
    /// </summary>
    public sealed class QueryArgumentValuesTests
    {
        [Theory]
        [InlineData(QueryexType.QxBool, "true", true)]
        [InlineData(QueryexType.QxBool, "false", false)]
        [InlineData(QueryexType.QxString, "\"ab\"", "ab")]
        public void A_scalar_converts_from_its_json_form(QueryexType type, string json, object expected)
        {
            Assert.Equal(expected, Convert(type, json));
        }

        [Theory]
        [InlineData("12.50", "12.50")]
        [InlineData("1.5e2", "150")]
        [InlineData("\"-0.0001\"", "-0.0001")]
        [InlineData("79228162514264337593543950335", "79228162514264337593543950335")]
        public void A_number_converts_exactly_from_a_json_number_or_the_wires_decimal_string(string json, string expected)
        {
            Assert.Equal(decimal.Parse(expected, System.Globalization.CultureInfo.InvariantCulture), Convert(QueryexType.QxNumeric, json));
        }

        [Fact]
        public void The_temporal_and_guid_types_convert_from_their_wire_strings()
        {
            Assert.Equal(new DateOnly(2026, 11, 17), Convert(QueryexType.QxDate, "\"2026-11-17\""));
            Assert.Equal(new DateTime(2026, 11, 17, 9, 30, 0), Convert(QueryexType.QxDateTime, "\"2026-11-17T09:30:00\""));
            Assert.Equal(new DateTimeOffset(2026, 11, 17, 9, 30, 0, TimeSpan.FromHours(3)), Convert(QueryexType.QxDateTimeOffset, "\"2026-11-17T09:30:00+03:00\""));
            Assert.Equal(Guid.Parse("8f0c2c1e-3d4a-4b5c-9d6e-7f8a9b0c1d2e"), Convert(QueryexType.QxGuid, "\"8f0c2c1e-3d4a-4b5c-9d6e-7f8a9b0c1d2e\""));
        }

        [Theory]
        [InlineData(QueryexType.QxBool, "1")]
        [InlineData(QueryexType.QxBool, "\"true\"")]
        [InlineData(QueryexType.QxString, "12")]
        [InlineData(QueryexType.QxNumeric, "\"1e2\"")]
        [InlineData(QueryexType.QxNumeric, "\"12,5\"")]
        [InlineData(QueryexType.QxNumeric, "true")]
        [InlineData(QueryexType.QxNumeric, "79228162514264337593543950336")]
        [InlineData(QueryexType.QxDate, "\"17/11/2026\"")]
        [InlineData(QueryexType.QxDate, "\"2026-11-17T00:00:00\"")]
        [InlineData(QueryexType.QxDateTimeOffset, "\"2026-11-17T09:30:00\"")]
        [InlineData(QueryexType.QxGuid, "\"8f0c2c1e3d4a4b5c9d6e7f8a9b0c1d2e\"")]
        [InlineData(QueryexType.QxGeography, "\"POINT(0 0)\"")]
        [InlineData(QueryexType.QxString, "[\"a\"]")]
        public void Any_other_form_is_a_bad_request_naming_the_parameter(QueryexType type, string json)
        {
            BadRequestException refused = Assert.Throws<BadRequestException>(() => Convert(type, json));

            Assert.Equal("p", refused.Arguments["parameter"]);
            Assert.Contains("'p'", refused.Message, StringComparison.Ordinal);
        }

        [Fact]
        public void Every_declared_parameter_is_bound_a_json_null_or_a_missing_value_to_null_and_an_unmentioned_value_ignored()
        {
            using var values = JsonDocument.Parse("""{ "Price": 5, "code": null, "unused": "x" }""");
            Dictionary<string, JsonElement> arguments = new(StringComparer.OrdinalIgnoreCase);
            foreach (JsonProperty property in values.RootElement.EnumerateObject())
            {
                arguments[property.Name] = property.Value;
            }

            QueryArguments bound = QueryArgumentValues.Bind(
                [
                    new QueryexParameterDeclaration("price", QueryexType.QxNumeric, IsNotNull: false),
                    new QueryexParameterDeclaration("code", QueryexType.QxString, IsNotNull: false),
                    new QueryexParameterDeclaration("missing", QueryexType.QxDate, IsNotNull: false),
                ],
                arguments);

            Assert.Equal(["price", "code", "missing"], bound.Declarations.Select(static declaration => declaration.Name));
            Assert.Equal(5m, bound.Values["price"]);
            Assert.Null(bound.Values["code"]);
            Assert.Null(bound.Values["missing"]);
            Assert.Empty(bound.Omitted);
        }

        /// <summary>Converts one JSON text for a parameter <c>p</c> of a type.</summary>
        /// <param name="type">The type.</param>
        /// <param name="json">The JSON text.</param>
        /// <returns>The value.</returns>
        private static object? Convert(QueryexType type, string json)
        {
            using var document = JsonDocument.Parse(json);
            return QueryArgumentValues.Convert(new QueryexParameterDeclaration("p", type, IsNotNull: false), document.RootElement.Clone());
        }
    }
}
