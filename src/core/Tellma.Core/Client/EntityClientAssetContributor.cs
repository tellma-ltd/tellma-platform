// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using System.Text.Json;
using Tellma.Core.Abstractions.Api;
using Tellma.Core.Abstractions.Client;
using Tellma.Core.Localization;

namespace Tellma.Core.Client
{
    // Spec 0029 §2.1: the summaries go to the eager Core chunk, the full metadata and the named
    // enums to the Entities chunk, which entity routes load, and the typed declarations (§15) to the
    // client project's generated folder.
    /// <summary>
    ///     Writes the client entity model: every entity's summary to the Core chunk, every entity's
    ///     metadata and every named enum to the Entities chunk, and the entities' TypeScript
    ///     declarations.
    /// </summary>
    /// <param name="model">The client entity model.</param>
    /// <param name="enums">The composition's C# enums, which the declarations write as literal unions.</param>
    internal sealed class EntityClientAssetContributor(IClientEntityModel model, NamedEnums enums) : IClientAssetContributor
    {
        /// <summary>The section of the entities, in the Core chunk (their summaries) and in the Entities chunk (their metadata).</summary>
        public const string EntitiesSection = "entities";

        /// <summary>The section of the named enums, in the Entities chunk.</summary>
        public const string EnumsSection = "enums";

        /// <inheritdoc />
        public void Contribute(ClientAssetContext context)
        {
            ArgumentNullException.ThrowIfNull(context);
            IReadOnlyList<ClientEntityMetadata> entities = model.Entities;
            IReadOnlyList<ClientEnumMetadata> named = model.Enums;

            // --- The summaries: each entity serialized as its summary type, which writes the summary's members alone ---
            IReadOnlyList<ClientEntitySummary> summaries = entities;
            context.Section(ClientMetadataChunk.Core, EntitiesSection, writer => JsonSerializer.Serialize(writer, summaries, TellmaJson.Options));

            // --- The full metadata and the named enums ---
            context.Section(ClientMetadataChunk.Entities, EntitiesSection, writer => JsonSerializer.Serialize(writer, entities, TellmaJson.Options));
            context.Section(ClientMetadataChunk.Entities, EnumsSection, writer => JsonSerializer.Serialize(writer, named, TellmaJson.Options));

            // --- The declarations: a C# enum is a literal union, a value set plain text ---
            HashSet<string> literal = new(enums.All.Select(static item => item.Name), StringComparer.Ordinal);
            context.File(EntityTypesWriter.FileName, EntityTypesWriter.Write(entities, [.. named.Where(item => literal.Contains(item.Name))]));
        }
    }
}
