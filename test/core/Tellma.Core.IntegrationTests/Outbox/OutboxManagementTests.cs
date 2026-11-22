// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.Metrics.Testing;
using System.Diagnostics.Metrics;
using System.Globalization;
using Tellma.Core.Abstractions.Access;
using Tellma.Core.Abstractions.Blobs;
using Tellma.Core.Abstractions.Crud;
using Tellma.Core.Abstractions.Email;
using Tellma.Core.Abstractions.Errors;
using Tellma.Core.Abstractions.Jobs;
using Tellma.Core.Abstractions.Tenancy;
using Tellma.Core.Abstractions.Validation;
using Tellma.Core.IntegrationTests.Jobs;
using Tellma.Core.Outbox;
using Tellma.Core.Testing.Email;
using Tellma.Testing.Entities;
using static Tellma.Core.IntegrationTests.Outbox.OutboxDatabase;

namespace Tellma.Core.IntegrationTests.Outbox
{
    /// <summary>
    ///     What becomes of an outgoing email after it was sent, against a fixture database: the
    ///     provider's delivery events recorded on it monotonically, repeats and stragglers skipped
    ///     and unknown references reported; the administrator's retry, cancel and delete under their
    ///     preconditions, stamped, the retry refused whole and the cancel skipping a target that
    ///     changed between their two rounds; and the nightly retention, which deletes the settled and
    ///     abandoned rows past their retention page by page, with their attachments, and keeps any row
    ///     a job may still send.
    /// </summary>
    /// <param name="fixture">The class's database, reset before each test.</param>
    [Trait("Category", "Integration")]
    public sealed class OutboxManagementTests(OutboxDatabase fixture) : IAsyncLifetime, IClassFixture<OutboxDatabase>
    {
        /// <summary>The transport name the delivery events are dispatched under.</summary>
        private const string Transport = "capture";

        /// <summary>A row id no row has.</summary>
        private const int Unknown = 987654;

        /// <summary>A stamp long before any test ran, which an update replaces.</summary>
        private static readonly DateTimeOffset LongAgo = new(2001, 1, 1, 0, 0, 0, TimeSpan.Zero);

        /// <summary>The columns a send and its delivery events fill, which a retry clears.</summary>
        private static readonly string[] OutcomeColumns =
            ["SentAt", "ProviderMessageId", "ErrorCode", "ErrorDetail", "DeliveryStatus", "DeliveryAt", "DeliveryRecipient", "DeliveryReason", "LastProviderEventId"];

        /// <summary>The cancellation token of the running test.</summary>
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

        // --- Delivery events ---

        [Fact]
        public async Task Repeated_and_out_of_order_events_leave_the_strongest_status_once_and_unknown_references_are_reported()
        {
            using MetricCollector<long> updates = Collector<long>(OutboxTelemetryNames.DeliveryUpdates);
            int[] ids = [.. await EnqueueAsync(Request(), Request())];
            _ = await DispatchAsync();
            IReadOnlyList<EmailDeliveryEvent> first = DeliveryEvents.For(Correlation(ids[0])).Deferred().Delivered().Bounced().Redeliver().Build();
            IReadOnlyList<EmailDeliveryEvent> second = DeliveryEvents.For(Correlation(ids[1])).Delivered().Opened().Build();
            IReadOnlyList<EmailDeliveryEvent> stray = DeliveryEvents.For(Correlation(Unknown)).Delivered().Build();

            // --- The delivery, the second row's open, and a reference to no row ---
            await DeliverAsync([first[1], second[0], second[1], stray[0]]);

            // --- Then the bounce, stronger, and last its repeat with the deferral that preceded it ---
            await DeliverAsync([first[2]]);
            await DeliverAsync([first[3], first[0]]);

            // --- Each row records its strongest event, once; the sending state is untouched ---
            IReadOnlyDictionary<string, object?> bounced = await RowAsync(ids[0]);
            Assert.Equal(
                (nameof(OutgoingEmailState.Sent), nameof(EmailDeliveryStatus.Bounced), "550 unknown user", "recipient@example.com", first[2].ProviderEventId, first[2].Timestamp),
                (bounced["State"], bounced["DeliveryStatus"], bounced["DeliveryReason"], bounced["DeliveryRecipient"], bounced["LastProviderEventId"], (DateTimeOffset?)bounced["DeliveryAt"]));
            IReadOnlyDictionary<string, object?> delivered = await RowAsync(ids[1]);
            Assert.Equal(
                (nameof(EmailDeliveryStatus.Delivered), second[0].ProviderEventId, null),
                (delivered["DeliveryStatus"], delivered["LastProviderEventId"], delivered["DeliveryReason"]));

            // --- Three applied, the repeat and the straggler stale, the stray unmatched and logged ---
            Assert.Equal([("applied", 3L), ("stale", 2L), ("unmatched", 1L)], Totals(updates));
            Assert.Contains(
                fixture.Logs.Entries,
                static entry => entry.EventId.Name == "OutboxDeliveryEventUnmatched" && entry.Message!.Contains(Text(Unknown), StringComparison.Ordinal));
        }

