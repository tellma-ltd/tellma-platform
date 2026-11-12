// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Microsoft.Data.SqlClient;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.Metrics.Testing;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Testing;
using System.Data.Common;
using System.Diagnostics;
using System.Diagnostics.Metrics;
using Tellma.Core.Abstractions.Caching;
using Tellma.Core.Abstractions.Data;
using Tellma.Core.Abstractions.Hosting;
using Tellma.Core.Abstractions.TableTypes;
using Tellma.Core.Data;
using Tellma.Core.Data.Batches;
using Tellma.Testing.Entities;

namespace Tellma.Core.IntegrationTests.Data
{
    /// <summary>
    ///     Raw-statement batches against a fixture database: the fixture's own shape, the schema
    ///     guard, transactions, result sets, deduplication, the factory handle, the guard wrapper,
    ///     table-valued parameters, statement ordinals, the change-tracking audit, cancellation, the
    ///     round-trip span and the slow-round-trip log.
    /// </summary>
    /// <param name="fixture">The class's database, reset before each test.</param>
    [Trait("Category", "Integration")]
    public class DataBatchTests(DataBatchDatabase fixture) : IClassFixture<DataBatchDatabase>, IAsyncLifetime
    {
        /// <summary>The stand-in jobs table, written by the statements that need a write.</summary>
        private static readonly TableName Jobs = new("core", "Jobs");

        /// <summary>The stand-in version-tag table.</summary>
        private static readonly TableName VersionTags = new("core", "VersionTags");

        /// <summary>A statement that inserts one job row.</summary>
        private static readonly FormattableString InsertJob = $"INSERT INTO [core].[Jobs] ([Id]) VALUES (NEXT VALUE FOR [core].[sq_Jobs]);";

        /// <summary>A statement that counts the job rows.</summary>
        private static readonly FormattableString CountJobs = $"SELECT COUNT(*) AS [N] FROM [core].[Jobs];";

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
        public async Task The_fixture_database_is_created_in_the_platform_collation_with_change_tracking_and_the_running_fingerprint()
        {
            FixtureTenant tenant = fixture.Tenant;

            Assert.Equal(DatabaseCollations.Platform, await SqlServerFixture.ScalarAsync<string>(tenant.ConnectionString, "SELECT CAST(DATABASEPROPERTYEX(DB_NAME(), 'Collation') AS nvarchar(128));"));
            Assert.True(await SqlServerFixture.ScalarAsync<bool>(tenant.ConnectionString, "SELECT [is_read_committed_snapshot_on] FROM sys.databases WHERE [name] = DB_NAME();"));
            Assert.NotNull(await SqlServerFixture.ScalarAsync<object>(tenant.ConnectionString, $"SELECT DATABASE_PRINCIPAL_ID(N'{DatabaseRoles.Application}');"));

            // --- Every table but the platform's own and the history tables is change-tracked ---
            Assert.Equal(
                ["core.Blobs", "core.Jobs", "core.Permissions", "core.RoleMemberships", "core.Roles", "core.UserPreferences", "core.Users", "core.UserStamps", "core.VersionTags", "fixture.Links", "fixture.Nodes", "fixture.Shipments", "fixture.WidgetExtras", "fixture.WidgetPartNotes", "fixture.WidgetParts", "fixture.Widgets"],
                await TrackedTablesAsync(tenant.ConnectionString));
            Assert.Equal(1, await SqlServerFixture.ScalarAsync<int>(tenant.ConnectionString, "SELECT CAST([temporal_type] AS int) FROM sys.tables WHERE [object_id] = OBJECT_ID(N'fixture.WidgetsHistory');"));

            // --- The schema guard's row is the running model's fingerprint ---
            Assert.Equal(tenant.Fingerprint, await SqlServerFixture.ScalarAsync<string>(tenant.ConnectionString, "SELECT [Fingerprint] FROM [dbo].[__TellmaSchema];"));
        }

        [Fact]
        public async Task A_database_that_does_not_list_the_running_fingerprint_refuses_a_read_a_validate_and_a_persist_batch_with_50501()
        {
            FixtureTenant tenant = fixture.Tenant;
            await SqlServerFixture.ExecuteAsync(tenant.ConnectionString, "UPDATE [dbo].[__TellmaSchema] SET [Fingerprint] = REPLICATE('0', 64);");

            await using AsyncServiceScope scope = tenant.CreateScope();
            ITenantDatabase database = scope.ServiceProvider.GetRequiredService<ITenantDatabase>();

            foreach (BatchPurpose purpose in new[] { BatchPurpose.Read, BatchPurpose.Validate, BatchPurpose.Persist })
            {
                IDataBatch batch = database.CreateBatch(purpose);
                BatchResult<RawResult> count = batch.Sql(CountJobs, new SqlOptions(resultSets: 1));
                if (purpose == BatchPurpose.Persist)
                {
                    _ = batch.Sql(InsertJob, new SqlOptions(writes: Set(Jobs)));
                }

                BatchAssertionFailedException refused = await Assert.ThrowsAsync<BatchAssertionFailedException>(() => batch.ExecuteAsync(Token));
                Assert.Equal(TellmaSqlErrors.SchemaBehind, refused.Number);
                Assert.Equal((TellmaSqlErrors.SchemaBehindCode, null), (refused.Code, refused.StatementOrdinal));
                Assert.False(count.IsCompleted, purpose.ToString());
            }

            // Nothing the persist batch carried was written.
            Assert.Equal(0, await SqlServerFixture.ScalarAsync<int>(tenant.ConnectionString, "SELECT COUNT(*) FROM [core].[Jobs];"));
        }

