// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Microsoft.Data.SqlClient;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.Metrics.Testing;
using System.Diagnostics.Metrics;
using System.Globalization;
using Tellma.Core.Abstractions.Caching;
using Tellma.Core.Abstractions.Data;
using Tellma.Core.Abstractions.Settings;
using Tellma.Core.Abstractions.TableTypes;
using Tellma.Core.Caching;
using Tellma.Core.Data;
using Tellma.Core.Data.Batches;
using Tellma.Testing.Entities;

namespace Tellma.Core.IntegrationTests.Data
{
    /// <summary>
    ///     Version tags under overlapping batches, each pause a real lock wait of the paused batch's own
    ///     session on the session that holds the lock, with the tag rows' locks read from the server's
    ///     lock table: the tag read before the
    ///     rows under a concurrent bump, a tag the persist guard read held until commit, the tag of a
    ///     cached list a persist batch probed held until commit so a deactivation waits, two bumps of
    ///     one read tag serialised on the guard instead of deadlocking, two writes of one caller's
    ///     stamp serialised, two bumps of different tags each read by the other serialised with no
    ///     deadlock, a settings save and a write of the saver's stamp waiting at the tags with no
    ///     deadlock, and a deadlock victim run again into a stale failure. The host runs without the
    ///     change-tracking audit, which is exact only while one batch at a time writes.
    /// </summary>
    /// <param name="fixture">The class's database, reset before each test.</param>
    [Trait("Category", "Integration")]
    public class VersionTagLockTests(UnauditedBatchDatabase fixture) : IClassFixture<UnauditedBatchDatabase>, IAsyncLifetime
    {
        /// <summary>The status of a granted lock request in <c>sys.dm_tran_locks</c>.</summary>
        private const string Granted = "GRANT";

        /// <summary>The fixture's cacheable list.</summary>
        private static readonly TableName Lookups = new(FixtureSchema.Name, "Lookups");

        /// <summary>The jobs table, written where a batch must write something.</summary>
        private static readonly TableName Jobs = new("core", "Jobs");

        /// <summary>The users' preference bags, whose writes bump each named user's preferences tag.</summary>
        private static readonly TableName UserPreferences = new("core", "UserPreferences");

        /// <summary>The list's own tag.</summary>
        private static readonly string LookupTag = VersionTagNames.ForEntity("fixture.Lookup");

        /// <summary>A statement that declares a write to the list and changes no row.</summary>
        private static readonly FormattableString TouchLookups = $"DELETE FROM [fixture].[Lookups] WHERE 1 = 0;";

        /// <summary>The gate: a read that waits while a side transaction holds the gate job's row.</summary>
        private static readonly FormattableString Gate = $"DECLARE @gate int = (SELECT COUNT(*) FROM [core].[Jobs] WITH (READCOMMITTEDLOCK) WHERE [Id] = 1001);";

        private static CancellationToken Token => TestContext.Current.CancellationToken;

        /// <inheritdoc />
        public async ValueTask InitializeAsync()
        {
            await fixture.Database.ResetAsync();
            await fixture.Database.SeedCallerAsync();
            await fixture.Database.SeedTagsAsync();
        }

        /// <inheritdoc />
        public ValueTask DisposeAsync()
        {
            GC.SuppressFinalize(this);
            return ValueTask.CompletedTask;
        }

        [Fact]
        public async Task A_bump_that_commits_between_a_batchs_tag_read_and_its_rows_leaves_the_rows_newer_than_their_stamp()
        {
            Dictionary<string, Guid> before = await fixture.Database.TagsAsync();
            await using SideTransaction side = await SideTransaction.HoldGateAsync(fixture.Database.Tenant.ConnectionString);

            // --- The loader: the prelude, the gate, then the rows ---
            await using AsyncServiceScope readerScope = fixture.Database.Tenant.CreateScope();
            var reader = (DataBatch)readerScope.ServiceProvider.GetRequiredService<ITenantDatabase>().CreateBatch(BatchPurpose.Read);
            _ = reader.Sql(Gate, null);
            BatchResult<RawResult> rows = reader.Sql($"SELECT COUNT(*) AS [N] FROM [fixture].[Lookups];", new SqlOptions(resultSets: 1));
            Task<BatchOutcome> reading = reader.ExecuteAsync(Token);
            _ = await WaitForBlockedAsync(readerScope, side.Session, reading);

            // --- A write commits a row and its bump while the loader waits at the gate ---
            await using (AsyncServiceScope writerScope = fixture.Database.Tenant.CreateScope())
            {
                var writer = (DataBatch)writerScope.ServiceProvider.GetRequiredService<ITenantDatabase>().CreateBatch(BatchPurpose.Persist);
                _ = writer.Sql(InsertLookup("late"), new SqlOptions(writes: Set(Lookups)));
                _ = await writer.ExecuteAsync(Token);
            }

            await side.ReleaseAsync();
            _ = await reading;

            // The loader saw the new row under the old tag: the next prelude reads the new tag and reloads.
            Assert.Equal(1, ((int[])rows.Value.ResultSets[0].GetBuffer(0))[0]);
            Assert.Equal(before[LookupTag], reader.ReadVersionTags![LookupTag].Value);
            Assert.NotEqual(before[LookupTag], (await fixture.Database.TagsAsync())[LookupTag]);
        }