        [Fact]
        public async Task The_update_returns_how_many_rows_it_changed_and_the_ids_that_match_no_row()
        {
            int id = Assert.Single(await EnqueueAsync(Request()));
            DeliverySelection selection = DeliveryEventSelection.Select(
            [
                .. DeliveryEvents.For(Correlation(id)).Delivered().Build(),
                .. DeliveryEvents.For(Correlation(Unknown)).Bounced().Build(),
            ]);

            // The update applies in any state: an email still pending records its event too.
            await using AsyncServiceScope scope = fixture.Tenant.Services.CreateAsyncScope();
            DeliveryUpdateResult result = await scope.ServiceProvider.GetRequiredService<IOutboxDeliveryWriter>()
                .ApplyAsync(FixtureTenant.TenantId, Assert.Single(selection.Tenants).Rows, Token);

            Assert.Equal((1, Unknown), (result.Applied, Assert.Single(result.Unmatched)));
            Assert.Equal(nameof(EmailDeliveryStatus.Delivered), (await RowAsync(id))["DeliveryStatus"]);
        }

        // --- Retry ---

        [Fact]
        public async Task A_retry_of_a_failed_email_clears_its_outcome_stamps_it_and_sends_it_again_under_a_new_job()
        {
            int id = Assert.Single(await EnqueueAsync(Request()));
            _ = await DispatchAsync();
            await SetAsync(id, """
                [State] = 'Failed', [ErrorCode] = N'Email.AttemptsExhausted', [ErrorDetail] = N'The provider was down.',
                [DeliveryStatus] = 'Deferred', [DeliveryAt] = SYSDATETIMEOFFSET(), [DeliveryRecipient] = N'ada@example.com',
                [DeliveryReason] = N'Mailbox busy', [LastProviderEventId] = N'evt-0001', [ModifiedAt] = '2001-01-01T00:00:00+00:00'
                """);
            object? firstJob = (await RowAsync(id))["JobId"];

            await ActAsync(OutgoingEmailService.RetryAction, id);

            // --- Pending under a new job, every outcome column cleared, stamped by the administrator ---
            IReadOnlyDictionary<string, object?> row = await RowAsync(id);
            Assert.Equal(nameof(OutgoingEmailState.Pending), row["State"]);
            Assert.All(OutcomeColumns, column => Assert.Null(row[column]));
            Assert.Equal((false, AdministratorId), (row["ExpectsDeliveryEvents"], row["ModifiedById"]));
            Assert.True((DateTimeOffset)row["ModifiedAt"]! > LongAgo);
            Assert.NotEqual(firstJob, row["JobId"]);
            IReadOnlyDictionary<string, object?> job = Assert.Single(await fixture.RowsAsync(Query($"SELECT [HandlerKey], [Status] FROM [core].[Jobs] WHERE [Id] = {row["JobId"]};")));
            Assert.Equal((OutgoingEmailService.SendHandlerKey, nameof(JobStatus.Pending)), (job["HandlerKey"], job["Status"]));

            // --- The new job sends it again ---
            _ = await DispatchAsync();
            Assert.Equal(2, fixture.Sender.Captured.Count);
            Assert.Equal(nameof(OutgoingEmailState.Sent), (await RowAsync(id))["State"]);
        }

