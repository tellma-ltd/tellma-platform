// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Microsoft.Data.SqlClient;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.Metrics.Testing;
using System.Diagnostics;
using System.Diagnostics.Metrics;
using System.Globalization;
using System.Text.Json;
using Tellma.Core.Abstractions.Access;
using Tellma.Core.Abstractions.Api;
using Tellma.Core.Abstractions.Data;
using Tellma.Core.Abstractions.Jobs;
using Tellma.Core.Abstractions.Notifications;
using Tellma.Core.Abstractions.Realtime;
using Tellma.Core.Composition;
using Tellma.Core.Data;
using Tellma.Core.Jobs;
using Tellma.Core.Notifications;
using Tellma.Testing.Entities;
using static Tellma.Core.IntegrationTests.Notifications.NotificationsDatabase;

namespace Tellma.Core.IntegrationTests.Notifications
{
    /// <summary>
    ///     The notifier's statements against a fixture database: the inbox rows a request writes and
    ///     the recipients it skips, the duplicate policies, the withdrawals, the delivery jobs a
    ///     request enqueues per qualifying slice, the event and the nudge after the commit, and a
    ///     failed batch that leaves no trace. Every batch's writes are audited against change
    ///     tracking. The tests run once with read-committed snapshot on and once with it off.
    /// </summary>
    /// <param name="fixture">The class's database, reset before each test.</param>
    public abstract class NotifierStatementTests(NotificationsDatabase fixture) : IAsyncLifetime
    {
        /// <summary>A dedup key, in the record-activity form.</summary>
        private const string ShipmentKey = "fixture.Shipment:42";

        /// <summary>Another dedup key.</summary>
        private const string OtherKey = "fixture.Shipment:43";

        /// <summary>The shipment the targeted requests point at.</summary>
        private const long ShipmentId = 42;

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
        public async Task The_database_reads_committed_rows_as_the_class_requires()
        {
            Assert.Equal(
                fixture.ReadCommittedSnapshot,
                await SqlServerFixture.ScalarAsync<bool>(fixture.ConnectionString, "SELECT [is_read_committed_snapshot_on] FROM sys.databases WHERE [name] = DB_NAME();"));
        }

        [Fact]
        public async Task A_request_writes_one_row_per_distinct_recipient_with_ids_from_one_range_in_recipient_order()
        {
            using MetricCollector<long> created = Collector(NotificationsTelemetryNames.Created);

            await NotifyAsync(Held(104, 102, 103, 102) with { ActorUserId = 101, Arguments = new { Number = "SO-1" } });

            // --- One row per distinct recipient, the ids contiguous in first-seen order ---
            IReadOnlyList<Row> rows = await RowsAsync();
            Assert.Equal([104, 102, 103], rows.Select(static row => row.UserId));
            Assert.Equal(Enumerable.Range(rows[0].Id, 3), rows.Select(static row => row.Id));

            // --- The columns, as the request and the type name them ---
            Assert.All(rows, static row =>
            {
                Assert.Equal(FixtureNotifications.Held, row.Type);
                Assert.Equal(nameof(NotificationSeverity.Warning), row.Severity);
                Assert.Equal(/*lang=json,strict*/ """{"number":"SO-1"}""", row.ArgumentsJson);
                Assert.Null(row.TargetResource);
                Assert.Null(row.TargetId);
                Assert.Equal(101, row.ActorUserId);
                Assert.Null(row.DedupKey);
                Assert.Null(row.ReadAt);
            });

            // --- What the statement output: the inserted users, counted and announced once ---
            Assert.Equal(3, Sum(created));
            Assert.Equal([102, 103, 104], Assert.Single(InboxEvents()).UserIds);
        }

        [Fact]
        public async Task An_inactive_or_unknown_recipient_receives_nothing_and_leaves_a_gap_in_the_range()
        {
            using MetricCollector<long> suppressed = Collector(NotificationsTelemetryNames.Suppressed);

            await NotifyAsync(Held(101, InactiveUserId, UnknownUserId, 102));

            // The range covers every recipient, so the skipped ones leave their ids unused.
            IReadOnlyList<Row> rows = await RowsAsync();
            Assert.Equal([101, 102], rows.Select(static row => row.UserId));
            Assert.Equal(rows[0].Id + 3, rows[1].Id);
            Assert.Equal(2, Sum(suppressed));
            Assert.Equal([101, 102], Assert.Single(InboxEvents()).UserIds);
        }

