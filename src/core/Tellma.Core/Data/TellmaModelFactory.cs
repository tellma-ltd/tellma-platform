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
    ///     The factory builds the runtime model once and hands that one instance to every context
    ///     it creates.
    /// </summary>
    internal sealed class TellmaModelFactory
    {
        /// <summary>The running deployment, whose id names the connections.</summary>
        private readonly DeploymentIdentity _deployment;

        /// <summary>The composition's runtime model, built on first use.</summary>
        private readonly Lazy<IModel> _model;

        /// <summary>Creates the factory.</summary>
        /// <param name="definition">What the composition puts in the model.</param>
        /// <param name="deployment">The running deployment, whose id names the connections.</param>
        public TellmaModelFactory(TellmaModelDefinition definition, DeploymentIdentity deployment)
        {
            Definition = definition;
            _deployment = deployment;
            _model = new Lazy<IModel>(Build, LazyThreadSafetyMode.ExecutionAndPublication);
        }

        /// <summary>What the composition puts in the model.</summary>
        public TellmaModelDefinition Definition { get; }

        /// <summary>Creates a context of the composition's model.</summary>
        /// <param name="connectionString">The database's connection string, or <see langword="null" /> for a context that only builds the model.</param>
        /// <returns>The context; the caller disposes it.</returns>
        public TellmaDbContext CreateContext(string? connectionString)
        {
            // EF keeps built models in a size-bounded cache that every Tellma context in the
            // process shares, and it stops adding entries once that cache is full. Handing each
            // context the model this factory built keeps one composition on one runtime model
            // however many other compositions the process builds. A context asked for its
            // design-time model still builds or finds that one through EF's cache.
            DbContextOptionsBuilder<TellmaDbContext> options = Options(connectionString);
            _ = options.UseModel(_model.Value);
            return new TellmaDbContext(options.Options, Definition);
        }

        /// <summary>The composition's runtime model, built from a context with no connection on the first call.</summary>
        /// <returns>The model; every call returns the same instance.</returns>
        public IModel BuildModel()
        {
            return _model.Value;
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

        /// <summary>The options of a context over the composition's definition, with no model set.</summary>
        /// <param name="connectionString">The database's connection string, or <see langword="null" /> for none.</param>
        /// <returns>The options builder.</returns>
        private DbContextOptionsBuilder<TellmaDbContext> Options(string? connectionString)
        {
            DbContextOptionsBuilder<TellmaDbContext> options = new();
            _ = options.UseTellmaSqlServer(connectionString, _deployment);
            return options;
        }

        /// <summary>Builds the runtime model through EF's model source, from a context with no connection.</summary>
        /// <returns>The model.</returns>
        private IModel Build()
        {
            using TellmaDbContext context = new(Options(null).Options, Definition);
            return context.Model;
        }
    }
}
