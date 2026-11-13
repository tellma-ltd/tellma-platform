// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Microsoft.Data.SqlClient;
using Microsoft.Extensions.DependencyInjection;
using System.Globalization;
using Tellma.Core.Abstractions.Access;
using Tellma.Core.Abstractions.Data;
using Tellma.Core.Abstractions.Jobs;
using Tellma.Core.Data;
using Tellma.Core.Data.Batches;
using Tellma.Testing.Entities;

namespace Tellma.Core.IntegrationTests.Jobs
{
    /// <summary>
    ///     The job queue against a fixture database: the enqueue statement riding a caller's batch,
    ///     the columns it stores, the owners it stamps in one statement and in request order, the
    ///     owners it refuses while their stored job has not ended or when they are missing, the wake
    ///     after the commit, and a failed batch that leaves no trace.
    /// </summary>
    /// <param name="fixture">The class's database, reset before each test.</param>
    [Trait("Category", "Integration")]
    public class JobEnqueueTests(JobsDatabase fixture) : IClassFixture<JobsDatabase>, IAsyncLifetime
    {
        /// <summary>The first shipment id the tests store, past the range of an <see cref="int" />.</summary>
        private const long FirstShipmentId = 5_000_000_001;

        /// <summary>The id of a job a test stores directly, far past the ids the sequence hands out.</summary>
        private const int StoredJobId = 2_000_000_001;

        private static CancellationToken Token => TestContext.Current.CancellationToken;

        /// <inheritdoc />
        public async ValueTask InitializeAsync()
        {
            await fixture.ResetAsync();
        }

        /// <inheritdoc />
        public ValueTask DisposeAsync()
        {
            GC.SuppressFinalize(this);
            return ValueTask.CompletedTask;
        }

        [Fact]
        public async Task An_arguments_only_job_rides_a_persist_batch_and_stores_its_label_and_arguments()
        {
            DateTimeOffset dueAt = new(2026, 4, 1, 9, 0, 0, TimeSpan.FromHours(3));
            JobMessage label = new("Jobs_Exporting", new Dictionary<string, object?> { ["entity"] = "Widget", ["count"] = 3 });

            await using AsyncServiceScope scope = fixture.Tenant.CreateScope();
            IDataBatch batch = Database(scope).CreateBatch(BatchPurpose.Persist);
            BatchResult<IReadOnlyList<int>> result = Queue(scope).Enqueue(batch,
            [
                new JobRequest(FixtureJobKeys.Probe, new { Depth = 3, Region = "east" }, dueAt, WellKnownIds.SystemUserId, null, null, label),
                new JobRequest(FixtureJobKeys.Probe, null, null, null, null, null),
            ]);
            _ = await batch.ExecuteAsync(Token);

            // --- Every column the statement stores, as the request and the scope named it ---
            Assert.True(result.IsCompleted);
            Assert.Equal(2, result.Value.Count);
            Dictionary<string, object?> first = await JobRowAsync(result.Value[0]);
            Assert.Equal(FixtureJobKeys.Probe, first["HandlerKey"]);
            Assert.Equal(nameof(JobStatus.Pending), first["Status"]);
            Assert.Equal(dueAt, first["DueAt"]);
            Assert.Equal(0, first["Attempts"]);
            Assert.Equal(/*lang=json,strict*/ """{"depth":3,"region":"east"}""", first["ArgumentsJson"]);
            Assert.Equal("Jobs_Exporting", first["LabelKey"]);
            Assert.Equal(/*lang=json,strict*/ """{"entity":"Widget","count":3}""", first["LabelArgumentsJson"]);
            Assert.Equal(WellKnownIds.SystemUserId, first["RunAsUserId"]);
            Assert.Equal(WellKnownIds.SystemUserId, first["RequestedById"]);
            Assert.Null(first["ConfirmedKeys"]);
            Assert.Null(first["LeaseToken"]);
            Assert.NotNull(first["CreatedAt"]);

            // --- A request without a due time is due when it is stored; one without a label or arguments stores none ---
            Dictionary<string, object?> second = await JobRowAsync(result.Value[1]);
            Assert.Equal(second["CreatedAt"], second["DueAt"]);
            Assert.Null(second["LabelKey"]);
            Assert.Null(second["LabelArgumentsJson"]);
            Assert.Null(second["ArgumentsJson"]);
            Assert.Null(second["RequestedById"]);
        }