        [Fact]
        public async Task A_retry_of_an_abandoned_email_whose_outcome_is_already_clear_still_stamps_it()
        {
            int id = Assert.Single(await EnqueueAsync(Request()));
            _ = await DispatchAsync();

            // --- Pending again with every column a retry clears already clear, and its job ended ---
            await SetAsync(id, "[State] = 'Pending', [SentAt] = NULL, [ProviderMessageId] = NULL, [ExpectsDeliveryEvents] = 0, [ModifiedAt] = '2001-01-01T00:00:00+00:00'");
            object? firstJob = (await RowAsync(id))["JobId"];

            await ActAsync(OutgoingEmailService.RetryAction, id);

            IReadOnlyDictionary<string, object?> row = await RowAsync(id);
            Assert.Equal((nameof(OutgoingEmailState.Pending), AdministratorId), (row["State"], row["ModifiedById"]));
            Assert.True((DateTimeOffset)row["ModifiedAt"]! > LongAgo);
            Assert.NotEqual(firstJob, row["JobId"]);
        }

        [Fact]
        public async Task A_retry_is_refused_whole_when_a_target_was_cancelled_since_its_validation_and_enqueues_no_job()
        {
            int[] ids = [.. await EnqueueAsync(Request(), Request())];
            _ = await DispatchAsync();
            (int kept, int cancelled) = (ids[0], ids[1]);
            await SetAsync(kept, "[State] = 'Failed', [ErrorCode] = N'Email.AttemptsExhausted'");
            await SetAsync(cancelled, "[State] = 'Failed', [ErrorCode] = N'Email.AttemptsExhausted'");
            IReadOnlyList<IReadOnlyDictionary<string, object?>> jobs = await fixture.RowsAsync("SELECT [Id] FROM [core].[Jobs] ORDER BY [Id];");
            object? keptJob = (await RowAsync(kept))["JobId"];

            // --- Another administrator cancels one target between the retry's two rounds ---
            fixture.Race.Arm(() => SetAsync(cancelled, "[State] = 'Cancelled'"));
            ValidationException refused = await Assert.ThrowsAsync<ValidationException>(() => ActAsync(OutgoingEmailService.RetryAction, [kept, cancelled]));

            // --- Nothing the retry composed is written: no row reset, no job enqueued ---
            Assert.True(fixture.Race.Ran);
            ValidationError error = Assert.Single(refused.Errors);
            Assert.Equal((OutgoingEmailCodes.NotRetryable, ValidationPath.Root), (error.Code, error.Path));
            IReadOnlyDictionary<string, object?> row = await RowAsync(kept);
            Assert.Equal((nameof(OutgoingEmailState.Failed), keptJob), (row["State"], row["JobId"]));
            Assert.Equal(nameof(OutgoingEmailState.Cancelled), (await RowAsync(cancelled))["State"]);
            Assert.Equal(jobs.Count, (await fixture.RowsAsync("SELECT [Id] FROM [core].[Jobs];")).Count);
        }

        [Fact]
        public async Task A_retry_of_a_rejected_email_is_refused_and_changes_nothing()
        {
            int id = Assert.Single(await EnqueueAsync(Request()));
            _ = await DispatchAsync();
            await SetAsync(id, "[State] = 'Rejected', [ErrorCode] = N'Email.Rejected'");

            ValidationException refused = await Assert.ThrowsAsync<ValidationException>(() => ActAsync(OutgoingEmailService.RetryAction, id));

            Assert.Equal(OutgoingEmailCodes.NotRetryable, Assert.Single(refused.Errors).Code);
            Assert.Equal(nameof(OutgoingEmailState.Rejected), (await RowAsync(id))["State"]);
        }

        [Fact]
        public async Task A_retry_of_an_email_whose_job_may_still_send_it_is_refused()
        {
            int id = Assert.Single(await EnqueueAsync(Request()));

            ValidationException refused = await Assert.ThrowsAsync<ValidationException>(() => ActAsync(OutgoingEmailService.RetryAction, id));

            Assert.Equal(OutgoingEmailCodes.NotRetryable, Assert.Single(refused.Errors).Code);
        }