        [Fact]
        public async Task The_running_fingerprint_as_the_older_of_two_rows_is_served()
        {
            FixtureTenant tenant = fixture.Tenant;
            await SqlServerFixture.ExecuteAsync(
                tenant.ConnectionString,
                "UPDATE [dbo].[__TellmaSchema] SET [AppliedAt] = DATEADD(DAY, -1, [AppliedAt]);",
                "INSERT INTO [dbo].[__TellmaSchema] ([Fingerprint], [MigrationId], [PlatformVersion], [AppliedAt]) VALUES (REPLICATE('f', 64), N'Next', N'next', SYSDATETIMEOFFSET());");

            await using AsyncServiceScope scope = tenant.CreateScope();
            IDataBatch batch = scope.ServiceProvider.GetRequiredService<ITenantDatabase>().CreateBatch(BatchPurpose.Read);
            BatchResult<RawResult> count = batch.Sql(CountJobs, new SqlOptions(resultSets: 1));
            _ = await batch.ExecuteAsync(Token);

            Assert.Equal(0, Int(count.Value));
        }

        [Fact]
        public async Task Result_sets_before_a_throw_are_read_and_the_throw_surfaces()
        {
            FixtureTenant tenant = fixture.Tenant;
            await using AsyncServiceScope scope = tenant.CreateScope();
            IDataBatch batch = scope.ServiceProvider.GetRequiredService<ITenantDatabase>().CreateBatch(BatchPurpose.Read);

            BatchResult<RawResult> first = batch.Sql($"SELECT 7 AS [Seven];", new SqlOptions(resultSets: 1));
            BatchResult<RawResult> second = batch.Sql($"SELECT {"eight"} AS [Eight];", new SqlOptions(resultSets: 1));
            _ = batch.Sql($"THROW 50600, N'Fixture.Stop', 1;", null);
            BatchResult<RawResult> after = batch.Sql($"SELECT 9 AS [Nine];", new SqlOptions(resultSets: 1));

            BatchAssertionFailedException thrown = await Assert.ThrowsAsync<BatchAssertionFailedException>(() => batch.ExecuteAsync(Token));

            Assert.Equal((50600, "Fixture.Stop", 2), (thrown.Number, thrown.Code, thrown.StatementOrdinal));
            _ = Assert.IsType<SqlException>(thrown.InnerException);
            Assert.Equal(7, Int(first.Value));
            Assert.Equal("eight", ((string[])second.Value.ResultSets[0].GetBuffer(0))[0]);
            Assert.False(after.IsCompleted);
        }

        [Fact]
        public async Task A_throw_and_a_foreign_key_violation_resolve_to_the_ordinals_of_the_statements_that_raised_them()
        {
            FixtureTenant tenant = fixture.Tenant;
            await using AsyncServiceScope scope = tenant.CreateScope();
            ITenantDatabase database = scope.ServiceProvider.GetRequiredService<ITenantDatabase>();

            // --- A THROW from the statement at ordinal 3 ---
            var throwing = (DataBatch)database.CreateBatch(BatchPurpose.Read);
            _ = throwing.Sql($"SELECT 0 AS [Zero];", new SqlOptions(resultSets: 1));
            _ = throwing.Sql($"DECLARE @x int = 1;\nSET @x += 1;", null);
            _ = throwing.Sql($"SELECT 2 AS [Two]\nFROM (VALUES (1)) AS v([One]);", new SqlOptions(resultSets: 1));
            _ = throwing.Sql($"IF 1 = 1\n    THROW 50601, N'Fixture.Three', 1;", null);
            _ = throwing.Sql($"SELECT 4 AS [Four];", new SqlOptions(resultSets: 1));

            BatchAssertionFailedException thrown = await Assert.ThrowsAsync<BatchAssertionFailedException>(() => throwing.ExecuteAsync(Token));
            Assert.Equal((3, "Fixture.Three"), (thrown.StatementOrdinal, thrown.Code));

            // --- A foreign-key violation from the statement at ordinal 4 ---
            TableName extras = new("fixture", "WidgetExtras");
            var violating = (DataBatch)database.CreateBatch(BatchPurpose.Persist);
            _ = violating.Sql(CountJobs, new SqlOptions(resultSets: 1));
            _ = violating.Sql(InsertJob, new SqlOptions(writes: Set(Jobs)));
            _ = violating.Sql(InsertJob, new SqlOptions(writes: Set(Jobs)));

            // A different text, so it does not collapse into the first count.
            _ = violating.Sql($"SELECT COUNT(*) AS [N] FROM [core].[Jobs] WHERE [Id] > 0;", new SqlOptions(resultSets: 1));
            _ = violating.Sql($"INSERT INTO [fixture].[WidgetExtras] ([Id], [Note])\nVALUES ({123456}, N'orphan');", new SqlOptions(writes: Set(extras)));

            ForeignKeyViolationException violated = await Assert.ThrowsAsync<ForeignKeyViolationException>(() => violating.ExecuteAsync(Token));
            Assert.Equal((4, "FK_WidgetExtras_Id"), (violated.StatementOrdinal, violated.ConstraintName));
            Assert.Equal(547, Assert.IsType<SqlException>(violated.InnerException).Number);

            // The in-text transaction rolled the earlier inserts back.
            Assert.Equal(0, await SqlServerFixture.ScalarAsync<int>(tenant.ConnectionString, "SELECT COUNT(*) FROM [core].[Jobs];"));
        }

        [Fact]
        public async Task Transaction_mode_none_leaves_autocommit_while_auto_and_explicit_roll_back()
        {
            FixtureTenant tenant = fixture.Tenant;
            await using AsyncServiceScope scope = tenant.CreateScope();
            ITenantDatabase database = scope.ServiceProvider.GetRequiredService<ITenantDatabase>();

            foreach (TransactionMode mode in new[] { TransactionMode.None, TransactionMode.Auto, TransactionMode.Explicit })
            {
                IDataBatch batch = database.CreateBatch(BatchPurpose.Persist);
                batch.TransactionMode = mode;
                _ = batch.Sql($"INSERT INTO [core].[VersionTags] ([Name]) VALUES ({mode.ToString()});", new SqlOptions(writes: Set(VersionTags)));
                _ = batch.Sql($"THROW 50600, N'Fixture.AfterTheWrite', 1;", null);

                _ = await Assert.ThrowsAsync<BatchAssertionFailedException>(() => batch.ExecuteAsync(Token));
            }

            // Only the autocommitted write survived its batch's failure.
            Assert.Equal("None", await SqlServerFixture.ScalarAsync<string>(tenant.ConnectionString, "SELECT STRING_AGG([Name], N',') FROM [core].[VersionTags];"));
        }