        [Fact]
        public async Task A_tag_the_guard_read_is_held_until_commit_so_a_bump_of_it_waits()
        {
            Guid expected = (await fixture.Database.TagsAsync())[LookupTag];
            await using SideTransaction side = await SideTransaction.HoldGateAsync(fixture.Database.Tenant.ConnectionString);

            // --- A depends on the list's tag, writes elsewhere, and pauses at the gate inside its transaction ---
            await using AsyncServiceScope scopeA = fixture.Database.Tenant.CreateScope();
            var a = (DataBatch)scopeA.ServiceProvider.GetRequiredService<ITenantDatabase>().CreateBatch(BatchPurpose.Persist);
            a.MarkConnectPrologue(DataBatchDatabase.Premises);
            a.DependsOn([new VersionTagDependency(LookupTag, new VersionTag(expected), VersionTagMismatchPolicy.Rerun)]);
            _ = a.Sql($"DELETE FROM [core].[Jobs] WHERE [Id] = 0;", new SqlOptions(writes: Set(Jobs)));
            _ = a.Sql(Gate, null);
            Task<BatchOutcome> running = a.ExecuteAsync(Token);
            int sessionA = await WaitForBlockedAsync(scopeA, side.Session, running);

            // A waits at the gate, past its guard, and still holds the shared lock the guard took on the tag's row.
            Assert.Contains(new LockRequest(sessionA, "S", Granted), await TagLocksAsync(LookupTag));

            // --- B bumps the list's tag: its update waits for A's shared lock on the row ---
            await using AsyncServiceScope scopeB = fixture.Database.Tenant.CreateScope();
            var b = (DataBatch)scopeB.ServiceProvider.GetRequiredService<ITenantDatabase>().CreateBatch(BatchPurpose.Persist);
            _ = b.Sql(TouchLookups, new SqlOptions(writes: Set(Lookups)));
            Task<BatchOutcome> bumping = b.ExecuteAsync(Token);
            int sessionB = await WaitForBlockedAsync(scopeB, sessionA, running, bumping);
            Assert.Contains(await TagLocksAsync(LookupTag), request => request.Session == sessionB && request.Status != Granted);
            Assert.False(bumping.IsCompleted);

            // --- A commits on the tag it read; B's bump lands after ---
            await side.ReleaseAsync();
            _ = await running;
            _ = await bumping;
            Assert.True(a.GuardPassed);
            Assert.Equal(b.Tag, (await fixture.Database.TagsAsync())[LookupTag]);
        }

