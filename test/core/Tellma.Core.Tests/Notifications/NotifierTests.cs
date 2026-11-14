// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Microsoft.Data.SqlClient.Server;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.Metrics.Testing;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Testing;
using Microsoft.Extensions.Options;
using System.Data;
using System.Diagnostics.Metrics;
using System.Globalization;
using System.Text;
using System.Text.Json;
using Tellma.Core.Abstractions.Api;
using Tellma.Core.Abstractions.Data;
using Tellma.Core.Abstractions.Notifications;
using Tellma.Core.Abstractions.Realtime;
using Tellma.Core.Abstractions.Tenancy;
using Tellma.Core.Calendars;
using Tellma.Core.Data.Batches;
using Tellma.Core.Jobs;
using Tellma.Core.Notifications;
using Tellma.Core.Queryex;
using Tellma.Core.Tests.Data.Batches;
using static Tellma.Core.Tests.Notifications.NotificationTestModel;

namespace Tellma.Core.Tests.Notifications
{
    /// <summary>
    ///     The notifier with no database: the statements it appends and their golden text, the
    ///     parameters and rows they bind, the slices and the delivery condition, every programming
    ///     error it refuses before appending anything, and the batch's one commit callback, fed the
    ///     result sets a round trip would return.
    /// </summary>
    public sealed class NotifierTests : IDisposable
    {
        /// <summary>The scope's user in a user scope.</summary>
        private const int UserId = 7;

        /// <summary>The tenant the harness's connection reports.</summary>
        private const int TenantId = 1;

        /// <summary>The golden insert of a request at ordinal 0, under Suppress and Replace.</summary>
        private const string Insert =
            """
            DECLARE @tb0_n int = (SELECT COUNT(*) FROM @tb0_t0);
            DECLARE @tb0_first sql_variant;
            EXEC sys.sp_sequence_get_range @sequence_name = N'core.sq_Notifications', @range_size = @tb0_n, @range_first_value = @tb0_first OUTPUT;
            INSERT INTO [core].[Notifications] ([Id], [UserId], [Type], [Severity], [ArgumentsJson], [TargetResource], [TargetId], [ActorUserId], [DedupKey], [CreatedAt])
            OUTPUT [inserted].[UserId]
            SELECT CAST(@tb0_first AS int) + [r].[Ordinal], [r].[UserId], @tb0_p0, @tb0_p1, @tb0_p2, @tb0_p3, @tb0_p4, @tb0_p5, @tb0_p6, SYSUTCDATETIME()
            FROM @tb0_t0 AS [r]
            WHERE EXISTS (SELECT 1 FROM [core].[Users] AS [u] WHERE [u].[Id] = [r].[UserId] AND [u].[IsActive] = 1)
              AND (@tb0_p7 = 0 OR NOT EXISTS (SELECT 1 FROM [core].[NotificationPreferences] AS [p] WHERE [p].[UserId] = [r].[UserId] AND [p].[Type] = @tb0_p0 AND [p].[Channel] = 'inbox' AND [p].[Enabled] = 0))
              AND (@tb0_p6 IS NULL OR NOT EXISTS (SELECT 1 FROM [core].[Notifications] AS [n] WHERE [n].[UserId] = [r].[UserId] AND [n].[DedupKey] = @tb0_p6 AND [n].[ReadAt] IS NULL));
            """;

        /// <summary>The golden delivery enqueue of a request at ordinal 0 under the delivering composition's condition.</summary>
        private const string Enqueue =
            """
            DECLARE @tb0_qualified TABLE ([SliceOrdinal] int NOT NULL PRIMARY KEY);
            INSERT INTO @tb0_qualified ([SliceOrdinal])
            SELECT [s].[SliceOrdinal] FROM @tb0_t1 AS [s]
            WHERE EXISTS (SELECT 1 FROM @tb0_t0 AS [r] INNER JOIN [core].[Users] AS [u] ON [u].[Id] = [r].[UserId] AND [u].[IsActive] = 1
                WHERE [r].[Ordinal] BETWEEN [s].[FirstOrdinal] AND [s].[LastOrdinal]
                  AND (COALESCE((SELECT [p].[Enabled] FROM [core].[NotificationPreferences] AS [p] WHERE [p].[UserId] = [r].[UserId] AND [p].[Type] = {0} AND [p].[Channel] = 'email'), 0) = 1
                    OR COALESCE((SELECT [p].[Enabled] FROM [core].[NotificationPreferences] AS [p] WHERE [p].[UserId] = [r].[UserId] AND [p].[Type] = {0} AND [p].[Channel] = 'push'), 1) = 1));
            DECLARE @tb0_jobFirst sql_variant, @tb0_jobN int = (SELECT COUNT(*) FROM @tb0_qualified);
            IF @tb0_jobN > 0 EXEC sys.sp_sequence_get_range @sequence_name = N'core.sq_Jobs', @range_size = @tb0_jobN, @range_first_value = @tb0_jobFirst OUTPUT;
            INSERT INTO [core].[Jobs] ([Id], [HandlerKey], [Status], [DueAt], [Attempts], [ArgumentsJson], [TraceParent], [RunAsUserId], [RequestedById], [CreatedAt])
            OUTPUT [inserted].[Id]
            SELECT CAST(@tb0_jobFirst AS int) + ROW_NUMBER() OVER (ORDER BY [s].[SliceOrdinal]) - 1, N'core.notification-delivery', 'Pending',
                SYSUTCDATETIME(), 0, [s].[ArgumentsJson], {1}, 1, NULL, SYSUTCDATETIME()
            FROM @tb0_t1 AS [s] INNER JOIN @tb0_qualified AS [q] ON [q].[SliceOrdinal] = [s].[SliceOrdinal];
            """;

