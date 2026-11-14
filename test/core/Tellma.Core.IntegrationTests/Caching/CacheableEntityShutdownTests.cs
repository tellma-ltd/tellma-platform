// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Microsoft.Extensions.DependencyInjection;
using Tellma.Core.Abstractions.Caching;
using Tellma.Core.Abstractions.Data;
using Tellma.Core.Caching;
using Tellma.Core.Data.Batches;
using Tellma.Core.IntegrationTests.Data;
using Tellma.Testing.Entities;

namespace Tellma.Core.IntegrationTests.Caching
{
    /// <summary>
    ///     A cached list read by a batch that commits after the store was disposed, as at host stop:
    ///     the list is skipped and logged, never turned into a failure of the committed write. The
    ///     class has a database of its own, since its store does not survive the test.
    /// </summary>
    /// <param name="fixture">The class's database.</param>
    [Trait("Category", "Integration")]
    public class CacheableEntityShutdownTests(DataBatchDatabase fixture) : IClassFixture<DataBatchDatabase>, IAsyncLifetime
    {
        private static CancellationToken Token => TestContext.Current.CancellationToken;

        /// <inheritdoc />
        public async ValueTask InitializeAsync()
        {
            await fixture.ResetAsync();
            await fixture.SeedCallerAsync();
            await fixture.SeedTagsAsync();
        }

        /// <inheritdoc />
        public ValueTask DisposeAsync()
        {
            GC.SuppressFinalize(this);
            return ValueTask.CompletedTask;
        }

        [Fact]
        public async Task A_persist_batch_that_commits_after_the_store_is_disposed_serves_its_rows_and_runs_its_commit_callbacks()
        {
            await SqlServerFixture.ExecuteAsync(
                fixture.Tenant.ConnectionString,
                "INSERT INTO [fixture].[Lookups] ([Id], [Code], [Name], [IsActive], [CreatedAt], [CreatedById], [ModifiedAt], [ModifiedById]) VALUES (1, 'L1', N'Lookup 1', 1, SYSDATETIMEOFFSET(), 1, SYSDATETIMEOFFSET(), 1);");
            await using AsyncServiceScope scope = fixture.CreateSettledScope();

            // --- A writing persist batch reads the cold list, then the host disposes the store before it runs ---
            var batch = (DataBatch)scope.ServiceProvider.GetRequiredService<ITenantDatabase>().CreateBatch(BatchPurpose.Persist);
            batch.MarkConnectPrologue(DataBatchDatabase.Premises);
            BatchResult<CacheResult<CachedEntitySet<Lookup>>> cold = batch.FromCache<Lookup>();
            _ = batch.Sql($"DELETE FROM [core].[Jobs] WHERE [Id] = 0;", new SqlOptions(writes: new HashSet<TableName> { new("core", "Jobs") }));
            bool committed = false;
            batch.OnCommitted(_ => committed = true);
            fixture.Tenant.Services.GetRequiredService<CacheableEntityStore>().Dispose();

            _ = await batch.ExecuteAsync(Token);

            // --- The commit stands: its callbacks ran, the rows were served, and the skipped store was logged ---
            Assert.True(committed);
            Assert.Equal((CacheOutcome.Miss, "L1"), (cold.Value.Outcome, Assert.Single(cold.Value.Value!.Items).Code));
            Assert.Single(fixture.Logs.GetSnapshot(), static record => record.Id.Name == "CacheableEntityNotStored");
        }
    }
}
