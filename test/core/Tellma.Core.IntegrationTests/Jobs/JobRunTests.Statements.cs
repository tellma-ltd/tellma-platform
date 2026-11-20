// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Microsoft.Data.SqlClient;
using Microsoft.Extensions.DependencyInjection;
using System.Globalization;
using System.Xml.Linq;
using Tellma.Core.Abstractions.Access;
using Tellma.Core.Abstractions.Data;
using Tellma.Core.Abstractions.Jobs;
using Tellma.Core.Abstractions.TableTypes;
using Tellma.Core.Data;
using Tellma.Core.Data.Batches;
using Tellma.Core.Jobs;

namespace Tellma.Core.IntegrationTests.Jobs
{
    // The lease statements on their own: the claim under contention and its plan, and the fence
    // each later statement holds against a row another claim took.
    public abstract partial class JobRunTests
    {
        /// <summary>The namespace of a showplan document.</summary>
        private static readonly XNamespace Showplan = "http://schemas.microsoft.com/sqlserver/2004/07/showplan";

        /// <summary>The name of the column a returned plan arrives in.</summary>
        private const string ShowplanColumn = "Microsoft SQL Server 2005 XML Showplan";

        [Fact]
        public async Task Claims_contending_for_one_keys_rows_lease_disjoint_sets_that_together_hold_every_row()
        {
            // --- Twenty-four due rows, and eight claims of three sent at once, each from its own connection ---
            List<int> ids = [];
            for (int index = 0; index < 24; index++)
            {
                ids.Add(await fixture.InsertJobAsync(JobRunKeys.Probe, dueSecondsAgo: 100 + (index * 7 % 24)));
            }

            IReadOnlyList<ClaimedJob>[] sets = await Task.WhenAll(Enumerable.Range(0, 8).Select(_ => Task.Run(() => ClaimAsync(Guid.NewGuid(), size: 3), Token)));

            // --- No row in two sets, every row in one, each set in due order ---
            Assert.Equal(ids.Order(), sets.SelectMany(static set => set.Select(static row => row.Job.Id)).Order());
            Assert.All(sets, static set => Assert.Equal(set.OrderBy(static row => row.Job.DueAt).ThenBy(static row => row.Job.Id), set));
        }

        [Fact]
        public async Task A_claim_leases_the_earliest_due_rows_first_and_rows_due_together_by_id()
        {
            int late = await fixture.InsertJobAsync(JobRunKeys.Probe, dueSecondsAgo: 10);
            int tiedFirst = await fixture.InsertJobAsync(JobRunKeys.Probe);
            int tiedSecond = await fixture.InsertJobAsync(JobRunKeys.Probe);
            int early = await fixture.InsertJobAsync(JobRunKeys.Probe, dueSecondsAgo: 60);
            await SqlServerFixture.ExecuteAsync(
                fixture.ConnectionString,
                Invariant($"UPDATE [core].[Jobs] SET [DueAt] = DATEADD(second, -30, SYSUTCDATETIME()) WHERE [Id] IN ({tiedFirst}, {tiedSecond});"));

            IReadOnlyList<ClaimedJob> claimed = await ClaimAsync(Guid.NewGuid(), size: 3);

            Assert.Equal([early, tiedFirst, tiedSecond], claimed.Select(static row => row.Job.Id));
            Assert.Null((await fixture.JobAsync(late)).LeaseToken);
        }

