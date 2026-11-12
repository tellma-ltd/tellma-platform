// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Tellma.Core.Composition;
using Tellma.Core.Data;

namespace Tellma.Core.Migrator
{
    /// <summary>
    ///     The base of a distribution's design-time factory for the tenant model, so <c>dotnet ef</c>
    ///     builds <see cref="TellmaDbContext" /> from the distribution's own composition without
    ///     discovering a host. The distribution derives it in its migrator project and points
    ///     <see cref="Compose" /> at its composition method; the migrations land in that project.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         Scaffolding never opens a connection. For <c>dotnet ef database update</c> the connection
    ///         string comes from <c>--connection</c>, else from the <c>TELLMA_TENANT_MIGRATIONS_SQL</c>
    ///         environment variable, else from the LocalDB database <c>TellmaTenantDesign</c>.
    ///     </para>
    ///     <para>
    ///         A database updated this way serves migration authoring only. Every table type's
    ///         migration grants <c>EXECUTE</c> to the application role, so the factory creates that role
    ///         in the database before a migration runs; the database still has no schema fingerprint,
    ///         no catalog row and no grants on its schemas, so no host can serve it.
    ///     </para>
    /// </remarks>
    /// <typeparam name="TContext">The tenant model's context, <see cref="TellmaDbContext" />.</typeparam>
    public abstract class TellmaDesignTimeDbContextFactory<TContext> : IDesignTimeDbContextFactory<TContext>
        where TContext : DbContext
    {
        /// <summary>The environment variable that names the database <c>dotnet ef database update</c> connects to.</summary>
        public const string ConnectionStringVariable = "TELLMA_TENANT_MIGRATIONS_SQL";

        /// <summary>The LocalDB database used when neither <c>--connection</c> nor the variable names one.</summary>
        public const string DefaultDatabase = "TellmaTenantDesign";

        /// <summary>
        ///     The slug the design-time composition runs under. The model does not depend on it; it
        ///     only names the design connection's application. A distribution may return its own.
        /// </summary>
        protected virtual string Slug => "design";

        /// <summary>The distribution's composition method: the same one its hosts and migrator compose with.</summary>
        /// <param name="builder">The builder.</param>
        public abstract void Compose(TellmaBuilder builder);

        /// <inheritdoc />
        /// <exception cref="InvalidOperationException"><typeparamref name="TContext" /> is not <see cref="TellmaDbContext" />.</exception>
        /// <exception cref="Abstractions.Composition.TellmaCompositionException">The composition has problems.</exception>
        public TContext CreateDbContext(string[] args)
        {
            // The platform's tenant context is the only one a distribution migrates.
            if (typeof(TContext) != typeof(TellmaDbContext))
            {
                throw new InvalidOperationException(
                    $"A Tellma design-time factory creates {nameof(TellmaDbContext)}; derive it as TellmaDesignTimeDbContextFactory<{nameof(TellmaDbContext)}>.");
            }

            string connectionString = DesignTimeConnections.Resolve(args, ConnectionStringVariable, DesignTimeConnections.LocalDb(DefaultDatabase));

            // --- The composition, realised without a host: only its model is used ---
            // The configuration is the migrator project's appsettings.json and the environment's
            // variables, read but never validated: a composition method may consult it, the model
            // never depends on it.
            IConfiguration configuration = new ConfigurationBuilder()
                .SetBasePath(AppContext.BaseDirectory)
                .AddJsonFile("appsettings.json", optional: true)
                .AddEnvironmentVariables()
                .Build();

            ServiceCollection services = [];
            _ = services.AddTellma(Slug, configuration, new DesignEnvironment(GetType().Assembly.GetName().Name ?? Slug), Compose);
            using ServiceProvider provider = services.BuildServiceProvider();

            // --- The context: this project's migrations, and the role every table type grants ---
            TellmaDbContext context = provider.GetRequiredService<TellmaModelFactory>()
                .CreateMigrationContext(connectionString, GetType().Assembly, new ApplicationRoleInterceptor());
            return (TContext)(DbContext)context;
        }

        /// <summary>The design-time host environment: Development, rooted at the tool's base directory.</summary>
        /// <param name="applicationName">The migrator project's assembly name.</param>
        private sealed class DesignEnvironment(string applicationName) : IHostEnvironment
        {
            /// <inheritdoc />
            public string EnvironmentName { get; set; } = Environments.Development;

            /// <inheritdoc />
            public string ApplicationName { get; set; } = applicationName;

            /// <inheritdoc />
            public string ContentRootPath { get; set; } = AppContext.BaseDirectory;

            /// <inheritdoc />
            public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
        }
    }
}
