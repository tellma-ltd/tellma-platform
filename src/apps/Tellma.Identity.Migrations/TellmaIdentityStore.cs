// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Tellma.Identity.Data;

namespace Tellma.Identity.Migrations
{
    /// <summary>
    ///     Applies the identity engine's migrations to a store from outside the engine's host: the
    ///     entry a distribution's migrator calls to migrate the in-proc store (schema
    ///     <c>idsvr</c> in its catalog), and the programmatic twin of the <c>dotnet ef database
    ///     update</c> an operator runs against a standalone store.
    /// </summary>
    /// <remarks>
    ///     The context is built exactly as the design-time factory builds it — the Identity store
    ///     options from <see cref="TellmaIdentityModelDefaults" /> and this assembly as the
    ///     migrations assembly — so what it applies is what <c>dotnet ef</c> scaffolded. A missing
    ///     database is created, in the platform collation the model carries.
    /// </remarks>
    public static class TellmaIdentityStore
    {
        /// <summary>Applies every pending identity migration to the database the connection string names.</summary>
        /// <param name="connectionString">The store's connection string; its principal needs DDL rights.</param>
        /// <returns>A task that completes when the store is at the engine's last migration.</returns>
        public static Task MigrateAsync(string connectionString)
        {
            return MigrateAsync(connectionString, CancellationToken.None);
        }

        /// <summary>Applies every pending identity migration to the database the connection string names.</summary>
        /// <param name="connectionString">The store's connection string; its principal needs DDL rights.</param>
        /// <param name="cancellationToken">Aborts the migration between statements.</param>
        /// <returns>A task that completes when the store is at the engine's last migration.</returns>
        public static async Task MigrateAsync(string connectionString, CancellationToken cancellationToken)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(connectionString);

            await using ServiceProvider modelServices = CreateModelServices();
            await using TellmaIdentityDbContext context = CreateContext(connectionString, modelServices);
            await context.Database.MigrateAsync(cancellationToken);
        }

        /// <summary>
        ///     The application services the identity model reads while it is built: the Identity
        ///     store options, schema version 3 in particular, without which the passkeys table would
        ///     silently vanish from the model.
        /// </summary>
        /// <returns>The service provider; the caller disposes it.</returns>
        internal static ServiceProvider CreateModelServices()
        {
            ServiceCollection services = new();
            services.Configure<IdentityOptions>(TellmaIdentityModelDefaults.ConfigureStoreOptions);
            return services.BuildServiceProvider();
        }

        /// <summary>Builds the identity context over SQL Server, with this assembly's migrations.</summary>
        /// <param name="connectionString">The store's connection string.</param>
        /// <param name="modelServices">The provider from <see cref="CreateModelServices" />.</param>
        /// <returns>The context; the caller disposes it.</returns>
        internal static TellmaIdentityDbContext CreateContext(string connectionString, IServiceProvider modelServices)
        {
            DbContextOptionsBuilder<TellmaIdentityDbContext> builder = new();
            builder.UseSqlServer(connectionString, static sql => sql.MigrationsAssembly(TellmaIdentityConstants.MigrationsAssemblyName));
            builder.UseApplicationServiceProvider(modelServices);
            return new TellmaIdentityDbContext(builder.Options);
        }
    }
}