        [Fact]
        public async Task A_cached_list_a_writing_persist_batch_probed_keeps_its_tag_locked_until_commit_so_a_deactivation_waits()
        {
            await SqlServerFixture.ExecuteAsync(
                fixture.Database.Tenant.ConnectionString,
                "INSERT INTO [fixture].[Lookups] ([Id], [Code], [Name], [IsActive], [CreatedAt], [CreatedById], [ModifiedAt], [ModifiedById]) VALUES (1, 'L1', N'Lookup 1', 1, SYSDATETIMEOFFSET(), 1, SYSDATETIMEOFFSET(), 1);");
            Guid before = (await fixture.Database.TagsAsync())[LookupTag];
            fixture.Database.Tenant.Services.GetRequiredService<CacheableEntityStore>().Remove(new CacheableEntityKey(FixtureTenant.TenantId, "fixture.Lookup", MultilingualShape.Primary));
            await using SideTransaction side = await SideTransaction.HoldGateAsync(fixture.Database.Tenant.ConnectionString);

            // --- A reads the cold list through its own probe, writes elsewhere, and pauses at the gate inside its transaction ---
            await using AsyncServiceScope scopeA = fixture.Database.CreateSettledScope();
            var a = (DataBatch)scopeA.ServiceProvider.GetRequiredService<ITenantDatabase>().CreateBatch(BatchPurpose.Persist);
            a.MarkConnectPrologue(DataBatchDatabase.Premises);
            BatchResult<CacheResult<CachedEntitySet<Lookup>>> cold = a.FromCache<Lookup>();
            _ = a.Sql($"DELETE FROM [core].[Jobs] WHERE [Id] = 0;", new SqlOptions(writes: Set(Jobs)));
            _ = a.Sql(Gate, null);
            Task<BatchOutcome> running = a.ExecuteAsync(Token);
            int sessionA = await WaitForBlockedAsync(scopeA, side.Session, running);
            Assert.Contains(new LockRequest(sessionA, "S", Granted), await TagLocksAsync(LookupTag));

            // --- B deactivates the row A read: its guard's exclusive lock on the list's tag waits for A's shared one ---
            await using AsyncServiceScope scopeB = fixture.Database.Tenant.CreateScope();
            var b = (DataBatch)scopeB.ServiceProvider.GetRequiredService<ITenantDatabase>().CreateBatch(BatchPurpose.Persist);
            b.MarkConnectPrologue(DataBatchDatabase.Premises);
            _ = b.Sql($"UPDATE [fixture].[Lookups] SET [IsActive] = 0 WHERE [Id] = 1;", new SqlOptions(writes: Set(Lookups)));
            Task<BatchOutcome> deactivating = b.ExecuteAsync(Token);
            int sessionB = await WaitForBlockedAsync(scopeB, sessionA, running, deactivating);
            Assert.Contains(await TagLocksAsync(LookupTag), request => request.Session == sessionB && request.Status != Granted);
            Assert.False(deactivating.IsCompleted);

            // --- A commits on the active row it read, under the tag its prologue read; B's deactivation lands after ---
            await side.ReleaseAsync();
            _ = await running;
            _ = await deactivating;
            Assert.True(a.GuardPassed);
            Assert.Equal((CacheOutcome.Miss, new VersionTag(before)), (cold.Value.Outcome, cold.Value.Tag));
            Assert.True(cold.Value.Value!.ById[1].IsActive);
            Assert.Equal(b.Tag, (await fixture.Database.TagsAsync())[LookupTag]);
            Assert.False(await SqlServerFixture.ScalarAsync<bool>(fixture.Database.Tenant.ConnectionString, "SELECT [IsActive] FROM [fixture].[Lookups] WHERE [Id] = 1;"));
        }

        [Fact]
        public async Task Two_batches_that_read_and_bump_one_tag_serialise_on_the_guard_and_the_second_fails_stale_never_deadlocked()
        {
            Guid expected = (await fixture.Database.TagsAsync())[LookupTag];
            await using SideTransaction side = await SideTransaction.HoldGateAsync(fixture.Database.Tenant.ConnectionString);
            using MetricCollector<long> stale = new(fixture.Database.Tenant.Services.GetRequiredService<IMeterFactory>(), CacheTelemetryNames.MeterName, CacheTelemetryNames.Stale);

            // --- A reads and bumps the list's tag, pausing at the gate after its guard ---
            await using AsyncServiceScope scopeA = fixture.Database.Tenant.CreateScope();
            DataBatch a = ReadsAndBumps(scopeA, expected);
            _ = a.Sql(Gate, null);
            Task<BatchOutcome> first = a.ExecuteAsync(Token);
            int sessionA = await WaitForBlockedAsync(scopeA, side.Session, first);

            // --- B does the same: its guard waits for A's exclusive lock on the first name both bump instead of sharing the row ---
            await using AsyncServiceScope scopeB = fixture.Database.Tenant.CreateScope();
            DataBatch b = ReadsAndBumps(scopeB, expected);
            Task<BatchOutcome> second = b.ExecuteAsync(Token);
            int sessionB = await WaitForBlockedAsync(scopeB, sessionA, first, second);
            Assert.Contains(await TagLocksAsync(VersionTagNames.Entities), request => request.Session == sessionB && request.Status != Granted);

            // --- A commits its bump; B's guard then reads the moved tag and fails, as a re-run would want ---
            await side.ReleaseAsync();
            _ = await first;
            BatchAssertionFailedException refused = await Assert.ThrowsAsync<BatchAssertionFailedException>(() => second);
            Assert.Equal(TellmaSqlErrors.StaleVersionTag, refused.Number);
            Assert.Equal(a.Tag, (await fixture.Database.TagsAsync())[LookupTag]);
            Assert.Equal(["write-guard"], stale.GetMeasurementSnapshot().Select(static m => (string)m.Tags[CacheTelemetryNames.StalePhaseTag]!));
        }