        // --- Cancel and delete ---

        [Fact]
        public async Task A_cancel_on_a_read_only_tenant_settles_the_pending_email_stamped_and_its_job_then_sends_nothing()
        {
            int id = Assert.Single(await EnqueueAsync(Request()));
            await SetAsync(id, "[ModifiedAt] = '2001-01-01T00:00:00+00:00'");

            await ActAsync(OutgoingEmailService.CancelAction, id, TenantState.ReadOnly);

            IReadOnlyDictionary<string, object?> row = await RowAsync(id);
            Assert.Equal((nameof(OutgoingEmailState.Cancelled), AdministratorId), (row["State"], row["ModifiedById"]));
            Assert.True((DateTimeOffset)row["ModifiedAt"]! > LongAgo);

            PolledJob polled = Assert.Single(await DispatchAsync());
            Assert.Equal(JobStatus.Succeeded, polled.Status);
            Assert.Empty(fixture.Sender.Captured);
        }

        [Fact]
        public async Task A_cancel_leaves_a_target_its_job_pre_marked_since_its_validation_as_it_is_and_cancels_the_other()
        {
            int[] ids = [.. await EnqueueAsync(Request(), Request())];
            (int cancelled, int sending) = (ids[0], ids[1]);
            await SetAsync(cancelled, "[ModifiedAt] = '2001-01-01T00:00:00+00:00'");
            await SetAsync(sending, "[ModifiedAt] = '2001-01-01T00:00:00+00:00'");

            // --- The sending job pre-marks one target between the cancel's two rounds ---
            fixture.Race.Arm(() => SetAsync(sending, "[State] = 'Sending'"));
            await ActAsync(OutgoingEmailService.CancelAction, [cancelled, sending]);

            // --- The cancel commits: one target cancelled and stamped, the pre-marked one untouched ---
            Assert.True(fixture.Race.Ran);
            IReadOnlyDictionary<string, object?> row = await RowAsync(cancelled);
            Assert.Equal((nameof(OutgoingEmailState.Cancelled), AdministratorId), (row["State"], row["ModifiedById"]));
            Assert.True((DateTimeOffset)row["ModifiedAt"]! > LongAgo);
            IReadOnlyDictionary<string, object?> untouched = await RowAsync(sending);
            Assert.Equal(nameof(OutgoingEmailState.Sending), untouched["State"]);
            Assert.Equal(LongAgo, (DateTimeOffset)untouched["ModifiedAt"]!);
        }

        [Fact]
        public async Task A_cancel_of_an_email_already_sent_is_refused()
        {
            int id = Assert.Single(await EnqueueAsync(Request()));
            _ = await DispatchAsync();

            ValidationException refused = await Assert.ThrowsAsync<ValidationException>(() => ActAsync(OutgoingEmailService.CancelAction, id));

            Assert.Equal(OutgoingEmailCodes.NotCancellable, Assert.Single(refused.Errors).Code);
        }

        [Fact]
        public async Task A_pending_email_with_a_live_job_cannot_be_deleted_until_it_is_cancelled()
        {
            int id = Assert.Single(await EnqueueAsync(Request()));

            ValidationException refused = await Assert.ThrowsAsync<ValidationException>(() => DeleteAsync(id));
            Assert.Equal(OutgoingEmailCodes.NotDeletable, Assert.Single(refused.Errors).Code);

            await ActAsync(OutgoingEmailService.CancelAction, id);
            await DeleteAsync(id);
            Assert.Empty(await fixture.RowsAsync(Query($"SELECT [Id] FROM [core].[OutgoingEmails] WHERE [Id] = {id};")));
        }

        // --- Retention ---