        /// <summary>The first line of every statement that enqueues.</summary>
        private const string Guard = "IF @@TRANCOUNT = 0 THROW 50422, N'Jobs.EnqueueOutsideTransaction', 1;";

        /// <summary><see cref="Enqueue" /> as a format: the type's parameter, then the trace parent's.</summary>
        private static readonly CompositeFormat EnqueueFormat = CompositeFormat.Parse(Enqueue);

        /// <summary>Arguments that serialise to a JSON array.</summary>
        private static readonly int[] NotAnObject = [1, 2];

        /// <summary>The composition with no sender.</summary>
        private readonly BatchHarness _plain = new(compose: Compose);

        /// <summary>The composition with senders for email and push.</summary>
        private readonly BatchHarness _delivering = new(compose: ComposeDelivering);

        /// <summary>The meter factory and the logs, Debug included.</summary>
        private readonly ServiceProvider _services = new ServiceCollection()
            .AddMetrics()
            .AddLogging(static logging => logging.SetMinimumLevel(LogLevel.Trace))
            .AddFakeLogging()
            .BuildServiceProvider();

        /// <summary>The events the notifiers publish.</summary>
        private readonly RecordingPublisher _publisher = new();

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

        /// <summary>The limits the tests run under: small, so slicing and the caps are cheap to reach.</summary>
        private static NotificationsOptions Limits { get; } = new() { DeliverySliceSize = 2, MaxRecipientsPerRequest = 6, MaxArgumentsChars = 60 };

        /// <inheritdoc />
        public void Dispose()
        {
            _plain.Dispose();
            _delivering.Dispose();
            _services.Dispose();
        }

        // --- The insert, the replace, and what they bind ---

        [Fact]
        public void A_request_appends_one_insert_that_writes_the_notification_table()
        {
            DataBatch batch = _plain.Batch(BatchPurpose.Persist);

            Notifier(_plain).Notify(batch, [Order([5, 6])]);

            BatchStatement statement = Assert.Single(batch.Statements);
            Assert.Equal(Insert, statement.Text, ignoreLineEndingDifferences: true);
            Assert.Equal([new TableName("core", "Notifications")], statement.Writes);
            Assert.False(statement.MayRunTwice);
            Assert.NotNull(statement.Reader);
        }

        [Fact]
        public void The_scalars_bind_with_the_columns_types_in_the_statements_order()
        {
            DataBatch batch = _plain.Batch(BatchPurpose.Persist);

            Notifier(_plain).Notify(batch, [Order([5], dedupKey: "acme.Order:42")]);

            List<ScalarParameter> scalars = [.. Assert.Single(batch.Statements).Parameters.OfType<ScalarParameter>()];
            Assert.Equal(
                [
                    ("@tb0_p0", SqlDbType.NVarChar, (object?)Shipped, 64),
                    ("@tb0_p1", SqlDbType.VarChar, "Info", 8),
                    ("@tb0_p2", SqlDbType.NVarChar, /*lang=json,strict*/ """{"number":"SO-42"}""", 4000),
                    ("@tb0_p3", SqlDbType.VarChar, OrderResource, 128),
                    ("@tb0_p4", SqlDbType.BigInt, 42L, null),
                    ("@tb0_p5", SqlDbType.Int, UserId, null),
                    ("@tb0_p6", SqlDbType.NVarChar, "acme.Order:42", 128),
                    ("@tb0_p7", SqlDbType.Bit, true, null),
                ],
                scalars.Select(static scalar => (scalar.Name, scalar.Type, scalar.Value, scalar.Size)));
        }

        [Fact]
        public void Repeated_recipients_collapse_and_are_numbered_in_first_seen_order()
        {
            DataBatch batch = _plain.Batch(BatchPurpose.Persist);

            Notifier(_plain).Notify(batch, [Order([9, 5, 9, 6, 5])]);

            List<IReadOnlyDictionary<string, object?>> rows = Rows(Assert.Single(batch.Statements), "@tb0_t0");
            Assert.Equal([(0, 9), (1, 5), (2, 6)], rows.Select(static row => ((int)row["Ordinal"]!, (int)row["UserId"]!)));
        }

