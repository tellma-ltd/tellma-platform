// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Tellma.Core.Abstractions.Caching;
using Tellma.Core.Abstractions.Data;
using Tellma.Core.Abstractions.TableTypes;
using Tellma.Core.Provisioning;

namespace Tellma.Core.Caching
{
    // The seed and the wholesale bump are spec 0013 §2.8, the only writes that insert tag rows or
    // move every tag at once.
    /// <summary>
    ///     The migrator's seed of a tenant database's version tags: one row for every name the
    ///     registry knows, inserted when absent on every run, and every tag moved after a run that
    ///     applied a migration or a provisioning step, since those write past the executor.
    /// </summary>
    /// <remarks>
    ///     Each call runs one maintenance batch, in a scope of its own, over a handle to the tenant's
    ///     database: the migrator runs outside any tenant scope.
    /// </remarks>
    /// <param name="scopes">Creates the scope each call's batch runs in.</param>
    /// <param name="databases">Opens the tenant's database outside any tenant scope.</param>
    /// <param name="registry">Every registered tag name.</param>
    /// <param name="logger">The logger.</param>
    internal sealed class TenantVersionTagSeeder(
        IServiceScopeFactory scopes,
        ITenantDatabaseFactory databases,
        IVersionTagRegistry registry,
        ILogger<TenantVersionTagSeeder> logger) : ITenantVersionTagSeeder
    {
        /// <summary>The tables both statements write.</summary>
        private static readonly IReadOnlySet<TableName> Writes = new HashSet<TableName> { VersionTagRowConfiguration.Table };

        /// <inheritdoc />
        public async Task SeedAsync(int tenantId, CancellationToken cancellationToken)
        {
            List<StringList> names = [.. registry.Names.Select(static name => new StringList { Id = name })];
            await RunAsync(
                tenantId,
                batch => batch.Sql(
                    $"INSERT INTO [core].[VersionTags] ([Name], [Tag]) SELECT n.[Id], NEWID() FROM {names} AS n WHERE NOT EXISTS (SELECT 1 FROM [core].[VersionTags] AS t WHERE t.[Name] = n.[Id]);",
                    new SqlOptions(writes: Writes)),
                cancellationToken).ConfigureAwait(false);
            CachingLog.VersionTagsSeeded(logger, tenantId, names.Count);
        }

        /// <inheritdoc />
        public async Task BumpAllAsync(int tenantId, CancellationToken cancellationToken)
        {
            await RunAsync(
                tenantId,
                static batch => batch.Sql($"UPDATE [core].[VersionTags] SET [Tag] = NEWID();", new SqlOptions(writes: Writes)),
                cancellationToken).ConfigureAwait(false);
            CachingLog.VersionTagsBumped(logger, tenantId);
        }

        /// <summary>Runs one maintenance batch over the tenant's database, in a scope of its own.</summary>
        /// <param name="tenantId">The tenant.</param>
        /// <param name="compose">Appends the batch's statement.</param>
        /// <param name="cancellationToken">Cancels the batch.</param>
        /// <returns>A task that completes when the batch committed.</returns>
        private async Task RunAsync(int tenantId, Action<IDataBatch> compose, CancellationToken cancellationToken)
        {
            AsyncServiceScope scope = scopes.CreateAsyncScope();
            await using (scope.ConfigureAwait(false))
            {
                ITenantDatabaseHandle database = await databases.OpenAsync(tenantId, scope.ServiceProvider, cancellationToken).ConfigureAwait(false);
                await using (database.ConfigureAwait(false))
                {
                    IDataBatch batch = database.CreateBatch(BatchPurpose.Maintenance);
                    compose(batch);
                    _ = await batch.ExecuteAsync(cancellationToken).ConfigureAwait(false);
                }
            }
        }
    }
}
