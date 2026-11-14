// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Tellma.Core.Abstractions.Hosting;
using Tellma.Core.EntityFrameworkCore.TableTypes;

namespace Tellma.Core.Data
{
    /// <summary>
    ///     The one place the platform applies SQL Server provider options, so every host and the
    ///     migrator build one model.
    /// </summary>
    public static class TellmaSqlServerDbContextOptionsExtensions
    {
        /// <summary>
        ///     The compatibility level the model is built for: SQL Server 2022. Pinned so a JSON column
        ///     stays <c>nvarchar(max)</c> and never turns into the native <c>json</c> type behind the
        ///     model's back.
        /// </summary>
        public const int CompatibilityLevel = 160;

        /// <summary>
        ///     Configures a context for the platform's SQL Server databases: <c>UseSqlServer</c> with the
        ///     <c>hierarchyid</c> mapping and the pinned compatibility level, the table-type extension
        ///     under <paramref name="sweepScope" />, and the platform's model conventions. A connection
        ///     string's <c>Application Name</c> is set to the deployment id, the value every connection a
        ///     host opens carries, so the provider's connections and the executor's share one pool.
        /// </summary>
        /// <param name="options">The options builder.</param>
        /// <param name="connectionString">
        ///     The connection string, or <see langword="null" /> for a context that only builds the model.
        /// </param>
        /// <param name="deployment">The running deployment, whose id names the connections.</param>
        /// <param name="sweepScope">
        ///     The table-type sweep scope: the types this context owns in its database. A tenant model is
        ///     <see cref="TableTypeSweepScopes.Tenant" />.
        /// </param>
        /// <returns>The same builder, for chaining.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="options" /> or <paramref name="deployment" /> is null.</exception>
        /// <exception cref="ArgumentException"><paramref name="sweepScope" /> is empty, or <paramref name="connectionString" /> is not a valid connection string.</exception>
        public static DbContextOptionsBuilder UseTellmaSqlServer(
            this DbContextOptionsBuilder options,
            string? connectionString,
            DeploymentIdentity deployment,
            string sweepScope = TableTypeSweepScopes.Tenant)
        {
            ArgumentNullException.ThrowIfNull(options);
            ArgumentNullException.ThrowIfNull(deployment);
            ArgumentException.ThrowIfNullOrWhiteSpace(sweepScope);

            // --- The provider: hierarchyid, and the pinned compatibility level ---
            _ = connectionString is null
                ? options.UseSqlServer(Configure)
                : options.UseSqlServer(WithApplicationName(connectionString, deployment), Configure);

            // --- The table types and the platform's conventions ---
            _ = options.UseTableTypes(sweepScope);
            ((IDbContextOptionsBuilderInfrastructure)options).AddOrUpdateExtension(new TellmaSqlServerOptionsExtension());
            return options;

            static void Configure(SqlServerDbContextOptionsBuilder sql)
            {
                _ = sql.UseHierarchyId();
                _ = sql.UseCompatibilityLevel(CompatibilityLevel);
            }
        }

        /// <summary>The connection string with its <c>Application Name</c> set to the deployment id.</summary>
        /// <param name="connectionString">The connection string.</param>
        /// <param name="deployment">The running deployment.</param>
        /// <returns>The composed string.</returns>
        internal static string WithApplicationName(string connectionString, DeploymentIdentity deployment)
        {
            // Composed with the builder, never by concatenation: a value with a ';' would otherwise
            // inject a keyword.
            SqlConnectionStringBuilder builder = new(connectionString)
            {
                ApplicationName = deployment.DeploymentId,
            };

            return builder.ConnectionString;
        }
    }

    /// <summary>
    ///     The table-type sweep scopes of the platform's contexts. A scope names the types a context
    ///     owns in its database: the cleanup sweep touches only types stamped with it, and a create
    ///     refuses a same-named type another scope owns. The values are stored in every database, so
    ///     they never change.
    /// </summary>
    public static class TableTypeSweepScopes
    {
        /// <summary>The tenant model's types, in every tenant database.</summary>
        public const string Tenant = "tellma.tenant";

        /// <summary>The catalog model's types, in the catalog database.</summary>
        public const string Catalog = "tellma.catalog";

        /// <summary>The data layer's test fixture model's types, in a fixture database.</summary>
        public const string Fixture = "tellma.fixture";
    }
}