        [Fact]
        public async Task Two_writes_of_one_callers_rows_serialise_on_the_callers_stamp_and_both_commit()
        {
            await using SideTransaction side = await SideTransaction.HoldGateAsync(fixture.Database.Tenant.ConnectionString);

            // --- A writes the caller's rows and pauses at the gate after its guard ---
            await using AsyncServiceScope scopeA = fixture.Database.Tenant.CreateScope();
            DataBatch a = WritesCallersRows(scopeA);
            _ = a.Sql(Gate, null);
            Task<BatchOutcome> first = a.ExecuteAsync(Token);
            int sessionA = await WaitForBlockedAsync(scopeA, side.Session, first);

            // --- B's guard waits for A's update lock on the stamp row ---
            await using AsyncServiceScope scopeB = fixture.Database.Tenant.CreateScope();
            DataBatch b = WritesCallersRows(scopeB);
            Task<BatchOutcome> second = b.ExecuteAsync(Token);
            _ = await WaitForBlockedAsync(scopeB, sessionA, first, second);

            // --- A's bump moves the preferences tag alone, so B's re-check still passes and B commits after ---
            await side.ReleaseAsync();
            _ = await first;
            _ = await second;
            Assert.Equal(b.Tag, await StampAsync("PreferencesTag", DataBatchDatabase.CallerId));
        }

