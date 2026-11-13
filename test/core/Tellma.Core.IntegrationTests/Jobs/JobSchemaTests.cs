// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Microsoft.Data.SqlClient;
using Tellma.Testing.Entities;

namespace Tellma.Core.IntegrationTests.Jobs
{
    /// <summary>
    ///     The job tables as the migrations create them: under read-committed snapshot on and off,
    ///     with lock escalation disabled on the queue, the seeded worker row, the delete paths
    ///     between schedules, their states, jobs and job entities, and the CHECK constraints that
    ///     tie a job's lease and pending confirmations to its status.
    /// </summary>
    /// <param name="fixture">The class's database, created with read-committed snapshot on and reset before each test.</param>
    /// <param name="sql">The suite's server.</param>
    [Trait("Category", "Integration")]
    public class JobSchemaTests(JobsDatabase fixture, SqlServerFixture sql) : IClassFixture<JobsDatabase>, IAsyncLifetime
    {
        /// <summary>The number SQL Server raises for a statement that violates a constraint.</summary>
        private const int ConstraintViolation = 547;

        /// <summary>The columns every job row needs, before the ones a test varies.</summary>
        private const string JobColumns = "[Id], [HandlerKey], [DueAt], [RunAsUserId], [CreatedAt]";

        /// <summary>The values of <see cref="JobColumns" /> for job 1.</summary>
        private const string JobValues = "1, N'fixture.probe', SYSUTCDATETIME(), 1, SYSUTCDATETIME()";

        /// <summary>The job tables, as <c>schema.table</c>, in ordinal order.</summary>
        private static readonly string[] JobTables = ["core.JobWorkerState", "core.Jobs", "core.ScheduleStates", "core.Schedules", "core.SchedulesHistory"];

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
        public async Task The_tables_migrate_under_read_committed_snapshot()
        {
            Assert.True(await SqlServerFixture.ScalarAsync<bool>(fixture.ConnectionString, "SELECT [is_read_committed_snapshot_on] FROM sys.databases WHERE [name] = DB_NAME();"));
            await AssertJobTablesAsync(fixture.ConnectionString);
        }

        [Fact]
        public async Task The_tables_migrate_without_read_committed_snapshot()
        {
            string connectionString = await FixtureDatabase.CreateAsync(sql.MasterConnectionString, sql.NewDatabaseName("fx_jobs_locking"), null, readCommittedSnapshot: false, Token);

            Assert.False(await SqlServerFixture.ScalarAsync<bool>(connectionString, "SELECT [is_read_committed_snapshot_on] FROM sys.databases WHERE [name] = DB_NAME();"));
            await AssertJobTablesAsync(connectionString);
        }

        [Fact]
        public async Task Deleting_a_schedule_takes_its_state_and_leaves_its_jobs_unattached()
        {
            await SqlServerFixture.ExecuteAsync(
                fixture.ConnectionString,
                "INSERT INTO [core].[Schedules] ([Id], [Name], [HandlerKey], [CronExpression], [RunAsUserId], [CreatedAt], [CreatedById], [ModifiedAt], [ModifiedById]) VALUES (501, N'Nightly', N'fixture.probe', N'0 1 * * *', 1, SYSDATETIMEOFFSET(), 1, SYSDATETIMEOFFSET(), 1);",
                $"INSERT INTO [core].[Jobs] ({JobColumns}, [Status], [ScheduleId]) VALUES ({JobValues}, 'Pending', 501);",
                "INSERT INTO [core].[ScheduleStates] ([ScheduleId], [LastJobId]) VALUES (501, 1);",
                "DELETE FROM [core].[Schedules] WHERE [Id] = 501;");

            Assert.Equal(0, await SqlServerFixture.ScalarAsync<int>(fixture.ConnectionString, "SELECT COUNT(*) FROM [core].[ScheduleStates] WHERE [ScheduleId] = 501;"));
            Assert.Equal(1, await SqlServerFixture.ScalarAsync<int>(fixture.ConnectionString, "SELECT COUNT(*) FROM [core].[Jobs] WHERE [Id] = 1 AND [ScheduleId] IS NULL;"));
        }

