// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Microsoft.Data.SqlClient;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.Metrics.Testing;
using System.Diagnostics.Metrics;
using Tellma.Core.Abstractions.Data;
using Tellma.Core.Data;
using Tellma.Core.Data.Batches;

namespace Tellma.Core.IntegrationTests.Data
{
    /// <summary>
    ///     A read that outruns its command timeout: it is re-run once, not as often as other
    ///     failures that may run twice, because a query that timed out under load holds its caller
    ///     for the whole timeout on every try.
    /// </summary>
    /// <param name="fixture">The class's database, whose host times commands out after one second.</param>
    [Trait("Category", "Integration")]
    public class DataBatchTimeoutTests(ShortTimeoutDatabase fixture) : IClassFixture<ShortTimeoutDatabase>, IAsyncLifetime
    {
        private static CancellationToken Token => TestContext.Current.CancellationToken;

        private DataBatchDatabase Database => fixture.Database;

        /// <inheritdoc />
        public async ValueTask InitializeAsync()
        {
            await Database.ResetAsync();
        }

        /// <inheritdoc />
        public ValueTask DisposeAsync()
        {
            GC.SuppressFinalize(this);
            return ValueTask.CompletedTask;
        }

        [Fact]
        public async Task A_read_that_times_out_is_re_run_once_and_then_reported_exhausted()
        {
            IMeterFactory meters = Database.Tenant.Services.GetRequiredService<IMeterFactory>();
            using MetricCollector<long> retries = new(meters, DataTelemetryNames.MeterName, DataTelemetryNames.Retries);
            await SqlServerFixture.ExecuteAsync(Database.Tenant.ConnectionString, "INSERT INTO [core].[VersionTags] ([Name]) VALUES (N'fx-gate');");

            // --- A side transaction holds the row the read waits for, past every attempt's timeout ---
            await using SqlConnection side = new(Database.Tenant.ConnectionString);
            await side.OpenAsync(Token);
            await using var holding = (SqlTransaction)await side.BeginTransactionAsync(Token);
            await using (SqlCommand lockRow = side.CreateCommand())
            {
                lockRow.Transaction = holding;
                lockRow.CommandText = "UPDATE [core].[VersionTags] SET [Tag] = NEWID() WHERE [Name] = N'fx-gate';";
                _ = await lockRow.ExecuteNonQueryAsync(Token);
            }

            await using AsyncServiceScope scope = Database.Tenant.CreateScope();
            DataAccessScope access = scope.ServiceProvider.GetRequiredService<DataAccessScope>();
            IDataBatch batch = scope.ServiceProvider.GetRequiredService<ITenantDatabase>().CreateBatch(BatchPurpose.Read);
            _ = batch.Sql(
                $"SELECT COUNT(*) AS [N] FROM [core].[VersionTags] WITH (READCOMMITTEDLOCK) WHERE [Name] = N'fx-gate';",
                new SqlOptions(resultSets: 1));

            DataAccessRetryExhaustedException exhausted = await Assert.ThrowsAsync<DataAccessRetryExhaustedException>(() => batch.ExecuteAsync(Token));

            // --- Two attempts, each timed out; one re-run, metered as an ambiguous timeout ---
            Assert.Equal(2, exhausted.Attempts);
            Assert.Equal(-2, Assert.IsType<SqlException>(exhausted.InnerException).Number);
            Assert.Equal((2, 1), (access.RoundTrips, access.Retries));
            CollectedMeasurement<long> retry = Assert.Single(retries.GetMeasurementSnapshot());
            Assert.Equal(
                (DataMetrics.AmbiguousClass, "-2"),
                ((string?)retry.Tags[DataTelemetryNames.ClassTag], (string?)retry.Tags[DataTelemetryNames.ErrorTag]));
            await holding.RollbackAsync(Token);
        }
    }
}
