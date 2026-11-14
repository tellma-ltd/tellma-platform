// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Microsoft.Data.SqlClient.Server;
using Microsoft.Extensions.DependencyInjection;
using System.Diagnostics;
using Tellma.Core.Abstractions.Data;
using Tellma.Core.Abstractions.Jobs;
using Tellma.Core.Abstractions.Tenancy;
using Tellma.Core.Abstractions.Validation;
using Tellma.Core.Calendars;
using Tellma.Core.Data.Batches;
using Tellma.Core.Jobs;
using Tellma.Core.Queryex;
using Tellma.Core.Tests.Data.Batches;

namespace Tellma.Core.Tests.Jobs
{
    /// <summary>
    ///     The job queue with no database: the enqueue statement it appends and the golden text of
    ///     that statement, the rows it binds (run-as defaulting, confirmed keys, trace parent, due
    ///     time, arguments and label), every programming error it refuses before appending anything,
    ///     and the post-commit wake of the batch's tenant.
    /// </summary>
    public sealed class JobQueueTests : IDisposable
    {
        /// <summary>The scope's user in a user scope.</summary>
        private const int UserId = 7;

        /// <summary>The tenant the harness's connection reports.</summary>
        private const int TenantId = 1;

        /// <summary>The job tests' model and handlers.</summary>
        private readonly BatchHarness _harness = new(compose: JobTestModel.Compose);

        /// <summary>A user scope's context.</summary>
        private static RequestContext UserScope { get; } = RequestContext.Tenantless(new GregorianCalendarSystem(), DateTimeOffset.UnixEpoch) with
        {
            Kind = PrincipalKind.User,
            UserId = UserId,
        };

        /// <summary>A system scope's context.</summary>
        private static RequestContext SystemScope { get; } = RequestContext.Tenantless(new GregorianCalendarSystem(), DateTimeOffset.UnixEpoch) with
        {
            Kind = PrincipalKind.System,
        };

        // --- The statement and its golden text ---

        [Fact]
        public void An_arguments_only_enqueue_appends_one_statement_that_writes_the_job_table()
        {
            DataBatch batch = _harness.Batch(BatchPurpose.Persist);

            BatchResult<IReadOnlyList<int>> result = Queue(UserScope).Enqueue(batch, [new JobRequest("acme.recompute-balances", null, null, null, null, null)]);

            Assert.False(result.IsCompleted);
            BatchStatement statement = Assert.Single(batch.Statements);
            Assert.Equal(
                """
                IF @@TRANCOUNT = 0 THROW 50422, N'Jobs.EnqueueOutsideTransaction', 1;
                DECLARE @tb0_first sql_variant, @tb0_n int = (SELECT COUNT(*) FROM @tb0_t0), @tb0_aff int;
                DECLARE @tb0_jobIds TABLE ([Ordinal] int NOT NULL PRIMARY KEY, [JobId] int NOT NULL);
                IF @tb0_n > 0 EXEC sys.sp_sequence_get_range @sequence_name = N'core.sq_Jobs', @range_size = @tb0_n, @range_first_value = @tb0_first OUTPUT;
                INSERT INTO @tb0_jobIds ([Ordinal], [JobId]) SELECT [r].[Ordinal], CAST(@tb0_first AS int) + [r].[Ordinal] FROM @tb0_t0 AS [r];
                INSERT INTO [core].[Jobs] ([Id], [HandlerKey], [LabelKey], [LabelArgumentsJson], [Status], [DueAt], [Attempts], [ArgumentsJson], [TraceParent], [ConfirmedKeys], [RunAsUserId], [RequestedById], [CreatedAt])
                SELECT [i].[JobId], [r].[HandlerKey], [r].[LabelKey], [r].[LabelArgumentsJson], 'Pending', COALESCE([r].[DueAt], SYSUTCDATETIME()), 0, [r].[ArgumentsJson], [r].[TraceParent], [r].[ConfirmedKeys], [r].[RunAsUserId], [r].[RequestedById], SYSUTCDATETIME()
                FROM @tb0_t0 AS [r] INNER JOIN @tb0_jobIds AS [i] ON [i].[Ordinal] = [r].[Ordinal];
                SELECT [Ordinal], [JobId] FROM @tb0_jobIds ORDER BY [Ordinal];
                """,
                statement.Text,
                ignoreLineEndingDifferences: true);
            Assert.Equal([new TableName("core", "Jobs")], statement.Writes);
            Assert.False(statement.MayRunTwice);
            Assert.NotNull(statement.Reader);
        }