        [Fact]
        public async Task The_retention_deletes_old_settled_and_abandoned_emails_releases_their_files_and_keeps_any_a_job_may_still_send()
        {
            using MetricCollector<long> retained = Collector<long>(OutboxTelemetryNames.Retained);
            EmailEnqueueRequest attached = Request() with
            {
                Message = Request().Message with { Attachments = [new EmailAttachment("report.csv", "text/csv", "a,b\n1,2\n"u8.ToArray())] },
            };
            int[] ids = [.. await EnqueueAsync(attached, Request(), Request(), Request())];
            _ = await DispatchAsync();
            (int settled, int abandoned, int live, int recent) = (ids[0], ids[1], ids[2], ids[3]);

            // --- Three rows past the retention: one sent, one unsettled whose job ended, one whose job is live again ---
            await SetAsync(abandoned, "[State] = 'Pending'");
            await SetAsync(live, "[State] = 'Pending'");
            await SqlServerFixture.ExecuteAsync(
                fixture.ConnectionString,
                Query($"UPDATE [core].[Jobs] SET [Status] = 'Pending', [CompletedAt] = NULL, [DueAt] = DATEADD(day, 1, SYSUTCDATETIME()) WHERE [Id] = (SELECT [JobId] FROM [core].[OutgoingEmails] WHERE [Id] = {live});"),
                Query($"UPDATE [core].[OutgoingEmails] SET [CreatedAt] = DATEADD(day, -200, SYSDATETIMEOFFSET()) WHERE [Id] IN ({settled}, {abandoned}, {live});"));

            _ = await fixture.InsertJobAsync(OutgoingEmailRetentionHandler.Key, WellKnownIds.SystemUserId);
            PolledJob polled = Assert.Single(await JobPollDriver.PollOnceAsync(fixture.Tenant, [OutgoingEmailRetentionHandler.Key], Token, Token));

            // --- The settled and abandoned rows are gone, with their attachment, whose file is released ---
            Assert.Equal(JobStatus.Succeeded, polled.Status);
            Assert.Equal([live, recent], (await fixture.RowsAsync("SELECT [Id] FROM [core].[OutgoingEmails] ORDER BY [Id];")).Select(static row => (int)row["Id"]!));
            Assert.Empty(await fixture.RowsAsync("SELECT [Id] FROM [core].[OutgoingEmailAttachments];"));
            Assert.Equal(nameof(BlobState.Released), Assert.Single(await fixture.RowsAsync("SELECT [State] FROM [core].[Blobs];"))["State"]);
            Assert.Equal([2L], retained.GetMeasurementSnapshot().Select(static point => point.Value));
        }

        [Fact]
        public async Task The_retention_deletes_more_old_emails_than_a_page_holds_page_by_page()
        {
            using MetricCollector<long> retained = Collector<long>(OutboxTelemetryNames.Retained);
            int source = Assert.Single(await EnqueueAsync(Request()));
            _ = await DispatchAsync();

            // --- One sent row past the retention, its ended job forgotten (a job owns one row), then
            // as many copies as fill a page ---
            await SetAsync(source, "[CreatedAt] = DATEADD(day, -200, SYSDATETIMEOFFSET()), [JobId] = NULL");
            await SqlServerFixture.ExecuteAsync(fixture.ConnectionString, Query($$"""
                DECLARE @columns nvarchar(max) = (
                    SELECT STRING_AGG(CONVERT(nvarchar(max), QUOTENAME([name])), N', ') WITHIN GROUP (ORDER BY [column_id])
                    FROM sys.columns WHERE [object_id] = OBJECT_ID(N'[core].[OutgoingEmails]') AND [name] <> N'Id' AND [is_computed] = 0 AND TYPE_NAME([system_type_id]) <> N'timestamp');
                DECLARE @text nvarchar(max) = N'INSERT INTO [core].[OutgoingEmails] ([Id], ' + @columns + N') '
                    + N'SELECT NEXT VALUE FOR [core].[sq_OutgoingEmails], ' + @columns + N' FROM [core].[OutgoingEmails] '
                    + N'CROSS JOIN (SELECT TOP ({{OutgoingEmailRetentionHandler.PageSize}}) 1 AS [n] FROM sys.all_columns) AS [c] WHERE [Id] = {{source}};';
                EXEC sp_executesql @text;
                """));

            _ = await fixture.InsertJobAsync(OutgoingEmailRetentionHandler.Key, WellKnownIds.SystemUserId);
            PolledJob polled = Assert.Single(await JobPollDriver.PollOnceAsync(fixture.Tenant, [OutgoingEmailRetentionHandler.Key], Token, Token));

            // --- A full page, then the short one; every row deleted ---
            Assert.Equal(JobStatus.Succeeded, polled.Status);
            Assert.Empty(await fixture.RowsAsync("SELECT [Id] FROM [core].[OutgoingEmails];"));
            Assert.Equal([500L, 1L], retained.GetMeasurementSnapshot().Select(static point => point.Value));
        }