        [Fact]
        public async Task A_settings_save_and_a_role_save_serialise_on_their_first_shared_tag_and_never_deadlock()
        {
            await using SideTransaction side = await SideTransaction.HoldGateAsync(fixture.Database.Tenant.ConnectionString);
            using MetricCollector<long> retries = new(fixture.Database.Tenant.Services.GetRequiredService<IMeterFactory>(), DataTelemetryNames.MeterName, DataTelemetryNames.Retries);

            // --- A bumps settings, expecting both tags, and pauses at the gate after its guard ---
            await using AsyncServiceScope scopeA = fixture.Database.Tenant.CreateScope();
            DataBatch a = Bumps(scopeA, VersionTagNames.Settings);
            _ = a.Sql(Gate, null);
            Task<BatchOutcome> first = a.ExecuteAsync(Token);
            int sessionA = await WaitForBlockedAsync(scopeA, side.Session, first);

            // --- B bumps permissions: its guard waits at the first name both lock, instead of
            // sharing the rows and deadlocking when each bump converts the row the other holds ---
            await using AsyncServiceScope scopeB = fixture.Database.Tenant.CreateScope();
            DataBatch b = Bumps(scopeB, VersionTagNames.Permissions);
            Task<BatchOutcome> second = b.ExecuteAsync(Token);
            int sessionB = await WaitForBlockedAsync(scopeB, sessionA, first, second);
            Assert.Contains(await TagLocksAsync(VersionTagNames.Permissions), request => request.Session == sessionB && request.Status != Granted);

            // --- A commits; B then reads the settings tag A moved and fails, never chosen as a deadlock victim ---
            await side.ReleaseAsync();
            _ = await first;
            BatchAssertionFailedException refused = await Assert.ThrowsAsync<BatchAssertionFailedException>(() => second);
            Assert.Equal(TellmaSqlErrors.StaleVersionTag, refused.Number);
            Assert.Empty(retries.GetMeasurementSnapshot());
            Dictionary<string, Guid> tags = await fixture.Database.TagsAsync();
            Assert.Equal(a.Tag, tags[VersionTagNames.Settings]);
            Assert.Equal(DataBatchDatabase.PermissionsTag, tags[VersionTagNames.Permissions]);
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public async Task A_settings_save_and_a_write_of_the_savers_stamp_take_the_tags_before_the_stamp_and_never_deadlock(bool bySaver)
        {
            int writer = bySaver ? DataBatchDatabase.CallerId : DataBatchDatabase.CallerId + 1;
            if (!bySaver)
            {
                await fixture.Database.SeedUserAsync(writer);
            }

            await using SideTransaction side = await SideTransaction.HoldGateAsync(fixture.Database.Tenant.ConnectionString);
            using MetricCollector<long> retries = new(fixture.Database.Tenant.Services.GetRequiredService<IMeterFactory>(), DataTelemetryNames.MeterName, DataTelemetryNames.Retries);

            // --- A, acting for the writer, writes the saver's preference rows and pauses at the gate
            // after its guard, holding shared locks on the tags and an update lock on its own stamp ---
            fixture.Database.Prologue.UserId = writer;
            await using AsyncServiceScope scopeA = fixture.Database.Tenant.CreateScope();
            var a = (DataBatch)scopeA.ServiceProvider.GetRequiredService<ITenantDatabase>().CreateBatch(BatchPurpose.Persist);
            a.MarkConnectPrologue(DataBatchDatabase.Premises);
            SqlIdentifier saver = a.Tvp<IdList>([new() { Id = DataBatchDatabase.CallerId }]);
            _ = a.Sql($"DELETE FROM [core].[UserPreferences] WHERE [UserId] IN (SELECT [Id] FROM {saver});", new SqlOptions(writes: Set(UserPreferences), userIds: saver));
            _ = a.Sql(Gate, null);
            Task<BatchOutcome> first = a.ExecuteAsync(Token);
            int sessionA = await WaitForBlockedAsync(scopeA, side.Session, first);

            // --- V, the saver, bumps settings: it waits at the settings tag A holds, before it takes
            // any lock on its stamp row, so A's bump of that row never waits for V ---
            fixture.Database.Prologue.UserId = DataBatchDatabase.CallerId;
            await using AsyncServiceScope scopeV = fixture.Database.Tenant.CreateScope();
            DataBatch v = Bumps(scopeV, VersionTagNames.Settings);
            Task<BatchOutcome> second = v.ExecuteAsync(Token);
            int sessionV = await WaitForBlockedAsync(scopeV, sessionA, first, second);
            Assert.Contains(await TagLocksAsync(VersionTagNames.Settings), request => request.Session == sessionV && request.Status != Granted);

            // --- A commits its stamp move; V then commits, neither chosen as a deadlock victim ---
            await side.ReleaseAsync();
            _ = await first;
            _ = await second;
            Assert.Empty(retries.GetMeasurementSnapshot());
            Assert.Equal(v.Tag, (await fixture.Database.TagsAsync())[VersionTagNames.Settings]);
            Assert.Equal(a.Tag, await StampAsync("PreferencesTag", DataBatchDatabase.CallerId));
        }

        [Fact]
        public async Task A_guarded_batch_chosen_as_a_deadlock_victim_is_run_again_and_fails_stale_on_the_tag_that_moved()
        {
            Guid expected = (await fixture.Database.TagsAsync())[LookupTag];
            using MetricCollector<long> retries = new(fixture.Database.Tenant.Services.GetRequiredService<IMeterFactory>(), DataTelemetryNames.MeterName, DataTelemetryNames.Retries);
            await using SqlConnection rival = new(fixture.Database.Tenant.ConnectionString);
            await rival.OpenAsync(Token);
            await using var rivalTransaction = (SqlTransaction)await rival.BeginTransactionAsync(Token);
            int rivalSession = await SessionAsync(rival, rivalTransaction, "SET DEADLOCK_PRIORITY HIGH; INSERT INTO [core].[Jobs] ([Id], [HandlerKey], [Status], [DueAt], [RunAsUserId], [CreatedAt]) VALUES (1001, N'fixture.probe', 'Pending', SYSUTCDATETIME(), 1, SYSUTCDATETIME());");

            // --- A holds a shared lock on the list's tag and waits at the gate the rival holds ---
            await using AsyncServiceScope scope = fixture.Database.Tenant.CreateScope();
            var a = (DataBatch)scope.ServiceProvider.GetRequiredService<ITenantDatabase>().CreateBatch(BatchPurpose.Persist);
            a.MarkConnectPrologue(DataBatchDatabase.Premises);
            a.DependsOn([new VersionTagDependency(LookupTag, new VersionTag(expected), VersionTagMismatchPolicy.Rerun)]);
            _ = a.Sql($"DELETE FROM [core].[Jobs] WHERE [Id] = 0;", new SqlOptions(writes: Set(Jobs)));
            _ = a.Sql(Gate, null);
            Task<BatchOutcome> running = a.ExecuteAsync(Token);
            int sessionA = await WaitForBlockedAsync(scope, rivalSession, running);
            Assert.Contains(new LockRequest(sessionA, "S", Granted), await TagLocksAsync(LookupTag));

            // --- The rival bumps that tag: a cycle the server breaks by killing A, the lower priority ---
            _ = await ExecuteInAsync(rival, rivalTransaction, $"UPDATE [core].[VersionTags] SET [Tag] = NEWID() WHERE [Name] = N'{LookupTag}';");
            await rivalTransaction.CommitAsync(Token);

            // --- A's re-run reads the moved tag: the guard fails it as stale, the runner's cue to recompose ---
            BatchAssertionFailedException refused = await Assert.ThrowsAsync<BatchAssertionFailedException>(() => running);
            Assert.Equal(TellmaSqlErrors.StaleVersionTag, refused.Number);
            Assert.Single(retries.GetMeasurementSnapshot());
        }

        /// <summary>A user's batch that bumps one tag explicitly and writes elsewhere, expecting the settings and permissions tags.</summary>
        /// <param name="scope">The scope.</param>
        /// <param name="name">The tag it bumps.</param>
        /// <returns>The batch.</returns>
        private static DataBatch Bumps(AsyncServiceScope scope, string name)
        {
            var batch = (DataBatch)scope.ServiceProvider.GetRequiredService<ITenantDatabase>().CreateBatch(BatchPurpose.Persist);
            batch.MarkConnectPrologue(DataBatchDatabase.Premises);
            batch.BumpVersionTag(name);
            _ = batch.Sql($"DELETE FROM [core].[Jobs] WHERE [Id] = 0;", new SqlOptions(writes: Set(Jobs)));
            return batch;
        }

        /// <summary>Runs statements inside an open transaction.</summary>
        /// <param name="connection">The connection.</param>
        /// <param name="transaction">The transaction.</param>
        /// <param name="text">The statements.</param>
        /// <returns>The rows affected.</returns>
        private static async Task<int> ExecuteInAsync(SqlConnection connection, SqlTransaction transaction, string text)
        {
            await using SqlCommand command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = text;
            return await command.ExecuteNonQueryAsync(Token);
        }

        /// <summary>Runs statements inside an open transaction and returns the session they ran on.</summary>
        /// <param name="connection">The connection.</param>
        /// <param name="transaction">The transaction.</param>
        /// <param name="text">The statements, which return no rows.</param>
        /// <returns>The connection's session id.</returns>
        private static async Task<int> SessionAsync(SqlConnection connection, SqlTransaction transaction, string text)
        {
            await using SqlCommand command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = text + "\nSELECT @@SPID;";
            return Convert.ToInt32(await command.ExecuteScalarAsync(Token), CultureInfo.InvariantCulture);
        }

        /// <summary>A user's batch that depends on the list's tag and bumps it.</summary>
        /// <param name="scope">The scope.</param>
        /// <param name="expected">The tag it depends on.</param>
        /// <returns>The batch.</returns>
        private static DataBatch ReadsAndBumps(AsyncServiceScope scope, Guid expected)
        {
            var batch = (DataBatch)scope.ServiceProvider.GetRequiredService<ITenantDatabase>().CreateBatch(BatchPurpose.Persist);
            batch.MarkConnectPrologue(DataBatchDatabase.Premises);
            batch.DependsOn([new VersionTagDependency(LookupTag, new VersionTag(expected), VersionTagMismatchPolicy.Rerun)]);
            _ = batch.Sql(TouchLookups, new SqlOptions(writes: Set(Lookups)));
            return batch;
        }

        /// <summary>A user's batch that writes the caller's own preference rows.</summary>
        /// <param name="scope">The scope.</param>
        /// <returns>The batch.</returns>
        private static DataBatch WritesCallersRows(AsyncServiceScope scope)
        {
            var batch = (DataBatch)scope.ServiceProvider.GetRequiredService<ITenantDatabase>().CreateBatch(BatchPurpose.Persist);
            batch.MarkConnectPrologue(DataBatchDatabase.Premises);
            _ = batch.Sql($"DELETE FROM [core].[UserPreferences] WHERE [UserId] = {DataBatchDatabase.CallerId};", SqlOptions.ForCaller(Set(UserPreferences)));
            return batch;
        }

        /// <summary>A statement that inserts one row into the list, audited to the system user.</summary>
        /// <param name="code">The row's code.</param>
        /// <returns>The statement.</returns>
        private static FormattableString InsertLookup(string code)
        {
            return $"INSERT INTO [fixture].[Lookups] ([Id], [Code], [Name], [IsActive], [CreatedAt], [CreatedById], [ModifiedAt], [ModifiedById]) VALUES (NEXT VALUE FOR [fixture].[sq_Lookups], {code}, {code}, 1, SYSDATETIMEOFFSET(), 1, SYSDATETIMEOFFSET(), 1);";
        }

        /// <summary>A set of tables.</summary>
        /// <param name="tables">The tables.</param>
        /// <returns>The set.</returns>
        private static HashSet<TableName> Set(params TableName[] tables)
        {
            return [.. tables];
        }

        /// <summary>One tag column of a user's stamp.</summary>
        /// <param name="column">The column.</param>
        /// <param name="userId">The user.</param>
        /// <returns>The tag.</returns>
        private async Task<Guid> StampAsync(string column, int userId)
        {
            return await SqlServerFixture.ScalarAsync<Guid>(
                fixture.Database.Tenant.ConnectionString,
                string.Create(CultureInfo.InvariantCulture, $"SELECT [{column}] FROM [core].[UserStamps] WHERE [UserId] = {userId};"));
        }

        /// <summary>
        ///     Waits until the batch of one scope waits for a lock that one session holds, failing at
        ///     once when a batch expected to wait finished instead. The wait is the batch's own,
        ///     observed on its session with the session it waits for, so no other lock wait in the
        ///     database, by another session or by the batch before it reached that lock, satisfies it.
        /// </summary>
        /// <param name="waiter">The scope whose batch is expected to wait.</param>
        /// <param name="holder">The session whose lock the batch waits for.</param>
        /// <param name="waiting">The batches expected to be waiting, the scope's last.</param>
        /// <returns>The session of the waiting batch.</returns>
        private async Task<int> WaitForBlockedAsync(AsyncServiceScope waiter, int holder, params Task[] waiting)
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(Token);
            timeout.CancelAfter(TimeSpan.FromSeconds(30));
            while (true)
            {
                // The session the scope's batch runs on, once it opened its connection: a re-run's, if it re-opened.
                if (DataBatchDatabase.LastSessionOf(waiter) is int session)
                {
                    string query = string.Create(
                        CultureInfo.InvariantCulture,
                        $"SELECT COUNT(*) FROM sys.dm_exec_requests WHERE [session_id] = {session} AND [wait_type] LIKE N'LCK%' AND [blocking_session_id] = {holder};");
                    if (await SqlServerFixture.ScalarAsync<int>(fixture.Database.MasterConnectionString, query) > 0)
                    {
                        return session;
                    }
                }

                // A batch that finished never waited: surface its failure, or fail for it.
                int finished = Array.FindIndex(waiting, static task => task.IsCompleted);
                if (finished >= 0)
                {
                    await waiting[finished];
                    Assert.Fail(string.Create(CultureInfo.InvariantCulture, $"The batch at {finished} was expected to wait for a lock that session {holder} holds and finished without waiting."));
                }

                // The barrier's own ceiling, not the test's cancellation: fail with what the server last
                // reported for the session, since a strict barrier's likely failure is a wait it never saw.
                try
                {
                    await Task.Delay(TimeSpan.FromMilliseconds(25), timeout.Token);
                }
                catch (OperationCanceledException) when (!Token.IsCancellationRequested)
                {
                    Assert.Fail(await DescribeUnobservedWaitAsync(DataBatchDatabase.LastSessionOf(waiter), holder));
                }
            }
        }

