// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.OpenApi;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.OpenApi;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using Tellma.Core.Api;
using Tellma.Distro.Acme.Web.Tests.Infrastructure;

namespace Tellma.Distro.Acme.Web.Tests.Snapshots
{
    /// <summary>
    ///     The reference distribution's wire surface, checked in beside these tests as two review
    ///     artifacts: <c>contracts.json</c>, every projected route's fingerprint with the long and the
    ///     idempotent routes, and <c>web.json</c>, the Development OpenAPI document. Any change to the
    ///     wire shows up as a diff of one of them and fails the build until the snapshot is updated in
    ///     the same change; the client metadata's Contracts chunk carries the same facts as the map.
    /// </summary>
    /// <remarks>
    ///     To record an intended change, run the suite once with <c>TELLMA_UPDATE_GOLDENS=1</c>: the run
    ///     rewrites the snapshots and fails on purpose, so a variable left set in an environment can
    ///     never be read as a pass. Review the diff, then run again without it.
    /// </remarks>
    public sealed class WireSnapshotTests
    {
        /// <summary>The variable that turns a run into a rewrite.</summary>
        private const string UpdateVariable = "TELLMA_UPDATE_GOLDENS";

        /// <summary>The route map's snapshot.</summary>
        private const string ContractsFile = "contracts.json";

        /// <summary>The OpenAPI document's snapshot.</summary>
        private const string OpenApiFile = "web.json";

        /// <summary>The OpenAPI document's name, as the web host registers and serves it.</summary>
        private const string DocumentName = "web";

        /// <summary>
        ///     How both snapshots are written: indented, with line feeds on every platform, and with
        ///     only the characters JSON requires escaped, so the files read as the text they hold.
        /// </summary>
        private static readonly JsonWriterOptions Indented = new()
        {
            Indented = true,
            NewLine = "\n",
            Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        };

        [Fact]
        public async Task Every_projected_route_s_fingerprint_matches_the_route_map_snapshot()
        {
            await using WebApplication app = AcmeWebApplication.Build("Production", AcmeWebApplication.ProductionSettings);

            Snapshot(ContractsFile, Canonical(Render(app.Services.GetRequiredService<IRouteContracts>())));
        }

        [Fact]
        public async Task The_emitted_contracts_chunk_equals_the_route_map_snapshot()
        {
            using AcmeEmit emit = await AcmeEmit.RunAsync(TestContext.Current.CancellationToken);

            var chunk = JsonNode.Parse(emit.Chunk("contracts-"));
            var snapshot = JsonNode.Parse(await File.ReadAllTextAsync(Path.Combine(SnapshotDirectory(), ContractsFile), TestContext.Current.CancellationToken));

            Assert.True(JsonNode.DeepEquals(snapshot, chunk), $"The emitted Contracts chunk differs from {ContractsFile}:\n{chunk}");
        }

        [Fact]
        public async Task The_development_openapi_document_matches_its_snapshot()
        {
            await using WebApplication app = AcmeWebApplication.Build("Development");

            // A started host hands its mapped routes to the API explorer as its pipeline is built;
            // this one is never started, so the routes are handed over as that step does, after
            // the routing Program.cs's calls already set up.
            _ = app.UseEndpoints(static _ => { });

            // --- The document as the host's own provider generates it for the endpoint it serves ---
            OpenApiDocument document = await app.Services.GetRequiredKeyedService<IOpenApiDocumentProvider>(DocumentName)
                .GetOpenApiDocumentAsync(TestContext.Current.CancellationToken);
            string written = await document.SerializeAsJsonAsync(OpenApiSpecVersion.OpenApi3_1, TestContext.Current.CancellationToken);

            Snapshot(OpenApiFile, Canonical(written));
        }