        [Fact]
        public void Replace_refreshes_the_unread_duplicates_ahead_of_the_insert()
        {
            DataBatch batch = _plain.Batch(BatchPurpose.Persist);

            Notifier(_plain).Notify(batch, [Order([5], dedupKey: "acme.Order:42", policy: DuplicatePolicy.Replace)]);

            Assert.Equal(
                """
                UPDATE [n] SET [Type] = @tb0_p0, [Severity] = @tb0_p1, [ArgumentsJson] = @tb0_p2, [TargetResource] = @tb0_p3,
                    [TargetId] = @tb0_p4, [ActorUserId] = @tb0_p5, [CreatedAt] = SYSUTCDATETIME()
                OUTPUT [inserted].[UserId]
                FROM [core].[Notifications] AS [n] JOIN @tb0_t0 AS [r] ON [r].[UserId] = [n].[UserId]
                WHERE [n].[DedupKey] = @tb0_p6 AND [n].[ReadAt] IS NULL
                  AND EXISTS (SELECT 1 FROM [core].[Users] AS [u] WHERE [u].[Id] = [r].[UserId] AND [u].[IsActive] = 1)
                  AND (@tb0_p7 = 0 OR NOT EXISTS (SELECT 1 FROM [core].[NotificationPreferences] AS [p] WHERE [p].[UserId] = [r].[UserId] AND [p].[Type] = @tb0_p0 AND [p].[Channel] = 'inbox' AND [p].[Enabled] = 0));

                """ + Insert,
                Assert.Single(batch.Statements).Text,
                ignoreLineEndingDifferences: true);
        }

        [Fact]
        public void Ongoing_counts_a_read_row_as_a_duplicate_too()
        {
            DataBatch batch = _plain.Batch(BatchPurpose.Persist);

            Notifier(_plain).Notify(batch, [Order([5], dedupKey: "acme.Order:42", policy: DuplicatePolicy.Ongoing)]);

            Assert.EndsWith(
                "AND (@tb0_p6 IS NULL OR NOT EXISTS (SELECT 1 FROM [core].[Notifications] AS [n] WHERE [n].[UserId] = [r].[UserId] AND [n].[DedupKey] = @tb0_p6));",
                Assert.Single(batch.Statements).Text,
                StringComparison.Ordinal);
        }

        [Fact]
        public void The_severity_is_the_types_unless_the_request_overrides_it()
        {
            DataBatch batch = _plain.Batch(BatchPurpose.Persist);

            Notifier(_plain).Notify(batch,
            [
                new NotificationRequest(Held, [5], null, null, null, null, null),
                new NotificationRequest(Held, [5], null, null, null, null, null, Severity: NotificationSeverity.Error),
            ]);

            Assert.Equal(["Warning", "Error"], batch.Statements.Select(static statement => Scalar(statement, 1)));
        }

        [Fact]
        public void The_actor_defaults_to_the_scopes_user_and_to_none_in_a_system_scope()
        {
            DataBatch user = _plain.Batch(BatchPurpose.Persist);
            DataBatch system = _plain.Batch(BatchPurpose.Persist);

            Notifier(_plain, UserScope).Notify(user, [Order([5], actor: null), Order([5], actor: 12)]);
            Notifier(_plain, SystemScope).Notify(system, [Order([5], actor: null), Order([5], actor: 12)]);

            Assert.Equal([UserId, 12], user.Statements.Select(static statement => Scalar(statement, 5)));
            Assert.Equal([null, 12], system.Statements.Select(static statement => Scalar(statement, 5)));
        }

        [Fact]
        public void A_request_with_no_recipients_appends_nothing()
        {
            DataBatch batch = _delivering.Batch(BatchPurpose.Persist);

            Notifier(_delivering).Notify(batch, [Order([])]);

            Assert.Empty(batch.Statements);
            Assert.Empty(batch.CommittedCallbacks);
        }

        // --- The delivery enqueue ---

        [Fact]
        public void With_a_channel_that_delivers_the_enqueue_follows_the_insert_inside_a_transaction()
        {
            DataBatch batch = _delivering.Batch(BatchPurpose.Persist);

            Notifier(_delivering).Notify(batch, [Order([5, 6, 7])]);

            BatchStatement statement = Assert.Single(batch.Statements);
            Assert.Equal(
                Guard + "\n" + Insert + "\n" + string.Format(CultureInfo.InvariantCulture, EnqueueFormat, "@tb0_p0", "@tb0_p8"),
                statement.Text,
                ignoreLineEndingDifferences: true);
            Assert.Equal(
                [new TableName("core", "Jobs"), new TableName("core", "Notifications")],
                statement.Writes.OrderBy(static table => table.Delimited, StringComparer.Ordinal));
            Assert.Equal(11, statement.Parameters.Count);
            Assert.Equal(SqlDbType.NVarChar, Assert.IsType<ScalarParameter>(statement.Parameters.Single(static parameter => parameter.Name == "@tb0_p8")).Type);
        }