        [Fact]
        public async Task Three_thousand_owners_take_their_jobs_in_one_statement_in_request_order()
        {
            const int count = 3_000;
            await SeedShipmentsAsync(count);

            // The requests name the rows out of key order, so request order is not row order: a
            // multiplier coprime to the count permutes the offsets.
            Shipment[] shipments = [.. Enumerable.Range(0, count).Select(static ordinal => new Shipment { Id = FirstShipmentId + (ordinal * 7_919L % count) })];

            await using AsyncServiceScope scope = fixture.Tenant.CreateScope();
            var batch = (DataBatch)Database(scope).CreateBatch(BatchPurpose.Persist);
            BatchResult<IReadOnlyList<int>> result = Queue(scope).Enqueue(
                batch,
                [.. shipments.Select(static shipment => new JobRequest(FixtureJobKeys.Ship, null, null, null, null, shipment))]);

            // One statement, beside the batch's version-tag prelude, carries every job and the owners' update.
            BatchStatement statement = Assert.Single(batch.Statements, static statement => statement.Writes.Count > 0);
            Assert.Equal([new TableName("core", "Jobs"), new TableName("fixture", "Shipments")], statement.Writes.OrderBy(static table => table.Name, StringComparer.Ordinal));
            _ = await batch.ExecuteAsync(Token);

            // --- The ids follow request order, one contiguous range ---
            IReadOnlyList<int> ids = result.Value;
            Assert.Equal(count, ids.Count);
            Assert.Equal(Enumerable.Range(ids[0], count), ids);

            // --- Each row, in the database and in memory, carries the job its request was given ---
            Dictionary<long, int?> stored = await ShipmentJobsAsync();
            Assert.Equal(count, stored.Count);
            for (int ordinal = 0; ordinal < count; ordinal++)
            {
                Assert.Equal(ids[ordinal], stored[shipments[ordinal].Id]);
                Assert.Equal(ids[ordinal], shipments[ordinal].JobId);
            }

            Assert.Equal(count, await SqlServerFixture.ScalarAsync<int>(fixture.ConnectionString, $"SELECT COUNT(*) FROM [core].[Jobs] WHERE [HandlerKey] = N'{FixtureJobKeys.Ship}' AND [Status] = 'Pending';"));
        }

        [Fact]
        public async Task An_owner_keyed_by_an_int_takes_its_job()
        {
            await SqlServerFixture.ExecuteAsync(
                fixture.ConnectionString,
                "INSERT INTO [fixture].[Exports] ([Id], [Name], [CreatedAt], [CreatedById], [ModifiedAt], [ModifiedById]) VALUES (7, N'Ledger', SYSDATETIMEOFFSET(), 1, SYSDATETIMEOFFSET(), 1);");
            Export export = new() { Id = 7 };

            await using AsyncServiceScope scope = fixture.Tenant.CreateScope();
            IReadOnlyList<int> ids = await Queue(scope).EnqueueAsync([new JobRequest(FixtureJobKeys.Export, null, null, null, null, export)], Token);

            int id = Assert.Single(ids);
            Assert.Equal(id, export.JobId);
            Assert.Equal(id, await SqlServerFixture.ScalarAsync<int>(fixture.ConnectionString, "SELECT [JobId] FROM [fixture].[Exports] WHERE [Id] = 7;"));
        }

        [Fact]
        public async Task The_pollers_are_woken_once_and_only_after_the_commit()
        {
            await using AsyncServiceScope scope = fixture.Tenant.CreateScope();
            IDataBatch batch = Database(scope).CreateBatch(BatchPurpose.Persist);
            _ = Queue(scope).Enqueue(batch, [new JobRequest(FixtureJobKeys.Probe, null, null, null, null, null)]);

            // Composing the batch wakes nothing; the commit wakes the tenant once.
            Assert.Equal(0, fixture.Wakes);
            _ = await batch.ExecuteAsync(Token);
            Assert.Equal(1, fixture.Wakes);
        }