        /// <summary>
        ///     Describes a wait that was expected and never observed: the session expected to wait, the
        ///     session expected to hold the lock, and the session's request as the server reports it now.
        /// </summary>
        /// <param name="session">The session the batch runs on, if it opened a connection.</param>
        /// <param name="holder">The session expected to hold the lock.</param>
        /// <returns>The failure message.</returns>
        private async Task<string> DescribeUnobservedWaitAsync(int? session, int holder)
        {
            string expected = string.Create(CultureInfo.InvariantCulture, $"The batch was expected to wait for a lock that session {holder} holds and was not seen waiting for it within 30 seconds");
            if (session is not int id)
            {
                return expected + ": it never opened a connection.";
            }

            // The session's current request, if any: what it waits for, on what, and behind whom.
            const string Query = """
                SELECT r.[status], r.[command], r.[wait_type], r.[wait_resource], CAST(ISNULL(r.[blocking_session_id], 0) AS int)
                  FROM sys.dm_exec_requests AS r
                 WHERE r.[session_id] = @session;
                """;
            await using SqlConnection connection = new(fixture.Database.MasterConnectionString);
            await connection.OpenAsync(Token);
            await using SqlCommand command = connection.CreateCommand();
            command.CommandText = Query;
            _ = command.Parameters.Add(new SqlParameter("@session", System.Data.SqlDbType.Int) { Value = id });
            await using SqlDataReader reader = await command.ExecuteReaderAsync(Token);
            if (!await reader.ReadAsync(Token))
            {
                return string.Create(CultureInfo.InvariantCulture, $"{expected}: session {id} has no request running.");
            }

            string? waitType = reader.IsDBNull(2) ? null : reader.GetString(2);
            return string.Create(
                CultureInfo.InvariantCulture,
                $"{expected}: session {id} has a request with status '{reader.GetString(0)}', command '{reader.GetString(1)}', wait type '{waitType ?? "none"}', wait resource '{reader.GetString(3)}', blocked by session {reader.GetInt32(4)}.");
        }