        [Fact]
        public async Task Two_identical_statements_collapse_into_one_when_they_declare_no_writes_and_both_run_when_they_do()
        {
            FixtureTenant tenant = fixture.Tenant;
            await using AsyncServiceScope scope = tenant.CreateScope();
            ITenantDatabase database = scope.ServiceProvider.GetRequiredService<ITenantDatabase>();

            // --- Two reads with one text and one set of values are one statement ---
            var reads = (DataBatch)database.CreateBatch(BatchPurpose.Read);
            BatchResult<RawResult> first = reads.Sql($"SELECT COUNT(*) AS [N] FROM [core].[Jobs] WHERE [Id] > {0};", new SqlOptions(resultSets: 1));
            BatchResult<RawResult> second = reads.Sql($"SELECT COUNT(*) AS [N] FROM [core].[Jobs] WHERE [Id] > {0};", new SqlOptions(resultSets: 1));
            BatchResult<RawResult> other = reads.Sql($"SELECT COUNT(*) AS [N] FROM [core].[Jobs] WHERE [Id] > {1};", new SqlOptions(resultSets: 1));
            _ = await reads.ExecuteAsync(Token);

            Assert.Same(first, second);
            Assert.NotSame(first, other);
            Assert.Equal(2, reads.Statements.Count);

            // --- Two writes with one text both run ---
            IDataBatch writes = database.CreateBatch(BatchPurpose.Persist);
            _ = writes.Sql(InsertJob, new SqlOptions(writes: Set(Jobs)));
            _ = writes.Sql(InsertJob, new SqlOptions(writes: Set(Jobs)));
            _ = await writes.ExecuteAsync(Token);

            Assert.Equal(2, await SqlServerFixture.ScalarAsync<int>(tenant.ConnectionString, "SELECT COUNT(*) FROM [core].[Jobs];"));
        }

        [Fact]
        public async Task A_handle_from_the_factory_runs_a_maintenance_statement_and_refuses_a_read_batch()
        {
            FixtureTenant tenant = fixture.Tenant;
            await using AsyncServiceScope scope = tenant.Services.CreateAsyncScope();
            ITenantDatabaseFactory factory = scope.ServiceProvider.GetRequiredService<ITenantDatabaseFactory>();

            await using ITenantDatabaseHandle handle = await factory.OpenAsync(FixtureTenant.TenantId, scope.ServiceProvider, Token);

            // --- Two maintenance batches in turn on the handle's one connection ---
            for (int run = 1; run <= 2; run++)
            {
                IDataBatch maintenance = handle.CreateBatch(BatchPurpose.Maintenance);
                _ = maintenance.Sql(InsertJob, new SqlOptions(writes: Set(Jobs)));
                BatchResult<RawResult> count = maintenance.Sql(CountJobs, new SqlOptions(resultSets: 1));
                _ = await maintenance.ExecuteAsync(Token);
                Assert.Equal(run, Int(count.Value));
            }

            // --- Everything that needs a request context is refused ---
            _ = Assert.Throws<InvalidOperationException>(() => handle.CreateBatch(BatchPurpose.Read));
            _ = Assert.Throws<InvalidOperationException>(() => handle.CreateBatch(BatchPurpose.Persist));
            _ = Assert.Throws<InvalidOperationException>(() => handle.Schema);
            _ = Assert.Throws<InvalidOperationException>(() => handle.Context);
        }

        [Fact]
        public async Task A_guarded_batch_runs_its_body_only_when_the_connect_prologue_reports_the_guard_passed()
        {
            DataBatchDatabase.ConnectPrologue prologue = fixture.Prologue;
            FixtureTenant tenant = fixture.Tenant;
            await fixture.SeedCallerAsync();
            await using AsyncServiceScope scope = tenant.CreateScope();
            ITenantDatabase database = scope.ServiceProvider.GetRequiredService<ITenantDatabase>();

            foreach (bool passes in new[] { false, true })
            {
                prologue.Passes = passes;
                var batch = (DataBatch)database.CreateBatch(BatchPurpose.Persist);
                batch.MarkConnectPrologue(DataBatchDatabase.Premises);
                BatchResult<RawResult> count = batch.Sql(CountJobs, new SqlOptions(resultSets: 1));
                _ = batch.Sql(InsertJob, new SqlOptions(writes: Set(Jobs)));
                bool committed = false;
                batch.OnCommitted(_ => committed = true);

                _ = await batch.ExecuteAsync(Token);

                Assert.Equal(passes, batch.GuardPassed);
                Assert.Equal(passes, count.IsCompleted);
                Assert.Equal(passes, committed);
                Assert.Contains(BatchAssembly.GuardOpen, batch.Assemble().Text, StringComparison.Ordinal);
            }

            Assert.Equal(1, await SqlServerFixture.ScalarAsync<int>(tenant.ConnectionString, "SELECT COUNT(*) FROM [core].[Jobs];"));
        }

        [Fact]
        public async Task Rows_bind_as_a_table_valued_parameter_through_tvp_and_through_a_hole()
        {
            FixtureTenant tenant = fixture.Tenant;
            await using AsyncServiceScope scope = tenant.CreateScope();
            IDataBatch batch = scope.ServiceProvider.GetRequiredService<ITenantDatabase>().CreateBatch(BatchPurpose.Read);

            IdList[] rows = [new() { Id = 3 }, new() { Id = 5 }, new() { Id = 8 }];
            SqlIdentifier ids = batch.Tvp(rows);
            BatchResult<RawResult> named = batch.Sql($"SELECT SUM([Id]) AS [Total] FROM {ids};", new SqlOptions(resultSets: 1));
            BatchResult<RawResult> inline = batch.Sql($"SELECT COUNT(*) AS [N] FROM {rows} WHERE [Id] > {4};", new SqlOptions(resultSets: 1));
            _ = await batch.ExecuteAsync(Token);

            Assert.Equal(16, Int(named.Value));
            Assert.Equal(2, Int(inline.Value));
        }