        [Fact]
        public async Task A_batch_that_fails_after_the_enqueue_stores_no_job_stamps_no_owner_and_wakes_nothing()
        {
            await SeedShipmentsAsync(1);
            Shipment shipment = new() { Id = FirstShipmentId };

            await using AsyncServiceScope scope = fixture.Tenant.CreateScope();
            IDataBatch batch = Database(scope).CreateBatch(BatchPurpose.Persist);
            _ = Queue(scope).Enqueue(batch, [new JobRequest(FixtureJobKeys.Ship, null, null, null, null, shipment)]);
            _ = batch.Sql($"THROW 50600, N'Fixture.AfterTheEnqueue', 1;", null);

            _ = await Assert.ThrowsAsync<BatchAssertionFailedException>(() => batch.ExecuteAsync(Token));

            // --- The transaction rolled the job and the owner's stamp back, and the commit's work never ran ---
            Assert.Equal(0, await SqlServerFixture.ScalarAsync<int>(fixture.ConnectionString, "SELECT COUNT(*) FROM [core].[Jobs];"));
            Assert.Null((await ShipmentJobsAsync())[FirstShipmentId]);
            Assert.Null(shipment.JobId);
            Assert.Equal(0, fixture.Wakes);
        }

        [Theory]
        [InlineData(nameof(JobStatus.Pending))]
        [InlineData(nameof(JobStatus.Running))]
        [InlineData(nameof(JobStatus.Held))]
        [InlineData(nameof(JobStatus.PendingConfirmation))]
        public async Task An_owner_whose_stored_job_has_not_ended_is_refused_whatever_its_member_says(string status)
        {
            await SeedShipmentsAsync(1);
            await StoreJobAsync(StoredJobId, status, FirstShipmentId);

            // The member in memory says the row has no job; the stored row decides.
            Shipment shipment = new() { Id = FirstShipmentId };
            await using AsyncServiceScope scope = fixture.Tenant.CreateScope();
            IDataBatch batch = Database(scope).CreateBatch(BatchPurpose.Persist);
            _ = Queue(scope).Enqueue(batch, [new JobRequest(FixtureJobKeys.Ship, null, null, null, null, shipment)]);

            BatchAssertionFailedException exception = await Assert.ThrowsAsync<BatchAssertionFailedException>(() => batch.ExecuteAsync(Token));

            // --- Refused as a whole: no job stored, the stored stamp and the member unchanged, nothing woken ---
            Assert.Equal((TellmaSqlErrors.Invariant, JobEnqueueCodes.OwnerUnavailable), (exception.Number, exception.Code));
            Assert.Equal(1, await SqlServerFixture.ScalarAsync<int>(fixture.ConnectionString, "SELECT COUNT(*) FROM [core].[Jobs];"));
            Assert.Equal(StoredJobId, (await ShipmentJobsAsync())[FirstShipmentId]);
            Assert.Null(shipment.JobId);
            Assert.Equal(0, fixture.Wakes);
        }

        [Theory]
        [InlineData(nameof(JobStatus.Succeeded))]
        [InlineData(nameof(JobStatus.Failed))]
        [InlineData(nameof(JobStatus.Cancelled))]
        public async Task An_owner_whose_stored_job_ended_is_enqueued_again(string status)
        {
            await SeedShipmentsAsync(2);
            await StoreJobAsync(StoredJobId, status, FirstShipmentId);

            // One row's job ended and the other never had one; the member still names the ended job.
            Shipment ended = new() { Id = FirstShipmentId, JobId = StoredJobId };
            Shipment fresh = new() { Id = FirstShipmentId + 1 };
            await using AsyncServiceScope scope = fixture.Tenant.CreateScope();
            IReadOnlyList<int> ids = await Queue(scope).EnqueueAsync(
                [new JobRequest(FixtureJobKeys.Ship, null, null, null, null, ended), new JobRequest(FixtureJobKeys.Ship, null, null, null, null, fresh)],
                Token);

            // --- Both rows are stamped with their new jobs, the ended job is kept, and the tenant is woken ---
            Dictionary<long, int?> stored = await ShipmentJobsAsync();
            Assert.Equal((ids[0], ids[1]), (stored[FirstShipmentId], stored[FirstShipmentId + 1]));
            Assert.Equal((ids[0], ids[1]), (ended.JobId, fresh.JobId));
            Assert.Equal(3, await SqlServerFixture.ScalarAsync<int>(fixture.ConnectionString, "SELECT COUNT(*) FROM [core].[Jobs];"));
            Assert.Equal(1, fixture.Wakes);
        }

