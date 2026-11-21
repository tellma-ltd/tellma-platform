// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Microsoft.Extensions.DependencyInjection;
using System.Text.Json;
using Tellma.Core.Abstractions.Client;
using Tellma.Core.Client;

namespace Tellma.Core.Tests.Client
{
    /// <summary>
    ///     The client entity model of the test model, composed once per suite, with what its
    ///     contributor writes: the Core chunk's summaries, the Entities chunk's entities and enums,
    ///     and the declarations.
    /// </summary>
    public sealed class ClientEntityModelComposition : IDisposable
    {
        /// <summary>The Entities chunk's sections, parsed.</summary>
        private readonly JsonDocument _entities;

        /// <summary>The Entities chunk's enums, parsed.</summary>
        private readonly JsonDocument _enums;

        /// <summary>The Core chunk's summaries, parsed.</summary>
        private readonly JsonDocument _summaries;

        /// <summary>Composes the model and runs its contributor.</summary>
        public ClientEntityModelComposition()
        {
            Services = ClientModel.Compose().BuildServiceProvider(validateScopes: true);
            Context = Contribute(Services);
            _summaries = JsonDocument.Parse(Section(Context, ClientMetadataChunk.Core, EntityClientAssetContributor.EntitiesSection));
            _entities = JsonDocument.Parse(Section(Context, ClientMetadataChunk.Entities, EntityClientAssetContributor.EntitiesSection));
            _enums = JsonDocument.Parse(Section(Context, ClientMetadataChunk.Entities, EntityClientAssetContributor.EnumsSection));
        }

        /// <summary>The realised composition's services.</summary>
        public ServiceProvider Services { get; }

        /// <summary>What the entity contributor wrote.</summary>
        public ClientAssetContext Context { get; }

        /// <summary>The model.</summary>
        public IClientEntityModel Model => Services.GetRequiredService<IClientEntityModel>();

        /// <summary>The Core chunk's summaries.</summary>
        public JsonElement Summaries => _summaries.RootElement;

        /// <summary>The Entities chunk's entities.</summary>
        public JsonElement Entities => _entities.RootElement;

        /// <summary>The Entities chunk's enums.</summary>
        public JsonElement Enums => _enums.RootElement;

        /// <summary>The declarations' text.</summary>
        public string Declarations => Context.Files.Single(static file => file.RelativePath == EntityTypesWriter.FileName).Content;

        /// <summary>Runs the entity contributor alone over a composition.</summary>
        /// <param name="services">The composition's services.</param>
        /// <returns>What it wrote.</returns>
        public static ClientAssetContext Contribute(IServiceProvider services)
        {
            ClientAssetContext context = new(services);
            services.GetServices<IClientAssetContributor>().OfType<EntityClientAssetContributor>().Single().Contribute(context);
            return context;
        }

        /// <summary>One section's bytes.</summary>
        /// <param name="context">What a contributor wrote.</param>
        /// <param name="chunk">The section's chunk.</param>
        /// <param name="name">The section's name.</param>
        /// <returns>The bytes.</returns>
        public static ReadOnlyMemory<byte> Section(ClientAssetContext context, ClientMetadataChunk chunk, string name)
        {
            return context.Sections.Single(section => section.Chunk == chunk && section.Name == name).Json;
        }

        /// <summary>An entity of the Entities chunk.</summary>
        /// <param name="name">The entity's name.</param>
        /// <returns>The entity.</returns>
        public JsonElement Entity(string name)
        {
            return Entities.EnumerateArray().Single(entity => entity.GetProperty("name").GetString() == name);
        }

        /// <summary>A member of an entity's list, by name: a property, navigation, child, action or field.</summary>
        /// <param name="owner">The entity, or an action.</param>
        /// <param name="list">The list's name, such as <c>properties</c>.</param>
        /// <param name="name">The member's name.</param>
        /// <returns>The member.</returns>
        public static JsonElement Member(JsonElement owner, string list, string name)
        {
            return owner.GetProperty(list).EnumerateArray().Single(member => member.GetProperty("name").GetString() == name);
        }

        /// <summary>A named enum of the Entities chunk.</summary>
        /// <param name="name">The enum's name.</param>
        /// <returns>The enum.</returns>
        public JsonElement Enum(string name)
        {
            return Enums.EnumerateArray().Single(item => item.GetProperty("name").GetString() == name);
        }

        /// <inheritdoc />
        public void Dispose()
        {
            _summaries.Dispose();
            _entities.Dispose();
            _enums.Dispose();
            Services.Dispose();
        }
    }
}
