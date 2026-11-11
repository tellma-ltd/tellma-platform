// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Microsoft.Data.SqlClient;
using Microsoft.Extensions.DependencyInjection;
using System.Data.Common;
using Tellma.Core.Abstractions.Data;
using Tellma.Core.Abstractions.Hosting;
using Tellma.Core.Abstractions.TableTypes;
using Tellma.Core.Data.Batches;
using Tellma.Core.IntegrationTests.Infrastructure;
using Tellma.Testing.Entities;

namespace Tellma.Core.IntegrationTests.Data
{
    /// <summary>
    ///     Raw-statement batches against a fixture database: the fixture's own shape, the schema
    ///     guard, transactions, result sets, deduplication, the factory handle, the guard wrapper,
    ///     table-valued parameters, statement ordinals and the change-tracking audit.
    /// </summary>
    [Trait("Category", "Integration")]
    public class DataBatchTests(SqlServerFixture sql)
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

        [Fact]
        public async Task The_fixture_database_is_created_in_the_platform_collation_with_change_tracking_and_the_running_fingerprint()
        {
            await using FixtureTenant tenant = await CreateTenantAsync("fx_shape");

            Assert.Equal(DatabaseCollations.Platform, await SqlServerFixture.ScalarAsync<string>(tenant.ConnectionString, "SELECT CAST(DATABASEPROPERTYEX(DB_NAME(), 'Collation') AS nvarchar(128));"));
            Assert.True(await SqlServerFixture.ScalarAsync<bool>(tenant.ConnectionString, "SELECT [is_read_committed_snapshot_on] FROM sys.databases WHERE [name] = DB_NAME();"));
            Assert.NotNull(await SqlServerFixture.ScalarAsync<object>(tenant.ConnectionString, $"SELECT DATABASE_PRINCIPAL_ID(N'{DatabaseRoles.Application}');"));

            // --- Every table but the platform's own and the history tables is change-tracked ---
            Assert.Equal(
                ["core.Blobs", "core.Jobs", "core.Users", "core.UserStamps", "core.VersionTags", "fixture.Links", "fixture.Nodes", "fixture.Shipments", "fixture.WidgetExtras", "fixture.WidgetPartNotes", "fixture.WidgetParts", "fixture.Widgets"],
                await TrackedTablesAsync(tenant.ConnectionString));
            Assert.Equal(1, await SqlServerFixture.ScalarAsync<int>(tenant.ConnectionString, "SELECT CAST([temporal_type] AS int) FROM sys.tables WHERE [object_id] = OBJECT_ID(N'fixture.WidgetsHistory');"));

            // --- The schema guard's row is the running model's fingerprint ---
            Assert.Equal(tenant.Fingerprint, await SqlServerFixture.ScalarAsync<string>(tenant.ConnectionString, "SELECT [Fingerprint] FROM [dbo].[__TellmaSchema];"));
        }

        [Fact]
        public async Task A_database_that_does_not_list_the_running_fingerprint_refuses_a_read_a_validate_and_a_persist_batch_with_50501()
        {
            await using FixtureTenant tenant = await CreateTenantAsync("fx_guard");
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

                SqlException refused = await Assert.ThrowsAsync<SqlException>(() => batch.ExecuteAsync(Token));
                Assert.Equal(TellmaSqlErrors.SchemaBehind, refused.Number);
                Assert.Equal(TellmaSqlErrors.SchemaBehindCode, refused.Message);
                Assert.False(count.IsCompleted, purpose.ToString());
            }

