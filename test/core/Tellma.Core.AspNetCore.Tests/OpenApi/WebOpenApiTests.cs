// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Microsoft.OpenApi;
using System.Net;
using System.Text.Json;
using Tellma.Core.Abstractions.Api;
using Tellma.Core.Abstractions.Composition;
using Tellma.Core.AspNetCore.OpenApi;
using Tellma.Core.AspNetCore.Tests.Api;
using Tellma.Testing.WebHost;

namespace Tellma.Core.AspNetCore.Tests.OpenApi
{
    /// <summary>
    ///     The web surface's OpenAPI document: served in Development alone, OpenAPI 3.1, the web
    ///     surface's operations only, each stamped with its securable and tagged by its resource,
    ///     decimals and longs as the string-or-number the wire carries, the members a save never
    ///     takes marked read-only, and every schema its real type, however deep it nests.
    /// </summary>
    /// <param name="fixture">The host the cases share.</param>
    public sealed class WebOpenApiTests(WebOpenApiTests.Host fixture) : IClassFixture<WebOpenApiTests.Host>
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
            TellmaTestHost host = fixture.Started;

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
        public async Task An_entity_schema_marks_what_a_save_never_takes_and_encodes_its_long_key_and_decimals()
        {
            TellmaTestHost host = fixture.Started;

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

            // --- A write-once and a derived member: read-only, each with its flag ---
            JsonElement code = properties.GetProperty("code");
            Assert.True(code.GetProperty("readOnly").GetBoolean());
            Assert.True(code.GetProperty(WebOpenApi.WriteOnceExtension).GetBoolean());
            Assert.False(code.TryGetProperty(WebOpenApi.DerivedExtension, out _));
            JsonElement balance = properties.GetProperty("balance");
            Assert.True(balance.GetProperty("readOnly").GetBoolean());
            Assert.True(balance.GetProperty(WebOpenApi.DerivedExtension).GetBoolean());

            // --- A decimal, through the generator: a string or a number of format decimal ---
            foreach (JsonElement amount in (JsonElement[])[balance, properties.GetProperty("creditLimit")])
            {
                Assert.Equal(["number", "string"], amount.GetProperty("type").EnumerateArray().Select(static type => type.GetString()).Order(StringComparer.Ordinal));
                Assert.Equal("decimal", amount.GetProperty("format").GetString());
            }

            Assert.False(properties.GetProperty("creditLimit").TryGetProperty("readOnly", out _));

            // --- A server-owned enum: a reference, read-only beside its $ref, its shared component untouched ---
            JsonElement state = properties.GetProperty("state");
            string reference = state.GetProperty("$ref").GetString()!;
            Assert.True(state.GetProperty("readOnly").GetBoolean());
            JsonElement component = document.GetProperty("components").GetProperty("schemas").GetProperty(reference[(reference.LastIndexOf('/') + 1)..]);
            Assert.False(component.TryGetProperty("readOnly", out _));

            // --- What a navigation's related entity carries, and no trace of the unknown-member count ---
            Assert.Equal(["id", "name", "name2", "name3", "imageId"], ledger.GetProperty(WebOpenApi.RelatedSelectExtension).GetProperty("createdBy").EnumerateArray().Select(static name => name.GetString()));
            Assert.DoesNotContain(properties.EnumerateObject(), static property => property.Name.StartsWith('$'));
            Assert.False(ledger.TryGetProperty("additionalProperties", out _));
        }

        [Fact]
        public async Task The_sync_answers_envelope_is_described_as_the_envelope()
        {
            TellmaTestHost host = fixture.Started;

            JsonElement document = await ReadAsync(host);
            JsonElement schemas = document.GetProperty("components").GetProperty("schemas");

            // The envelope's schema nests past the wire's limit under the answer, and is still the real type.
            JsonElement answer = schemas.GetProperty(Reference(Response(document, "/{tenantId}/api/web/client/sync"))!);
            Assert.Equal(nameof(MeResult), Reference(answer.GetProperty("properties").GetProperty("me")));
            Assert.True(schemas.GetProperty(nameof(MeResult)).GetProperty("properties").TryGetProperty("access", out _));
        }