        [Fact]
        public void An_entity_enqueue_also_sets_every_owners_job_in_one_update_and_writes_the_owner_table()
        {
            DataBatch batch = _harness.Batch(BatchPurpose.Persist);

            _ = Queue(UserScope).Enqueue(batch, [Ship(42), Ship(43)]);

            BatchStatement statement = Assert.Single(batch.Statements);
            Assert.EndsWith(
                """
                FROM @tb0_t0 AS [r] INNER JOIN @tb0_jobIds AS [i] ON [i].[Ordinal] = [r].[Ordinal];
                UPDATE [o] SET [JobId] = [i].[JobId]
                FROM [jobs].[Shipments] AS [o]
                INNER JOIN @tb0_t0 AS [r] ON [r].[OwnerId] = [o].[Id]
                INNER JOIN @tb0_jobIds AS [i] ON [i].[Ordinal] = [r].[Ordinal]
                WHERE [o].[JobId] IS NULL OR NOT EXISTS (SELECT 1 FROM [core].[Jobs] AS [a] WITH (READCOMMITTEDLOCK) WHERE [a].[Id] = [o].[JobId] AND [a].[Status] IN ('Pending', 'Running', 'Held', 'PendingConfirmation'));
                SET @tb0_aff = @@ROWCOUNT;
                IF @tb0_aff <> (SELECT COUNT(*) FROM @tb0_t0 WHERE [OwnerId] IS NOT NULL) THROW 50422, N'Jobs.OwnerUnavailable', 1;
                SELECT [Ordinal], [JobId] FROM @tb0_jobIds ORDER BY [Ordinal];
                """.ReplaceLineEndings("\n"),
                statement.Text.ReplaceLineEndings("\n"),
                StringComparison.Ordinal);
            Assert.Equal([new TableName("core", "Jobs"), new TableName("jobs", "Shipments")], statement.Writes.OrderBy(static table => table.Delimited, StringComparer.Ordinal));
            Assert.Equal([42L, 43L], Rows(statement).Select(static row => row["OwnerId"]));
        }

        [Fact]
        public void The_requests_bind_as_one_table_in_request_order()
        {
            DataBatch batch = _harness.Batch(BatchPurpose.Persist);

            _ = Queue(UserScope).Enqueue(batch, [Recompute(), Recompute(), Recompute()]);

            Assert.Equal([0, 1, 2], Rows(Assert.Single(batch.Statements)).Select(static row => row["Ordinal"]));
        }

        // --- What each row carries ---

        [Fact]
        public void A_user_scope_runs_the_job_as_its_user_with_its_confirmations_and_trace_parent()
        {
            using ActivitySource source = new("Tellma.Core.Tests.Jobs");
            using ActivityListener listener = new()
            {
                ShouldListenTo = candidate => candidate == source,
                Sample = static (ref _) => ActivitySamplingResult.AllDataAndRecorded,
            };
            ActivitySource.AddActivityListener(listener);
            using Activity activity = source.StartActivity("enqueue")!;
            Assert.Equal(ActivityIdFormat.W3C, activity.IdFormat);
            DataBatch batch = _harness.Batch(BatchPurpose.Persist);
            JobMessage label = new("Jobs_Exporting", new Dictionary<string, object?> { ["entity"] = "Widget" });

            _ = Queue(UserScope, new FixedConfirmations("Sales.Backdated", "Inventory.Negative", "Sales.Backdated")).Enqueue(batch,
            [
                new JobRequest("acme.recompute-balances", new { Depth = 3 }, new DateTimeOffset(2026, 4, 1, 9, 0, 0, TimeSpan.FromHours(3)), 12, null, null, label),
            ]);

            IReadOnlyDictionary<string, object?> row = Assert.Single(Rows(Assert.Single(batch.Statements)));
            Assert.Equal("acme.recompute-balances", row["HandlerKey"]);
            Assert.Equal(UserId, row["RunAsUserId"]);
            Assert.Equal(12, row["RequestedById"]);
            Assert.Equal("Inventory.Negative,Sales.Backdated", row["ConfirmedKeys"]);
            Assert.Equal(activity.Id, row["TraceParent"]);
            Assert.Equal(new DateTimeOffset(2026, 4, 1, 6, 0, 0, TimeSpan.Zero), row["DueAt"]);
            Assert.Equal(TimeSpan.Zero, ((DateTimeOffset)row["DueAt"]!).Offset);
            Assert.Equal(/*lang=json,strict*/ """{"depth":3}""", row["ArgumentsJson"]);
            Assert.Equal("Jobs_Exporting", row["LabelKey"]);
            Assert.Equal(/*lang=json,strict*/ """{"entity":"Widget"}""", row["LabelArgumentsJson"]);
            Assert.Null(row["OwnerId"]);
        }

