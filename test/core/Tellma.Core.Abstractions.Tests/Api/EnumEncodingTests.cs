// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using System.Text.Json;
using Tellma.Core.Abstractions.Api;
using Tellma.Core.Abstractions.Crud;
using Tellma.Core.Abstractions.Errors;

namespace Tellma.Core.Abstractions.Tests.Api
{
    /// <summary>
    ///     An enum reads back only as what it defines: one member, or for a flags enum a list of
    ///     declared members. Everything else is the body's binding error, as a value and as a key.
    /// </summary>
    public class EnumEncodingTests
    {
        [Fact]
        public void A_member_round_trips_by_its_name_or_its_declared_wire_name()
        {
            Assert.Equal("\"ReadOnly\"", Write(McpExposure.ReadOnly));
            Assert.Equal(McpExposure.ReadOnly, Read<McpExposure>("\"ReadOnly\""));
            Assert.Equal(ForbiddenReason.OutsideFilter, Read<ForbiddenReason>("\"outside-filter\""));
        }

        [Theory]
        [InlineData("\"ReadOnly, Hidden\"")]
        [InlineData("\"Full,ReadOnly\"")]
        [InlineData("\"ReadOnly\\u002C Hidden\"")]
        [InlineData("\"Bogus\"")]
        [InlineData("\"1\"")]
        [InlineData("\"-1\"")]
        [InlineData("\" 1\"")]
        [InlineData("\"\\t+1\"")]
        [InlineData("1")]
        [InlineData("null")]
        public void An_enum_without_flags_refuses_a_list_a_number_and_an_unknown_name(string json)
        {
            _ = Assert.Throws<JsonException>(() => Read<McpExposure>(json));
        }

        [Fact]
        public void A_list_is_refused_even_when_its_bits_spell_a_defined_member()
        {
            // Red | Green is Both: the list must not silently become another member.
            Assert.Equal(Shade.Both, Read<Shade>("\"Both\""));
            _ = Assert.Throws<JsonException>(() => Read<Shade>("\"Red, Green\""));
            _ = Assert.Throws<JsonException>(() => Read<ForbiddenReason>("\"no-grant, restricted-path\""));
        }

        [Theory]
        [InlineData(/*lang=json,strict*/ """{"ReadOnly, Hidden":1}""")]
        [InlineData(/*lang=json,strict*/ """{"1":1}""")]
        [InlineData(/*lang=json,strict*/ """{"Bogus":1}""")]
        public void An_enum_key_refuses_what_an_enum_value_refuses(string json)
        {
            _ = Assert.Throws<JsonException>(() => Read<Dictionary<McpExposure, int>>(json));
        }

        [Fact]
        public void An_enum_key_round_trips()
        {
            Dictionary<ForbiddenReason, int> map = new() { [ForbiddenReason.NoGrant] = 1 };

            Assert.Equal(/*lang=json,strict*/ """{"no-grant":1}""", Write(map));
            Assert.Equal(1, Read<Dictionary<ForbiddenReason, int>>(/*lang=json,strict*/ """{"no-grant":1}""")[ForbiddenReason.NoGrant]);
        }

        [Fact]
        public void A_flags_enum_reads_a_list_of_declared_members()
        {
            Assert.Equal(StackOperations.Query | StackOperations.Details, Read<StackOperations>("\"Query, Details\""));
            Assert.Equal(StackOperations.All | StackOperations.Enlist, Read<StackOperations>(Write(StackOperations.All | StackOperations.Enlist)));
            Assert.Equal(
                StackOperations.Read,
                Read<Dictionary<StackOperations, int>>(/*lang=json,strict*/ """{"Query, Details, Export":1}""").Keys.Single());
        }

        [Theory]
        [InlineData("\"A, 2\"")]
        [InlineData("\"2, A\"")]
        [InlineData("\"A, 4\"")]
        [InlineData("\"A,+4\"")]
        [InlineData("\"A, Bogus\"")]
        [InlineData("2")]
        public void A_flags_enum_refuses_an_undeclared_bit_a_number_and_an_unknown_name(string json)
        {
            _ = Assert.Throws<JsonException>(() => Read<Gapped>(json));
        }

        [Fact]
        public void A_flags_enum_reads_its_declared_bits_around_a_gap()
        {
            Assert.Equal(Gapped.A | Gapped.C, Read<Gapped>("\"A, C\""));
        }

        [Fact]
        public void An_undefined_value_is_never_written()
        {
            _ = Assert.Throws<JsonException>(() => Write((McpExposure)7));
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

        /// <summary>An enum without flags whose third member equals the bits of the first two.</summary>
        private enum Shade
        {
            /// <summary>No colour.</summary>
            None,

            /// <summary>Red.</summary>
            Red,

            /// <summary>Green.</summary>
            Green,

            /// <summary>Red and green, as one member.</summary>
            Both,
        }

        /// <summary>A flags enum with an undeclared bit between its members.</summary>
        [Flags]
        private enum Gapped
        {
            /// <summary>No flag.</summary>
            None = 0,

            /// <summary>The first bit.</summary>
            A = 1,

            /// <summary>The third bit; the second is undeclared.</summary>
            C = 4,
        }
    }
}