        [Fact]
        public async Task The_claim_seeks_the_available_index_under_its_room()
        {
            // --- A table whose ended rows far outnumber the due rows of any one key ---
            await SqlServerFixture.ExecuteAsync(
                fixture.ConnectionString,
                Invariant($"""
                    INSERT INTO [core].[Jobs] ([Id], [HandlerKey], [Status], [DueAt], [Attempts], [CompletedAt], [RunAsUserId], [CreatedAt])
                    SELECT 1000000 + [n].[N], CONCAT(N'test.key', [n].[N] % 20), CASE WHEN [n].[N] % 53 = 0 THEN 'Pending' ELSE 'Succeeded' END,
                        DATEADD(second, -[n].[N], SYSUTCDATETIME()), 1, CASE WHEN [n].[N] % 53 = 0 THEN NULL ELSE SYSUTCDATETIME() END, {ActiveUserIdValue}, SYSUTCDATETIME()
                    FROM (SELECT TOP (40000) ROW_NUMBER() OVER (ORDER BY (SELECT NULL)) AS [N] FROM sys.all_columns AS [a] CROSS JOIN sys.all_columns AS [b]) AS [n];
                    """),
                "UPDATE STATISTICS [core].[Jobs] WITH FULLSCAN;");

            // --- The plan of the claim as its batch sends it, parameterised: run once with the actual
            // plans returned after each statement's own results ---
            await using SqlConnection connection = new(fixture.ConnectionString);
            await connection.OpenAsync(Token);
            await using (SqlCommand on = new("SET STATISTICS XML ON;", connection))
            {
                _ = await on.ExecuteNonQueryAsync(Token);
            }

            List<XDocument> plans = [];
            await using (SqlCommand claim = await ClaimCommandAsync(connection, Guid.NewGuid(), size: 25, key: "test.key7"))
            {
                await using SqlDataReader reader = await claim.ExecuteReaderAsync(Token);
                do
                {
                    bool plan = reader.FieldCount == 1 && reader.GetName(0) == ShowplanColumn;
                    while (await reader.ReadAsync(Token))
                    {
                        if (plan)
                        {
                            plans.Add(XDocument.Parse(reader.GetString(0)));
                        }
                    }
                }
                while (await reader.NextResultAsync(Token));
            }

            // --- The update's rows come from a seek of the filtered index ---
            XElement[] seeks = [.. plans.SelectMany(static plan => plan.Descendants(Showplan + "RelOp"))
                .Where(static op => (string?)op.Attribute("PhysicalOp") == "Index Seek")
                .Where(static op => op.Descendants(Showplan + "Object").Any(static target => (string?)target.Attribute("Index") == "[IX_Jobs_Available]"))];
            Assert.NotEmpty(seeks);
            Assert.DoesNotContain(
                plans.SelectMany(static plan => plan.Descendants(Showplan + "RelOp")),
                static op => (string?)op.Attribute("PhysicalOp") is "Clustered Index Scan" or "Table Scan" or "Index Scan"
                    && op.Descendants(Showplan + "Object").Any(static target => (string?)target.Attribute("Table") == "[Jobs]"));
        }

        [Fact]
        public async Task A_claim_run_again_under_its_token_returns_its_rows_once_and_a_renewal_extends_only_the_leases_still_held()
        {
            int id = await fixture.InsertJobAsync(JobRunKeys.Probe);
            var lapsed = Guid.NewGuid();
            var current = Guid.NewGuid();
            Assert.Equal(id, Assert.Single(await ClaimAsync(lapsed, size: 1)).Job.Id);

            // --- The first lease lapses and a second claim takes the row; running it again returns the row, counted once ---
            await SqlServerFixture.ExecuteAsync(fixture.ConnectionString, Invariant($"UPDATE [core].[Jobs] SET [LeaseExpiresAt] = DATEADD(second, -1, SYSUTCDATETIME()) WHERE [Id] = {id};"));
            Assert.Equal((id, 2), Pair(Assert.Single(await ClaimAsync(current, size: 1))));
            Assert.Equal((id, 2), Pair(Assert.Single(await ClaimAsync(current, size: 1))));

            // --- The renewal returns the row under the token that holds it, never under the lapsed one ---
            IReadOnlyList<RenewedJob> renewed = await MaintenanceAsync(batch => JobStatements.Renew(
                batch,
                [new JobLeaseList { Id = lapsed, LeaseSeconds = 60 }, new JobLeaseList { Id = current, LeaseSeconds = 60 }],
                []));
            Assert.Equal([(id, current)], renewed.Select(static row => (row.Id, row.LeaseToken)));
        }

        [Fact]
        public async Task A_checkpoint_or_a_completion_under_a_token_that_lost_its_row_fails_on_the_fence_and_rolls_its_transaction_back()
        {
            int id = await fixture.InsertJobAsync(JobRunKeys.Probe);
            var lost = Guid.NewGuid();
            _ = Assert.Single(await ClaimAsync(lost, size: 1));
            await SqlServerFixture.ExecuteAsync(fixture.ConnectionString, Invariant($"UPDATE [core].[Jobs] SET [LeaseToken] = NEWID() WHERE [Id] = {id};"));

            // --- The checkpoint: the fence fails the batch, and the note before it rolls back ---
            BatchAssertionFailedException checkpoint = await Assert.ThrowsAsync<BatchAssertionFailedException>(() => PersistAsync(batch =>
            {
                Note(batch, id, "checkpoint");
                JobStatements.AppendProgress(batch, lost, [new JobProgressList { Id = id, ProgressPercent = 10 }]);
            }));
            Assert.True(JobCompletionFailures.IsLeaseLost(checkpoint));

            // --- The completion: the same fence, the same rollback ---
            BatchAssertionFailedException completion = await Assert.ThrowsAsync<BatchAssertionFailedException>(() => PersistAsync(batch =>
            {
                _ = JobStatements.Complete(batch, lost, [JobOutcome.Succeeded(null).ToRow(id)]);
                Note(batch, id, "completion");
            }));
            Assert.True(JobCompletionFailures.IsLeaseLost(completion));

            JobRow row = await fixture.JobAsync(id);
            Assert.Equal((nameof(JobStatus.Running), null), (row.Status, row.ProgressPercent));
            Assert.Empty(await fixture.RunLogAsync());
        }

