// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Tellma.Core.Data;

namespace Tellma.Testing.Entities
{
    /// <summary>
    ///     The fixture database's EF context: the fixture composition's tenant model, built by the
    ///     platform exactly as a tenant context builds it, with the fixture's own migrations and
    ///     table-type sweep scope. It creates and migrates fixture databases, nothing else.
    /// </summary>
    /// <param name="options">The options; see <see cref="Configure" />.</param>
    public sealed class TellmaFixtureDbContext(DbContextOptions<TellmaFixtureDbContext> options) : DbContext(options)
    {
        /// <summary>Configures options for a fixture database.</summary>
        /// <param name="options">The options builder.</param>
        /// <param name="connectionString">The database's connection string, or <see langword="null" /> for a context that only builds the model.</param>
        /// <returns>The same builder.</returns>
        public static DbContextOptionsBuilder<TellmaFixtureDbContext> Configure(DbContextOptionsBuilder<TellmaFixtureDbContext> options, string? connectionString)
        {
            ArgumentNullException.ThrowIfNull(options);

            // The platform's provider options and conventions, then this assembly's migrations.
            _ = options.UseTellmaSqlServer(connectionString, FixtureComposition.Deployment, TableTypeSweepScopes.Fixture);
            _ = options.UseSqlServer(static sql => sql.MigrationsAssembly(typeof(TellmaFixtureDbContext).Assembly.GetName().Name));
            return options;
        }

        /// <summary>Creates a context over a fixture database.</summary>
        /// <param name="connectionString">The database's connection string.</param>
        /// <returns>The context; the caller disposes it.</returns>
        public static TellmaFixtureDbContext Create(string connectionString)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(connectionString);
            return new TellmaFixtureDbContext(Configure(new DbContextOptionsBuilder<TellmaFixtureDbContext>(), connectionString).Options);
        }

        /// <inheritdoc />
        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            ArgumentNullException.ThrowIfNull(modelBuilder);
            TellmaDbContext.Build(modelBuilder, FixtureComposition.Definition);
        }
    }

    /// <summary>
    ///     The design-time factory of the fixture context, so <c>dotnet ef</c> scaffolds the fixture
    ///     migrations from this project. Scaffolding never opens a connection.
    /// </summary>
    public sealed class TellmaFixtureDesignTimeFactory : IDesignTimeDbContextFactory<TellmaFixtureDbContext>
    {
        /// <summary>The database a design-time context names; nothing connects to it while scaffolding.</summary>
        public const string DesignConnectionString =
            "Server=(localdb)\\MSSQLLocalDB;Database=TellmaFixtureDesign;Trusted_Connection=True;TrustServerCertificate=True";

        /// <inheritdoc />
        public TellmaFixtureDbContext CreateDbContext(string[] args)
        {
            return TellmaFixtureDbContext.Create(DesignConnectionString);
        }
    }
}
