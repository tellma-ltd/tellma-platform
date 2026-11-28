// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using System.Text;
using System.Text.Json;
using Tellma.Core.Abstractions.Api;
using Tellma.Core.Abstractions.Errors;

namespace Tellma.Core.Abstractions.Tests.Api
{
    /// <summary>
    ///     The ids a problem body carries as display data: a JSON number within ±(2^53 − 1), a string
    ///     beyond it, and either read back.
    /// </summary>
    public class DisplayIdsTests
    {
        [Theory]
        [InlineData(0L, "0")]
        [InlineData(5L, "5")]
        [InlineData(9_007_199_254_740_991L, "9007199254740991")]
        [InlineData(-9_007_199_254_740_991L, "-9007199254740991")]
        [InlineData(9_007_199_254_740_992L, "\"9007199254740992\"")]
        [InlineData(-9_007_199_254_740_992L, "\"-9007199254740992\"")]
        [InlineData(long.MaxValue, "\"9223372036854775807\"")]
        public void An_id_is_a_number_while_it_is_a_safe_integer_and_a_string_beyond(long id, string expected)
        {
            Assert.Equal(expected, Written(writer => DisplayIds.Write(writer, id)));
            Assert.Equal(expected[0] != '"', DisplayIds.IsSafeInteger(id));
        }

        [Fact]
        public void A_conflict_writes_its_id_as_display_data()
        {
            ConcurrencyConflict small = new(42, "stamp-1", 3, "Sara", IsMissing: false);
            ConcurrencyConflict large = new(9_007_199_254_740_993, ModifiedAt: null, ModifiedById: null, ModifiedByName: null, IsMissing: true);

            Assert.Equal(
                /*lang=json,strict*/ """{"id":42,"modifiedAt":"stamp-1","modifiedById":3,"modifiedByName":"Sara","isMissing":false}""",
                JsonSerializer.Serialize(small, TellmaJson.Options));
            Assert.Equal(
                /*lang=json,strict*/ """{"id":"9007199254740993","isMissing":true}""",
                JsonSerializer.Serialize(large, TellmaJson.Options));
        }

        [Fact]
        public void A_confirmation_writes_each_of_its_ids_as_display_data()
        {
            ConfirmationRequest request = new("Inventory.BackdatedRecost", [7, 9_007_199_254_740_993], new Dictionary<string, object?>());

            Assert.Equal(
                /*lang=json,strict*/ """{"key":"Inventory.BackdatedRecost","ids":[7,"9007199254740993"],"arguments":{}}""",
                JsonSerializer.Serialize(request, TellmaJson.Options));
        }

        [Fact]
        public void A_problem_bodys_ids_read_back_from_numbers_and_strings()
        {
            ConcurrencyConflict conflict = JsonSerializer.Deserialize<ConcurrencyConflict>(
                /*lang=json,strict*/ """{"id":"9007199254740993","isMissing":true}""", TellmaJson.Options)!;
            ConfirmationRequest request = JsonSerializer.Deserialize<ConfirmationRequest>(
                /*lang=json,strict*/ """{"key":"K.Warn","ids":[7,"9007199254740993"],"arguments":{}}""", TellmaJson.Options)!;

            Assert.Equal(9_007_199_254_740_993, conflict.Id);
            Assert.Equal([7, 9_007_199_254_740_993], request.Ids);
        }

        [Theory]
        [InlineData(/*lang=json,strict*/ """{"id":1.5,"isMissing":true}""")]
        [InlineData(/*lang=json,strict*/ """{"id":"x","isMissing":true}""")]
        [InlineData(/*lang=json,strict*/ """{"id":true,"isMissing":true}""")]
        [InlineData(/*lang=json,strict*/ """{"id":"9223372036854775808","isMissing":true}""")]
        public void A_display_id_that_is_not_an_integer_in_range_is_refused(string json)
        {
            _ = Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<ConcurrencyConflict>(json, TellmaJson.Options));
        }

        /// <summary>Runs a write on a fresh writer and returns its text.</summary>
        /// <param name="write">The write.</param>
        /// <returns>The JSON text.</returns>
        private static string Written(Action<Utf8JsonWriter> write)
        {
            using MemoryStream stream = new();
            using (Utf8JsonWriter writer = new(stream))
            {
                write(writer);
            }

            return Encoding.UTF8.GetString(stream.ToArray());
        }
    }
}