        [Fact]
        public void The_recipients_are_cut_into_slices_each_carrying_its_delivery_arguments()
        {
            DataBatch batch = _delivering.Batch(BatchPurpose.Persist);

            Notifier(_delivering).Notify(batch, [Order([5, 6, 5, 7, 8, 9], severity: NotificationSeverity.Success)]);

            List<IReadOnlyDictionary<string, object?>> slices = Rows(Assert.Single(batch.Statements), "@tb0_t1");
            Assert.Equal([(0, 0, 1), (1, 2, 3), (2, 4, 4)], slices.Select(static slice => ((int)slice["SliceOrdinal"]!, (int)slice["FirstOrdinal"]!, (int)slice["LastOrdinal"]!)));
            List<NotificationDeliveryArguments> arguments = [.. slices.Select(static slice => JsonSerializer.Deserialize<NotificationDeliveryArguments>((string)slice["ArgumentsJson"]!, TellmaJson.Options)!)];
            Assert.Equal([[5, 6], [7, 8], [9]], arguments.Select(static argument => argument.RecipientIds.ToArray()));
            NotificationDeliveryArguments first = arguments[0];
            Assert.Equal(
                (Shipped, NotificationSeverity.Success, /*lang=json,strict*/ """{"number":"SO-42"}""", OrderResource, 42L, UserId),
                (first.Type, first.Severity, first.ArgumentsJson, first.TargetResource, first.TargetId, first.ActorUserId));
        }

        [Fact]
        public void An_inbox_only_request_enqueues_no_delivery()
        {
            DataBatch batch = _delivering.Batch(BatchPurpose.Persist);

            Notifier(_delivering).Notify(batch, [Order([5]) with { InboxOnly = true }]);

            BatchStatement statement = Assert.Single(batch.Statements);
            Assert.Equal(Insert, statement.Text, ignoreLineEndingDifferences: true);
            Assert.Equal([new TableName("core", "Notifications")], statement.Writes);
        }

        [Fact]
        public void Without_a_channel_that_delivers_no_request_enqueues_a_delivery()
        {
            Assert.Empty(_plain.Services.GetRequiredService<NotificationDeliveryChannels>().Channels);
            Assert.Null(_plain.Services.GetRequiredService<NotificationDeliveryChannels>().Condition);
        }

        [Fact]
        public void The_delivering_channels_are_the_registered_ones_with_a_sender_in_registration_order()
        {
            NotificationDeliveryChannels channels = _delivering.Services.GetRequiredService<NotificationDeliveryChannels>();

            Assert.Equal(["email", Push], channels.Channels.Select(static channel => channel.Key));
        }

        [Fact]
        public void The_condition_inlines_each_channels_key_and_default()
        {
            string? condition = NotificationDeliveryChannels.BuildCondition([new("sms", DefaultEnabled: false), new("push", DefaultEnabled: true)]);

            Assert.Equal(
                """
                COALESCE((SELECT [p].[Enabled] FROM [core].[NotificationPreferences] AS [p] WHERE [p].[UserId] = [r].[UserId] AND [p].[Type] = {0} AND [p].[Channel] = 'sms'), 0) = 1
                        OR COALESCE((SELECT [p].[Enabled] FROM [core].[NotificationPreferences] AS [p] WHERE [p].[UserId] = [r].[UserId] AND [p].[Type] = {0} AND [p].[Channel] = 'push'), 1) = 1
                """,
                condition,
                ignoreLineEndingDifferences: true);
            Assert.Null(NotificationDeliveryChannels.BuildCondition([]));
        }

        [Fact]
        public void A_channel_only_request_is_the_delivery_enqueue_alone()
        {
            DataBatch batch = _delivering.Batch(BatchPurpose.Persist);

            Notifier(_delivering).Notify(batch, [new NotificationRequest(SmsOnly, [5, 6], new { number = "SO-42" }, null, null, null, null)]);

            BatchStatement statement = Assert.Single(batch.Statements);
            Assert.Equal(
                Guard + "\n" + string.Format(CultureInfo.InvariantCulture, EnqueueFormat, "@tb0_p0", "@tb0_p1"),
                statement.Text,
                ignoreLineEndingDifferences: true);
            Assert.Equal([new TableName("core", "Jobs")], statement.Writes);
            Assert.Equal(["@tb0_t0", "@tb0_t1", "@tb0_p0", "@tb0_p1"], statement.Parameters.Select(static parameter => parameter.Name));
        }

        [Fact]
        public void A_channel_only_request_with_no_channel_that_delivers_appends_nothing()
        {
            DataBatch batch = _plain.Batch(BatchPurpose.Persist);

            Notifier(_plain).Notify(batch, [new NotificationRequest(SmsOnly, [5, 6], null, null, null, null, null)]);

            Assert.Empty(batch.Statements);
            Assert.Empty(batch.CommittedCallbacks);
        }

        // --- The withdrawal ---

        [Fact]
        public void A_withdrawal_deletes_every_row_of_its_key()
        {
            DataBatch batch = _plain.Batch(BatchPurpose.Persist);

            Notifier(_plain).Withdraw(batch, [new WithdrawRequest("acme.Order:42")]);

            BatchStatement statement = Assert.Single(batch.Statements);
            Assert.Equal(
                """
                DELETE [n] OUTPUT [deleted].[UserId], [deleted].[Type]
                FROM [core].[Notifications] AS [n] WHERE [n].[DedupKey] = @tb0_p0;
                """,
                statement.Text,
                ignoreLineEndingDifferences: true);
            Assert.Equal([new TableName("core", "Notifications")], statement.Writes);
            Assert.True(statement.MayRunTwice);
            ScalarParameter key = Assert.IsType<ScalarParameter>(Assert.Single(statement.Parameters));
            Assert.Equal((SqlDbType.NVarChar, (object?)"acme.Order:42", 128), (key.Type, key.Value, key.Size));
        }

