// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.Extensions.DependencyInjection;
using Tellma.Core.Abstractions.Data;
using Tellma.Core.Composition;
using Tellma.Core.Data;
using Tellma.Core.Tests.Infrastructure;

namespace Tellma.Core.Tests.Access
{
    /// <summary>
    ///     A host's tenant model with Core's access tables and nothing else, composed once per test class:
    ///     the entity metadata the host serves and the design-time model migrations are generated from.
    /// </summary>
    public sealed class AccessModel : IDisposable
    {
        private readonly ServiceProvider _provider;

        /// <summary>Composes Core alone.</summary>
        public AccessModel()
            : this(static _ => { })
        {
        }

        /// <summary>Composes Core with a distribution's composition method.</summary>
        /// <param name="compose">The composition method.</param>
        // Internal: a class fixture may define a single public constructor.
        internal AccessModel(Action<TellmaBuilder> compose)
        {
            _provider = TestComposition.Compose(compose).BuildServiceProvider(validateScopes: true);
            Metadata = _provider.GetRequiredService<IEntityMetadataProvider>();
            Definition = _provider.GetRequiredService<TellmaModelDefinition>();

            using TellmaDbContext context = _provider.GetRequiredService<TellmaModelFactory>().CreateContext(null);
            DesignModel = context.GetService<IDesignTimeModel>().Model;
        }

        /// <summary>The host's entity metadata.</summary>
        public IEntityMetadataProvider Metadata { get; }

        /// <summary>What the composition put in the model.</summary>
        internal TellmaModelDefinition Definition { get; }

        /// <summary>The design-time model migrations are generated from.</summary>
        public IModel DesignModel { get; }

        /// <summary>The host's services.</summary>
        public IServiceProvider Services => _provider;

        /// <summary>The design-time entity type mapped to a table of the <c>core</c> schema.</summary>
        /// <param name="table">The table.</param>
        /// <returns>The entity type.</returns>
        public IEntityType Table(string table)
        {
            return DesignModel.GetEntityTypes().Single(entityType => entityType.GetSchema() == "core" && entityType.GetTableName() == table);
        }

        /// <inheritdoc />
        public void Dispose()
        {
            _provider.Dispose();
        }
    }
}