        [Fact]
        public void The_retention_runs_nightly_after_the_job_retention_under_its_own_built_in_schedule()
        {
            Assert.Equal(
                (7, "core.outgoing-email-retention", "45 3 * * *", 500),
                (OutgoingEmailRetentionHandler.ScheduleId, OutgoingEmailRetentionHandler.Key, OutgoingEmailRetentionHandler.Cron, OutgoingEmailRetentionHandler.PageSize));
        }

        /// <summary>A plain internal message to Ada, enqueued by the api.</summary>
        /// <returns>The request.</returns>
        private static EmailEnqueueRequest Request()
        {
            return new EmailEnqueueRequest(new EmailMessage
            {
                To = [new EmailAddress(SignInEmail(AdaId), "Ada")],
                Subject = "The monthly report",
                TextBody = "The report is attached.",
                Audience = EmailAudience.Internal,
            });
        }

        /// <summary>The outbox's correlation of a row of the fixture tenant.</summary>
        /// <param name="id">The row's id.</param>
        /// <returns>The correlation.</returns>
        private static EmailCorrelation Correlation(int id)
        {
            return new EmailCorrelation(OutgoingEmailHandler.CorrelationOwnerKey, Text(id), FixtureTenant.TenantId);
        }

        /// <summary>An id as invariant text.</summary>
        /// <param name="id">The id.</param>
        /// <returns>The text.</returns>
        private static string Text(int id)
        {
            return id.ToString(CultureInfo.InvariantCulture);
        }

        /// <summary>A statement formatted in the invariant culture.</summary>
        /// <param name="text">The statement.</param>
        /// <returns>Its text.</returns>
        private static string Query(FormattableString text)
        {
            return FormattableString.Invariant(text);
        }

        /// <summary>The counts of a counter by its result tag, in result order.</summary>
        /// <param name="collector">The counter's collector.</param>
        /// <returns>Each result and its total.</returns>
        private static IEnumerable<(string Result, long Total)> Totals(MetricCollector<long> collector)
        {
            return collector.GetMeasurementSnapshot()
                .GroupBy(static point => (string)point.Tags[OutboxTelemetryNames.ResultTag]!)
                .Select(static group => (group.Key, group.Sum(static point => point.Value)))
                .OrderBy(static pair => pair.Key, StringComparer.Ordinal);
        }

        /// <summary>A collector of one of the outbox's instruments on the host's meter factory.</summary>
        /// <typeparam name="T">The instrument's value type.</typeparam>
        /// <param name="instrument">The instrument's name.</param>
        /// <returns>The collector.</returns>
        private MetricCollector<T> Collector<T>(string instrument)
            where T : struct
        {
            return new MetricCollector<T>(fixture.Tenant.Services.GetRequiredService<IMeterFactory>(), OutboxTelemetryNames.MeterName, instrument);
        }

        /// <summary>Enqueues emails in a job's frame, which commits them with its completion.</summary>
        /// <param name="requests">The requests.</param>
        /// <returns>The rows' ids, in request order.</returns>
        private async Task<IReadOnlyList<int>> EnqueueAsync(params EmailEnqueueRequest[] requests)
        {
            fixture.Script.Probe = async (batch, context) =>
            {
                _ = await context.Services.GetRequiredService<IEmailOutbox>().EnqueueAsync(context.Host, requests, Token);
                _ = await context.Host.PersistAsync(Token);
            };
            _ = await fixture.InsertJobAsync(JobRunKeys.Probe, WellKnownIds.SystemUserId);
            PolledJob polled = Assert.Single(await JobPollDriver.PollOnceAsync(fixture.Tenant, [JobRunKeys.Probe], Token, Token));
            Assert.Equal(JobStatus.Succeeded, polled.Status);
            return [.. (await fixture.RowsAsync("SELECT [Id] FROM [core].[OutgoingEmails] ORDER BY [Id];")).Select(static row => (int)row["Id"]!)];
        }