        [Fact]
        public async Task The_change_tracking_audit_passes_a_declared_write_and_refuses_an_undeclared_one()
        {
            FixtureTenant tenant = fixture.Tenant;
            await using AsyncServiceScope scope = tenant.CreateScope();
            ITenantDatabase database = scope.ServiceProvider.GetRequiredService<ITenantDatabase>();

            IDataBatch declared = database.CreateBatch(BatchPurpose.Persist);
            _ = declared.Sql(InsertJob, new SqlOptions(writes: Set(Jobs)));
            _ = await declared.ExecuteAsync(Token);

            IDataBatch undeclared = database.CreateBatch(BatchPurpose.Persist);
            _ = undeclared.Sql(InsertJob, new SqlOptions(writes: Set(VersionTags)));
            InvalidOperationException refused = await Assert.ThrowsAsync<InvalidOperationException>(() => undeclared.ExecuteAsync(Token));
            Assert.Contains("[core].[Jobs]", refused.Message, StringComparison.Ordinal);

            // A read that writes nothing passes too.
            IDataBatch read = database.CreateBatch(BatchPurpose.Read);
            _ = read.Sql(CountJobs, new SqlOptions(resultSets: 1));
            _ = await read.ExecuteAsync(Token);
        }

        [Fact]
        public async Task A_marked_batch_whose_body_has_no_text_runs_its_prologue_alone()
        {
            await using AsyncServiceScope scope = fixture.Tenant.CreateScope();
            ITenantDatabase database = scope.ServiceProvider.GetRequiredService<ITenantDatabase>();

            // --- Nothing after the prologue, then only a table-valued parameter after it ---
            foreach (bool withTvp in new[] { false, true })
            {
                var batch = (DataBatch)database.CreateBatch(BatchPurpose.Read);
                batch.MarkConnectPrologue(DataBatchDatabase.Premises);
                if (withTvp)
                {
                    IdList[] rows = [new() { Id = 1 }];
                    _ = batch.Tvp(rows);
                }

                _ = await batch.ExecuteAsync(Token);

                Assert.True(batch.GuardPassed);
                Assert.DoesNotContain(BatchAssembly.GuardOpen, batch.Assemble().Text, StringComparison.Ordinal);
            }
        }

        [Fact]
        public async Task A_failed_batch_leaves_no_transaction_open_on_the_handles_connection()
        {
            await using AsyncServiceScope scope = fixture.Tenant.Services.CreateAsyncScope();
            ITenantDatabaseFactory factory = scope.ServiceProvider.GetRequiredService<ITenantDatabaseFactory>();
            await using ITenantDatabaseHandle handle = await factory.OpenAsync(FixtureTenant.TenantId, scope.ServiceProvider, Token);

            foreach (TransactionMode mode in new[] { TransactionMode.None, TransactionMode.Auto, TransactionMode.Explicit })
            {
                IDataBatch failing = handle.CreateBatch(BatchPurpose.Maintenance);
                failing.TransactionMode = mode;
                _ = failing.Sql(InsertJob, new SqlOptions(writes: Set(Jobs)));
                _ = failing.Sql($"THROW 50600, N'Fixture.AfterTheWrite', 1;", null);
                _ = await Assert.ThrowsAsync<BatchAssertionFailedException>(() => failing.ExecuteAsync(Token));

                // The next batch runs on the same physical connection, so an abandoned transaction
                // would still be open there: XACT_ABORT rolled the in-text one back with the batch.
                IDataBatch probe = handle.CreateBatch(BatchPurpose.Maintenance);
                BatchResult<RawResult> open = probe.Sql($"SELECT @@TRANCOUNT AS [Open];", new SqlOptions(resultSets: 1));
                _ = await probe.ExecuteAsync(Token);
                Assert.True(Int(open.Value) == 0, mode.ToString());
            }

            // Only the autocommitted write survived its batch's failure.
            Assert.Equal(1, await SqlServerFixture.ScalarAsync<int>(fixture.Tenant.ConnectionString, "SELECT COUNT(*) FROM [core].[Jobs];"));
        }

        [Fact]
        public async Task A_reader_that_fails_after_the_send_stops_the_batch_before_it_commits_and_reports_the_commit_unknown()
        {
            await using AsyncServiceScope scope = fixture.Tenant.CreateScope();
            IDataBatch batch = scope.ServiceProvider.GetRequiredService<ITenantDatabase>().CreateBatch(BatchPurpose.Persist);
            _ = batch.Sql(InsertJob, new SqlOptions(writes: Set(Jobs)));

            // The raw reader takes no time column. The server holds output until a packet fills or the
            // batch ends, so the NOWAIT message sends the set at once, while the delay keeps the server
            // inside the batch for the client to stop it. The delay is long so a loaded machine still
            // stops it in time; a stopped batch returns at once.
            _ = batch.Sql($"SELECT CAST('10:00' AS time) AS [At];", new SqlOptions(resultSets: 1));
            _ = batch.Sql($"RAISERROR(N'', 0, 1) WITH NOWAIT;", null);
            _ = batch.Sql($"WAITFOR DELAY '00:00:20';", null);
            bool committed = false;
            batch.OnCommitted(_ => committed = true);

            // The reader failed after the server had the batch: a write may have committed before the
            // client saw the failing set, and with no rows declared to probe, nothing tells.
            DataAccessAmbiguousException ambiguous = await Assert.ThrowsAsync<DataAccessAmbiguousException>(() => batch.ExecuteAsync(Token));
            Assert.Equal(CommitVerdict.Unknown, ambiguous.Verdict);
            _ = Assert.IsType<InvalidOperationException>(ambiguous.InnerException);

            Assert.False(committed);
            Assert.Equal(0, await SqlServerFixture.ScalarAsync<int>(fixture.Tenant.ConnectionString, "SELECT COUNT(*) FROM [core].[Jobs];"));
        }