        [Fact]
        public async Task A_muted_mutable_type_is_skipped_while_a_non_mutable_type_ignores_the_mute_and_another_channel_is_never_read()
        {
            // A non-mutable type cannot be muted through the preferences endpoint; the row is stored
            // directly, as an older composition might have left it.
            await PreferAsync(
                (101, FixtureNotifications.Shipped, CoreFeature.InboxChannel, false),
                (101, FixtureNotifications.Held, CoreFeature.InboxChannel, false),
                (102, FixtureNotifications.Shipped, CoreFeature.EmailChannel, false));

            await NotifyAsync(Shipped(101, 102) with { InboxOnly = true }, Held(101, 102));

            IReadOnlyList<Row> rows = await RowsAsync();
            Assert.Equal([102], rows.Where(static row => row.Type == FixtureNotifications.Shipped).Select(static row => row.UserId));
            Assert.Equal([101, 102], rows.Where(static row => row.Type == FixtureNotifications.Held).Select(static row => row.UserId));
        }

        [Fact]
        public async Task The_severity_is_the_types_unless_the_request_overrides_it()
        {
            await NotifyAsync(Shipped(101), Shipped(102) with { Severity = NotificationSeverity.Error });

            IReadOnlyList<Row> rows = await RowsAsync();
            Assert.Equal([nameof(NotificationSeverity.Info), nameof(NotificationSeverity.Error)], rows.Select(static row => row.Severity));
        }

        [Fact]
        public async Task A_duplicate_is_suppressed_against_an_unread_row_of_any_type_and_not_against_a_read_one()
        {
            await NotifyAsync(Held(101, 102) with { DedupKey = ShipmentKey });
            await MarkReadAsync(101);
            fixture.Events.Clear();
            using MetricCollector<long> created = Collector(NotificationsTelemetryNames.Created);
            using MetricCollector<long> suppressed = Collector(NotificationsTelemetryNames.Suppressed);

            // The key matches whatever the type: the shipped request collides with the held rows.
            await NotifyAsync(Shipped(101, 102) with { DedupKey = ShipmentKey });

            IReadOnlyList<Row> rows = await RowsAsync();
            Assert.Equal([101, 102, 101], rows.Select(static row => row.UserId));
            Assert.Equal(FixtureNotifications.Shipped, rows[2].Type);
            Assert.Equal((1, 1), (Sum(created), Sum(suppressed)));
            Assert.Equal([101], Assert.Single(InboxEvents()).UserIds);
        }

        [Fact]
        public async Task An_ongoing_condition_is_suppressed_against_a_read_row_too()
        {
            await NotifyAsync(Held(101, 102) with { DedupKey = ShipmentKey, OnDuplicate = DuplicatePolicy.Ongoing });
            await MarkReadAsync(101);
            fixture.Events.Clear();

            await NotifyAsync(Held(101, 102, 103) with { DedupKey = ShipmentKey, OnDuplicate = DuplicatePolicy.Ongoing });

            Assert.Equal([101, 102, 103], (await RowsAsync()).Select(static row => row.UserId));
            Assert.Equal([103], Assert.Single(InboxEvents()).UserIds);
        }