            // Nothing the persist batch carried was written.
            Assert.Equal(0, await SqlServerFixture.ScalarAsync<int>(tenant.ConnectionString, "SELECT COUNT(*) FROM [core].[Jobs];"));
        }

        [Fact]
        public async Task The_running_fingerprint_as_the_older_of_two_rows_is_served()
        {
            await using FixtureTenant tenant = await CreateTenantAsync("fx_older");
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
            await using FixtureTenant tenant = await CreateTenantAsync("fx_throw");
            await using AsyncServiceScope scope = tenant.CreateScope();
            IDataBatch batch = scope.ServiceProvider.GetRequiredService<ITenantDatabase>().CreateBatch(BatchPurpose.Read);

            BatchResult<RawResult> first = batch.Sql($"SELECT 7 AS [Seven];", new SqlOptions(resultSets: 1));
            BatchResult<RawResult> second = batch.Sql($"SELECT {"eight"} AS [Eight];", new SqlOptions(resultSets: 1));
            _ = batch.Sql($"THROW 50000, N'Stop.', 1;", null);
            BatchResult<RawResult> after = batch.Sql($"SELECT 9 AS [Nine];", new SqlOptions(resultSets: 1));

            SqlException thrown = await Assert.ThrowsAsync<SqlException>(() => batch.ExecuteAsync(Token));

            Assert.Equal(50000, thrown.Number);
            Assert.Equal(7, Int(first.Value));
            Assert.Equal("eight", ((string[])second.Value.ResultSets[0].GetBuffer(0))[0]);
            Assert.False(after.IsCompleted);
        }

        [Fact]
        public async Task A_throw_and_a_foreign_key_violation_resolve_to_the_ordinals_of_the_statements_that_raised_them()
        {
            await using FixtureTenant tenant = await CreateTenantAsync("fx_ordinal");
            await using AsyncServiceScope scope = tenant.CreateScope();
            ITenantDatabase database = scope.ServiceProvider.GetRequiredService<ITenantDatabase>();

            // --- A THROW from the statement at ordinal 3 ---
            var throwing = (DataBatch)database.CreateBatch(BatchPurpose.Read);
            _ = throwing.Sql($"SELECT 0 AS [Zero];", new SqlOptions(resultSets: 1));
            _ = throwing.Sql($"DECLARE @x int = 1;\nSET @x += 1;", null);
            _ = throwing.Sql($"SELECT 2 AS [Two]\nFROM (VALUES (1)) AS v([One]);", new SqlOptions(resultSets: 1));
            _ = throwing.Sql($"IF 1 = 1\n    THROW 50000, N'Three.', 1;", null);
            _ = throwing.Sql($"SELECT 4 AS [Four];", new SqlOptions(resultSets: 1));

            SqlException thrown = await Assert.ThrowsAsync<SqlException>(() => throwing.ExecuteAsync(Token));
            Assert.Equal(3, throwing.Assemble().StatementOrdinalAt(thrown.LineNumber));

            // --- A foreign-key violation from the statement at ordinal 4 ---
            TableName extras = new("fixture", "WidgetExtras");
            var violating = (DataBatch)database.CreateBatch(BatchPurpose.Persist);
            _ = violating.Sql(CountJobs, new SqlOptions(resultSets: 1));
            _ = violating.Sql(InsertJob, new SqlOptions(writes: Set(Jobs)));
            _ = violating.Sql(InsertJob, new SqlOptions(writes: Set(Jobs)));

            // A different text, so it does not collapse into the first count.
            _ = violating.Sql($"SELECT COUNT(*) AS [N] FROM [core].[Jobs] WHERE [Id] > 0;", new SqlOptions(resultSets: 1));
            _ = violating.Sql($"INSERT INTO [fixture].[WidgetExtras] ([Id], [Note])\nVALUES ({123456}, N'orphan');", new SqlOptions(writes: Set(extras)));

            SqlException violated = await Assert.ThrowsAsync<SqlException>(() => violating.ExecuteAsync(Token));
            Assert.Equal(547, violated.Number);
            Assert.Equal(4, violating.Assemble().StatementOrdinalAt(violated.LineNumber));

            // The in-text transaction rolled the earlier inserts back.
            Assert.Equal(0, await SqlServerFixture.ScalarAsync<int>(tenant.ConnectionString, "SELECT COUNT(*) FROM [core].[Jobs];"));
        }

        [Fact]
        public async Task Transaction_mode_none_leaves_autocommit_while_auto_and_explicit_roll_back()
        {
            await using FixtureTenant tenant = await CreateTenantAsync("fx_txmode");
            await using AsyncServiceScope scope = tenant.CreateScope();
            ITenantDatabase database = scope.ServiceProvider.GetRequiredService<ITenantDatabase>();

            foreach (TransactionMode mode in new[] { TransactionMode.None, TransactionMode.Auto, TransactionMode.Explicit })
            {
                IDataBatch batch = database.CreateBatch(BatchPurpose.Persist);
                batch.TransactionMode = mode;
                _ = batch.Sql($"INSERT INTO [core].[VersionTags] ([Name]) VALUES ({mode.ToString()});", new SqlOptions(writes: Set(VersionTags)));
                _ = batch.Sql($"THROW 50000, N'After the write.', 1;", null);

                _ = await Assert.ThrowsAsync<SqlException>(() => batch.ExecuteAsync(Token));
            }

            // Only the autocommitted write survived its batch's failure.
            Assert.Equal("None", await SqlServerFixture.ScalarAsync<string>(tenant.ConnectionString, "SELECT STRING_AGG([Name], N',') FROM [core].[VersionTags];"));
        }

        [Fact]
        public async Task Two_identical_statements_collapse_into_one_when_they_declare_no_writes_and_both_run_when_they_do()
        {
            await using FixtureTenant tenant = await CreateTenantAsync("fx_dedup");
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
            await using FixtureTenant tenant = await CreateTenantAsync("fx_handle");
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
            ConnectPrologue prologue = new();
            await using FixtureTenant tenant = await CreateTenantAsync("fx_guarded", services => services.AddSingleton<IDataBatchContributor>(prologue));
            await using AsyncServiceScope scope = tenant.CreateScope();
            ITenantDatabase database = scope.ServiceProvider.GetRequiredService<ITenantDatabase>();

            foreach (bool passes in new[] { false, true })
            {
                prologue.Passes = passes;
                var batch = (DataBatch)database.CreateBatch(BatchPurpose.Persist);
                batch.MarkConnectPrologue();
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
            await using FixtureTenant tenant = await CreateTenantAsync("fx_tvp");
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
            await using FixtureTenant tenant = await CreateTenantAsync("fx_audit");
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

        /// <summary>Creates a fixture tenant over a new database the suite drops at the end.</summary>
        /// <param name="prefix">The prefix identifying the test.</param>
        /// <param name="configureServices">Adds services after the composition.</param>
        /// <returns>The tenant.</returns>
        private Task<FixtureTenant> CreateTenantAsync(string prefix, Action<IServiceCollection>? configureServices = null)
        {
            return FixtureTenant.CreateAsync(sql.MasterConnectionString, sql.NewDatabaseName(prefix), configureServices, cancellationToken: Token);
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

        /// <summary>
        ///     A stand-in for the connect prologue: on a marked batch it declares the guard variable with
        ///     the verdict the test chose, returns it, and reports it from its reader.
        /// </summary>
        private sealed class ConnectPrologue : IDataBatchContributor
        {
            /// <summary>The verdict the next batch's guard reports.</summary>
            public bool Passes { get; set; }

            /// <inheritdoc />
            public int Order => 0;

            /// <inheritdoc />
            public void Contribute(IDataBatch batch, DataBatchStage stage)
            {
                if (stage != DataBatchStage.Prologue || batch is not DataBatch { CarriesConnectPrologue: true } marked)
                {
                    return;
                }

                StatementDraft draft = marked.Begin();
                int ordinal = draft.Ordinal;
                marked.Append(
                    draft,
                    $"DECLARE {ReservedSqlNames.Guard} bit = {(Passes ? 1 : 0)};\nSELECT {ReservedSqlNames.Guard} AS [Guard];",
                    reader: async (cursor, cancellationToken) =>
                    {
                        DbDataReader reader = await cursor.NextAsync(ordinal, cancellationToken);
                        _ = await reader.ReadAsync(cancellationToken);
                        marked.ReportGuard(reader.GetBoolean(0));
                        cursor.CountRows(1);
                    });
            }
        }
    }
}
