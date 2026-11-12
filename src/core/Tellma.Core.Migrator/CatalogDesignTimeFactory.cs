// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Tellma.Core.Tenancy;

namespace Tellma.Core.Migrator
{
    /// <summary>
    ///     The design-time factory of the catalog context, so <c>dotnet ef</c> targets this project for
    ///     the platform-owned catalog migrations without a host. It builds the context exactly as a
    ///     host does, so what <c>dotnet ef</c> scaffolds is what the migrator applies.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         Scaffolding never opens a connection. For <c>dotnet ef database update</c> the connection
    ///         string comes from <c>--connection</c>, else from the
    ///         <c>TELLMA_CATALOG_MIGRATIONS_SQL</c> environment variable, else from the LocalDB database
    ///         <c>TellmaCatalogDesign</c>.
    ///     </para>
    ///     <para>
    ///         A database updated this way serves migration authoring only. The migration grants the
    ///         membership table type to the application role, so the factory creates that role in the
    ///         database before the migration runs; the database still has no catalog row, no
    ///         application principal and no grants on its schema, so no host can serve it.
    ///     </para>
    /// </remarks>
    public sealed class CatalogDesignTimeFactory : IDesignTimeDbContextFactory<CatalogDbContext>
    {
        /// <summary>The environment variable that names the database <c>dotnet ef database update</c> connects to.</summary>
        public const string ConnectionStringVariable = "TELLMA_CATALOG_MIGRATIONS_SQL";

        /// <summary>The LocalDB database used when neither <c>--connection</c> nor the variable names one.</summary>
        public const string DefaultDatabase = "TellmaCatalogDesign";

        /// <inheritdoc />
        public CatalogDbContext CreateDbContext(string[] args)
        {
            string connectionString = DesignTimeConnections.Resolve(args, ConnectionStringVariable, DesignTimeConnections.LocalDb(DefaultDatabase));
            DbContextOptionsBuilder<CatalogDbContext> builder = CatalogDbContext.Configure(new DbContextOptionsBuilder<CatalogDbContext>(), connectionString);
            _ = builder.AddInterceptors(new ApplicationRoleInterceptor());
            return new CatalogDbContext(builder.Options);
        }
    }
}
