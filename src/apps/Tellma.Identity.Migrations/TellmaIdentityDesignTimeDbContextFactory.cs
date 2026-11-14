// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Microsoft.EntityFrameworkCore.Design;
using Tellma.Identity.Data;

namespace Tellma.Identity.Migrations
{
    /// <summary>
    ///     Design-time factory for <c>dotnet ef</c>. It must replicate the runtime Identity store
    ///     options that shape the model — schema version 3 in particular, without which the
    ///     passkeys table would silently vanish from generated migrations — which it does through
    ///     the same <see cref="TellmaIdentityModelDefaults" /> the runtime uses. It builds the
    ///     context the way <see cref="TellmaIdentityStore" /> does, so a store the migrator applies
    ///     is the store <c>dotnet ef</c> scaffolded.
    /// </summary>
    public sealed class TellmaIdentityDesignTimeDbContextFactory : IDesignTimeDbContextFactory<TellmaIdentityDbContext>
    {
        /// <summary>The environment variable that names the store <c>dotnet ef</c> connects to.</summary>
        public const string ConnectionStringVariable = "TELLMA_IDENTITY_MIGRATIONS_SQL";

        /// <inheritdoc />
        public TellmaIdentityDbContext CreateDbContext(string[] args)
        {
            // Scaffolding never needs a live server; the connection string only matters for
            // `dotnet ef database update`, where `--connection` overrides it, else it comes from
            // the environment, else from LocalDB.
            string connectionString = Environment.GetEnvironmentVariable(ConnectionStringVariable)
                ?? "Server=(localdb)\\MSSQLLocalDB;Database=TellmaIdentityDesign;Trusted_Connection=True;TrustServerCertificate=True";

            // The provider lives as long as the context: dotnet ef disposes the context, and the
            // process ends right after, so nothing outlives the command.
            return TellmaIdentityStore.CreateContext(connectionString, TellmaIdentityStore.CreateModelServices());
        }
    }
}