        [Fact]
        public async Task A_read_is_cancelled_by_the_callers_token_or_the_scopes_while_it_runs()
        {
            foreach (bool byScope in new[] { false, true })
            {
                await using AsyncServiceScope scope = fixture.Tenant.CreateScope();
                using CancellationTokenSource cancel = new();
                if (byScope)
                {
                    scope.ServiceProvider.GetRequiredService<DataCancellation>().Token = cancel.Token;
                }

                IDataBatch read = scope.ServiceProvider.GetRequiredService<ITenantDatabase>().CreateBatch(BatchPurpose.Read);
                _ = read.Sql($"WAITFOR DELAY '00:00:10';\nSELECT 1 AS [One];", new SqlOptions(resultSets: 1));

                long started = Stopwatch.GetTimestamp();
                cancel.CancelAfter(TimeSpan.FromSeconds(1));
                Exception cancelled = await Assert.ThrowsAnyAsync<Exception>(() => read.ExecuteAsync(byScope ? Token : cancel.Token));

                Assert.True(cancelled is OperationCanceledException or SqlException, cancelled.ToString());
                Assert.True(Stopwatch.GetElapsedTime(started) < TimeSpan.FromSeconds(6), byScope ? "scope" : "caller");
            }
        }

        [Fact]
        public async Task A_persist_cancelled_after_the_send_runs_to_completion()
        {
            await using AsyncServiceScope scope = fixture.Tenant.CreateScope();
            using CancellationTokenSource cancel = new();
            IDataBatch persist = scope.ServiceProvider.GetRequiredService<ITenantDatabase>().CreateBatch(BatchPurpose.Persist);
            _ = persist.Sql(InsertJob, new SqlOptions(writes: Set(Jobs)));
            _ = persist.Sql($"WAITFOR DELAY '00:00:03';", null);

            cancel.CancelAfter(TimeSpan.FromSeconds(1));
            _ = await persist.ExecuteAsync(cancel.Token);

            Assert.True(cancel.IsCancellationRequested);
            Assert.Equal(1, await SqlServerFixture.ScalarAsync<int>(fixture.Tenant.ConnectionString, "SELECT COUNT(*) FROM [core].[Jobs];"));
        }

        [Fact]
        public async Task A_throwing_commit_callback_is_logged_and_never_thrown_and_the_others_run_in_order()
        {
            await using AsyncServiceScope scope = fixture.Tenant.CreateScope();
            scope.ServiceProvider.GetRequiredService<DataAccessScope>().Operation = "fx-callbacks";
            IDataBatch batch = scope.ServiceProvider.GetRequiredService<ITenantDatabase>().CreateBatch(BatchPurpose.Persist);
            _ = batch.Sql(InsertJob, new SqlOptions(writes: Set(Jobs)));

            List<int> ran = [];
            batch.OnCommitted(_ => ran.Add(1));
            batch.OnCommitted(_ =>
            {
                ran.Add(2);
                throw new InvalidDataException("The second callback failed.");
            });
            batch.OnCommitted(_ => ran.Add(3));

            _ = await batch.ExecuteAsync(Token);

            // --- The batch committed, every callback ran in order, and the failure was only logged ---
            Assert.Equal([1, 2, 3], ran);
            Assert.Equal(1, await SqlServerFixture.ScalarAsync<int>(fixture.Tenant.ConnectionString, "SELECT COUNT(*) FROM [core].[Jobs];"));
            FakeLogRecord failure = Assert.Single(fixture.Logs.GetSnapshot(), static record => record.Id.Name == "DataBatch.OnCommittedFailed");
            Assert.Equal(LogLevel.Error, failure.Level);
            _ = Assert.IsType<InvalidDataException>(failure.Exception);
            Assert.Equal(("fx-callbacks", "1"), (failure.GetStructuredStateValue("Operation"), failure.GetStructuredStateValue("TenantId")));
        }

        [Fact]
        public async Task An_explicit_transaction_counts_its_begin_and_commit_as_round_trips()
        {
            foreach ((TransactionMode mode, int expected) in new[] { (TransactionMode.Auto, 1), (TransactionMode.Explicit, 3) })
            {
                await using AsyncServiceScope scope = fixture.Tenant.CreateScope();
                DataAccessScope access = scope.ServiceProvider.GetRequiredService<DataAccessScope>();
                IDataBatch batch = scope.ServiceProvider.GetRequiredService<ITenantDatabase>().CreateBatch(BatchPurpose.Persist);
                batch.TransactionMode = mode;
                _ = batch.Sql(InsertJob, new SqlOptions(writes: Set(Jobs)));

                BatchOutcome outcome = await batch.ExecuteAsync(Token);

                Assert.Equal((mode, expected, expected), (mode, outcome.RoundTrips, access.RoundTrips));
            }
        }

        [Fact]
        public async Task The_round_trip_span_is_named_for_the_operation_and_purpose_and_carries_no_text()
        {
            string operation = $"fx-span-{Guid.NewGuid():N}";
            List<Activity> stopped = [];
            using ActivityListener listener = new()
            {
                ShouldListenTo = static source => source.Name == DataTelemetryNames.ActivitySourceName,
                Sample = static (ref _) => ActivitySamplingResult.AllDataAndRecorded,
                ActivityStopped = activity =>
                {
                    // Spans of other suites' scopes running alongside are not this test's.
                    if (Equals(activity.GetTagItem(DataTelemetryNames.OperationAttribute), operation))
                    {
                        lock (stopped)
                        {
                            stopped.Add(activity);
                        }
                    }
                },
            };
            ActivitySource.AddActivityListener(listener);

            await using AsyncServiceScope scope = fixture.Tenant.CreateScope();
            scope.ServiceProvider.GetRequiredService<DataAccessScope>().Operation = operation;
            IDataBatch batch = scope.ServiceProvider.GetRequiredService<ITenantDatabase>().CreateBatch(BatchPurpose.Read);
            _ = batch.Sql(CountJobs, new SqlOptions(resultSets: 1));
            _ = await batch.ExecuteAsync(Token);

            Activity span = Assert.Single(stopped);
            Assert.Equal($"{operation} read", span.OperationName);
            Assert.Equal(ActivityKind.Client, span.Kind);
            Assert.Equal(
                new Dictionary<string, object?>
                {
                    [DataTelemetryNames.RoleTag] = DataTelemetryNames.TenantRole,
                    [DataTelemetryNames.PurposeAttribute] = "read",
                    [DataTelemetryNames.OperationAttribute] = operation,
                    [DataTelemetryNames.AttemptAttribute] = 1,
                    [DataTelemetryNames.TenantIdAttribute] = FixtureTenant.TenantId,
                },
                span.TagObjects.ToDictionary(static tag => tag.Key, static tag => tag.Value));
        }

