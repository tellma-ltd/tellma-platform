// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Microsoft.Extensions.DependencyInjection;
using System.Text;
using System.Text.Json;
using Tellma.Core.Abstractions.Client;
using Tellma.Core.Migrator.Emit;

namespace Tellma.Core.Tests.Migrator
{
    /// <summary>
    ///     The emitted client metadata: canonical chunks named by their hash, the defaults every chunk
    ///     carries, the loaders module, and the paths the emit keeps for itself.
    /// </summary>
    public class ClientAssetBundleTests
    {
        [Fact]
        public void Base32_follows_Crockfords_alphabet_most_significant_bit_first()
        {
            Assert.Equal(string.Empty, ContentHash.Encode([]));
            Assert.Equal("00", ContentHash.Encode([0x00]));
            Assert.Equal("ZW", ContentHash.Encode([0xFF]));
            Assert.Equal("CSQPYRG", ContentHash.Encode("foob"u8));
            Assert.Equal("CSQPYRK1", ContentHash.Encode("fooba"u8));
        }

        [Fact]
        public void A_hash_is_twelve_unambiguous_characters_that_change_with_the_bytes()
        {
            string hash = ContentHash.Of("{}\n"u8);

            Assert.Equal(ContentHash.Length, hash.Length);
            Assert.All(hash, static c => Assert.Contains(c, "0123456789ABCDEFGHJKMNPQRSTVWXYZ"));
            Assert.Equal(hash, ContentHash.Of("{}\n"u8));
            Assert.NotEqual(hash, ContentHash.Of("{} \n"u8));
        }

        [Fact]
        public void A_hash_matches_the_vectors_the_client_build_reads_names_back_by()
        {
            // @tellma/core-tools pins the same two vectors: the client build refuses an emitted
            // file whose name is not the hash of its content.
            Assert.Equal("WERC8GMRZGE1", ContentHash.Of([]));
            Assert.Equal("WD346811P10Q", ContentHash.Of(/*lang=json,strict*/ "{\"a\":1}\n"u8));
        }

        [Fact]
        public void Canonical_JSON_orders_members_ordinally_keeps_arrays_and_re_escapes_text()
        {
            using var document = JsonDocument.Parse("""{ "b": [3, 1, {"z": true, "Z": null}], "a": "\u0041<", "B": 1.50 }""");
            using MemoryStream stream = new();
            using (Utf8JsonWriter writer = new(stream, new JsonWriterOptions { Encoder = Abstractions.Api.TellmaJson.Options.Encoder }))
            {
                CanonicalJson.Write(writer, document.RootElement);
            }

            Assert.Equal(/*lang=json,strict*/ """{"B":1.50,"a":"A\u003C","b":[3,1,{"Z":null,"z":true}]}""", Encoding.UTF8.GetString(stream.ToArray()));
        }

        [Fact]
        public void Every_chunk_carries_its_defaults_and_the_core_chunk_its_format()
        {
            ClientAssetBundle bundle = Create(context => context.Section(ClientMetadataChunk.Core, "languages", static writer =>
            {
                writer.WriteStartArray();
                writer.WriteStringValue("en");
                writer.WriteEndArray();
            }));

            using JsonDocument core = Chunk(bundle, "core");
            using JsonDocument entities = Chunk(bundle, "entities");
            using JsonDocument contracts = Chunk(bundle, "contracts");

            // --- The core chunk: its format, the written section, and every array section empty ---
            Assert.Equal(1, core.RootElement.GetProperty("formatVersion").GetInt32());
            Assert.Equal("en", core.RootElement.GetProperty("languages")[0].GetString());
            foreach (string name in new[] { "calendars", "entities", "securables", "settingKeys", "settingCategories", "notificationTypes", "notificationChannels", "clientEvents", "jobHandlers", "widgetTypes", "codedReports" })
            {
                Assert.Equal(JsonValueKind.Array, core.RootElement.GetProperty(name).ValueKind);
                Assert.Equal(0, core.RootElement.GetProperty(name).GetArrayLength());
            }

            // --- The other chunks, complete and empty ---
            Assert.Equal(["entities", "enums"], entities.RootElement.EnumerateObject().Select(static member => member.Name));
            Assert.Equal(["fingerprints", "idempotent", "long"], contracts.RootElement.EnumerateObject().Select(static member => member.Name));
            Assert.Equal(JsonValueKind.Object, contracts.RootElement.GetProperty("fingerprints").ValueKind);
        }

        [Fact]
        public void A_chunk_is_named_after_the_hash_of_its_bytes_and_ends_in_a_line_feed()
        {
            ClientAssetBundle bundle = Create(static _ => { });

            foreach (EmittedFile file in bundle.Files.Where(static file => file.RelativePath.StartsWith("assets/", StringComparison.Ordinal)))
            {
                string name = Path.GetFileNameWithoutExtension(file.RelativePath);
                Assert.Equal(ContentHash.Of(file.Content.Span), name[(name.LastIndexOf('-') + 1)..]);
                Assert.Equal((byte)'\n', file.Content.Span[^1]);
                Assert.True(ClientAssetBundle.IsEmittedAsset(Path.GetFileName(file.RelativePath)));
            }
        }

