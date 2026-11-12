// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Migrations.Operations;
using Microsoft.Extensions.DependencyInjection;
using Tellma.Core.Abstractions.Access;
using Tellma.Core.Abstractions.Composition;
using Tellma.Core.Composition;
using Tellma.Core.Data;
using Tellma.Core.Tests.Infrastructure;

namespace Tellma.Core.Tests.Data.Model
{
    /// <summary>
    ///     The test model, composed once per test class the way a host composes it: a feature
    ///     contributing the test entities and an open user configuration, with two leaf
    ///     substitutions. Exposes the runtime model the host registers, the design-time model
    ///     migrations are generated from, and the migration that creates the model from nothing.
    /// </summary>
    public sealed class DataModel : IDisposable
    {
        /// <summary>A connection string the migrations services accept; nothing ever connects with it.</summary>
        public const string UnusedConnectionString = "Server=(local);Database=TellmaModelTests;Integrated Security=true;TrustServerCertificate=true";

        private readonly ServiceProvider _provider;

        /// <summary>Composes the test model.</summary>
        public DataModel()
        {
            IServiceCollection services = TestComposition.Compose(static tellma => tellma
                .AddFeature(new TestFeature<Tag.A>("fixture", contribute: Contribute))
                .UseEntity<Center, MyCenter>()
                .UseEntity<User, MyTestUser>());

            _provider = services.BuildServiceProvider(validateScopes: true);
            Factory = _provider.GetRequiredService<TellmaModelFactory>();
            RuntimeModel = _provider.GetRequiredService<IModel>();

            using TellmaDbContext context = Factory.CreateContext(UnusedConnectionString);
            DesignModel = context.GetService<IDesignTimeModel>().Model;
            Operations = context.GetService<IMigrationsModelDiffer>().GetDifferences(null, DesignModel.GetRelationalModel());
            Sql = string.Join(
                Environment.NewLine,
                context.GetService<IMigrationsSqlGenerator>().Generate(Operations, DesignModel).Select(static command => command.CommandText));
        }

        /// <summary>The composition's model factory.</summary>
        internal TellmaModelFactory Factory { get; }

        /// <summary>The runtime model the host registers as its <see cref="IModel" />.</summary>
        public IModel RuntimeModel { get; }

        /// <summary>The design-time model migrations are generated from.</summary>
        public IModel DesignModel { get; }

        /// <summary>The operations that create the model in an empty database.</summary>
        public IReadOnlyList<MigrationOperation> Operations { get; }

        /// <summary>The SQL of <see cref="Operations" />.</summary>
        public string Sql { get; }

        /// <summary>The host's services.</summary>
        public IServiceProvider Services => _provider;

        /// <summary>The design-time entity type mapped to a table.</summary>
        /// <param name="schema">The table's schema.</param>
        /// <param name="table">The table.</param>
        /// <returns>The entity type.</returns>
        public IEntityType Table(string schema, string table)
        {
            return DesignModel.GetEntityTypes().Single(entityType => entityType.GetSchema() == schema && entityType.GetTableName() == table);
        }

        /// <summary>The design-time entity type of a class.</summary>
        /// <typeparam name="T">The class.</typeparam>
        /// <returns>The entity type.</returns>
        public IEntityType Entity<T>()
        {
            return DesignModel.FindEntityType(typeof(T)) ?? throw new InvalidOperationException($"{typeof(T).Name} is not in the model.");
        }

        /// <inheritdoc />
        public void Dispose()
        {
            _provider.Dispose();
        }

        /// <summary>The test feature's contribution.</summary>
        /// <param name="contribution">The contribution.</param>
        private static void Contribute(FeatureContribution contribution)
        {
            _ = contribution
                .Entity<TestJob>()
                .Entity<Widget>()
                .Entity<WidgetExtra>()
                .Entity<Center>()
                .Entity<Node>()
                .Entity<Shipment>()
                .Entity<Configured>()
                .Model<ConfiguredConfiguration>()
                .TableType<CollatedKeyRow>()
                .Model(typeof(TestUserConfiguration<>));
        }
    }
}
