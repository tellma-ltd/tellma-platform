// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using System.Diagnostics.CodeAnalysis;
using System.Text.Json;
using Tellma.Core.Abstractions.Errors;
using Tellma.Core.Mcp.Tools;

namespace Tellma.Core.Mcp.Tests.Tools
{
    /// <summary>
    ///     The tools' argument reader: undeclared names refused, JSON null read as absent, and each
    ///     reader refusing a value of the wrong shape as a bad request naming the argument.
    /// </summary>
    public sealed class ToolArgumentsTests
    {
        /// <summary>The names the arguments under test declare.</summary>
        private static readonly string[] Declared = ["entity", "top", "ids", "flag", "format", "arguments", "include"];

        [Fact]
        public void An_undeclared_argument_is_refused_by_name()
        {
            BadRequestException refused = Assert.Throws<BadRequestException>(() => Read("""{"entity":"gl.Center","limit":5}"""));

            Assert.Equal("limit", refused.Arguments["path"]);
        }

        [Fact]
        public void A_json_null_and_a_blank_string_are_absent()
        {
            ToolArguments arguments = Read("""{"entity":"  ","top":null}""");

            Assert.Null(arguments.String("entity"));
            Assert.Null(arguments.Integer("top", 1, 10));
            Assert.False(arguments.Has("top"));
        }

        [Theory]
        [InlineData("0")]
        [InlineData("11")]
        [InlineData("1.5")]
        [InlineData("\"5\"")]
        public void An_integer_outside_its_range_or_not_an_integer_is_refused(string value)
        {
            ToolArguments arguments = Read($$"""{"top":{{value}}}""");

            BadRequestException refused = Assert.Throws<BadRequestException>(() => arguments.Integer("top", 1, 10));
            Assert.Equal("top", refused.Arguments["path"]);
        }

        [Fact]
        public void Ids_take_integers_and_digit_strings_and_refuse_anything_else()
        {
            Assert.Equal([3L, 9007199254740993L], Read("""{"ids":[3,"9007199254740993"]}""").Ids("ids", 1, 5));
            _ = Assert.Throws<BadRequestException>(() => Read("""{"ids":[0]}""").Ids("ids", 1, 5));
            _ = Assert.Throws<BadRequestException>(() => Read("""{"ids":["-1"]}""").Ids("ids", 1, 5));
            _ = Assert.Throws<BadRequestException>(() => Read("""{"ids":[]}""").Ids("ids", 1, 5));
            _ = Assert.Throws<BadRequestException>(() => Read("""{"ids":[1,2,3]}""").Ids("ids", 1, 2));
        }

        [Fact]
        public void A_closed_set_matches_ignoring_case_and_answers_its_own_spelling()
        {
            Assert.Equal("objects", Read("""{"format":"OBJECTS"}""").OneOf("format", ["table", "objects"]));
            _ = Assert.Throws<BadRequestException>(() => Read("""{"format":"csv"}""").OneOf("format", ["table", "objects"]));
        }

        [Fact]
        public void A_boolean_refuses_a_string()
        {
            Assert.True(Read("""{"flag":true}""").Boolean("flag"));
            Assert.False(Read("""{}""").Boolean("flag"));
            _ = Assert.Throws<BadRequestException>(() => Read("""{"flag":"true"}""").Boolean("flag"));
        }

        [Fact]
        public void Scalars_refuse_a_nested_value_and_a_name_given_twice_in_another_case()
        {
            IReadOnlyDictionary<string, JsonElement> values = Read("""{"arguments":{"code":"A","n":1,"none":null}}""").Scalars("arguments")!;
            Assert.Equal("A", values["CODE"].GetString());
            _ = Assert.Throws<BadRequestException>(() => Read("""{"arguments":{"code":["A"]}}""").Scalars("arguments"));
            _ = Assert.Throws<BadRequestException>(() => Read("""{"arguments":{"code":"A","Code":"B"}}""").Scalars("arguments"));
        }

        /// <summary>Reads a call's arguments from JSON.</summary>
        /// <param name="json">The arguments object.</param>
        /// <returns>The reader.</returns>
        private static ToolArguments Read([StringSyntax(StringSyntaxAttribute.Json)] string json)
        {
            Dictionary<string, JsonElement> values = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(json)!;
            return new ToolArguments(values, Declared);
        }
    }
}