        [Fact]
        public void A_system_scope_runs_the_job_as_the_system_user_and_stores_no_confirmations()
        {
            DataBatch batch = _harness.Batch(BatchPurpose.Persist);

            _ = Queue(SystemScope, new FixedConfirmations("Sales.Backdated")).Enqueue(batch, [Recompute()]);

            IReadOnlyDictionary<string, object?> row = Assert.Single(Rows(Assert.Single(batch.Statements)));
            Assert.Equal(1, row["RunAsUserId"]);
            Assert.Null(row["ConfirmedKeys"]);
            Assert.Null(row["DueAt"]);
            Assert.Null(row["ArgumentsJson"]);
            Assert.Null(row["LabelKey"]);
        }

        [Fact]
        public void A_named_run_as_user_wins_over_the_scope()
        {
            DataBatch batch = _harness.Batch(BatchPurpose.Persist);

            _ = Queue(SystemScope).Enqueue(batch, [new JobRequest("acme.recompute-balances", null, null, null, 31, null)]);

            Assert.Equal(31, Assert.Single(Rows(Assert.Single(batch.Statements)))["RunAsUserId"]);
        }

        [Fact]
        public void A_scope_without_confirmations_stores_none()
        {
            DataBatch batch = _harness.Batch(BatchPurpose.Persist);

            _ = Queue(UserScope).Enqueue(batch, [Recompute()]);

            Assert.Null(Assert.Single(Rows(Assert.Single(batch.Statements)))["ConfirmedKeys"]);
        }

        // --- The wake ---

        [Fact]
        public void Once_the_batch_commits_the_batchs_tenant_is_woken_and_no_other()
        {
            TenantPollerWake wake = _harness.Services.GetRequiredService<TenantPollerWake>();
            int woken = 0;
            int other = 0;
            using IDisposable subscription = wake.Subscribe(TenantId, () => woken++);
            using IDisposable elsewhere = wake.Subscribe(TenantId + 1, () => other++);
            DataBatch batch = _harness.Batch(BatchPurpose.Persist);

            _ = Queue(UserScope).Enqueue(batch, [Recompute()]);
            Assert.Equal(0, woken);

            // The callback reads nothing of the outcome; the statement never ran, so no id is set.
            Assert.Single(batch.CommittedCallbacks)(null!);
            Assert.Equal((1, 0), (woken, other));
        }

        // --- Programming errors, each refused before anything is appended ---

        [Fact]
        public void A_key_no_handler_registers_is_refused_naming_it()
        {
            AssertRefused<InvalidOperationException>(UserScope, [new JobRequest("acme.unknown", null, null, null, null, null)], "No job handler on this instance registers the key 'acme.unknown'.");
        }

        [Fact]
        public void A_scope_with_no_user_to_default_to_is_refused()
        {
            var anonymous = RequestContext.Tenantless(new GregorianCalendarSystem(), DateTimeOffset.UnixEpoch);

            AssertRefused<InvalidOperationException>(anonymous, [Recompute()], "names no user to run as, and the scope has no user to default to");
        }

        [Theory]
        [InlineData(0L)]
        [InlineData(-3L)]
        public void A_row_with_a_temporary_id_is_refused(long id)
        {
            AssertRefused<InvalidOperationException>(UserScope, [Ship(id)], "has a temporary id");
        }

        [Fact]
        public void A_row_whose_member_names_a_job_is_left_to_the_statement()
        {
            // The member may name the row's ended job, as a retry's row does; the stored row decides.
            DataBatch batch = _harness.Batch(BatchPurpose.Persist);
            Shipment shipment = new() { Id = 42, JobId = 1001 };

            _ = Queue(UserScope).Enqueue(batch, [new JobRequest("acme.ship", null, null, null, null, shipment)]);

            Assert.Equal([42L], Rows(Assert.Single(batch.Statements)).Select(static row => row["OwnerId"]));
            Assert.Equal(1001, shipment.JobId);
        }