        [Fact]
        public async Task A_missing_owner_is_refused_and_takes_the_other_owners_jobs_with_it()
        {
            await SeedShipmentsAsync(1);
            Shipment present = new() { Id = FirstShipmentId };
            Shipment missing = new() { Id = FirstShipmentId + 1 };

            await using AsyncServiceScope scope = fixture.Tenant.CreateScope();
            IDataBatch batch = Database(scope).CreateBatch(BatchPurpose.Persist);
            _ = Queue(scope).Enqueue(batch, [new JobRequest(FixtureJobKeys.Ship, null, null, null, null, present), new JobRequest(FixtureJobKeys.Ship, null, null, null, null, missing)]);

            BatchAssertionFailedException exception = await Assert.ThrowsAsync<BatchAssertionFailedException>(() => batch.ExecuteAsync(Token));

            // --- Refused as a whole: the present row is not stamped either ---
            Assert.Equal((TellmaSqlErrors.Invariant, JobEnqueueCodes.OwnerUnavailable), (exception.Number, exception.Code));
            Assert.Equal(0, await SqlServerFixture.ScalarAsync<int>(fixture.ConnectionString, "SELECT COUNT(*) FROM [core].[Jobs];"));
            Assert.Null((await ShipmentJobsAsync())[FirstShipmentId]);
            Assert.Equal((null, null), (present.JobId, missing.JobId));
            Assert.Equal(0, fixture.Wakes);
        }

        [Fact]
        public async Task An_enqueue_whose_batch_was_switched_to_autocommit_refuses_to_run()
        {
            await SeedShipmentsAsync(1);
            Shipment shipment = new() { Id = FirstShipmentId };

            // The queue refuses an autocommit batch; switching the mode afterwards meets the statement's own guard.
            await using AsyncServiceScope scope = fixture.Tenant.CreateScope();
            IDataBatch batch = Database(scope).CreateBatch(BatchPurpose.Persist);
            _ = Queue(scope).Enqueue(batch, [new JobRequest(FixtureJobKeys.Ship, null, null, null, null, shipment)]);
            batch.TransactionMode = TransactionMode.None;

            BatchAssertionFailedException exception = await Assert.ThrowsAsync<BatchAssertionFailedException>(() => batch.ExecuteAsync(Token));

            Assert.Equal((TellmaSqlErrors.Invariant, JobEnqueueCodes.OutsideTransaction), (exception.Number, exception.Code));
            Assert.Equal(0, await SqlServerFixture.ScalarAsync<int>(fixture.ConnectionString, "SELECT COUNT(*) FROM [core].[Jobs];"));
            Assert.Null((await ShipmentJobsAsync())[FirstShipmentId]);
            Assert.Equal(0, fixture.Wakes);
        }

        [Fact]
        public async Task Rows_of_two_entity_types_are_refused_before_anything_is_appended()
        {
            await using AsyncServiceScope scope = fixture.Tenant.CreateScope();
            var batch = (DataBatch)Database(scope).CreateBatch(BatchPurpose.Persist);

            InvalidOperationException exception = Assert.Throws<InvalidOperationException>(() => Queue(scope).Enqueue(batch,
            [
                new JobRequest(FixtureJobKeys.Ship, null, null, null, null, new Shipment { Id = FirstShipmentId }),
                new JobRequest(FixtureJobKeys.Export, null, null, null, null, new Export { Id = 7 }),
            ]));

            Assert.Contains("one call enqueues the rows of one entity type", exception.Message, StringComparison.Ordinal);
            Assert.Empty(batch.Statements);
            Assert.Empty(batch.CommittedCallbacks);
        }

        /// <summary>The scope's tenant database.</summary>
        /// <param name="scope">The scope.</param>
        /// <returns>The database.</returns>
        private static ITenantDatabase Database(AsyncServiceScope scope)
        {
            return scope.ServiceProvider.GetRequiredService<ITenantDatabase>();
        }

        /// <summary>The scope's job queue.</summary>
        /// <param name="scope">The scope.</param>
        /// <returns>The queue.</returns>
        private static IJobQueue Queue(AsyncServiceScope scope)
        {
            return scope.ServiceProvider.GetRequiredService<IJobQueue>();
        }

