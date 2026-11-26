// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Microsoft.Extensions.DependencyInjection;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using Tellma.Core.Abstractions.Client;
using Tellma.Core.Client;
using Tellma.Core.Tests.Infrastructure;

namespace Tellma.Core.Tests.Client
{
    /// <summary>
    ///     The client's fixtures of the entity model, which its type tests compile against: the
    ///     Entities chunk, the Core chunk's summaries and the declarations the test model writes,
    ///     and the declarations the platform alone writes, which core's sources compile against.
    /// </summary>
    /// <remarks>
    ///     A fixture that no longer matches fails with the difference. After an intended change, run
    ///     the suite once with <c>TELLMA_UPDATE_GOLDENS=1</c>: the run rewrites the fixtures and fails
    ///     on purpose, and the next run passes once the diff is reviewed.
    /// </remarks>
    public sealed class ClientEntityFixturesTests(ClientEntityModelComposition composition) : IClassFixture<ClientEntityModelComposition>
    {
        private const string UpdateVariable = "TELLMA_UPDATE_GOLDENS";

        /// <summary>The fixtures' JSON layout: indented by two spaces, line feeds, readable text.</summary>
        private static readonly JsonSerializerOptions Indented = new()
        {
            WriteIndented = true,
            NewLine = "\n",
            Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        };

        [Fact]
        public void The_entities_chunk_fixture_is_what_the_test_model_writes()
        {
            JsonObject chunk = new()
            {
                ["entities"] = Node(ClientMetadataChunk.Entities, EntityClientAssetContributor.EntitiesSection),
                ["enums"] = Node(ClientMetadataChunk.Entities, EntityClientAssetContributor.EnumsSection),
            };

            Golden("client/projects/core/tellma-core-testing/fixtures/entity-model/entities-chunk.json", chunk.ToJsonString(Indented) + "\n");
        }

        [Fact]
        public void The_summaries_fixture_is_what_the_test_model_writes()
        {
            JsonNode summaries = Node(ClientMetadataChunk.Core, EntityClientAssetContributor.EntitiesSection);

            Golden("client/projects/core/tellma-core-testing/fixtures/entity-model/core-entities.json", summaries.ToJsonString(Indented) + "\n");
        }

        [Fact]
        public void The_declarations_fixture_is_what_the_test_model_writes()
        {
            // Kept as text: a declaration file under the client workspace that augments @tellma/core
            // would join every program its tools build over the workspace.
            Golden("client/tools/tests/fixtures/entity-model/tellma-entities.d.ts.txt", composition.Declarations);
        }

        [Fact]
        public void The_platform_declarations_fixture_is_what_the_platform_alone_writes()
        {
            // The platform with no feature of its own: core's entities alone, the closed interfaces a
            // distribution's emitted declarations give them. The client's emitted-types typecheck
            // compiles core's sources against this text, so code that holds only while those rows
            // are open JSON objects fails in core's own gates rather than in a distribution's build.
            using ServiceProvider services = TestComposition.Compose(static _ => { }).BuildServiceProvider(validateScopes: true);
            ClientAssetContext context = ClientEntityModelComposition.Contribute(services);
            string declarations = context.Files.Single(static file => file.RelativePath == EntityTypesWriter.FileName).Content;

            Golden("client/tools/typecheck/fixtures/tellma-entities.d.ts.txt", declarations);
        }

        /// <summary>A section of the test model, parsed.</summary>
        /// <param name="chunk">The section's chunk.</param>
        /// <param name="name">The section's name.</param>
        /// <returns>The section.</returns>
        private JsonNode Node(ClientMetadataChunk chunk, string name)
        {
            return JsonNode.Parse(ClientEntityModelComposition.Section(composition.Context, chunk, name).Span)!;
        }

        /// <summary>Compares a rendering with its fixture, or rewrites the fixture when asked to.</summary>
        /// <param name="relativePath">The fixture's path from the repository's root.</param>
        /// <param name="rendered">The rendering.</param>
        private static void Golden(string relativePath, string rendered)
        {
            string path = Path.Combine(RepositoryRoot(), relativePath);
            if (Environment.GetEnvironmentVariable(UpdateVariable) == "1")
            {
                _ = Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                File.WriteAllText(path, rendered, new UTF8Encoding(false));
                Assert.Fail("The entity model fixture " + relativePath + " was rewritten; review the diff and run again without " + UpdateVariable + " set.");
            }

            Assert.True(File.Exists(path), "No entity model fixture " + relativePath + ". Set " + UpdateVariable + " to record one.");
            Assert.Equal(File.ReadAllText(path).ReplaceLineEndings("\n"), rendered);
        }

        /// <summary>The repository's root, four directories above this source file.</summary>
        /// <param name="here">This file's path, supplied by the compiler.</param>
        /// <returns>The directory.</returns>
        private static string RepositoryRoot([CallerFilePath] string here = "")
        {
            return Path.GetFullPath(Path.Combine(Path.GetDirectoryName(here)!, "..", "..", "..", ".."));
        }
    }
}