        [Fact]
        public async Task Deleting_a_job_unattaches_its_schedule_state_and_its_row()
        {
            await SqlServerFixture.ExecuteAsync(
                fixture.ConnectionString,
                "INSERT INTO [core].[Schedules] ([Id], [Name], [HandlerKey], [CronExpression], [RunAsUserId], [CreatedAt], [CreatedById], [ModifiedAt], [ModifiedById]) VALUES (501, N'Nightly', N'fixture.probe', N'0 1 * * *', 1, SYSDATETIMEOFFSET(), 1, SYSDATETIMEOFFSET(), 1);",
                $"INSERT INTO [core].[Jobs] ({JobColumns}, [Status]) VALUES ({JobValues}, 'Pending');",
                "INSERT INTO [core].[ScheduleStates] ([ScheduleId], [LastJobId]) VALUES (501, 1);",
                "INSERT INTO [fixture].[Shipments] ([Id], [State], [JobId], [CreatedAt], [CreatedById], [ModifiedAt], [ModifiedById]) VALUES (9, 'Pending', 1, SYSDATETIMEOFFSET(), 1, SYSDATETIMEOFFSET(), 1);",
                "DELETE FROM [core].[Jobs] WHERE [Id] = 1;");

            Assert.Equal(1, await SqlServerFixture.ScalarAsync<int>(fixture.ConnectionString, "SELECT COUNT(*) FROM [core].[ScheduleStates] WHERE [ScheduleId] = 501 AND [LastJobId] IS NULL;"));
            Assert.Equal(1, await SqlServerFixture.ScalarAsync<int>(fixture.ConnectionString, "SELECT COUNT(*) FROM [fixture].[Shipments] WHERE [Id] = 9 AND [JobId] IS NULL;"));
        }

        [Fact]
        public async Task A_running_job_holds_a_lease_and_a_job_in_any_other_status_none()
        {
            // --- Running with its lease, and queued without one, are both accepted ---
            await SqlServerFixture.ExecuteAsync(
                fixture.ConnectionString,
                $"INSERT INTO [core].[Jobs] ({JobColumns}, [Status], [LeaseToken], [LeaseExpiresAt]) VALUES ({JobValues}, 'Running', NEWID(), SYSUTCDATETIME());",
                $"INSERT INTO [core].[Jobs] ({JobColumns}, [Status]) VALUES (2, N'fixture.probe', SYSUTCDATETIME(), 1, SYSUTCDATETIME(), 'Pending');");

            // --- Running without a lease, or with half of one, and a lease on a job that is not running, are refused ---
            await AssertRefusedAsync("CK_Jobs_Lease", $"INSERT INTO [core].[Jobs] ({JobColumns}, [Status]) VALUES (3, N'fixture.probe', SYSUTCDATETIME(), 1, SYSUTCDATETIME(), 'Running');");
            await AssertRefusedAsync("CK_Jobs_Lease", $"INSERT INTO [core].[Jobs] ({JobColumns}, [Status], [LeaseToken]) VALUES (3, N'fixture.probe', SYSUTCDATETIME(), 1, SYSUTCDATETIME(), 'Running', NEWID());");
            await AssertRefusedAsync("CK_Jobs_Lease", $"INSERT INTO [core].[Jobs] ({JobColumns}, [Status], [LeaseToken], [LeaseExpiresAt]) VALUES (3, N'fixture.probe', SYSUTCDATETIME(), 1, SYSUTCDATETIME(), 'Pending', NEWID(), SYSUTCDATETIME());");
            await AssertRefusedAsync("CK_Jobs_Lease", "UPDATE [core].[Jobs] SET [Status] = 'Succeeded' WHERE [Id] = 1;");
        }

        [Fact]
        public async Task A_job_waiting_for_confirmation_holds_what_it_waits_for_and_a_job_in_any_other_status_nothing()
        {
            await SqlServerFixture.ExecuteAsync(
                fixture.ConnectionString,
                $"INSERT INTO [core].[Jobs] ({JobColumns}, [Status], [PendingConfirmationsJson]) VALUES ({JobValues}, 'PendingConfirmation', N'[{{\"key\":\"Sales.Backdated\"}}]');");

            await AssertRefusedAsync("CK_Jobs_PendingConfirmations", $"INSERT INTO [core].[Jobs] ({JobColumns}, [Status]) VALUES (2, N'fixture.probe', SYSUTCDATETIME(), 1, SYSUTCDATETIME(), 'PendingConfirmation');");
            await AssertRefusedAsync("CK_Jobs_PendingConfirmations", $"INSERT INTO [core].[Jobs] ({JobColumns}, [Status], [PendingConfirmationsJson]) VALUES (2, N'fixture.probe', SYSUTCDATETIME(), 1, SYSUTCDATETIME(), 'Pending', N'[]');");
            await AssertRefusedAsync("CK_Jobs_PendingConfirmations", "UPDATE [core].[Jobs] SET [Status] = 'Pending' WHERE [Id] = 1;");
        }

        [Fact]
        public async Task The_worker_state_holds_its_one_seeded_row()
        {
            Assert.Equal(1, await SqlServerFixture.ScalarAsync<int>(fixture.ConnectionString, "SELECT COUNT(*) FROM [core].[JobWorkerState] WHERE [Id] = 1 AND [LastTickAt] IS NULL;"));
            await AssertRefusedAsync("CK_JobWorkerState_SingleRow", "INSERT INTO [core].[JobWorkerState] ([Id]) VALUES (2);");
        }

