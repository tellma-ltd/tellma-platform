// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Microsoft.Data.SqlClient;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.Metrics.Testing;
using System.Diagnostics.Metrics;
using System.Globalization;
using System.Text.Json;
using Tellma.Core.Abstractions.Caching;
using Tellma.Core.Abstractions.Data;
using Tellma.Core.Abstractions.Localization;
using Tellma.Core.Abstractions.Settings;
using Tellma.Core.Abstractions.TableTypes;
using Tellma.Core.Caching;
using Tellma.Core.Data;
using Tellma.Core.Data.Batches;
using Tellma.Core.Tenancy;
using Tellma.Testing.Entities;

namespace Tellma.Core.IntegrationTests.Data
{
    /// <summary>
    ///     Version tags under overlapping batches, each pause a real lock wait: the tag read before the
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
        /// <summary>The fixture's cacheable list.</summary>
        private static readonly TableName Lookups = new(FixtureSchema.Name, "Lookups");

        /// <summary>The stand-in jobs table, written where a batch must write something.</summary>
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
            await WaitForLockWaitersAsync(1, reading);

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
            await WaitForLockWaitersAsync(1, running);

            // --- B bumps the list's tag: its update waits for A's shared lock on the row ---
            await using AsyncServiceScope scopeB = fixture.Database.Tenant.CreateScope();
            var b = (DataBatch)scopeB.ServiceProvider.GetRequiredService<ITenantDatabase>().CreateBatch(BatchPurpose.Persist);
            _ = b.Sql(TouchLookups, new SqlOptions(writes: Set(Lookups)));
            Task<BatchOutcome> bumping = b.ExecuteAsync(Token);
            await WaitForLockWaitersAsync(2, running, bumping);
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
            await using AsyncServiceScope scopeA = SettledScope();
            var a = (DataBatch)scopeA.ServiceProvider.GetRequiredService<ITenantDatabase>().CreateBatch(BatchPurpose.Persist);
            a.MarkConnectPrologue(DataBatchDatabase.Premises);
            BatchResult<CacheResult<CachedEntitySet<Lookup>>> cold = a.FromCache<Lookup>();
            _ = a.Sql($"DELETE FROM [core].[Jobs] WHERE [Id] = 0;", new SqlOptions(writes: Set(Jobs)));
            _ = a.Sql(Gate, null);
            Task<BatchOutcome> running = a.ExecuteAsync(Token);
            await WaitForLockWaitersAsync(1, running);

            // --- B deactivates the row A read: its guard's exclusive lock on the list's tag waits for A's shared one ---
            await using AsyncServiceScope scopeB = fixture.Database.Tenant.CreateScope();
            var b = (DataBatch)scopeB.ServiceProvider.GetRequiredService<ITenantDatabase>().CreateBatch(BatchPurpose.Persist);
            b.MarkConnectPrologue(DataBatchDatabase.Premises);
            _ = b.Sql($"UPDATE [fixture].[Lookups] SET [IsActive] = 0 WHERE [Id] = 1;", new SqlOptions(writes: Set(Lookups)));
            Task<BatchOutcome> deactivating = b.ExecuteAsync(Token);
            await WaitForLockWaitersAsync(2, running, deactivating);
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
            await WaitForLockWaitersAsync(1, first);

            // --- B does the same: its guard waits for A's update lock instead of sharing the row ---
            await using AsyncServiceScope scopeB = fixture.Database.Tenant.CreateScope();
            DataBatch b = ReadsAndBumps(scopeB, expected);
            Task<BatchOutcome> second = b.ExecuteAsync(Token);
            await WaitForLockWaitersAsync(2, first, second);

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
            await WaitForLockWaitersAsync(1, first);

            // --- B's guard waits for A's update lock on the stamp row ---
            await using AsyncServiceScope scopeB = fixture.Database.Tenant.CreateScope();
            DataBatch b = WritesCallersRows(scopeB);
            Task<BatchOutcome> second = b.ExecuteAsync(Token);
            await WaitForLockWaitersAsync(2, first, second);

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
            await WaitForLockWaitersAsync(1, first);

            // --- B bumps permissions: its guard waits at the first name both lock, instead of
            // sharing the rows and deadlocking when each bump converts the row the other holds ---
            await using AsyncServiceScope scopeB = fixture.Database.Tenant.CreateScope();
            DataBatch b = Bumps(scopeB, VersionTagNames.Permissions);
            Task<BatchOutcome> second = b.ExecuteAsync(Token);
            await WaitForLockWaitersAsync(2, first, second);

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
            await WaitForLockWaitersAsync(1, first);