        /// <summary>The user id the jobs run as, for SQL text.</summary>
        private static string ActiveUserIdValue => JobRunDatabase.ActiveUserId.ToString(CultureInfo.InvariantCulture);

        /// <summary>A claimed row's id and attempts.</summary>
        /// <param name="row">The row.</param>
        /// <returns>The pair.</returns>
        private static (int Id, int Attempts) Pair(ClaimedJob row)
        {
            return (row.Job.Id, row.Job.Attempts);
        }

        /// <summary>The claim statement of a handler key as its batch sends it, as a command of a connection of the test's own.</summary>
        /// <param name="connection">The connection.</param>
        /// <param name="token">The claim's token.</param>
        /// <param name="size">How many rows it may lease.</param>
        /// <param name="key">The handler key.</param>
        /// <returns>The command.</returns>
        private async Task<SqlCommand> ClaimCommandAsync(SqlConnection connection, Guid token, int size, string key)
        {
            DataBatch draft = Batch(BatchPurpose.Maintenance, out AsyncServiceScope scope);
            await using (scope)
            {
                _ = JobStatements.Claim(draft, key, size, token, 60, "test/1");
                BatchStatement statement = Assert.Single(draft.Statements, static statement => statement.Writes.Count > 0);
                SqlCommand command = new(statement.Text, connection);
                foreach (BatchParameter parameter in statement.Parameters)
                {
                    _ = command.Parameters.Add(parameter.Create());
                }

                return command;
            }
        }

        /// <summary>Claims the probe handler's due rows in a batch of its own.</summary>
        /// <param name="token">The claim's token.</param>
        /// <param name="size">How many rows it may lease.</param>
        /// <returns>The claimed rows.</returns>
        private Task<IReadOnlyList<ClaimedJob>> ClaimAsync(Guid token, int size)
        {
            return MaintenanceAsync(batch => JobStatements.Claim(batch, JobRunKeys.Probe, size, token, 60, "test/1").Rows);
        }

        /// <summary>Runs one statement in a maintenance batch of the system scope, each statement committing on its own.</summary>
        /// <typeparam name="T">The statement's result.</typeparam>
        /// <param name="append">Appends the statement.</param>
        /// <returns>The result.</returns>
        private async Task<T> MaintenanceAsync<T>(Func<DataBatch, BatchResult<T>> append)
        {
            DataBatch batch = Batch(BatchPurpose.Maintenance, out AsyncServiceScope scope);
            await using (scope)
            {
                batch.TransactionMode = TransactionMode.None;
                BatchResult<T> result = append(batch);
                _ = await batch.ExecuteAsync(Token);
                return result.Value;
            }
        }

        /// <summary>Runs statements in one transaction of a persist batch of the system scope.</summary>
        /// <param name="append">Appends the statements.</param>
        /// <returns>A task that completes when the batch committed.</returns>
        private async Task PersistAsync(Action<DataBatch> append)
        {
            DataBatch batch = Batch(BatchPurpose.Persist, out AsyncServiceScope scope);
            await using (scope)
            {
                append(batch);
                _ = await batch.ExecuteAsync(Token);
            }
        }

        /// <summary>A batch of a fresh system scope.</summary>
        /// <param name="purpose">The batch's purpose.</param>
        /// <param name="scope">The scope; the caller disposes it.</param>
        /// <returns>The batch.</returns>
        private DataBatch Batch(BatchPurpose purpose, out AsyncServiceScope scope)
        {
            scope = fixture.Tenant.CreateScope(WellKnownIds.SystemUserId);
            return (DataBatch)scope.ServiceProvider.GetRequiredService<ITenantDatabase>().CreateBatch(purpose);
        }
    }
}
