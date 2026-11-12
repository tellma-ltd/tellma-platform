// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using System.Buffers;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Tellma.Core.Abstractions.Api;
using Tellma.Core.Abstractions.Client;

namespace Tellma.Core.Migrator.Emit
{
    /// <summary>One file of the emit: its path under the generated folder, with forward slashes, and its bytes.</summary>
    /// <param name="RelativePath">The path under the generated folder.</param>
    /// <param name="Content">The bytes.</param>
    internal sealed record EmittedFile(string RelativePath, ReadOnlyMemory<byte> Content);

    /// <summary>
    ///     The files one emit writes into a distribution's generated folder (spec 0024 §3.2): the
    ///     three metadata chunks under <c>assets/</c>, each named after the hash of its bytes; the
    ///     loaders module <c>tellma-metadata.ts</c> that names them; and every contributor's generated
    ///     file. Every byte is a function of the sections and files alone.
    /// </summary>
    internal sealed partial class ClientAssetBundle
    {
        /// <summary>The loaders module's path under the generated folder.</summary>
        public const string ModulePath = "tellma-metadata.ts";

        /// <summary>The folder of the chunks under the generated folder.</summary>
        public const string AssetsFolder = "assets";

        /// <summary>The folder the client build serves the assets folder from, which the module's paths name.</summary>
        public const string ServedFolder = "tm-metadata";

        /// <summary>The Core chunk's format, which the client refuses to read when it differs.</summary>
        public const int FormatVersion = 1;

        /// <summary>The Core chunk's member the emit writes itself.</summary>
        private const string FormatVersionMember = "formatVersion";

        /// <summary>The empty array every absent array section defaults to.</summary>
        private static readonly byte[] EmptyArray = "[]"u8.ToArray();

        /// <summary>The empty object every absent object section defaults to.</summary>
        private static readonly byte[] EmptyObject = "{}"u8.ToArray();

        /// <summary>
        ///     Each chunk's members that are present even when no contributor writes them, with their
        ///     empty value: the Core chunk's array sections (spec 0024 §3.1), and every member of the
        ///     Entities and Contracts chunks, so each chunk always has the shape the client reads.
        /// </summary>
        private static readonly Dictionary<ClientMetadataChunk, (string Name, byte[] Empty)[]> Defaults = new()
        {
            [ClientMetadataChunk.Core] =
            [
                ("languages", EmptyArray), ("calendars", EmptyArray), ("entities", EmptyArray), ("securables", EmptyArray),
                ("settingKeys", EmptyArray), ("settingCategories", EmptyArray), ("notificationTypes", EmptyArray),
                ("notificationChannels", EmptyArray), ("clientEvents", EmptyArray), ("jobHandlers", EmptyArray),
                ("widgetTypes", EmptyArray), ("codedReports", EmptyArray),
            ],
            [ClientMetadataChunk.Entities] = [("entities", EmptyArray), ("enums", EmptyArray)],
            [ClientMetadataChunk.Contracts] = [("fingerprints", EmptyObject), ("long", EmptyArray), ("idempotent", EmptyArray)],
        };

        /// <summary>The writer options of every chunk: the platform's encoder, no indentation.</summary>
        private static readonly JsonWriterOptions WriterOptions = new() { Encoder = TellmaJson.Options.Encoder };

        /// <summary>Text without a byte-order mark: generated files are plain UTF-8.</summary>
        private static readonly UTF8Encoding Utf8 = new(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);

        /// <summary>Initializes a bundle.</summary>
        /// <param name="files">The files, in ordinal order of their paths.</param>
        private ClientAssetBundle(IReadOnlyList<EmittedFile> files)
        {
            Files = files;
        }

        /// <summary>Every file of the emit, in ordinal order of its path.</summary>
        public IReadOnlyList<EmittedFile> Files { get; }

        /// <summary>
        ///     Builds the files from what the contributors wrote, or the problems that prevent it: a
        ///     generated file at the module's path or under the assets folder, or a Core section that
        ///     takes the format's member.
        /// </summary>
        /// <param name="sections">The sections the contributors wrote.</param>
        /// <param name="files">The generated files the contributors wrote.</param>
        /// <param name="problems">Every problem; empty when the bundle was built.</param>
        /// <returns>The bundle, or null when there are problems.</returns>
        public static ClientAssetBundle? Create(IReadOnlyList<ClientMetadataSection> sections, IReadOnlyList<ClientGeneratedFile> files, out IReadOnlyList<string> problems)
        {
            ArgumentNullException.ThrowIfNull(sections);
            ArgumentNullException.ThrowIfNull(files);
            List<string> found = [];

            // --- Paths and members the emit owns ---
            foreach (ClientGeneratedFile file in files)
            {
                if (string.Equals(file.RelativePath, ModulePath, StringComparison.OrdinalIgnoreCase)
                    || file.RelativePath.StartsWith(AssetsFolder + "/", StringComparison.OrdinalIgnoreCase))
                {
                    found.Add($"A contributor wrote the generated file '{file.RelativePath}', but '{ModulePath}' and the '{AssetsFolder}' folder are the emit's own.");
                }
            }

            if (sections.Any(static section => section.Chunk == ClientMetadataChunk.Core && section.Name == FormatVersionMember))
            {
                found.Add($"A contributor wrote the Core chunk's '{FormatVersionMember}' section, which the emit writes itself.");
            }

            problems = found;
            if (found.Count > 0)
            {
                return null;
            }

            // --- The chunks, each named after its hash ---
            List<EmittedFile> emitted = [];
            Dictionary<ClientMetadataChunk, string> served = [];
            foreach (ClientMetadataChunk chunk in Enum.GetValues<ClientMetadataChunk>())
            {
                byte[] content = Chunk(chunk, sections);
                string name = $"{ChunkName(chunk)}-{ContentHash.Of(content)}.json";
                emitted.Add(new EmittedFile($"{AssetsFolder}/{name}", content));
                served[chunk] = $"{ServedFolder}/{name}";
            }

            // --- The loaders module, then the contributors' files ---
            emitted.Add(new EmittedFile(ModulePath, Utf8.GetBytes(Module(served))));
            emitted.AddRange(files.Select(static file => new EmittedFile(file.RelativePath, Utf8.GetBytes(file.Content.ReplaceLineEndings("\n")))));

            return new ClientAssetBundle([.. emitted.OrderBy(static file => file.RelativePath, StringComparer.Ordinal)]);
        }