        [Fact]
        public async Task Replace_refreshes_the_unread_duplicate_in_place_and_inserts_for_the_rest()
        {
            // --- Three unread rows of the key, one of them then read, all created long ago ---
            await NotifyAsync(Held(101, 102, 103) with { DedupKey = ShipmentKey, Arguments = new { N = 1 } });
            await MarkReadAsync(102);
            DateTimeOffset longAgo = new(2020, 1, 1, 0, 0, 0, TimeSpan.Zero);
            await SqlServerFixture.ExecuteAsync(fixture.ConnectionString, "UPDATE [core].[Notifications] SET [CreatedAt] = '2020-01-01T00:00:00+00:00';");
            IReadOnlyList<Row> before = await RowsAsync();
            fixture.Events.Clear();
            using MetricCollector<long> replaced = Collector(NotificationsTelemetryNames.Replaced);
            using MetricCollector<long> created = Collector(NotificationsTelemetryNames.Created);

            await NotifyAsync(Shipped(101, 102, 104) with
            {
                DedupKey = ShipmentKey,
                OnDuplicate = DuplicatePolicy.Replace,
                Arguments = new { N = 2 },
                ActorUserId = 105,
            });

            // --- 101's unread row became the request's, keeping its id, unread and on top ---
            IReadOnlyList<Row> rows = await RowsAsync();
            Row refreshed = Assert.Single(rows, static row => row.UserId == 101);
            Assert.Equal(before.Single(static row => row.UserId == 101).Id, refreshed.Id);
            Assert.Equal(
                (FixtureNotifications.Shipped, nameof(NotificationSeverity.Info), /*lang=json,strict*/ """{"n":2}""", FixtureNotifications.ShipmentResource, ShipmentId, 105, null),
                (refreshed.Type, refreshed.Severity, refreshed.ArgumentsJson, refreshed.TargetResource, refreshed.TargetId, refreshed.ActorUserId, refreshed.ReadAt));
            Assert.True(refreshed.CreatedAt > longAgo);

            // --- 102's read row stays as it was beside a new one; 103 is no recipient; 104 is new ---
            Assert.Equal(before.Single(static row => row.UserId == 102), rows.First(static row => row.UserId == 102));
            Assert.Equal(FixtureNotifications.Shipped, rows.Last(static row => row.UserId == 102).Type);
            Assert.Equal(before.Single(static row => row.UserId == 103), Assert.Single(rows, static row => row.UserId == 103));
            Assert.Single(rows, static row => row.UserId == 104);

            // --- Both result sets were read: one refresh, two inserts, one event to all three ---
            Assert.Equal((1, 2), (Sum(replaced), Sum(created)));
            Assert.Equal([101, 102, 104], Assert.Single(InboxEvents()).UserIds);
        }

        [Fact]
        public async Task A_withdrawal_for_everyone_deletes_the_read_and_unread_rows_of_its_key_and_tells_exactly_those_users()
        {
            await NotifyAsync(Held(101, 102, 103) with { DedupKey = ShipmentKey }, Held(104) with { DedupKey = OtherKey });
            await MarkReadAsync(102);
            fixture.Events.Clear();
            using MetricCollector<long> withdrawn = Collector(NotificationsTelemetryNames.Withdrawn);

            await WithdrawAsync(new WithdrawRequest(ShipmentKey));

            Assert.Equal([104], (await RowsAsync()).Select(static row => row.UserId));
            Assert.Equal(3, Sum(withdrawn));
            Assert.Equal([101, 102, 103], Assert.Single(InboxEvents()).UserIds);
        }

        [Fact]
        public async Task A_withdrawal_for_named_recipients_deletes_only_their_rows_and_tells_only_those_who_had_one()
        {
            await NotifyAsync(Held(101, 102, 103) with { DedupKey = ShipmentKey });
            fixture.Events.Clear();

            await WithdrawAsync(new WithdrawRequest(ShipmentKey, [102, 103, 105]));

            Assert.Equal([101], (await RowsAsync()).Select(static row => row.UserId));
            Assert.Equal([102, 103], Assert.Single(InboxEvents()).UserIds);
        }

        [Fact]
        public async Task A_withdrawal_of_an_absent_key_deletes_nothing_and_tells_no_one()
        {
            await NotifyAsync(Held(101) with { DedupKey = ShipmentKey });
            fixture.Events.Clear();

            await WithdrawAsync(new WithdrawRequest(OtherKey));

            Assert.Single(await RowsAsync());
            Assert.Empty(InboxEvents());
        }

        [Fact]
        public async Task Requests_and_withdrawals_on_one_batch_tell_each_changed_inbox_once()
        {
            await NotifyAsync(Held(101, 102) with { DedupKey = OtherKey });
            fixture.Events.Clear();

            await using AsyncServiceScope scope = fixture.Tenant.CreateScope();
            IDataBatch batch = scope.ServiceProvider.GetRequiredService<ITenantDatabase>().CreateBatch(BatchPurpose.Persist);
            INotifier notifier = scope.ServiceProvider.GetRequiredService<INotifier>();
            notifier.Notify(batch, [Held(102, 103) with { InboxOnly = true }]);
            notifier.Withdraw(batch, [new WithdrawRequest(OtherKey)]);
            notifier.Notify(batch, [Held(103, 104) with { InboxOnly = true }]);
            _ = await batch.ExecuteAsync(Token);

            Assert.Equal([102, 103, 103, 104], (await RowsAsync()).Select(static row => row.UserId));
            Assert.Equal([101, 102, 103, 104], Assert.Single(InboxEvents()).UserIds);
        }