        /// <summary>
        ///     Every lock request on a tag's row: the session, the mode, and whether it is granted or
        ///     waits, read from the server's lock table by the row's key in the tags' clustered index.
        /// </summary>
        /// <param name="name">The tag's name.</param>
        /// <returns>The requests.</returns>
        private async Task<IReadOnlyList<LockRequest>> TagLocksAsync(string name)
        {
            // The row's key hash, read past any lock, identifies it in sys.dm_tran_locks within the index's hobt.
            const string Query = """
                SELECT CAST(l.[request_session_id] AS int), l.[request_mode], l.[request_status]
                  FROM sys.dm_tran_locks AS l
                 WHERE l.[resource_database_id] = DB_ID() AND l.[resource_type] = N'KEY'
                   AND l.[resource_associated_entity_id] = (SELECT p.[hobt_id] FROM sys.partitions AS p WHERE p.[object_id] = OBJECT_ID(N'[core].[VersionTags]') AND p.[index_id] = 1)
                   AND RTRIM(l.[resource_description]) = (SELECT %%lockres%% FROM [core].[VersionTags] WITH (NOLOCK) WHERE [Name] = @name);
                """;
            List<LockRequest> requests = [];
            await using SqlConnection connection = new(fixture.Database.Tenant.ConnectionString);
            await connection.OpenAsync(Token);
            await using SqlCommand command = connection.CreateCommand();
            command.CommandText = Query;
            _ = command.Parameters.Add(new SqlParameter("@name", System.Data.SqlDbType.NVarChar, 128) { Value = name });
            await using SqlDataReader reader = await command.ExecuteReaderAsync(Token);
            while (await reader.ReadAsync(Token))
            {
                requests.Add(new LockRequest(reader.GetInt32(0), reader.GetString(1), reader.GetString(2)));
            }

            return requests;
        }