        [Fact]
        public void A_row_named_twice_is_refused()
        {
            AssertRefused<InvalidOperationException>(UserScope, [Ship(42), Ship(42)], "is named by more than one request");
        }

        [Fact]
        public void Rows_of_two_entity_types_in_one_call_are_refused()
        {
            AssertRefused<InvalidOperationException>(
                UserScope,
                [Ship(42), new JobRequest("acme.inspect", null, null, null, null, new Inspection { Id = 5 })],
                "one call enqueues the rows of one entity type, so enqueue each type in its own call");
        }

        [Fact]
        public void An_entity_handler_without_a_row_is_refused()
        {
            AssertRefused<InvalidOperationException>(UserScope, [new JobRequest("acme.ship", null, null, null, null, null)], "each of its requests names the row as its Entity");
        }

        [Fact]
        public void An_arguments_only_handler_given_a_row_is_refused()
        {
            AssertRefused<InvalidOperationException>(UserScope, [new JobRequest("acme.recompute-balances", null, null, null, null, new Shipment { Id = 42 })], "processes no rows");
        }

        [Fact]
        public void A_row_of_another_entity_than_the_handlers_is_refused()
        {
            AssertRefused<InvalidOperationException>(UserScope, [new JobRequest("acme.ship", null, null, null, null, new Inspection { Id = 5 })], "not the");
        }

        [Fact]
        public void Arguments_over_64_KB_are_refused()
        {
            ArgumentException exception = AssertRefused<ArgumentException>(
                UserScope,
                [new JobRequest("acme.recompute-balances", new { Text = new string('x', JobLimits.MaxHandlerArgumentsBytes) }, null, null, null, null)],
                $"a job carries at most {JobLimits.MaxHandlerArgumentsBytes}");
            Assert.Equal("requests", exception.ParamName);
        }

        [Fact]
        public void A_label_too_wide_for_its_column_is_refused()
        {
            JobMessage label = new("Jobs_Exporting", new Dictionary<string, object?> { ["text"] = new string('x', JobLimits.MaxArgumentsLength) });

            ArgumentException exception = AssertRefused<ArgumentException>(UserScope, [new JobRequest("acme.recompute-balances", null, null, null, null, null, label)], "a job stores at most 4000");
            Assert.Equal("requests", exception.ParamName);
        }

        [Fact]
        public void Confirmations_too_wide_for_their_column_are_refused()
        {
            string[] keys = [.. Enumerable.Range(0, 100).Select(static index => $"Sales.Confirmation{index:D3}")];

            AssertRefused<InvalidOperationException>(UserScope, [Recompute()], "a job carries at most 2000", new FixedConfirmations(keys));
        }

        [Theory]
        [InlineData(BatchPurpose.Read)]
        [InlineData(BatchPurpose.Validate)]
        public void A_batch_that_does_not_write_is_refused(BatchPurpose purpose)
        {
            DataBatch batch = _harness.Batch(purpose);

            ArgumentException exception = Assert.Throws<ArgumentException>(() => Queue(UserScope).Enqueue(batch, [Recompute()]));

            Assert.Contains($"A {purpose} batch does not write", exception.Message, StringComparison.Ordinal);
            Assert.Empty(batch.Statements);
        }

        [Fact]
        public void A_maintenance_batch_may_enqueue()
        {
            DataBatch batch = _harness.Batch(BatchPurpose.Maintenance);

            _ = Queue(SystemScope).Enqueue(batch, [Recompute()]);

            Assert.Single(batch.Statements);
        }

        [Theory]
        [InlineData(BatchPurpose.Persist)]
        [InlineData(BatchPurpose.Maintenance)]
        public void A_batch_whose_statements_autocommit_is_refused(BatchPurpose purpose)
        {
            DataBatch batch = _harness.Batch(purpose);
            batch.TransactionMode = TransactionMode.None;

            ArgumentException exception = Assert.Throws<ArgumentException>(() => Queue(SystemScope).Enqueue(batch, [Recompute()]));

            Assert.Equal("batch", exception.ParamName);
            Assert.Contains("runs in None mode", exception.Message, StringComparison.Ordinal);
            Assert.Empty(batch.Statements);
            Assert.Empty(batch.CommittedCallbacks);
        }

        [Fact]
        public void A_batch_in_an_explicit_transaction_may_enqueue()
        {
            DataBatch batch = _harness.Batch(BatchPurpose.Persist);
            batch.TransactionMode = TransactionMode.Explicit;

            _ = Queue(UserScope).Enqueue(batch, [Recompute()]);

            Assert.Single(batch.Statements);
        }