        [Fact]
        public async Task One_delivery_job_is_enqueued_per_slice_with_an_active_recipient_who_receives_a_channel()
        {
            // --- The slices of two: [101, 102] qualify by the sms default; [103, 104] turned sms
            // off and never turned email on; [inactive, unknown] has no active recipient; [105] qualifies ---
            await PreferAsync(
                (103, FixtureNotifications.Shipped, FixtureNotifications.SmsChannel, false),
                (104, FixtureNotifications.Shipped, FixtureNotifications.SmsChannel, false));
            using MetricCollector<long> enqueued = Collector(NotificationsTelemetryNames.DeliveryEnqueued);
            using Activity activity = new Activity("fixture.notify").SetIdFormat(ActivityIdFormat.W3C).Start();

            await NotifyAsync(Shipped(101, 102, 103, 104, InactiveUserId, UnknownUserId, 105) with { ActorUserId = 101, Arguments = new { Number = "SO-42" } });
            activity.Stop();

            // --- Two delivery jobs, their ids contiguous in slice order, each carrying its slice ---
            IReadOnlyList<JobRow> jobs = await JobsAsync();
            Assert.Equal(2, jobs.Count);
            Assert.Equal(jobs[0].Id + 1, jobs[1].Id);
            Assert.All(jobs, row =>
            {
                Assert.Equal(NotificationStatements.DeliveryHandlerKey, row.HandlerKey);
                Assert.Equal(nameof(JobStatus.Pending), row.Status);
                Assert.Equal(WellKnownIds.SystemUserId, row.RunAsUserId);
                Assert.Null(row.RequestedById);
                Assert.Equal(activity.Id, row.TraceParent);
            });

            NotificationDeliveryArguments first = Delivery(jobs[0]);
            Assert.Equal(
                (FixtureNotifications.Shipped, NotificationSeverity.Info, /*lang=json,strict*/ """{"number":"SO-42"}""", FixtureNotifications.ShipmentResource, ShipmentId, 101),
                (first.Type, first.Severity, first.ArgumentsJson, first.TargetResource, first.TargetId, first.ActorUserId));
            Assert.Equal([101, 102], first.RecipientIds);
            Assert.Equal([105], Delivery(jobs[1]).RecipientIds);

            // --- Counted, and the poller nudged once after the commit ---
            Assert.Equal(2, Sum(enqueued));
            Assert.Equal(1, fixture.Wakes);
        }

        [Fact]
        public async Task A_recipient_who_turned_on_a_channel_that_is_off_by_default_qualifies_their_slice()
        {
            await PreferAsync(
                (101, FixtureNotifications.Held, FixtureNotifications.SmsChannel, false),
                (101, FixtureNotifications.Held, CoreFeature.EmailChannel, true),
                (102, FixtureNotifications.Held, FixtureNotifications.SmsChannel, false));

            await NotifyAsync(Held(101, 102));

            Assert.Equal([101, 102], Delivery(Assert.Single(await JobsAsync())).RecipientIds);
        }

        [Fact]
        public async Task No_job_is_enqueued_and_no_job_id_is_taken_when_no_active_recipient_receives_a_channel()
        {
            await PreferAsync(
                (101, FixtureNotifications.Held, FixtureNotifications.SmsChannel, false),
                (102, FixtureNotifications.Held, FixtureNotifications.SmsChannel, false));
            long sequence = await JobSequenceAsync();

            await NotifyAsync(Held(101, 102, InactiveUserId));

            // The inbox rows are written; the delivery takes nothing.
            Assert.Equal([101, 102], (await RowsAsync()).Select(static row => row.UserId));
            Assert.Empty(await JobsAsync());
            Assert.Equal(sequence, await JobSequenceAsync());
            Assert.Equal(0, fixture.Wakes);
        }

        [Fact]
        public async Task An_inbox_only_request_enqueues_no_job_and_takes_no_job_id()
        {
            long sequence = await JobSequenceAsync();

            await NotifyAsync(Held(101, 102, 103) with { InboxOnly = true });

            Assert.Equal(3, (await RowsAsync()).Count);
            Assert.Empty(await JobsAsync());
            Assert.Equal(sequence, await JobSequenceAsync());
            Assert.Equal(0, fixture.Wakes);
        }