        /// <summary>One request in the server's lock table.</summary>
        /// <param name="Session">The requesting session.</param>
        /// <param name="Mode">The mode, such as <c>S</c> or <c>X</c>.</param>
        /// <param name="Status"><see cref="Granted" />, <c>WAIT</c> or <c>CONVERT</c>.</param>
        private readonly record struct LockRequest(int Session, string Mode, string Status);

        /// <summary>
        ///     A transaction on a connection of its own that holds the gate job's row: every batch that
        ///     reads the gate waits until it is released, which rolls it back, so it writes nothing.
        /// </summary>
        private sealed class SideTransaction : IAsyncDisposable
        {
            /// <summary>The connection.</summary>
            private readonly SqlConnection _connection;

            /// <summary>The transaction, until released.</summary>
            private SqlTransaction? _transaction;

            /// <summary>Wraps an open connection and its transaction.</summary>
            /// <param name="connection">The connection.</param>
            /// <param name="transaction">The transaction holding the gate.</param>
            /// <param name="session">The connection's session id.</param>
            private SideTransaction(SqlConnection connection, SqlTransaction transaction, int session)
            {
                _connection = connection;
                _transaction = transaction;
                Session = session;
            }

            /// <summary>The session holding the gate, which a batch reading the gate waits for.</summary>
            public int Session { get; }

            /// <summary>Opens a transaction that inserts the gate job, uncommitted.</summary>
            /// <param name="connectionString">The database.</param>
            /// <returns>The held transaction.</returns>
            public static async Task<SideTransaction> HoldGateAsync(string connectionString)
            {
                SqlConnection connection = new(connectionString);
                await connection.OpenAsync(Token);
                var transaction = (SqlTransaction)await connection.BeginTransactionAsync(Token);
                int session = await SessionAsync(
                    connection,
                    transaction,
                    "INSERT INTO [core].[Jobs] ([Id], [HandlerKey], [Status], [DueAt], [RunAsUserId], [CreatedAt]) VALUES (1001, N'fixture.probe', 'Pending', SYSUTCDATETIME(), 1, SYSUTCDATETIME());");
                return new SideTransaction(connection, transaction, session);
            }

            /// <summary>Rolls the transaction back, opening the gate.</summary>
            /// <returns>A task that completes when released.</returns>
            public async Task ReleaseAsync()
            {
                if (_transaction is SqlTransaction transaction)
                {
                    _transaction = null;
                    await transaction.RollbackAsync(Token);
                    await transaction.DisposeAsync();
                }
            }

            /// <inheritdoc />
            public async ValueTask DisposeAsync()
            {
                await ReleaseAsync();
                await _connection.DisposeAsync();
            }
        }
    }
}
