// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Microsoft.OpenApi;
using System.Net;
using System.Text.Json;
using Tellma.Core.AspNetCore.OpenApi;
using Tellma.Core.AspNetCore.Tests.Api;
using Tellma.Core.AspNetCore.Tests.Infrastructure;

namespace Tellma.Core.AspNetCore.Tests.OpenApi
{
    /// <summary>
    ///     The web surface's OpenAPI document: served in Development alone, OpenAPI 3.1, the web
    ///     surface's operations only, each stamped with its securable and tagged by its resource,
    ///     decimals and longs as the string-or-number the wire carries, and the members a save never
    ///     takes marked read-only.
    /// </summary>
    public sealed class WebOpenApiTests
    {
        private static CancellationToken Token => TestContext.Current.CancellationToken;

        [Theory]
        [InlineData(typeof(decimal), false)]
        [InlineData(typeof(decimal?), true)]
        public void A_decimal_is_a_string_or_a_number_of_format_decimal(Type type, bool nullable)
        {
            OpenApiSchema schema = new() { Type = JsonSchemaType.Number | (nullable ? JsonSchemaType.Null : 0), Pattern = "^-?\\d+$" };

            ScalarSchemas.Apply(schema, type);

            Assert.Equal(JsonSchemaType.String | JsonSchemaType.Number | (nullable ? JsonSchemaType.Null : 0), schema.Type);
            Assert.Equal("decimal", schema.Format);
            Assert.Null(schema.Pattern);
        }

        [Theory]
        [InlineData(typeof(long), false)]
        [InlineData(typeof(long?), true)]
        public void A_long_is_a_string_or_an_integer_of_format_int64(Type type, bool nullable)
        {
            OpenApiSchema schema = new() { Type = JsonSchemaType.Integer | JsonSchemaType.String, Pattern = "^-?(?:0|[1-9]\\d*)$" };

            ScalarSchemas.Apply(schema, type);

            Assert.Equal(JsonSchemaType.String | JsonSchemaType.Integer | (nullable ? JsonSchemaType.Null : 0), schema.Type);
            Assert.Equal("int64", schema.Format);
            Assert.Null(schema.Pattern);
        }

        [Fact]
        public void Another_scalar_is_left_as_the_exporter_wrote_it()
        {
            OpenApiSchema schema = new() { Type = JsonSchemaType.Integer, Format = "int32" };

            ScalarSchemas.Apply(schema, typeof(int));

            Assert.Equal(JsonSchemaType.Integer, schema.Type);
            Assert.Equal("int32", schema.Format);
        }

        [Fact]
        public async Task The_document_is_not_served_outside_development()
        {
            await using TellmaTestHost host = await StartAsync("Production");

            using HttpResponseMessage response = await host.Client.GetAsync("/openapi/web.json", Token);

            Assert.NotEqual(HttpStatusCode.OK, response.StatusCode);
        }

        [Fact]
        public async Task The_document_describes_the_web_surface_alone_with_securables_and_tags()
        {
            await using TellmaTestHost host = await StartAsync("Development");

            JsonElement document = await ReadAsync(host);

            // --- OpenAPI 3.1, the web surface's paths alone ---
            Assert.StartsWith("3.1", document.GetProperty("openapi").GetString(), StringComparison.Ordinal);
            string[] paths = [.. document.GetProperty("paths").EnumerateObject().Select(static path => path.Name)];
            Assert.NotEmpty(paths);
            Assert.All(paths, static path => Assert.StartsWith("/{tenantId}/api/web/", path, StringComparison.Ordinal));

            // --- A stack's query requires its securable, and is tagged by its resource segment ---
            JsonElement query = Operation(document, "/{tenantId}/api/web/ledgers/query");
            JsonElement securable = query.GetProperty(WebOpenApi.SecurableExtension);
            Assert.Equal(ProjectionModel.Ledgers, securable.GetProperty("resource").GetString());
            Assert.Equal("Read", securable.GetProperty("action").GetString());
            Assert.Equal(["ledgers"], query.GetProperty("tags").EnumerateArray().Select(static tag => tag.GetString()));

            // --- Every tag an operation names is declared, and no other ---
            HashSet<string> declared = [.. document.GetProperty("tags").EnumerateArray().Select(static tag => tag.GetProperty("name").GetString()!)];
            HashSet<string> used = [.. document.GetProperty("paths").EnumerateObject()
                .SelectMany(static path => path.Value.EnumerateObject())
                .Where(static operation => operation.Value.TryGetProperty("tags", out _))
                .SelectMany(static operation => operation.Value.GetProperty("tags").EnumerateArray())
                .Select(static tag => tag.GetString()!)];
            Assert.Equal(used, declared);
        }

        [Fact]
        public async Task An_entity_schema_marks_what_a_save_never_takes_and_encodes_its_long_key()
        {
            await using TellmaTestHost host = await StartAsync("Development");

            JsonElement document = await ReadAsync(host);
            JsonElement ledger = document.GetProperty("components").GetProperty("schemas").GetProperty(nameof(PjLedger));
            JsonElement properties = ledger.GetProperty("properties");

            // --- The long key: a string or an integer of format int64 ---
            JsonElement id = properties.GetProperty("id");
            Assert.Equal(["integer", "string"], id.GetProperty("type").EnumerateArray().Select(static type => type.GetString()).Order(StringComparer.Ordinal));
            Assert.Equal("int64", id.GetProperty("format").GetString());

            // --- The audit members are the server's, and the name is the caller's ---
            Assert.True(properties.GetProperty("createdAt").GetProperty("readOnly").GetBoolean());
            Assert.True(properties.GetProperty("modifiedById").GetProperty("readOnly").GetBoolean());
            Assert.False(properties.GetProperty("name").TryGetProperty("readOnly", out _));

            // --- What a navigation's related entity carries, and no trace of the unknown-member count ---
            Assert.Equal(["id", "name", "name2", "name3", "imageId"], ledger.GetProperty(WebOpenApi.RelatedSelectExtension).GetProperty("createdBy").EnumerateArray().Select(static name => name.GetString()));
            Assert.DoesNotContain(properties.EnumerateObject(), static property => property.Name.StartsWith('$'));
            Assert.False(ledger.TryGetProperty("additionalProperties", out _));
        }

        /// <summary>Starts a host composing the projection suite's model.</summary>
        /// <param name="environment">The host's environment.</param>
        /// <returns>The host.</returns>
        private static Task<TellmaTestHost> StartAsync(string environment)
        {
            return TellmaTestHost.StartAsync(options =>
            {
                options.Environment = environment;
                options.Compose = ProjectionModel.Compose;
                options.ConfigureServices = WebApiProjectionTests.StandIns;
            });
        }

        /// <summary>Reads the document.</summary>
        /// <param name="host">The host.</param>
        /// <returns>The document.</returns>
        private static async Task<JsonElement> ReadAsync(TellmaTestHost host)
        {
            using HttpResponseMessage response = await host.Client.GetAsync("/openapi/web.json", Token);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            string body = await response.Content.ReadAsStringAsync(Token);
            using var json = JsonDocument.Parse(body);
            return json.RootElement.Clone();
        }

        /// <summary>The one operation of a path.</summary>
        /// <param name="document">The document.</param>
        /// <param name="path">The path.</param>
        /// <returns>The operation.</returns>
        private static JsonElement Operation(JsonElement document, string path)
        {
            return Assert.Single(document.GetProperty("paths").GetProperty(path).EnumerateObject()).Value;
        }
    }
}