        [Fact]
        public async Task A_channel_only_type_enqueues_its_delivery_alone_and_tells_no_inbox()
        {
            long rowSequence = await SequenceAsync(NotificationStatements.NotificationsSequence);

            await NotifyAsync(new NotificationRequest(FixtureNotifications.Texted, [101, 102, 103], null, null, null, null, null));

            // --- No inbox row and no row id taken; one job per slice; no event, one nudge ---
            Assert.Empty(await RowsAsync());
            Assert.Equal(rowSequence, await SequenceAsync(NotificationStatements.NotificationsSequence));
            IReadOnlyList<JobRow> jobs = await JobsAsync();
            Assert.Equal(2, jobs.Count);
            Assert.Equal([101, 102], Delivery(jobs[0]).RecipientIds);
            Assert.Equal([103], Delivery(jobs[1]).RecipientIds);
            Assert.Equal(FixtureNotifications.Texted, Delivery(jobs[0]).Type);
            Assert.Empty(InboxEvents());
            Assert.Equal(1, fixture.Wakes);
        }

        [Fact]
        public async Task A_failed_batch_rolls_the_rows_and_the_jobs_back_and_tells_no_one()
        {
            await using AsyncServiceScope scope = fixture.Tenant.CreateScope();
            IDataBatch batch = scope.ServiceProvider.GetRequiredService<ITenantDatabase>().CreateBatch(BatchPurpose.Persist);
            scope.ServiceProvider.GetRequiredService<INotifier>().Notify(batch, [Held(101, 102)]);
            _ = batch.Sql($"THROW 50600, N'Fixture.AfterTheNotification', 1;", null);

            _ = await Assert.ThrowsAsync<BatchAssertionFailedException>(() => batch.ExecuteAsync(Token));

            Assert.Empty(await RowsAsync());
            Assert.Empty(await JobsAsync());
            Assert.Empty(InboxEvents());
            Assert.Equal(0, fixture.Wakes);
        }

        [Fact]
        public async Task A_request_that_enqueues_on_a_batch_switched_to_autocommit_refuses_to_run()
        {
            // The notifier refuses an autocommit batch; switching the mode afterwards meets the statement's own guard.
            await using AsyncServiceScope scope = fixture.Tenant.CreateScope();
            IDataBatch batch = scope.ServiceProvider.GetRequiredService<ITenantDatabase>().CreateBatch(BatchPurpose.Persist);
            scope.ServiceProvider.GetRequiredService<INotifier>().Notify(batch, [Held(101, 102)]);
            batch.TransactionMode = TransactionMode.None;

            BatchAssertionFailedException exception = await Assert.ThrowsAsync<BatchAssertionFailedException>(() => batch.ExecuteAsync(Token));

            Assert.Equal((TellmaSqlErrors.Invariant, JobEnqueueCodes.OutsideTransaction), (exception.Number, exception.Code));
            Assert.Empty(await RowsAsync());
            Assert.Empty(await JobsAsync());
            Assert.Empty(InboxEvents());
            Assert.Equal(0, fixture.Wakes);
        }

        /// <summary>A request of the non-mutable warning type to some recipients.</summary>
        /// <param name="recipients">The recipients.</param>
        /// <returns>The request.</returns>
        private static NotificationRequest Held(params int[] recipients)
        {
            return new NotificationRequest(FixtureNotifications.Held, recipients, null, null, null, null, null);
        }

        /// <summary>A request of the mutable type to some recipients, targeting the test's shipment.</summary>
        /// <param name="recipients">The recipients.</param>
        /// <returns>The request.</returns>
        private static NotificationRequest Shipped(params int[] recipients)
        {
            return new NotificationRequest(FixtureNotifications.Shipped, recipients, null, FixtureNotifications.ShipmentResource, ShipmentId, null, null);
        }

        /// <summary>The arguments a delivery job carries.</summary>
        /// <param name="job">The job.</param>
        /// <returns>The arguments.</returns>
        private static NotificationDeliveryArguments Delivery(JobRow job)
        {
            Assert.NotNull(job.ArgumentsJson);
            NotificationDeliveryArguments? arguments = JsonSerializer.Deserialize<NotificationDeliveryArguments>(job.ArgumentsJson, TellmaJson.Options);
            Assert.NotNull(arguments);
            return arguments;
        }

        /// <summary>The sum of a counter's measurements.</summary>
        /// <param name="collector">The counter's collector.</param>
        /// <returns>The sum.</returns>
        private static long Sum(MetricCollector<long> collector)
        {
            return collector.GetMeasurementSnapshot().Sum(static measurement => measurement.Value);
        }