        [Fact]
        public void The_same_sections_in_another_order_yield_the_same_bytes()
        {
            ClientAssetBundle first = Create(static context =>
            {
                context.Section(ClientMetadataChunk.Core, "calendars", static writer => writer.WriteRawValue("""["gc","uq"]"""));
                context.Section(ClientMetadataChunk.Contracts, "fingerprints", static writer => writer.WriteRawValue("""{"b":"2","a":"1"}"""));
            });
            ClientAssetBundle second = Create(static context =>
            {
                context.Section(ClientMetadataChunk.Contracts, "fingerprints", static writer => writer.WriteRawValue("""{"a":"1","b":"2"}"""));
                context.Section(ClientMetadataChunk.Core, "calendars", static writer => writer.WriteRawValue("""["gc","uq"]"""));
            });

            Assert.Equal(first.Files.Select(static file => file.RelativePath), second.Files.Select(static file => file.RelativePath));
            Assert.All(first.Files.Zip(second.Files), static pair => Assert.True(pair.First.Content.Span.SequenceEqual(pair.Second.Content.Span)));
        }

        [Fact]
        public void The_loaders_module_names_each_chunk_by_its_served_path()
        {
            ClientAssetBundle bundle = Create(static _ => { });
            string module = Encoding.UTF8.GetString(Assert.Single(bundle.Files, static file => file.RelativePath == ClientAssetBundle.ModulePath).Content.Span);

            Assert.Contains("import { tmMetadataLoaders } from '@tellma/core';", module, StringComparison.Ordinal);
            Assert.Contains("export const tellmaMetadata = tmMetadataLoaders({", module, StringComparison.Ordinal);
            foreach (EmittedFile chunk in bundle.Files.Where(static file => file.RelativePath.StartsWith("assets/", StringComparison.Ordinal)))
            {
                Assert.Contains($"'tm-metadata/{Path.GetFileName(chunk.RelativePath)}'", module, StringComparison.Ordinal);
            }

            Assert.DoesNotContain("\r", module, StringComparison.Ordinal);
        }

        [Fact]
        public void A_contributor_file_is_written_with_line_feeds_beside_the_chunks()
        {
            ClientAssetBundle bundle = Create(static context => context.File("tellma-entities.d.ts", "line one\r\nline two\r\n"));

            EmittedFile file = Assert.Single(bundle.Files, static file => file.RelativePath == "tellma-entities.d.ts");
            Assert.Equal("line one\nline two\n", Encoding.UTF8.GetString(file.Content.Span));
            Assert.Equal(bundle.Files.Select(static file => file.RelativePath).Order(StringComparer.Ordinal), bundle.Files.Select(static file => file.RelativePath));
        }

        [Theory]
        [InlineData("tellma-metadata.ts")]
        [InlineData("Tellma-Metadata.TS")]
        [InlineData("assets/extra.json")]
        public void A_contributor_cannot_write_the_emits_own_paths(string path)
        {
            ClientAssetContext context = new(new ServiceCollection().BuildServiceProvider());
            context.File(path, "x");

            Assert.Null(ClientAssetBundle.Create(context.Sections, context.Files, out IReadOnlyList<string> problems));
            Assert.Contains(path, Assert.Single(problems), StringComparison.Ordinal);
        }

        [Fact]
        public void A_contributor_cannot_write_the_format_version()
        {
            ClientAssetContext context = new(new ServiceCollection().BuildServiceProvider());
            context.Section(ClientMetadataChunk.Core, "formatVersion", static writer => writer.WriteNumberValue(2));

            Assert.Null(ClientAssetBundle.Create(context.Sections, context.Files, out IReadOnlyList<string> problems));
            Assert.Contains("formatVersion", Assert.Single(problems), StringComparison.Ordinal);
        }

        [Theory]
        [InlineData("core-0123456789AB.json", true)]
        [InlineData("strings-en-0123456789AB.json", true)]
        [InlineData("strings-zh-Hans-0123456789AB.json", true)]
        [InlineData("core-0123456789AI.json", false)]
        [InlineData("core-0123456789A.json", false)]
        [InlineData("logo.png", false)]
        [InlineData("reports-0123456789AB.json", false)]
        public void Only_the_names_an_emit_writes_count_as_emitted_assets(string name, bool emitted)
        {
            Assert.Equal(emitted, ClientAssetBundle.IsEmittedAsset(name));
        }

        /// <summary>Builds a bundle from what a contributor writes.</summary>
        /// <param name="contribute">The contribution.</param>
        /// <returns>The bundle.</returns>
        private static ClientAssetBundle Create(Action<ClientAssetContext> contribute)
        {
            ClientAssetContext context = new(new ServiceCollection().BuildServiceProvider());
            contribute(context);
            var bundle = ClientAssetBundle.Create(context.Sections, context.Files, out IReadOnlyList<string> problems);
            Assert.Empty(problems);
            return bundle!;
        }

        /// <summary>Parses one chunk of a bundle.</summary>
        /// <param name="bundle">The bundle.</param>
        /// <param name="name">The chunk's name.</param>
        /// <returns>The chunk.</returns>
        private static JsonDocument Chunk(ClientAssetBundle bundle, string name)
        {
            return JsonDocument.Parse(Assert.Single(bundle.Files, file => file.RelativePath.StartsWith($"assets/{name}-", StringComparison.Ordinal)).Content);
        }
    }
}