        [Fact]
        public void A_withdrawal_for_named_recipients_deletes_their_rows_alone()
        {
            DataBatch batch = _plain.Batch(BatchPurpose.Persist);

            Notifier(_plain).Withdraw(batch, [new WithdrawRequest("acme.Order:42", [6, 5, 6])]);

            BatchStatement statement = Assert.Single(batch.Statements);
            Assert.Equal(
                """
                DELETE [n] OUTPUT [deleted].[UserId], [deleted].[Type]
                FROM [core].[Notifications] AS [n] WHERE [n].[DedupKey] = @tb0_p0 AND [n].[UserId] IN (SELECT [Id] FROM @tb0_t0);
                """,
                statement.Text,
                ignoreLineEndingDifferences: true);
            Assert.Equal([6, 5], Rows(statement, "@tb0_t0").Select(static row => (int)row["Id"]!));
        }

        [Fact]
        public void A_withdrawal_for_no_recipients_appends_nothing()
        {
            DataBatch batch = _plain.Batch(BatchPurpose.Persist);

            Notifier(_plain).Withdraw(batch, [new WithdrawRequest("acme.Order:42", [])]);

            Assert.Empty(batch.Statements);
        }

        [Fact]
        public void A_withdrawal_rides_an_autocommit_batch()
        {
            DataBatch batch = _plain.Batch(BatchPurpose.Maintenance);
            batch.TransactionMode = TransactionMode.None;

            Notifier(_plain).Withdraw(batch, [new WithdrawRequest("acme.Order:42")]);

            _ = Assert.Single(batch.Statements);
        }

        // --- Programming errors, each refused before anything is appended ---

        [Fact]
        public void An_unregistered_type_is_refused()
        {
            AssertRefused<InvalidOperationException>([new NotificationRequest("acme.unknown", [5], null, null, null, null, null)], "The notification type 'acme.unknown' is not registered.");
        }

        [Fact]
        public void A_missing_recipient_list_is_refused()
        {
            AssertRefused<InvalidOperationException>([new NotificationRequest(Held, null!, null, null, null, null, null)], "has no recipient list");
        }

        [Fact]
        public void Too_many_recipients_are_refused_even_when_they_repeat()
        {
            AssertRefused<InvalidOperationException>([new NotificationRequest(Held, [5, 5, 5, 5, 5, 5, 5], null, null, null, null, null)], "names 7 recipients; a request names at most 6");
        }

        [Theory]
        [InlineData(DuplicatePolicy.Replace)]
        [InlineData(DuplicatePolicy.Ongoing)]
        public void Replace_and_ongoing_without_a_key_are_refused(DuplicatePolicy policy)
        {
            AssertRefused<InvalidOperationException>([Order([5], policy: policy)], $"is {policy} without a DedupKey");
        }

        [Theory]
        [InlineData(0)]
        [InlineData(129)]
        public void A_key_that_is_empty_or_too_long_is_refused(int length)
        {
            AssertRefused<InvalidOperationException>([Order([5], dedupKey: new string('k', length))], $"has a DedupKey of {length} characters; a key takes 1 to 128");
        }

        [Fact]
        public void An_undefined_policy_or_severity_is_refused()
        {
            AssertRefused<InvalidOperationException>([Order([5], policy: (DuplicatePolicy)9)], "has the duplicate policy 9, which is not defined");
            AssertRefused<InvalidOperationException>([Order([5], severity: (NotificationSeverity)9)], "has the severity 9, which is not defined");
        }

        [Fact]
        public void An_inbox_only_request_or_a_key_on_a_channel_only_type_is_refused()
        {
            AssertRefused<InvalidOperationException>([new NotificationRequest(SmsOnly, [5], null, null, null, null, null, InboxOnly: true)], "is InboxOnly, but the type is channel-only");
            AssertRefused<InvalidOperationException>([new NotificationRequest(SmsOnly, [5], null, null, null, null, "acme.sms")], "carries a DedupKey, but the type is channel-only");
        }

        [Fact]
        public void A_target_other_than_the_types_is_refused()
        {
            AssertRefused<InvalidOperationException>([Order([5]) with { TargetId = null }], "must target the type's resource 'acme.Order' with a TargetId");
            AssertRefused<InvalidOperationException>([Order([5]) with { TargetResource = "acme.Invoice" }], "must target the type's resource 'acme.Order' with a TargetId, but it targets 'acme.Invoice'");
            AssertRefused<InvalidOperationException>([new NotificationRequest(Held, [5], null, OrderResource, 42, null, null)], "carries a target, but the type declares none");
            AssertRefused<InvalidOperationException>([new NotificationRequest(Held, [5], null, null, 42, null, null)], "carries a target, but the type declares none");
        }