        [Fact]
        public async Task A_slow_round_trip_logs_its_measures_and_text_but_never_a_value()
        {
            await using AsyncServiceScope scope = fixture.Tenant.CreateScope();
            scope.ServiceProvider.GetRequiredService<DataAccessScope>().Operation = "fx-slow";
            IDataBatch batch = scope.ServiceProvider.GetRequiredService<ITenantDatabase>().CreateBatch(BatchPurpose.Read);
            IdList[] rows = [new() { Id = 3 }, new() { Id = 5 }];
            _ = batch.Sql($"SELECT [Id] FROM {rows} WHERE [Id] > {0} AND {"fx-secret"} <> N'';", new SqlOptions(resultSets: 1));
            _ = await batch.ExecuteAsync(Token);

            // Every round trip of the class's host is slow, and this test made one.
            FakeLogRecord record = Assert.Single(fixture.Logs.GetSnapshot(), static record => record.Id.Name == "DataBatch.SlowRoundTrip");
            (string, string?)[] expected =
            [
                ("TenantId", "1"), ("Operation", "fx-slow"), ("Purpose", "read"), ("Attempt", "1"), ("Statements", "1"), ("TableRows", "2"), ("RowsRead", "2"),
            ];
            Assert.Equal(expected, expected.Select(field => (field.Item1, record.GetStructuredStateValue(field.Item1))));
            Assert.NotNull(record.GetStructuredStateValue("DurationMs"));
            Assert.Contains("WHERE [Id] > @tb0_p0 AND @tb0_p1 <> N''", record.GetStructuredStateValue("Text"), StringComparison.Ordinal);
            Assert.DoesNotContain("fx-secret", record.Message, StringComparison.Ordinal);
        }

        [Fact]
        public async Task A_persist_batch_composed_under_a_tag_that_moved_is_refused_with_50412_and_writes_nothing()
        {
            // --- The tenant's settings, its permissions, and the caller's own permissions each move in turn ---
            foreach (string moved in new[]
            {
                $"UPDATE [core].[VersionTags] SET [Tag] = NEWID() WHERE [Name] = N'{VersionTagNames.Settings}';",
                $"UPDATE [core].[VersionTags] SET [Tag] = NEWID() WHERE [Name] = N'{VersionTagNames.Permissions}';",
                $"UPDATE [core].[UserStamps] SET [PermissionsTag] = NEWID() WHERE [UserId] = {DataBatchDatabase.CallerId};",
            })
            {
                await fixture.ResetAsync();
                await fixture.SeedCallerAsync();
                await SqlServerFixture.ExecuteAsync(fixture.Tenant.ConnectionString, moved);

                BatchAssertionFailedException stale = await Assert.ThrowsAsync<BatchAssertionFailedException>(() => PersistJobAsync(DataBatchDatabase.Premises));

                // The re-checks are the frame's own lines: no statement raised the refusal.
                Assert.Equal((TellmaSqlErrors.StaleVersionTag, TellmaSqlErrors.StaleVersionTagCode, null), (stale.Number, stale.Code, stale.StatementOrdinal));
                Assert.Equal(0, await JobCountAsync());
            }

            // --- Nothing moved: the batch commits ---
            await fixture.ResetAsync();
            await fixture.SeedCallerAsync();
            await PersistJobAsync(DataBatchDatabase.Premises);
            Assert.Equal(1, await JobCountAsync());
        }

        [Fact]
        public async Task A_caller_deactivated_since_the_prologue_is_refused_with_50401_and_writes_nothing()
        {
            await fixture.SeedCallerAsync();
            await SqlServerFixture.ExecuteAsync(fixture.Tenant.ConnectionString, $"UPDATE [core].[Users] SET [IsActive] = 0 WHERE [Id] = {DataBatchDatabase.CallerId};");

            BatchAssertionFailedException refused = await Assert.ThrowsAsync<BatchAssertionFailedException>(() => PersistJobAsync(DataBatchDatabase.Premises));

            Assert.Equal((TellmaSqlErrors.CallerInvalid, TellmaSqlErrors.CallerInvalidCode), (refused.Number, refused.Code));
            Assert.Equal(0, await JobCountAsync());
        }

        [Fact]
        public async Task The_platforms_own_persist_batch_re_checks_its_tags_without_a_caller()
        {
            // No caller row exists, and the platform's prologue declares none.
            await SqlServerFixture.ExecuteAsync(
                fixture.Tenant.ConnectionString,
                $"INSERT INTO [core].[VersionTags] ([Name], [Tag]) VALUES (N'{VersionTagNames.Settings}', '{DataBatchDatabase.SettingsTag}');");

            await PersistJobAsync(GuardPremises.ForSystem(new VersionTag(DataBatchDatabase.SettingsTag)));
            Assert.Equal(1, await JobCountAsync());
        }