        /// <summary>Renders the route map: every route's fingerprint in mapping order, then the long and the idempotent routes.</summary>
        /// <param name="contracts">The route model.</param>
        /// <returns>The map's text, ending in a line feed.</returns>
        private static string Render(IRouteContracts contracts)
        {
            using MemoryStream buffer = new();
            using (Utf8JsonWriter writer = new(buffer, Indented))
            {
                writer.WriteStartObject();

                // --- Template to fingerprint ---
                writer.WriteStartObject("fingerprints");
                foreach (RouteContract route in contracts.Routes)
                {
                    writer.WriteString(route.Template, route.Fingerprint);
                }

                writer.WriteEndObject();

                // --- The two route lists ---
                WriteTemplates(writer, "long", contracts.Routes.Where(static route => route.Long));
                WriteTemplates(writer, "idempotent", contracts.Routes.Where(static route => route.Idempotent));
                writer.WriteEndObject();
            }

            return Encoding.UTF8.GetString(buffer.ToArray()) + "\n";
        }

        /// <summary>Writes the templates of some routes as a named array.</summary>
        /// <param name="writer">The writer.</param>
        /// <param name="name">The array's name.</param>
        /// <param name="routes">The routes.</param>
        private static void WriteTemplates(Utf8JsonWriter writer, string name, IEnumerable<RouteContract> routes)
        {
            writer.WriteStartArray(name);
            foreach (RouteContract route in routes)
            {
                writer.WriteStringValue(route.Template);
            }

            writer.WriteEndArray();
        }

        /// <summary>
        ///     A generated JSON text in the one form both platforms agree on: every string value's line
        ///     breaks as line feeds, written indented with line feeds and ending in one.
        /// </summary>
        /// <param name="json">The generated text.</param>
        /// <returns>The canonical text.</returns>
        private static string Canonical(string json)
        {
            // The compiler writes XML documentation with the platform's newline, and the OpenAPI
            // generator copies a summary's line breaks into its description verbatim: the same
            // source yields CR LF inside strings on Windows and LF on Linux. Normalizing the text's
            // own line breaks cannot reach those escaped ones, so the values themselves are rewritten.
            JsonNode? canonical = WithLineFeeds(JsonNode.Parse(json));

            using MemoryStream buffer = new();
            using (Utf8JsonWriter writer = new(buffer, Indented))
            {
                if (canonical is null)
                {
                    writer.WriteNullValue();
                }
                else
                {
                    canonical.WriteTo(writer);
                }
            }

            return Encoding.UTF8.GetString(buffer.ToArray()) + "\n";
        }

        /// <summary>A copy of a JSON tree whose string values carry line feeds as their only line breaks.</summary>
        /// <param name="node">The tree.</param>
        /// <returns>The copy.</returns>
        private static JsonNode? WithLineFeeds(JsonNode? node)
        {
            return node switch
            {
                JsonObject members => new JsonObject(members.Select(static member => KeyValuePair.Create(member.Key, WithLineFeeds(member.Value)))),
                JsonArray items => new JsonArray([.. items.Select(WithLineFeeds)]),
                JsonValue value when value.GetValueKind() == JsonValueKind.String => JsonValue.Create(value.GetValue<string>().ReplaceLineEndings("\n")),
                _ => node?.DeepClone(),
            };
        }

        /// <summary>Compares a rendering with its snapshot, or rewrites the snapshot when the run is a rewrite.</summary>
        /// <param name="name">The snapshot's file name.</param>
        /// <param name="rendered">The rendering.</param>
        private static void Snapshot(string name, string rendered)
        {
            string path = Path.Combine(SnapshotDirectory(), name);
            if (Environment.GetEnvironmentVariable(UpdateVariable) is "1" or "true")
            {
                File.WriteAllText(path, rendered, new UTF8Encoding(false));
                Assert.Fail($"{name} was rewritten; review the diff and run again without {UpdateVariable} set.");
            }

            Assert.True(File.Exists(path), $"{name} is missing; run once with {UpdateVariable}=1 to write it.");
            Assert.Equal(File.ReadAllText(path).ReplaceLineEndings("\n"), rendered);
        }

        /// <summary>The directory of the snapshots, beside this source file.</summary>
        /// <param name="here">This source file's path, supplied by the compiler.</param>
        /// <returns>The directory.</returns>
        private static string SnapshotDirectory([CallerFilePath] string here = "")
        {
            return Path.GetDirectoryName(here)!;
        }
    }
}