        [Fact]
        public void Arguments_that_are_not_an_object_or_too_long_are_refused()
        {
            AssertRefused<InvalidOperationException>([Order([5]) with { Arguments = NotAnObject }], "serialise to JSON that is not an object");
            AssertRefused<InvalidOperationException>([Order([5]) with { Arguments = new { note = new string('x', 60) } }], "has arguments of 71 characters once serialised; a notification carries at most 60");
        }

        [Fact]
        public void A_null_request_is_refused()
        {
            AssertRefused<ArgumentNullException>([Order([5]), null!], "The notification request at 1 is null.");
        }

        [Fact]
        public void A_batch_that_autocommits_or_does_not_write_is_refused()
        {
            DataBatch autocommit = _plain.Batch(BatchPurpose.Maintenance);
            autocommit.TransactionMode = TransactionMode.None;
            DataBatch read = _plain.Batch(BatchPurpose.Read);

            ArgumentException none = Assert.Throws<ArgumentException>(() => Notifier(_plain).Notify(autocommit, [Order([5])]));
            ArgumentException reading = Assert.Throws<ArgumentException>(() => Notifier(_plain).Withdraw(read, [new WithdrawRequest("acme.Order:42")]));

            Assert.Contains("runs in None mode", none.Message, StringComparison.Ordinal);
            Assert.Contains("A Read batch does not write", reading.Message, StringComparison.Ordinal);
            Assert.Empty(autocommit.Statements);
            Assert.Empty(read.Statements);
        }

        [Fact]
        public void A_withdrawal_without_a_key_or_with_too_many_recipients_is_refused()
        {
            DataBatch batch = _plain.Batch(BatchPurpose.Persist);

            InvalidOperationException keyless = Assert.Throws<InvalidOperationException>(() => Notifier(_plain).Withdraw(batch, [new WithdrawRequest(null!)]));
            InvalidOperationException crowded = Assert.Throws<InvalidOperationException>(() => Notifier(_plain).Withdraw(batch, [new WithdrawRequest("k", [1, 2, 3, 4, 5, 6, 7])]));

            Assert.Contains("The withdrawal has no DedupKey", keyless.Message, StringComparison.Ordinal);
            Assert.Contains("The withdrawal of 'k' names 7 recipients", crowded.Message, StringComparison.Ordinal);
            Assert.Empty(batch.Statements);
        }

        [Fact]
        public void A_call_that_would_pass_the_batchs_parameter_cap_appends_nothing()
        {
            DataBatch batch = _plain.Batch(BatchPurpose.Persist);

            // Nine parameters each: 222 requests and the fingerprint take 1,999, so a 223rd passes the 2,000.
            InvalidOperationException exception = Assert.Throws<InvalidOperationException>(
                () => Notifier(_plain).Notify(batch, [.. Enumerable.Repeat(new NotificationRequest(Held, [5], null, null, null, null, null), 223)]));

            Assert.Contains("more than the 2000 one batch may declare", exception.Message, StringComparison.Ordinal);
            Assert.Empty(batch.Statements);
            Assert.Empty(batch.CommittedCallbacks);
        }

        // --- After commit ---

        [Fact]
        public void One_callback_serves_every_request_on_the_batch_raised_or_withdrawn()
        {
            DataBatch batch = _plain.Batch(BatchPurpose.Persist);
            Notifier notifier = Notifier(_plain);

            notifier.Notify(batch, [Order([5])]);
            notifier.Withdraw(batch, [new WithdrawRequest("acme.Order:41")]);
            notifier.Notify(batch, [new NotificationRequest(Held, [6], null, null, null, null, null)]);

            Assert.Equal(3, batch.Statements.Count);
            _ = Assert.Single(batch.CommittedCallbacks);
        }

        [Fact]
        public async Task After_commit_each_changed_inbox_receives_one_event_and_the_poller_is_woken()
        {
            TenantPollerWake wake = _delivering.Services.GetRequiredService<TenantPollerWake>();
            int woken = 0;
            using IDisposable subscription = wake.Subscribe(TenantId, () => woken++);
            DataBatch batch = _delivering.Batch(BatchPurpose.Persist);
            Notifier notifier = Notifier(_delivering);
            notifier.Notify(batch, [Order([5, 6, 7], dedupKey: "acme.Order:42", policy: DuplicatePolicy.Replace)]);
            notifier.Withdraw(batch, [new WithdrawRequest("acme.Order:41")]);

            // The replace refreshed 7, the insert wrote 5, one slice enqueued, and the withdrawal deleted 5's and 8's rows.
            await ReadAsync(batch, Ids(7), Ids(5), Ids(1001), Withdrawn((5, Shipped), (8, Held)));
            Assert.Equal(0, woken);
            Assert.Single(batch.CommittedCallbacks)(null!);

            ClientEvent published = Assert.Single(_publisher.Published);
            Assert.Equal("inbox.changed", published.Name);
            Assert.Equal([5, 7, 8], published.UserIds);
            Assert.Null(published.Payload);
            Assert.Equal(1, woken);
        }