        /// <summary>Whether a file in the assets folder is one an emit writes, so a later emit may delete it once it is stale.</summary>
        /// <param name="fileName">The file's name.</param>
        /// <returns>True for a chunk or string pack's name.</returns>
        public static bool IsEmittedAsset(string fileName)
        {
            return EmittedAssetName().IsMatch(fileName);
        }

        /// <summary>A chunk's name, the first part of its file name.</summary>
        /// <param name="chunk">The chunk.</param>
        /// <returns>The name.</returns>
        private static string ChunkName(ClientMetadataChunk chunk)
        {
            return chunk switch
            {
                ClientMetadataChunk.Core => "core",
                ClientMetadataChunk.Entities => "entities",
                ClientMetadataChunk.Contracts => "contracts",
                _ => throw new ArgumentOutOfRangeException(nameof(chunk), chunk, "Not a client metadata chunk."),
            };
        }

        /// <summary>
        ///     One chunk's bytes: an object of its sections and the defaults no contributor wrote, plus
        ///     the format on the Core chunk, in canonical form and ending in a line feed.
        /// </summary>
        /// <param name="chunk">The chunk.</param>
        /// <param name="sections">Every section written.</param>
        /// <returns>The bytes.</returns>
        private static byte[] Chunk(ClientMetadataChunk chunk, IReadOnlyList<ClientMetadataSection> sections)
        {
            // --- The members: the written sections, then the defaults they leave out ---
            Dictionary<string, ReadOnlyMemory<byte>> members = new(StringComparer.Ordinal);
            foreach (ClientMetadataSection section in sections.Where(section => section.Chunk == chunk))
            {
                members[section.Name] = section.Json;
            }

            foreach ((string name, byte[] empty) in Defaults[chunk])
            {
                _ = members.TryAdd(name, empty);
            }

            if (chunk == ClientMetadataChunk.Core)
            {
                members[FormatVersionMember] = Encoding.ASCII.GetBytes(FormatVersion.ToString(System.Globalization.CultureInfo.InvariantCulture));
            }

            // --- The object, members by name, each value canonical ---
            ArrayBufferWriter<byte> buffer = new();
            using (Utf8JsonWriter writer = new(buffer, WriterOptions))
            {
                writer.WriteStartObject();
                foreach ((string name, ReadOnlyMemory<byte> json) in members.OrderBy(static member => member.Key, StringComparer.Ordinal))
                {
                    writer.WritePropertyName(name);
                    using var value = JsonDocument.Parse(json);
                    CanonicalJson.Write(writer, value.RootElement);
                }

                writer.WriteEndObject();
            }

            return [.. buffer.WrittenSpan, (byte)'\n'];
        }

        /// <summary>The loaders module, naming each chunk by its served path.</summary>
        /// <param name="served">Each chunk's served path.</param>
        /// <returns>The module's text.</returns>
        private static string Module(Dictionary<ClientMetadataChunk, string> served)
        {
            // Paths hold only the served folder, a chunk name, a dash, base32 and '.json', so they
            // need no escaping inside single quotes. No string pack is written yet.
            return $$"""
                // Written by the migrator's emit-client-assets; every build rewrites it.

                import { tmMetadataLoaders } from '@tellma/core';

                /** The loaders of this build's client metadata. */
                export const tellmaMetadata = tmMetadataLoaders({
                  core: '{{served[ClientMetadataChunk.Core]}}',
                  entities: '{{served[ClientMetadataChunk.Entities]}}',
                  contracts: '{{served[ClientMetadataChunk.Contracts]}}',
                  strings: {},
                });

                """.ReplaceLineEndings("\n");
        }

        /// <summary>The names of the files an emit writes into the assets folder.</summary>
        /// <returns>The pattern.</returns>
        [GeneratedRegex("^(?:core|entities|contracts|strings-[A-Za-z0-9-]+)-[0-9A-HJKMNP-TV-Z]{12}\\.json$", RegexOptions.CultureInvariant)]
        private static partial Regex EmittedAssetName();
    }
}