        /// <summary>A collector of one of the notifier's counters, on the host's meters only.</summary>
        /// <param name="instrument">The counter's name.</param>
        /// <returns>The collector; the caller disposes it.</returns>
        private MetricCollector<long> Collector(string instrument)
        {
            return new MetricCollector<long>(fixture.Tenant.Services.GetRequiredService<IMeterFactory>(), NotificationsTelemetryNames.MeterName, instrument);
        }

        /// <summary>The <c>inbox.changed</c> events the host published since the last reset or clear.</summary>
        /// <returns>The events, in order.</returns>
        private IReadOnlyList<ClientEvent> InboxEvents()
        {
            return [.. fixture.Events.Published.Where(static published => published.Name == Notifier.InboxChangedEvent)];
        }

        /// <summary>Raises some requests in a persist batch of their own, in a system scope.</summary>
        /// <param name="requests">The requests.</param>
        /// <returns>A task that completes once the batch committed.</returns>
        private async Task NotifyAsync(params NotificationRequest[] requests)
        {
            await using AsyncServiceScope scope = fixture.Tenant.CreateScope();
            await scope.ServiceProvider.GetRequiredService<INotifier>().NotifyAsync(requests, Token);
        }

        /// <summary>Withdraws in a persist batch of its own, in a system scope.</summary>
        /// <param name="requests">The withdrawals.</param>
        /// <returns>A task that completes once the batch committed.</returns>
        private async Task WithdrawAsync(params WithdrawRequest[] requests)
        {
            await using AsyncServiceScope scope = fixture.Tenant.CreateScope();
            await scope.ServiceProvider.GetRequiredService<INotifier>().WithdrawAsync(requests, Token);
        }

        /// <summary>Stores preference rows directly.</summary>
        /// <param name="preferences">The rows: user, type, channel and whether it is on.</param>
        /// <returns>A task that completes when the rows are stored.</returns>
        private Task PreferAsync(params (int UserId, string Type, string Channel, bool Enabled)[] preferences)
        {
            return SqlServerFixture.ExecuteAsync(
                fixture.ConnectionString,
                [.. preferences.Select(static preference => string.Create(
                    CultureInfo.InvariantCulture,
                    $"INSERT INTO [core].[NotificationPreferences] ([UserId], [Type], [Channel], [Enabled]) VALUES ({preference.UserId}, N'{preference.Type}', '{preference.Channel}', {(preference.Enabled ? 1 : 0)});"))]);
        }

        /// <summary>Marks every row of a user read.</summary>
        /// <param name="userId">The user.</param>
        /// <returns>A task that completes when the rows are marked.</returns>
        private Task MarkReadAsync(int userId)
        {
            return SqlServerFixture.ExecuteAsync(
                fixture.ConnectionString,
                string.Create(CultureInfo.InvariantCulture, $"UPDATE [core].[Notifications] SET [ReadAt] = SYSDATETIMEOFFSET() WHERE [UserId] = {userId};"));
        }

        /// <summary>The current value of the job sequence.</summary>
        /// <returns>The value.</returns>
        private Task<long> JobSequenceAsync()
        {
            return SequenceAsync(JobStatements.JobsSequence);
        }

        /// <summary>The current value of a sequence, which moves whenever a range is taken from it.</summary>
        /// <param name="sequence">The sequence, as <c>schema.name</c>.</param>
        /// <returns>The value.</returns>
        private async Task<long> SequenceAsync(string sequence)
        {
            return await SqlServerFixture.ScalarAsync<long>(
                fixture.ConnectionString,
                $"SELECT CAST([current_value] AS bigint) FROM sys.sequences WHERE [object_id] = OBJECT_ID(N'{sequence}');");
        }