        /// <summary>Stores shipments with consecutive ids from <see cref="FirstShipmentId" />, none with a job.</summary>
        /// <param name="count">How many.</param>
        /// <returns>A task that completes when they are stored.</returns>
        private async Task SeedShipmentsAsync(int count)
        {
            await SqlServerFixture.ExecuteAsync(
                fixture.ConnectionString,
                string.Create(CultureInfo.InvariantCulture, $"""
                    INSERT INTO [fixture].[Shipments] ([Id], [State], [CreatedAt], [CreatedById], [ModifiedAt], [ModifiedById])
                    SELECT CAST({FirstShipmentId - 1} AS bigint) + [n].[N], 'Pending', SYSDATETIMEOFFSET(), 1, SYSDATETIMEOFFSET(), 1
                    FROM (SELECT TOP ({count}) ROW_NUMBER() OVER (ORDER BY (SELECT NULL)) AS [N] FROM sys.all_columns AS [a] CROSS JOIN sys.all_columns AS [b]) AS [n];
                    """));
        }

        /// <summary>Stores a job of the ship handler in a status and stamps a shipment with it.</summary>
        /// <param name="id">The job's id.</param>
        /// <param name="status">The job's status; a running job holds a lease and one waiting for confirmation what it waits for.</param>
        /// <param name="shipmentId">The shipment that owns the job.</param>
        /// <returns>A task that completes when the job is stored.</returns>
        private async Task StoreJobAsync(int id, string status, long shipmentId)
        {
            // The CHECK constraints tie a lease to a running job and the awaited confirmations to a waiting one.
            (string columns, string values) = status switch
            {
                nameof(JobStatus.Running) => (", [LeaseToken], [LeaseExpiresAt]", ", NEWID(), SYSUTCDATETIME()"),
                nameof(JobStatus.PendingConfirmation) => (", [PendingConfirmationsJson]", """, N'[{"key":"Sales.Backdated"}]'"""),
                _ => (string.Empty, string.Empty),
            };

            await SqlServerFixture.ExecuteAsync(
                fixture.ConnectionString,
                string.Create(CultureInfo.InvariantCulture, $"INSERT INTO [core].[Jobs] ([Id], [HandlerKey], [Status], [DueAt], [RunAsUserId], [CreatedAt]{columns}) VALUES ({id}, N'{FixtureJobKeys.Ship}', '{status}', SYSUTCDATETIME(), 1, SYSUTCDATETIME(){values});"),
                string.Create(CultureInfo.InvariantCulture, $"UPDATE [fixture].[Shipments] SET [JobId] = {id} WHERE [Id] = {shipmentId};"));
        }

        /// <summary>Every shipment's job.</summary>
        /// <returns>Each shipment id's job id.</returns>
        private async Task<Dictionary<long, int?>> ShipmentJobsAsync()
        {
            Dictionary<long, int?> jobs = [];
            await using SqlConnection connection = new(fixture.ConnectionString);
            await connection.OpenAsync(Token);
            await using SqlCommand command = connection.CreateCommand();
            command.CommandText = "SELECT [Id], [JobId] FROM [fixture].[Shipments];";
            await using SqlDataReader reader = await command.ExecuteReaderAsync(Token);
            while (await reader.ReadAsync(Token))
            {
                jobs[reader.GetInt64(0)] = reader.IsDBNull(1) ? null : reader.GetInt32(1);
            }

            return jobs;
        }

        /// <summary>One job row, each column by name, nulls as <see langword="null" />.</summary>
        /// <param name="id">The job.</param>
        /// <returns>The row.</returns>
        private async Task<Dictionary<string, object?>> JobRowAsync(int id)
        {
            await using SqlConnection connection = new(fixture.ConnectionString);
            await connection.OpenAsync(Token);
            await using SqlCommand command = connection.CreateCommand();
            command.CommandText = "SELECT * FROM [core].[Jobs] WHERE [Id] = @id;";
            _ = command.Parameters.AddWithValue("@id", id);
            await using SqlDataReader reader = await command.ExecuteReaderAsync(Token);
            Assert.True(await reader.ReadAsync(Token));
            Dictionary<string, object?> row = new(StringComparer.Ordinal);
            for (int column = 0; column < reader.FieldCount; column++)
            {
                row[reader.GetName(column)] = reader.IsDBNull(column) ? null : reader.GetValue(column);
            }

            return row;
        }
    }
}