        [Fact]
        public async Task A_value_as_deep_as_the_wire_carries_is_described_level_by_level()
        {
            await using TellmaTestHost host = await TellmaTestHost.StartAsync(static options =>
            {
                options.Environment = "Development";
                options.Compose = static tellma => tellma.AddFeature(ProjectionModel.Feature(static contribution => contribution.ApiService<PjDepthService>()));
                options.ConfigureServices = WebApiProjectionTests.StandIns;
            });

            JsonElement document = await ReadAsync(host);
            JsonElement schemas = document.GetProperty("components").GetProperty("schemas");

            // --- The body and the answer are the deep value's schema ---
            string root = Reference(Response(document, "/{tenantId}/api/web/depth/answer"))!;
            JsonElement body = Operation(document, "/{tenantId}/api/web/depth/read").GetProperty("requestBody").GetProperty("content").GetProperty("application/json").GetProperty("schema");
            Assert.Equal(nameof(PjDepth), root);
            Assert.Equal(root, Reference(body));

            // --- One component per level, down to the innermost member, which takes any JSON value ---
            // A polymorphic level's component names its shapes rather than members, so the walk steps
            // from it to its one shape without counting a level.
            int levels = 0;
            List<string> polymorphic = [];
            for (string id = root; id != nameof(JsonElement);)
            {
                JsonElement schema = schemas.GetProperty(id);
                if (schema.TryGetProperty("properties", out JsonElement properties))
                {
                    levels++;
                    id = Reference(properties.GetProperty("next"))!;
                }
                else
                {
                    polymorphic.Add(id);
                    id = Reference(schema)!;
                }
            }

            Assert.Equal(PjDepth.Levels, levels);
            Assert.Equal([nameof(PjShape)], polymorphic);
        }

        [Fact]
        public void A_types_schema_nests_two_levels_per_object_and_four_per_polymorphic_level_however_shallow_its_values()
        {
            JsonSerializerOptions options = new(TellmaJson.Options) { MaxDepth = 4 * WebOpenApi.SchemaMaxDepth };

            // --- Any value is an object of its own; each object level adds two, a polymorphic one four ---
            Assert.Equal(1, SchemaDepthCheck.Depth(options, typeof(JsonElement)));
            Assert.Equal(1 + 2, SchemaDepthCheck.Depth(options, typeof(PjLevel<JsonElement>)));
            Assert.Equal(1 + 4, SchemaDepthCheck.Depth(options, typeof(PjLevel<PjLevel<JsonElement>>)));
            Assert.Equal(1 + 4, SchemaDepthCheck.Depth(options, typeof(PjShape)));
            Assert.Equal(1 + 2 + 4, SchemaDepthCheck.Depth(options, typeof(PjLevel<PjShape>)));

            // --- The deepest types the document describes, and one level past it ---
            Assert.Equal(WebOpenApi.SchemaMaxDepth, SchemaDepthCheck.Depth(options, typeof(PjTallest)));
            Assert.Equal(WebOpenApi.SchemaMaxDepth + 1, SchemaDepthCheck.Depth(options, typeof(PjTooTall)));
        }

        [Fact]
        public async Task A_body_whose_type_nests_as_deep_as_the_document_describes_is_described()
        {
            await using TellmaTestHost host = await TellmaTestHost.StartAsync(Compose("Development", static contribution => contribution.ApiService<PjTallestService>()));

            JsonElement document = await ReadAsync(host);

            JsonElement body = Operation(document, "/{tenantId}/api/web/tallest/read").GetProperty("requestBody").GetProperty("content").GetProperty("application/json").GetProperty("schema");
            Assert.Equal(nameof(PjTallest), Reference(body));
        }

        [Fact]
        public async Task A_body_whose_type_nests_past_what_the_document_describes_fails_startup_in_development()
        {
            (WebApplication app, _, _) = TellmaTestHost.Build(Compose("Development", static contribution => contribution.ApiService<PjTooTallService>()));
            await using (app)
            {
                TellmaCompositionException exception = await Assert.ThrowsAsync<TellmaCompositionException>(() => app.StartAsync(Token));

                CompositionProblem problem = Assert.Single(exception.Problems);
                Assert.Equal(SchemaDepthCheck.CheckName, problem.Source);
                Assert.Equal($"The web surface's type PjTooTall has a schema {WebOpenApi.SchemaMaxDepth + 1} levels deep, past the OpenAPI document's limit of {WebOpenApi.SchemaMaxDepth}, so the Development document would fail.", problem.Problem);
            }
        }

        [Fact]
        public async Task A_body_whose_type_nests_past_what_the_document_describes_fails_the_whole_document_without_the_check()
        {
            Action<TestHostOptions> compose = Compose("Development", static contribution => contribution.ApiService<PjTooTallService>());
            await using TellmaTestHost host = await TellmaTestHost.StartAsync(options =>
            {
                compose(options);
                Action<IServiceCollection>? prior = options.ConfigureServices;
                options.ConfigureServices = services =>
                {
                    prior?.Invoke(services);
                    _ = services.Remove(Assert.Single(services, static descriptor => descriptor.ServiceType == typeof(IStartupCheck) && descriptor.ImplementationType == typeof(SchemaDepthCheck)));
                };
            });

            // The check's limit is the generator's own: a schema one level past it and no document is served.
            using HttpResponseMessage response = await host.Client.GetAsync("/openapi/web.json", Token);

            Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
        }

        [Fact]
        public async Task The_check_measures_nothing_without_the_route_model()
        {
            await using ServiceProvider services = new ServiceCollection().BuildServiceProvider();

            IReadOnlyList<CompositionProblem> problems = await new SchemaDepthCheck().CheckAsync(services, Token);

            Assert.Empty(problems);
        }