        [Fact]
        public async Task A_built_in_schedule_is_seeded_running_as_the_system_user_with_a_state_not_yet_due()
        {
            await using SqlConnection connection = new(fixture.ConnectionString);
            await connection.OpenAsync(Token);
            await using SqlCommand command = connection.CreateCommand();
            command.CommandText = """
                SELECT [s].[HandlerKey], [s].[Code], [s].[Name], [s].[CronExpression], [s].[ArgumentsJson], [s].[TimeZoneId],
                    [s].[RunAsUserId], [s].[CreatedById], [s].[ModifiedById], [s].[IsActive], [s].[PausedReason],
                    [s].[MissedPolicy], [s].[OverlapPolicy], [s].[CatchUpWindowMinutes],
                    [t].[ScheduleId], [t].[NextDueAt], [t].[LastJobId], [t].[LeaseToken]
                FROM [core].[Schedules] AS [s]
                LEFT JOIN [core].[ScheduleStates] AS [t] ON [t].[ScheduleId] = [s].[Id]
                WHERE [s].[Id] = @id;
                """;
            _ = command.Parameters.AddWithValue("@id", FixtureFeature.ProbeScheduleId);
            await using SqlDataReader reader = await command.ExecuteReaderAsync(Token);
            Assert.True(await reader.ReadAsync(Token));

            // --- The schedule row: the handler key is its code and name, and the system user runs and authored it ---
            Assert.Equal((FixtureJobKeys.Probe, FixtureJobKeys.Probe, FixtureJobKeys.Probe, "0 1 * * *"), (reader.GetString(0), reader.GetString(1), reader.GetString(2), reader.GetString(3)));
            Assert.True(reader.IsDBNull(4) && reader.IsDBNull(5));
            Assert.Equal((1, 1, 1), (reader.GetInt32(6), reader.GetInt32(7), reader.GetInt32(8)));
            Assert.True(reader.GetBoolean(9));
            Assert.True(reader.IsDBNull(10));
            Assert.Equal(("Coalesce", "Skip", 1440), (reader.GetString(11), reader.GetString(12), reader.GetInt32(13)));

            // --- Its state row: present, with no due time until the scheduler first computes one ---
            Assert.False(reader.IsDBNull(14));
            Assert.True(reader.IsDBNull(15) && reader.IsDBNull(16) && reader.IsDBNull(17));
            Assert.False(await reader.ReadAsync(Token));
        }

        /// <summary>
        ///     Asserts the job tables exist, the queue alone disables lock escalation, the worker's
        ///     row is seeded, the schedules are system-versioned, and both id sequences exist.
        /// </summary>
        /// <param name="connectionString">The database.</param>
        /// <returns>A task that completes when the assertions ran.</returns>
        private static async Task AssertJobTablesAsync(string connectionString)
        {
            Dictionary<string, string> escalation = [];
            await using (SqlConnection connection = new(connectionString))
            {
                await connection.OpenAsync(Token);
                await using SqlCommand command = connection.CreateCommand();
                command.CommandText = """
                    SELECT s.[name] + N'.' + t.[name], t.[lock_escalation_desc]
                    FROM sys.tables AS t
                    JOIN sys.schemas AS s ON s.[schema_id] = t.[schema_id]
                    WHERE s.[name] = N'core' AND (t.[name] LIKE N'Job%' OR t.[name] LIKE N'Schedule%');
                    """;
                await using SqlDataReader reader = await command.ExecuteReaderAsync(Token);
                while (await reader.ReadAsync(Token))
                {
                    escalation[reader.GetString(0)] = reader.GetString(1);
                }
            }

            Assert.Equal(JobTables, escalation.Keys.Order(StringComparer.Ordinal));
            Assert.All(escalation, static table => Assert.Equal(table.Key == "core.Jobs" ? "DISABLE" : "TABLE", table.Value));
            Assert.Equal(1, await SqlServerFixture.ScalarAsync<int>(connectionString, "SELECT COUNT(*) FROM [core].[JobWorkerState];"));
            Assert.Equal(2, await SqlServerFixture.ScalarAsync<int>(connectionString, "SELECT CAST([temporal_type] AS int) FROM sys.tables WHERE [object_id] = OBJECT_ID(N'core.Schedules');"));
            Assert.Equal(2, await SqlServerFixture.ScalarAsync<int>(connectionString, "SELECT COUNT(*) FROM sys.sequences WHERE [schema_id] = SCHEMA_ID(N'core') AND [name] IN (N'sq_Jobs', N'sq_Schedules');"));
        }

        /// <summary>Asserts a statement is refused by the named constraint.</summary>
        /// <param name="constraint">The constraint.</param>
        /// <param name="statement">The statement.</param>
        /// <returns>A task that completes when the assertion ran.</returns>
        private async Task AssertRefusedAsync(string constraint, string statement)
        {
            SqlException exception = await Assert.ThrowsAsync<SqlException>(() => SqlServerFixture.ExecuteAsync(fixture.ConnectionString, statement));

            Assert.Equal(ConstraintViolation, exception.Number);
            Assert.Contains($"\"{constraint}\"", exception.Message, StringComparison.Ordinal);
        }
    }
}