        /// <summary>Every inbox row, in id order.</summary>
        /// <returns>The rows.</returns>
        private async Task<IReadOnlyList<Row>> RowsAsync()
        {
            List<Row> rows = [];
            await using SqlConnection connection = new(fixture.ConnectionString);
            await connection.OpenAsync(Token);
            await using SqlCommand command = connection.CreateCommand();
            command.CommandText = "SELECT [Id], [UserId], [Type], [Severity], [ArgumentsJson], [TargetResource], [TargetId], [ActorUserId], [DedupKey], [CreatedAt], [ReadAt] FROM [core].[Notifications] ORDER BY [Id];";
            await using SqlDataReader reader = await command.ExecuteReaderAsync(Token);
            while (await reader.ReadAsync(Token))
            {
                rows.Add(new Row(
                    reader.GetInt32(0),
                    reader.GetInt32(1),
                    reader.GetString(2),
                    reader.GetString(3),
                    reader.IsDBNull(4) ? null : reader.GetString(4),
                    reader.IsDBNull(5) ? null : reader.GetString(5),
                    reader.IsDBNull(6) ? null : reader.GetInt64(6),
                    reader.IsDBNull(7) ? null : reader.GetInt32(7),
                    reader.IsDBNull(8) ? null : reader.GetString(8),
                    reader.GetDateTimeOffset(9),
                    reader.IsDBNull(10) ? null : reader.GetDateTimeOffset(10)));
            }

            return rows;
        }

        /// <summary>Every job row, in id order.</summary>
        /// <returns>The rows.</returns>
        private async Task<IReadOnlyList<JobRow>> JobsAsync()
        {
            List<JobRow> jobs = [];
            await using SqlConnection connection = new(fixture.ConnectionString);
            await connection.OpenAsync(Token);
            await using SqlCommand command = connection.CreateCommand();
            command.CommandText = "SELECT [Id], [HandlerKey], [Status], [ArgumentsJson], [TraceParent], [RunAsUserId], [RequestedById] FROM [core].[Jobs] ORDER BY [Id];";
            await using SqlDataReader reader = await command.ExecuteReaderAsync(Token);
            while (await reader.ReadAsync(Token))
            {
                jobs.Add(new JobRow(
                    reader.GetInt32(0),
                    reader.GetString(1),
                    reader.GetString(2),
                    reader.IsDBNull(3) ? null : reader.GetString(3),
                    reader.IsDBNull(4) ? null : reader.GetString(4),
                    reader.GetInt32(5),
                    reader.IsDBNull(6) ? null : reader.GetInt32(6)));
            }

            return jobs;
        }

        /// <summary>An inbox row as stored.</summary>
        /// <param name="Id">The row's id.</param>
        /// <param name="UserId">The recipient.</param>
        /// <param name="Type">The type key.</param>
        /// <param name="Severity">The severity's name.</param>
        /// <param name="ArgumentsJson">The arguments.</param>
        /// <param name="TargetResource">The target resource.</param>
        /// <param name="TargetId">The target row's id.</param>
        /// <param name="ActorUserId">The actor.</param>
        /// <param name="DedupKey">The dedup key.</param>
        /// <param name="CreatedAt">When the row was written or last refreshed.</param>
        /// <param name="ReadAt">When the recipient read it.</param>
        private sealed record Row(
            int Id, int UserId, string Type, string Severity, string? ArgumentsJson, string? TargetResource, long? TargetId, int? ActorUserId, string? DedupKey, DateTimeOffset CreatedAt, DateTimeOffset? ReadAt);

        /// <summary>A job row as stored, in the columns the delivery enqueue writes.</summary>
        /// <param name="Id">The job's id.</param>
        /// <param name="HandlerKey">The handler key.</param>
        /// <param name="Status">The status's name.</param>
        /// <param name="ArgumentsJson">The arguments.</param>
        /// <param name="TraceParent">The raising activity's trace parent.</param>
        /// <param name="RunAsUserId">The user the job runs as.</param>
        /// <param name="RequestedById">The user who requested it.</param>
        private sealed record JobRow(int Id, string HandlerKey, string Status, string? ArgumentsJson, string? TraceParent, int RunAsUserId, int? RequestedById);
    }

    /// <summary>The notifier's statements with read-committed snapshot on, as provisioning creates a tenant database.</summary>
    /// <param name="fixture">The class's database.</param>
    [Trait("Category", "Integration")]
    public sealed class NotifierSnapshotTests(NotificationsDatabase fixture) : NotifierStatementTests(fixture), IClassFixture<NotificationsDatabase>;

    /// <summary>The notifier's statements with read-committed snapshot off, where readers take shared locks.</summary>
    /// <param name="fixture">The class's database.</param>
    [Trait("Category", "Integration")]
    public sealed class NotifierLockingTests(LockingNotificationsDatabase fixture) : NotifierStatementTests(fixture), IClassFixture<LockingNotificationsDatabase>;
}