        [Fact]
        public async Task A_batch_whose_requests_changed_no_inbox_publishes_nothing_and_wakes_no_poller()
        {
            TenantPollerWake wake = _delivering.Services.GetRequiredService<TenantPollerWake>();
            int woken = 0;
            using IDisposable subscription = wake.Subscribe(TenantId, () => woken++);
            DataBatch batch = _delivering.Batch(BatchPurpose.Persist);
            Notifier(_delivering).Notify(batch, [Order([5])]);

            await ReadAsync(batch, Ids(), Ids());
            Assert.Single(batch.CommittedCallbacks)(null!);

            Assert.Empty(_publisher.Published);
            Assert.Equal(0, woken);
        }

        [Fact]
        public void A_statement_that_never_completed_counts_nothing()
        {
            DataBatch batch = _plain.Batch(BatchPurpose.Persist);
            Notifier(_plain).Notify(batch, [Order([5])]);

            Assert.Single(batch.CommittedCallbacks)(null!);

            Assert.Empty(_publisher.Published);
        }

        [Fact]
        public async Task The_counters_record_what_each_committed_statement_did_by_type()
        {
            IMeterFactory meters = _services.GetRequiredService<IMeterFactory>();
            using MetricCollector<long> created = new(meters, NotificationsTelemetryNames.MeterName, NotificationsTelemetryNames.Created);
            using MetricCollector<long> suppressed = new(meters, NotificationsTelemetryNames.MeterName, NotificationsTelemetryNames.Suppressed);
            using MetricCollector<long> replaced = new(meters, NotificationsTelemetryNames.MeterName, NotificationsTelemetryNames.Replaced);
            using MetricCollector<long> withdrawn = new(meters, NotificationsTelemetryNames.MeterName, NotificationsTelemetryNames.Withdrawn);
            using MetricCollector<long> enqueued = new(meters, NotificationsTelemetryNames.MeterName, NotificationsTelemetryNames.DeliveryEnqueued);
            DataBatch batch = _delivering.Batch(BatchPurpose.Persist);
            Notifier notifier = Notifier(_delivering);
            notifier.Notify(batch, [Order([5, 6, 7, 8], dedupKey: "acme.Order:42", policy: DuplicatePolicy.Replace)]);
            notifier.Notify(batch, [new NotificationRequest(SmsOnly, [5], null, null, null, null, null)]);
            notifier.Withdraw(batch, [new WithdrawRequest("acme.Order:41")]);

            await ReadAsync(batch, Ids(7), Ids(5, 6), Ids(1001, 1002), Ids(1003), Withdrawn((5, Shipped), (6, Shipped), (8, Held)));
            Assert.Single(batch.CommittedCallbacks)(null!);

            Assert.Equal([(2L, Shipped)], Points(created));
            Assert.Equal([(1L, Shipped)], Points(replaced));
            Assert.Equal([(1L, Shipped)], Points(suppressed));
            Assert.Equal([(2L, Shipped), (1L, SmsOnly)], Points(enqueued));
            Assert.Equal([(2L, Shipped), (1L, Held)], Points(withdrawn));
        }

        [Fact]
        public async Task The_outcomes_are_logged_at_debug_level()
        {
            DataBatch batch = _plain.Batch(BatchPurpose.Persist);
            Notifier notifier = Notifier(_plain);
            notifier.Notify(batch, [Order([5, 6])]);
            notifier.Withdraw(batch, [new WithdrawRequest("acme.Order:41")]);

            await ReadAsync(batch, Ids(5), Withdrawn((6, Shipped)));
            Assert.Single(batch.CommittedCallbacks)(null!);

            FakeLogRecord inserted = Assert.Single(Logs("NotificationsInserted"));
            FakeLogRecord withdrawnLog = Assert.Single(Logs("NotificationsWithdrawn"));
            Assert.Equal(LogLevel.Debug, inserted.Level);
            Assert.Equal($"The notification {Shipped} named 2 recipients, wrote 1 rows and enqueued 0 delivery jobs.", inserted.Message);
            Assert.Equal("The withdrawal of acme.Order:41 deleted 1 notifications.", withdrawnLog.Message);
        }

        [Fact]
        public async Task A_publish_that_fails_is_logged_and_never_thrown()
        {
            _publisher.Failure = new InvalidOperationException("The hub is down.");
            DataBatch batch = _plain.Batch(BatchPurpose.Persist);
            Notifier(_plain).Notify(batch, [Order([5])]);
            await ReadAsync(batch, Ids(5));

            Assert.Single(batch.CommittedCallbacks)(null!);

            FakeLogRecord failed = Assert.Single(Logs("InboxEventFailed"));
            Assert.Equal(LogLevel.Warning, failed.Level);
            Assert.Equal("The hub is down.", failed.Exception?.Message);
        }

        // --- Helpers ---

        /// <summary>A request of the targeted mutable type.</summary>
        /// <param name="recipients">The recipients.</param>
        /// <param name="dedupKey">The duplicate key.</param>
        /// <param name="policy">The duplicate policy.</param>
        /// <param name="severity">The severity override.</param>
        /// <param name="actor">The actor.</param>
        private static NotificationRequest Order(
            int[] recipients, string? dedupKey = null, DuplicatePolicy policy = DuplicatePolicy.Suppress, NotificationSeverity? severity = null, int? actor = null)
        {
            return new NotificationRequest(Shipped, recipients, new { number = "SO-42" }, OrderResource, 42, actor, dedupKey, policy, Severity: severity);
        }