        [Fact]
        public async Task A_throw_outside_the_two_bands_is_a_defect()
        {
            await using AsyncServiceScope scope = fixture.Tenant.CreateScope();
            IDataBatch batch = scope.ServiceProvider.GetRequiredService<ITenantDatabase>().CreateBatch(BatchPurpose.Read);
            _ = batch.Sql($"THROW 50000, N'Out of band.', 1;", null);

            InvalidOperationException defect = await Assert.ThrowsAsync<InvalidOperationException>(() => batch.ExecuteAsync(Token));
            Assert.Equal(50000, Assert.IsType<SqlException>(defect.InnerException).Number);
        }

        [Fact]
        public async Task Arithmetic_failures_duplicate_keys_and_check_constraints_translate_with_the_statement_that_raised_them()
        {
            await using AsyncServiceScope scope = fixture.Tenant.CreateScope();
            ITenantDatabase database = scope.ServiceProvider.GetRequiredService<ITenantDatabase>();

            // --- 8134 and 8115 ---
            IDataBatch divide = database.CreateBatch(BatchPurpose.Read);
            _ = divide.Sql($"SELECT 1 AS [One];", new SqlOptions(resultSets: 1));
            _ = divide.Sql($"SELECT 1 / {0} AS [Quotient];", new SqlOptions(resultSets: 1));
            ArithmeticFailureException divided = await Assert.ThrowsAsync<ArithmeticFailureException>(() => divide.ExecuteAsync(Token));
            Assert.Equal((ArithmeticFailureException.DivideByZero, 1), (divided.Reason, divided.StatementOrdinal));

            IDataBatch overflow = database.CreateBatch(BatchPurpose.Read);
            _ = overflow.Sql($"SELECT CAST(2147483647 AS int) + {1} AS [Sum];", new SqlOptions(resultSets: 1));
            ArithmeticFailureException overflowed = await Assert.ThrowsAsync<ArithmeticFailureException>(() => overflow.ExecuteAsync(Token));
            Assert.Equal((ArithmeticFailureException.Overflow, 0), (overflowed.Reason, overflowed.StatementOrdinal));

            // --- 2627 names the primary key ---
            IDataBatch duplicate = database.CreateBatch(BatchPurpose.Persist);
            _ = duplicate.Sql($"INSERT INTO [core].[Jobs] ([Id]) VALUES ({5});", new SqlOptions(writes: Set(Jobs)));
            _ = duplicate.Sql($"INSERT INTO [core].[Jobs] ([Id]) VALUES ({5});", new SqlOptions(writes: Set(Jobs)));
            UniqueConstraintViolationException duplicated = await Assert.ThrowsAsync<UniqueConstraintViolationException>(() => duplicate.ExecuteAsync(Token));
            Assert.Equal(("PK_Jobs", 1), (duplicated.IndexName, duplicated.StatementOrdinal));

            // --- 547 on a check constraint is no reference: the generic exception ---
            IDataBatch check = database.CreateBatch(BatchPurpose.Persist);
            _ = check.Sql($"INSERT INTO [core].[Jobs] ([Id]) VALUES ({0});", new SqlOptions(writes: Set(Jobs)));
            DataAccessException checkFailed = await Assert.ThrowsAsync<DataAccessException>(() => check.ExecuteAsync(Token));
            Assert.Equal(typeof(DataAccessException), checkFailed.GetType());
            Assert.Equal(0, checkFailed.StatementOrdinal);

            Assert.Equal(0, await JobCountAsync());
        }

        [Fact]
        public async Task A_concurrency_guard_reports_its_conflict_rows_before_its_throw()
        {
            await using AsyncServiceScope scope = fixture.Tenant.CreateScope();
            var batch = (DataBatch)scope.ServiceProvider.GetRequiredService<ITenantDatabase>().CreateBatch(BatchPurpose.Read);
            BatchResult<RawResult> before = batch.Sql($"SELECT 1 AS [One];", new SqlOptions(resultSets: 1));

            // --- A guard that selects its conflicts only when there are some, then throws ---
            StatementDraft draft = batch.Begin();
            batch.Append(
                draft,
                """
                IF EXISTS (SELECT 1)
                BEGIN
                    SELECT CAST(7 AS bigint) AS [Id], 'M' AS [Reason], CAST(NULL AS datetimeoffset) AS [ModifiedAt], CAST(NULL AS int) AS [ModifiedById],
                        CAST(NULL AS nvarchar(255)) AS [Name], CAST(NULL AS nvarchar(255)) AS [Name2], CAST(NULL AS nvarchar(255)) AS [Name3];
                    THROW 50409, N'Concurrency', 1;
                END;
                """,
                reader: async (cursor, cancellationToken) =>
                {
                    if (await cursor.NextIfAsync(static set => set.FieldCount == 7 && set.GetName(1) == "Reason", cancellationToken) is DbDataReader set)
                    {
                        await cursor.ReadConflictsAsync(set, cancellationToken);
                    }
                });

            ConcurrencyConflictException conflict = await Assert.ThrowsAsync<ConcurrencyConflictException>(() => batch.ExecuteAsync(Token));

            ConflictRow row = Assert.Single(conflict.Conflicts);
            Assert.Equal((7L, true, null), (row.Id, row.IsMissing, row.Name));
            Assert.Equal(1, conflict.StatementOrdinal);

            // The statement before the guard completed; the failed batch hands out what it read.
            Assert.Equal(1, Int(before.Value));
        }

        [Fact]
        public async Task A_transient_throw_is_re_run_until_the_attempts_run_out()
        {
            await using AsyncServiceScope scope = fixture.Tenant.CreateScope();
            DataAccessScope access = scope.ServiceProvider.GetRequiredService<DataAccessScope>();
            access.Operation = "fx-transient";
            IDataBatch batch = scope.ServiceProvider.GetRequiredService<ITenantDatabase>().CreateBatch(BatchPurpose.Read);
            BatchResult<RawResult> before = batch.Sql($"SELECT 1 AS [One];", new SqlOptions(resultSets: 1));
            _ = batch.Sql($"THROW 50503, N'Busy', 1;", null);

            DataAccessRetryExhaustedException exhausted = await Assert.ThrowsAsync<DataAccessRetryExhaustedException>(() => batch.ExecuteAsync(Token));

            // --- Four attempts, three re-runs, each logged ---
            Assert.Equal(4, exhausted.Attempts);
            Assert.Equal(TellmaSqlErrors.Transient, Assert.IsType<SqlException>(exhausted.InnerException).Number);
            Assert.Equal((4, 3), (access.RoundTrips, access.Retries));
            Assert.Equal(3, fixture.Logs.GetSnapshot().Count(static record => record.Id.Name == "DataBatch.Retrying"));

            // The final attempt's sets complete; the re-run attempts completed nothing twice.
            Assert.Equal(1, Int(before.Value));
        }

