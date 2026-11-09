// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.Extensions.DependencyInjection;
using Tellma.Core.Abstractions.Hosting;
using Tellma.Core.Composition;
using Tellma.Core.Data.Metadata;
using Tellma.Core.Data.TableTypes;

namespace Tellma.Core.Data
{
    /// <summary>
    ///     Creates the contexts of one composition's tenant model: the connection-less context the
    ///     host's model is built from, and the short-lived per-database contexts the migrator runs.
    /// </summary>
    /// <param name="definition">What the composition puts in the model.</param>
    /// <param name="deployment">The running deployment, whose id names the connections.</param>
    internal sealed class TellmaModelFactory(TellmaModelDefinition definition, DeploymentIdentity deployment)
    {
        /// <summary>What the composition puts in the model.</summary>
        public TellmaModelDefinition Definition { get; } = definition;

        /// <summary>Creates a context of the composition's model.</summary>
        /// <param name="connectionString">The database's connection string, or <see langword="null" /> for a context that only builds the model.</param>
        /// <returns>The context; the caller disposes it.</returns>
        public TellmaDbContext CreateContext(string? connectionString)
        {
            DbContextOptionsBuilder<TellmaDbContext> options = new();
            _ = options.UseTellmaSqlServer(connectionString, deployment);
            return new TellmaDbContext(options.Options, Definition);
        }

        /// <summary>Builds the composition's runtime model from a context with no connection.</summary>
        /// <returns>The model.</returns>
        public IModel BuildModel()
        {
            using TellmaDbContext context = CreateContext(null);
            return context.Model;
        }

        /// <summary>
        ///     Registers the composition's model: its definition, this factory, the model as a singleton
        ///     <see cref="IModel" /> built on first use, the table-type binder over it, and the entity
        ///     metadata, which the startup gate builds and validates before the host serves traffic.
        /// </summary>
        /// <param name="services">The host's services.</param>
        /// <param name="record">The realised composition.</param>
        public static void Register(IServiceCollection services, TellmaCompositionRecord record)
        {
            ArgumentNullException.ThrowIfNull(services);
            ArgumentNullException.ThrowIfNull(record);

            _ = services.AddSingleton(TellmaModelDefinition.From(record));
            _ = services.AddSingleton<TellmaModelFactory>();
            _ = services.AddSingleton(static provider => provider.GetRequiredService<TellmaModelFactory>().BuildModel());
            _ = services.AddSingleton<TableTypeBinder>();
            _ = services.AddSingleton(static provider => new EntityMetadataSource(
                provider.GetRequiredService<IModel>,
                provider.GetRequiredService<TellmaModelDefinition>()));
            _ = services.AddSingleton(static provider => provider.GetRequiredService<EntityMetadataSource>().Provider);
        }
    }
}
