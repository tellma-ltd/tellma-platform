// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using System.Text.Json;
using Tellma.Core.Abstractions.Access;
using Tellma.Core.Abstractions.Api;
using Tellma.Core.Abstractions.Data;

namespace Tellma.Core.Abstractions.Tests.Api
{
    /// <summary>The save, access-check and entities records bind the bodies a client sends and write the shapes it reads.</summary>
    public class CrudWireRecordTests
    {
        [Fact]
        public void A_save_request_returns_the_saved_rows_under_a_checked_save_by_default()
        {
            SaveRequest<Row> request = Read<SaveRequest<Row>>(/*lang=json,strict*/ """{ "entities": [{ "name": "east" }] }""");

            Assert.Equal("east", Assert.Single(request.Entities).Name);
            Assert.True(request.ReturnEntities);
            Assert.Null(request.Include);
            Assert.Equal(ConcurrencyMode.Check, request.Concurrency);
        }

        [Theory]
        [InlineData(/*lang=json,strict*/ """{ "returnEntities": false }""")]
        [InlineData(/*lang=json,strict*/ """{ "entities": null }""")]
        public void A_save_request_names_its_rows(string body)
        {
            _ = Assert.Throws<JsonException>(() => Read<SaveRequest<Row>>(body));
        }

        [Fact]
        public void An_access_check_request_lists_its_securables()
        {
            AccessCheckRequest request = Read<AccessCheckRequest>(
                /*lang=json,strict*/ """{ "userId": 7, "securables": [{ "resource": "gl.Center", "action": "Read" }] }""");

            Assert.Equal(7, request.UserId);
            Assert.Equal([new SecurableRef("gl.Center", "Read")], request.Securables);
            Assert.Equal(/*lang=json,strict*/ """{"userId":7,"securables":[{"resource":"gl.Center","action":"Read"}]}""", Write(request));
            _ = Assert.Throws<JsonException>(() => Read<AccessCheckRequest>(/*lang=json,strict*/ """{ "userId": 7 }"""));
        }

        [Fact]
        public void An_entities_result_writes_its_ids_as_wire_strings_and_its_rows()
        {
            EntitiesResult<Row> result = new() { Ids = [4], Entities = [new Row { Name = "east" }] };

            Assert.Equal(/*lang=json,strict*/ """{"ids":["4"],"entities":[{"name":"east"}]}""", Write(result));
            _ = Assert.Throws<ArgumentNullException>(() => new EntitiesResult<Row> { Ids = null!, Entities = [] });
            _ = Assert.Throws<ArgumentNullException>(() => new EntitiesResult<Row> { Ids = [], Entities = null! });
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

        /// <summary>A row of a save body.</summary>
        public sealed class Row
        {
            /// <summary>A scalar.</summary>
            public string? Name { get; set; }
        }
    }
}