        /// <summary>Asserts a call is refused before anything is appended.</summary>
        /// <typeparam name="TException">The exception expected.</typeparam>
        /// <param name="requests">The requests.</param>
        /// <param name="message">A fragment of the expected message.</param>
        private void AssertRefused<TException>(IReadOnlyList<NotificationRequest> requests, string message)
            where TException : Exception
        {
            DataBatch batch = _delivering.Batch(BatchPurpose.Persist);

            TException exception = Assert.Throws<TException>(() => Notifier(_delivering).Notify(batch, requests));

            Assert.Contains(message, exception.Message, StringComparison.Ordinal);
            Assert.Empty(batch.Statements);
            Assert.Empty(batch.CommittedCallbacks);
        }

        /// <summary>A notifier over a harness's composition for one scope.</summary>
        /// <param name="harness">The harness.</param>
        /// <param name="context">The scope's context; a user scope when null.</param>
        private Notifier Notifier(BatchHarness harness, RequestContext? context = null)
        {
            return new Notifier(
                new UnusedDatabase(),
                new FixedContext(context ?? UserScope),
                harness.Services.GetRequiredService<INotificationTypeRegistry>(),
                Options.Create(Limits),
                harness.Services.GetRequiredService<NotificationDeliveryChannels>(),
                _publisher,
                harness.Services.GetRequiredService<TenantPollerWake>(),
                new NotificationsMetrics(_services.GetRequiredService<IMeterFactory>()),
                _services.GetRequiredService<ILoggerFactory>());
        }

        /// <summary>Feeds the batch's statements the result sets a round trip would return, and applies their completions.</summary>
        /// <param name="batch">The batch.</param>
        /// <param name="sets">The result sets, in the order the statements read them.</param>
        private static async Task ReadAsync(DataBatch batch, params DataTable[] sets)
        {
            using DataSet data = new();
            data.Tables.AddRange(sets);
            using DataTableReader reader = data.CreateDataReader();
            ResultSetCursor cursor = new(reader);
            foreach (BatchStatement statement in batch.Statements)
            {
                await statement.Reader!(cursor, TestContext.Current.CancellationToken);
            }

            await cursor.EndAsync(TestContext.Current.CancellationToken);
            cursor.ApplyCompletions();
        }

        /// <summary>A result set of one <c>int</c> column.</summary>
        /// <param name="ids">The rows.</param>
        private static DataTable Ids(params int[] ids)
        {
            DataTable table = new();
            _ = table.Columns.Add("Id", typeof(int));
            foreach (int id in ids)
            {
                _ = table.Rows.Add(id);
            }

            return table;
        }

        /// <summary>A withdrawal's result set: the deleted rows' users and types.</summary>
        /// <param name="rows">The rows.</param>
        private static DataTable Withdrawn(params (int UserId, string Type)[] rows)
        {
            DataTable table = new();
            _ = table.Columns.Add("UserId", typeof(int));
            _ = table.Columns.Add("Type", typeof(string));
            foreach ((int userId, string type) in rows)
            {
                _ = table.Rows.Add(userId, type);
            }

            return table;
        }

        /// <summary>The value of a statement's scalar <c>p{index}</c>.</summary>
        /// <param name="statement">The statement.</param>
        /// <param name="index">The scalar's index.</param>
        private static object? Scalar(BatchStatement statement, int index)
        {
            string name = ReservedSqlNames.Statement(statement.Ordinal, ReservedSqlNames.StatementNames.Scalar(index));
            return Assert.IsType<ScalarParameter>(statement.Parameters.Single(parameter => parameter.Name == name)).Value;
        }

        /// <summary>The rows one of a statement's table parameters binds, each copied column by column.</summary>
        /// <param name="statement">The statement.</param>
        /// <param name="name">The parameter's name.</param>
        private static List<IReadOnlyDictionary<string, object?>> Rows(BatchStatement statement, string name)
        {
            TableParameter table = Assert.IsType<TableParameter>(statement.Parameters.Single(parameter => parameter.Name == name));
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

        /// <summary>A counter's recorded points as value and type tag, in recording order.</summary>
        /// <param name="collector">The collector.</param>
        private static List<(long Value, string? Type)> Points(MetricCollector<long> collector)
        {
            return [.. collector.GetMeasurementSnapshot().Select(static point => (point.Value, point.Tags[NotificationsTelemetryNames.TypeTag] as string))];
        }

        /// <summary>The notifier's log records of one event.</summary>
        /// <param name="name">The event's name.</param>
        private List<FakeLogRecord> Logs(string name)
        {
            return [.. _services.GetFakeLogCollector().GetSnapshot().Where(record => record.Id.Name == name)];
        }

        /// <summary>A request context accessor that answers one fixed context.</summary>
        /// <param name="context">The context.</param>
        private sealed class FixedContext(RequestContext context) : IRequestContextAccessor
        {
            public RequestContext Current => context;
        }

        /// <summary>A tenant database the tests never reach: every request here rides a batch the test holds.</summary>
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
