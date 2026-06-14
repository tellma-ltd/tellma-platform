// Copyright (c) 2026 Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Tellma.Core.EntityFrameworkCore.TableTypes;

namespace Tellma.Core.EntityFrameworkCore.MigrationsHost
{
    /// <summary>
    ///     The design-time factory <c>dotnet ef</c> uses to instantiate the context. Scaffolding
    ///     commands (<c>migrations add</c>, <c>migrations script</c>, <c>migrations bundle</c>)
    ///     never open the connection, so a placeholder connection string suffices unless
    ///     <c>TELLMA_MIGRATIONSHOST_SQL</c> points somewhere real.
    /// </summary>
    public class MigrationsHostContextFactory : IDesignTimeDbContextFactory<MigrationsHostContext>
    {
        /// <inheritdoc />
        public MigrationsHostContext CreateDbContext(string[] args)
        {
            string connectionString = Environment.GetEnvironmentVariable("TELLMA_MIGRATIONSHOST_SQL")
                ?? "Server=(localdb)\\MSSQLLocalDB;Database=TellmaMigrationsHost;Integrated Security=true;TrustServerCertificate=true";

            DbContextOptionsBuilder<MigrationsHostContext> optionsBuilder = new();
            optionsBuilder
                .UseSqlServer(connectionString)
                .UseTableTypes(sweepScope: nameof(MigrationsHostContext));
            return new MigrationsHostContext(optionsBuilder.Options);
        }
    }
}