        /// <summary>Polls the sending jobs once.</summary>
        /// <returns>The jobs the poll ran.</returns>
        private Task<IReadOnlyList<PolledJob>> DispatchAsync()
        {
            return JobPollDriver.PollOnceAsync(fixture.Tenant, [OutgoingEmailService.SendHandlerKey], Token, Token);
        }

        /// <summary>Dispatches delivery events as the webhook endpoint does, in a scope of no tenant.</summary>
        /// <param name="events">The events.</param>
        /// <returns>A task that completes when the events are applied.</returns>
        private async Task DeliverAsync(IReadOnlyList<EmailDeliveryEvent> events)
        {
            await using AsyncServiceScope scope = fixture.Tenant.Services.CreateAsyncScope();
            await scope.ServiceProvider.GetRequiredService<IEmailDeliveryEventDispatcher>().DispatchAsync(Transport, events, Token);
        }

        /// <summary>Runs one of the outgoing emails' actions on one row as the administrator.</summary>
        /// <param name="action">The action's name.</param>
        /// <param name="id">The row.</param>
        /// <param name="state">The tenant's state the request sees; the registered one when null.</param>
        /// <returns>A task that completes when the action committed.</returns>
        private Task ActAsync(string action, int id, TenantState? state = null)
        {
            return ActAsync(action, [id], state);
        }

        /// <summary>Runs one of the outgoing emails' actions on rows as the administrator.</summary>
        /// <param name="action">The action's name.</param>
        /// <param name="ids">The rows.</param>
        /// <param name="state">The tenant's state the request sees; the registered one when null.</param>
        /// <returns>A task that completes when the action committed.</returns>
        private async Task ActAsync(string action, int[] ids, TenantState? state = null)
        {
            await using AsyncServiceScope scope = await fixture.UserScopeAsync(AdministratorId, state);
            _ = await Emails(scope).ExecuteActionAsync(action, ids, null, new ActionOptions(), Token);
        }

        /// <summary>Deletes one row as the administrator.</summary>
        /// <param name="id">The row.</param>
        /// <returns>A task that completes when the delete committed.</returns>
        private async Task DeleteAsync(int id)
        {
            await using AsyncServiceScope scope = await fixture.UserScopeAsync(AdministratorId);
            _ = await Emails(scope).DeleteByIdsAsync([id], cancellationToken: Token);
        }

        /// <summary>The outgoing emails' service of a scope.</summary>
        /// <param name="scope">The scope.</param>
        /// <returns>The service.</returns>
        private static EntityService<OutgoingEmail, int> Emails(AsyncServiceScope scope)
        {
            return scope.ServiceProvider.GetRequiredService<EntityService<OutgoingEmail, int>>();
        }

        /// <summary>Sets columns of one row directly, as a state the outbox reaches otherwise.</summary>
        /// <param name="id">The row.</param>
        /// <param name="assignments">The <c>SET</c> clause's assignments.</param>
        /// <returns>A task that completes when the row is updated.</returns>
        private Task SetAsync(int id, string assignments)
        {
            return SqlServerFixture.ExecuteAsync(fixture.ConnectionString, Query($"UPDATE [core].[OutgoingEmails] SET {assignments} WHERE [Id] = {id};"));
        }

        /// <summary>Reads one outgoing email row.</summary>
        /// <param name="id">The row.</param>
        /// <returns>The row's columns.</returns>
        private async Task<IReadOnlyDictionary<string, object?>> RowAsync(int id)
        {
            return Assert.Single(await fixture.RowsAsync(Query($"SELECT * FROM [core].[OutgoingEmails] WHERE [Id] = {id};")));
        }
    }
}