        [Fact]
        public async Task The_schema_generator_reads_the_hosts_JSON_options_under_the_documents_own_nesting_limit()
        {
            TellmaTestHost host = fixture.Started;
            JsonSerializerOptions http = host.Services.GetRequiredService<IOptions<JsonOptions>>().Value.SerializerOptions;

            JsonSerializerOptions schema = SchemaJson.Create(host.Services).SerializerOptions;

            // --- The same rules, converters and resolvers, under the document's limit ---
            Assert.Equal(WebOpenApi.SchemaMaxDepth, schema.MaxDepth);
            Assert.Same(http.PropertyNamingPolicy, schema.PropertyNamingPolicy);
            Assert.Equal(http.NumberHandling, schema.NumberHandling);
            Assert.Equal(http.UnmappedMemberHandling, schema.UnmappedMemberHandling);
            Assert.Equal([.. http.Converters.Select(static converter => converter.GetType())], [.. schema.Converters.Select(static converter => converter.GetType())]);
            Assert.Equal([.. http.TypeInfoResolverChain.Select(static resolver => resolver.GetType())], [.. schema.TypeInfoResolverChain.Select(static resolver => resolver.GetType())]);

            // --- The wire keeps its own, the document served ---
            _ = await ReadAsync(host);
            Assert.Equal(TellmaJson.Options.MaxDepth, http.MaxDepth);
        }

        [Fact]
        public void The_documents_nesting_limit_without_a_document_fails()
        {
            ServiceCollection services = new();

            InvalidOperationException exception = Assert.Throws<InvalidOperationException>(() => SchemaJson.Register(services, WebOpenApi.DocumentName));

            Assert.StartsWith("The OpenAPI document 'web' registers no service that reads the HTTP JSON options", exception.Message, StringComparison.Ordinal);
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

        /// <summary>Composes a host of the projection suite's feature with a contribution of its own.</summary>
        /// <param name="environment">The host's environment.</param>
        /// <param name="contribute">What the feature contributes.</param>
        /// <returns>The host's options.</returns>
        private static Action<TestHostOptions> Compose(string environment, Action<FeatureContribution> contribute)
        {
            return options =>
            {
                options.Environment = environment;
                options.Compose = tellma => tellma.AddFeature(ProjectionModel.Feature(contribute));
                options.ConfigureServices = WebApiProjectionTests.StandIns;
            };
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

        /// <summary>The schema of the answer of a path's one operation.</summary>
        /// <param name="document">The document.</param>
        /// <param name="path">The path.</param>
        /// <returns>The schema.</returns>
        private static JsonElement Response(JsonElement document, string path)
        {
            // The one media type the answer is written in, with its charset.
            return Assert.Single(Operation(document, path).GetProperty("responses").GetProperty("200").GetProperty("content").EnumerateObject()).Value.GetProperty("schema");
        }

        /// <summary>The component a schema references, itself or as one alternative of several, such as a nullable member's.</summary>
        /// <param name="schema">The schema.</param>
        /// <param name="required">Whether a schema without a reference fails the test.</param>
        /// <returns>The component's name; null when it references none and none is required.</returns>
        private static string? Reference(JsonElement schema, bool required = true)
        {
            if (schema.TryGetProperty("$ref", out JsonElement direct))
            {
                string target = direct.GetString()!;
                return target[(target.LastIndexOf('/') + 1)..];
            }

            foreach (string alternatives in (string[])["oneOf", "anyOf", "allOf"])
            {
                if (schema.TryGetProperty(alternatives, out JsonElement entries)
                    && entries.EnumerateArray().Select(static entry => Reference(entry, required: false)).FirstOrDefault(static id => id is not null) is string id)
                {
                    return id;
                }
            }

            Assert.False(required, "The schema " + schema.GetRawText() + " references no component.");
            return null;
        }

        /// <summary>The one operation of a path.</summary>
        /// <param name="document">The document.</param>
        /// <param name="path">The path.</param>
        /// <returns>The operation.</returns>
        private static JsonElement Operation(JsonElement document, string path)
        {
            return Assert.Single(document.GetProperty("paths").GetProperty(path).EnumerateObject()).Value;
        }

        /// <summary>
        ///     One Development host for the cases that read its document and options: serving the
        ///     document changes nothing, so each case reads the same one. A case that composes another
        ///     model or environment starts a host of its own.
        /// </summary>
        public sealed class Host : IAsyncLifetime
        {
            /// <summary>The host, once started.</summary>
            private TellmaTestHost? _host;

            /// <summary>The started host.</summary>
            internal TellmaTestHost Started => _host ?? throw new InvalidOperationException("The host has not started.");

            /// <inheritdoc />
            public async ValueTask InitializeAsync()
            {
                _host = await StartAsync("Development");
            }

            /// <inheritdoc />
            public async ValueTask DisposeAsync()
            {
                if (_host is not null)
                {
                    await _host.DisposeAsync();
                }
            }
        }
    }
}