        [Fact]
        public async Task The_executor_instruments_measure_scopes_attempts_statements_and_retries()
        {
            IMeterFactory meters = fixture.Tenant.Services.GetRequiredService<IMeterFactory>();
            using MetricCollector<int> roundTrips = new(meters, DataTelemetryNames.MeterName, DataTelemetryNames.RoundTrips);
            using MetricCollector<double> duration = new(meters, DataTelemetryNames.MeterName, DataTelemetryNames.RoundTripDuration);
            using MetricCollector<int> statements = new(meters, DataTelemetryNames.MeterName, DataTelemetryNames.Statements);
            using MetricCollector<long> retries = new(meters, DataTelemetryNames.MeterName, DataTelemetryNames.Retries);

            // --- A write that commits on its own, then a transient failure: the second attempt succeeds ---
            await using (AsyncServiceScope scope = fixture.Tenant.CreateScope())
            {
                scope.ServiceProvider.GetRequiredService<DataAccessScope>().Operation = "fx-metrics";
                IDataBatch batch = scope.ServiceProvider.GetRequiredService<ITenantDatabase>().CreateBatch(BatchPurpose.Persist);
                batch.TransactionMode = TransactionMode.None;
                _ = batch.Sql(
                    $"IF NOT EXISTS (SELECT 1 FROM [core].[VersionTags] WHERE [Name] = N'fx-once')\nBEGIN\n    INSERT INTO [core].[VersionTags] ([Name]) VALUES (N'fx-once');\n    THROW 50503, N'Busy', 1;\nEND;",
                    new SqlOptions(writes: Set(VersionTags)));
                _ = await batch.ExecuteAsync(Token);
            }

            // --- The scope's round trips, recorded when it ended ---
            CollectedMeasurement<int> scopeTrips = Assert.Single(roundTrips.GetMeasurementSnapshot(), static measurement => Equals(measurement.Tags[DataTelemetryNames.OperationTag], "fx-metrics"));
            Assert.Equal(2, scopeTrips.Value);

            // --- Each attempt's duration, by outcome ---
            Assert.Equal(
                ["retried", "ok"],
                duration.GetMeasurementSnapshot()
                    .Where(static measurement => Equals(measurement.Tags[DataTelemetryNames.OperationTag], "fx-metrics"))
                    .Select(static measurement => (string?)measurement.Tags[DataTelemetryNames.OutcomeTag]));

            // --- The batch's statements, once, and the one re-run with its class and number ---
            Assert.Contains(statements.GetMeasurementSnapshot(), static measurement => measurement.Value == 1 && Equals(measurement.Tags[DataTelemetryNames.PurposeTag], "persist"));
            CollectedMeasurement<long> retry = Assert.Single(retries.GetMeasurementSnapshot());
            Assert.Equal(("reported", "50503"), ((string?)retry.Tags[DataTelemetryNames.ClassTag], (string?)retry.Tags[DataTelemetryNames.ErrorTag]));
        }

        /// <summary>Persists one job in a batch the connect-prologue stand-in marks with the premises.</summary>
        /// <param name="premises">The premises.</param>
        /// <returns>A task that completes when the batch committed.</returns>
        private async Task PersistJobAsync(GuardPremises premises)
        {
            await using AsyncServiceScope scope = fixture.Tenant.CreateScope();
            var batch = (DataBatch)scope.ServiceProvider.GetRequiredService<ITenantDatabase>().CreateBatch(BatchPurpose.Persist);
            batch.MarkConnectPrologue(premises);
            _ = batch.Sql(InsertJob, new SqlOptions(writes: Set(Jobs)));
            _ = await batch.ExecuteAsync(Token);
        }

        /// <summary>The job rows.</summary>
        /// <returns>The count.</returns>
        private async Task<int> JobCountAsync()
        {
            return await SqlServerFixture.ScalarAsync<int>(fixture.Tenant.ConnectionString, "SELECT COUNT(*) FROM [core].[Jobs];");
        }

        /// <summary>The change-tracked tables, as <c>schema.table</c>, in order.</summary>
        /// <param name="connectionString">The database.</param>
        /// <returns>The tables.</returns>
        private static async Task<List<string>> TrackedTablesAsync(string connectionString)
        {
            string? joined = await SqlServerFixture.ScalarAsync<string>(connectionString, """
                SELECT STRING_AGG(CAST(s.[name] + N'.' + t.[name] AS nvarchar(max)), N'|') WITHIN GROUP (ORDER BY s.[name], t.[name])
                FROM sys.change_tracking_tables AS c
                JOIN sys.tables AS t ON t.[object_id] = c.[object_id]
                JOIN sys.schemas AS s ON s.[schema_id] = t.[schema_id];
                """);
            return [.. (joined ?? string.Empty).Split('|', StringSplitOptions.RemoveEmptyEntries)];
        }

        /// <summary>The first column of a raw result's first row, as an <see cref="int" />.</summary>
        /// <param name="result">The result.</param>
        /// <returns>The value.</returns>
        private static int Int(RawResult result)
        {
            return ((int[])result.ResultSets[0].GetBuffer(0))[0];
        }

        /// <summary>A set of tables.</summary>
        /// <param name="tables">The tables.</param>
        /// <returns>The set.</returns>
        private static HashSet<TableName> Set(params TableName[] tables)
        {
            return [.. tables];
        }
    }
}
