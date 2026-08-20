// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Microsoft.Extensions.DependencyInjection;
using Tellma.Identity.Data;

namespace Tellma.Identity.Migrations
{
    /// <summary>
    ///     Design-time factory for <c>dotnet ef</c>. It must replicate the runtime Identity store
    ///     options that shape the model — schema version 3 in particular, without which the
    ///     passkeys table would silently vanish from generated migrations — which it does through
    ///     the same <see cref="TellmaIdentityModelDefaults" /> the runtime uses.
    /// </summary>
    public sealed class TellmaIdentityDesignTimeDbContextFactory : IDesignTimeDbContextFactory<TellmaIdentityDbContext>
    {
        /// <inheritdoc />
        public TellmaIdentityDbContext CreateDbContext(string[] args)
        {
            // Scaffolding never needs a live server; the connection string only matters for
            // `dotnet ef database update`, where it comes from the environment.
            string connectionString = Environment.GetEnvironmentVariable("TELLMA_IDENTITY_MIGRATIONS_SQL")
                ?? "Server=(localdb)\\MSSQLLocalDB;Database=TellmaIdentityDesign;Trusted_Connection=True;TrustServerCertificate=True";

            // The Identity model reads its store options from the application service provider.
            ServiceCollection services = new();
            services.Configure<IdentityOptions>(TellmaIdentityModelDefaults.ConfigureStoreOptions);
            ServiceProvider provider = services.BuildServiceProvider();

            DbContextOptionsBuilder<TellmaIdentityDbContext> builder = new();
            builder.UseSqlServer(connectionString, static sql => sql.MigrationsAssembly(TellmaIdentityConstants.MigrationsAssemblyName));
            builder.UseApplicationServiceProvider(provider);

            return new TellmaIdentityDbContext(builder.Options);
        }
    }
}