            // --- V, the saver, bumps settings: it waits at the settings tag A holds, before it takes
            // any lock on its stamp row, so A's bump of that row never waits for V ---
            fixture.Database.Prologue.UserId = DataBatchDatabase.CallerId;
            await using AsyncServiceScope scopeV = fixture.Database.Tenant.CreateScope();
            DataBatch v = Bumps(scopeV, VersionTagNames.Settings);
            Task<BatchOutcome> second = v.ExecuteAsync(Token);
            await WaitForLockWaitersAsync(2, first, second);

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
            _ = await ExecuteInAsync(rival, rivalTransaction, "SET DEADLOCK_PRIORITY HIGH; INSERT INTO [core].[Jobs] ([Id]) VALUES (1001);");

            // --- A holds a shared lock on the list's tag and waits at the gate the rival holds ---
            await using AsyncServiceScope scope = fixture.Database.Tenant.CreateScope();
            var a = (DataBatch)scope.ServiceProvider.GetRequiredService<ITenantDatabase>().CreateBatch(BatchPurpose.Persist);
            a.MarkConnectPrologue(DataBatchDatabase.Premises);
            a.DependsOn([new VersionTagDependency(LookupTag, new VersionTag(expected), VersionTagMismatchPolicy.Rerun)]);
            _ = a.Sql($"DELETE FROM [core].[Jobs] WHERE [Id] = 0;", new SqlOptions(writes: Set(Jobs)));
            _ = a.Sql(Gate, null);
            Task<BatchOutcome> running = a.ExecuteAsync(Token);
            await WaitForLockWaitersAsync(1, running);

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

        /// <summary>A scope of the fixture tenant whose context carries settings of one language, as the settings load leaves it.</summary>
        /// <returns>The scope.</returns>
        private AsyncServiceScope SettledScope()
        {
            AsyncServiceScope scope = fixture.Database.Tenant.CreateScope();
            IRequestContextHolder holder = scope.ServiceProvider.GetRequiredService<IRequestContextHolder>();
            TenantSettings settings = new()
            {
                TenantId = FixtureTenant.TenantId,
                Tag = VersionTag.New(),
                Names = ["Fixture", null, null],
                Languages = [new LanguageInfo("en", "English", "E", false)],
                Shape = MultilingualShape.Primary,
                Calendars = [],
                TimeZone = TimeZoneInfo.Utc,
                SqlServerTimeZoneName = "UTC",
                ModifiedAt = DateTimeOffset.UnixEpoch,
                Entries = new Dictionary<string, JsonElement>(),
            };
            holder.Set(holder.Current with { TenantSettings = settings });
            return scope;
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
        ///     Waits until at least the given number of requests in the fixture database wait for a
        ///     lock, failing at once when a batch expected to wait finished instead.
        /// </summary>
        /// <param name="count">The number of waiters.</param>
        /// <param name="waiting">The batches expected to be waiting.</param>
        /// <returns>A task that completes when they wait.</returns>
        private async Task WaitForLockWaitersAsync(int count, params Task[] waiting)
        {
            string database = new SqlConnectionStringBuilder(fixture.Database.Tenant.ConnectionString).InitialCatalog;
            string query = $"SELECT COUNT(*) FROM sys.dm_exec_requests WHERE [database_id] = DB_ID(N'{database}') AND [wait_type] LIKE N'LCK%';";
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(Token);
            timeout.CancelAfter(TimeSpan.FromSeconds(30));
            while (await SqlServerFixture.ScalarAsync<int>(fixture.Database.MasterConnectionString, query) < count)
            {
                // A batch that finished never waited: surface its failure, or fail for it.
                int finished = Array.FindIndex(waiting, static task => task.IsCompleted);
                if (finished >= 0)
                {
                    await waiting[finished];
                    Assert.Fail(string.Create(CultureInfo.InvariantCulture, $"The batch at {finished} was expected to wait for a lock and finished without waiting."));
                }

                await Task.Delay(TimeSpan.FromMilliseconds(25), timeout.Token);
            }
        }

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
            private SideTransaction(SqlConnection connection, SqlTransaction transaction)
            {
                _connection = connection;
                _transaction = transaction;
            }

            /// <summary>Opens a transaction that inserts the gate job, uncommitted.</summary>
            /// <param name="connectionString">The database.</param>
            /// <returns>The held transaction.</returns>
            public static async Task<SideTransaction> HoldGateAsync(string connectionString)
            {
                SqlConnection connection = new(connectionString);
                await connection.OpenAsync(Token);
                var transaction = (SqlTransaction)await connection.BeginTransactionAsync(Token);
                await using SqlCommand command = connection.CreateCommand();
                command.Transaction = transaction;
                command.CommandText = "INSERT INTO [core].[Jobs] ([Id]) VALUES (1001);";
                _ = await command.ExecuteNonQueryAsync(Token);
                return new SideTransaction(connection, transaction);
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