        public void Dispose()
        {
            _harness.Dispose();
        }

        /// <summary>A request for the arguments-only handler.</summary>
        private static JobRequest Recompute()
        {
            return new JobRequest("acme.recompute-balances", null, null, null, null, null);
        }

        /// <summary>A request for the shipments' handler over one saved shipment.</summary>
        /// <param name="id">The shipment's id.</param>
        private static JobRequest Ship(long id)
        {
            return new JobRequest("acme.ship", null, null, null, null, new Shipment { Id = id });
        }

        /// <summary>Asserts the queue refuses the requests and leaves the batch empty and without a callback.</summary>
        /// <typeparam name="TException">The exception expected.</typeparam>
        /// <param name="context">The scope's context.</param>
        /// <param name="requests">The requests.</param>
        /// <param name="message">A fragment of the expected message.</param>
        /// <param name="confirmations">The scope's confirmations; none when null.</param>
        /// <returns>The exception.</returns>
        private TException AssertRefused<TException>(RequestContext context, IReadOnlyList<JobRequest> requests, string message, IConfirmations? confirmations = null)
            where TException : Exception
        {
            DataBatch batch = _harness.Batch(BatchPurpose.Persist);

            TException exception = Assert.Throws<TException>(() => Queue(context, confirmations).Enqueue(batch, requests));

            Assert.Contains(message, exception.Message, StringComparison.Ordinal);
            Assert.Empty(batch.Statements);
            Assert.Empty(batch.CommittedCallbacks);
            return exception;
        }

        /// <summary>A queue over the harness's composition for one scope.</summary>
        /// <param name="context">The scope's context.</param>
        /// <param name="confirmations">The scope's confirmations; none when null.</param>
        private JobQueue Queue(RequestContext context, IConfirmations? confirmations = null)
        {
            return new JobQueue(
                new UnusedDatabase(),
                new FixedContext(context),
                _harness.Services.GetRequiredService<JobHandlerRegistry>(),
                _harness.Services.GetRequiredService<IEntityMetadataProvider>(),
                _harness.Services.GetRequiredService<TenantPollerWake>(),
                confirmations);
        }

        /// <summary>The rows the statement's one table parameter binds, each copied column by column.</summary>
        /// <param name="statement">The statement.</param>
        private static List<IReadOnlyDictionary<string, object?>> Rows(BatchStatement statement)
        {
            TableParameter table = Assert.IsType<TableParameter>(Assert.Single(statement.Parameters));
            List<IReadOnlyDictionary<string, object?>> rows = [];

            // The binder streams one record it refills per row, so each is copied as it passes.
            foreach (SqlDataRecord record in (IEnumerable<SqlDataRecord>)table.Create().Value)
            {
                Dictionary<string, object?> row = new(StringComparer.Ordinal);
                for (int ordinal = 0; ordinal < record.FieldCount; ordinal++)
                {
                    row[record.GetName(ordinal)] = record.IsDBNull(ordinal) ? null : record.GetValue(ordinal);
                }

                rows.Add(row);
            }

            return rows;
        }

        /// <summary>A request context accessor that answers one fixed context.</summary>
        /// <param name="context">The context.</param>
        private sealed class FixedContext(RequestContext context) : IRequestContextAccessor
        {
            public RequestContext Current => context;
        }

        /// <summary>The confirmations a scope's entry point seeded.</summary>
        /// <param name="keys">The confirmed keys.</param>
        private sealed class FixedConfirmations(params string[] keys) : IConfirmations
        {
            public IReadOnlyCollection<string> Confirmed => keys;

            public bool IsConfirmed(string key)
            {
                return keys.Contains(key);
            }

            public void Require(string key, IReadOnlyList<long>? rows = null, IReadOnlyDictionary<string, object?>? arguments = null)
            {
                throw new NotSupportedException();
            }
        }

        /// <summary>A tenant database the tests never reach: every enqueue here rides a batch the test holds.</summary>
        private sealed class UnusedDatabase : ITenantDatabase
        {
            public QueryexSchema Schema => throw new NotSupportedException();

            public QueryexContextValues Context => throw new NotSupportedException();

            public IDataBatch CreateBatch(BatchPurpose purpose)
            {
                throw new NotSupportedException();
            }
        }
    }
}
