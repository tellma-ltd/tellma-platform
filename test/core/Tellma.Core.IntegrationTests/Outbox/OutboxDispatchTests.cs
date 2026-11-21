// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.Metrics.Testing;
using System.Diagnostics.Metrics;
using System.Globalization;
using Tellma.Core.Abstractions.Access;
using Tellma.Core.Abstractions.Data;
using Tellma.Core.Abstractions.Email;
using Tellma.Core.Abstractions.Jobs;
using Tellma.Core.Abstractions.Notifications;
using Tellma.Core.Data.Batches;
using Tellma.Core.IntegrationTests.Jobs;
using Tellma.Core.Outbox;
using Tellma.Core.Testing.Email;
using Tellma.Testing.Entities;
using Tellma.Testing.Support.Logging;
using static Tellma.Core.IntegrationTests.Outbox.OutboxDatabase;

namespace Tellma.Core.IntegrationTests.Outbox
{
    /// <summary>
    ///     The sending job against a fixture database: each outcome of the email sender settling its
    ///     row and ending its job, a transient failure retried until the last attempt fails it, a
    ///     sender that throws, a row found still sending failed unsent, a settled row left alone, an
    ///     attachment whose object is gone rejecting its email, a pre-mark that a re-run repeats, the
    ///     administrators' one inbox notice per claim, and the outbox's instruments.
    /// </summary>
    /// <param name="fixture">The class's database, reset before each test.</param>
    [Trait("Category", "Integration")]
    public sealed class OutboxDispatchTests(OutboxDatabase fixture) : IAsyncLifetime, IClassFixture<OutboxDatabase>
    {
        /// <summary>The notification delivery handler's key, whose jobs an inbox-only notice never enqueues.</summary>
        private const string Delivery = "core.notification-delivery";

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

        [Fact]
        public async Task A_pending_email_is_sent_once_correlated_with_its_row_and_settles_sent()
        {
            using MetricCollector<long> enqueued = Collector<long>(OutboxTelemetryNames.Enqueued);
            using MetricCollector<long> settled = Collector<long>(OutboxTelemetryNames.Settled);
            using MetricCollector<double> latency = Collector<double>(OutboxTelemetryNames.Latency);
            int id = Assert.Single(await EnqueueAsync(Request()));

            PolledJob polled = Assert.Single(await DispatchAsync());

            // --- One message, its envelope rebuilt from the row and correlated with it ---
            CapturedEmail captured = Assert.Single(fixture.Sender.Captured);
            Assert.Equal([new EmailAddress(SignInEmail(AdaId), "Ada")], captured.Message.To);
            Assert.Equal(("The monthly report", "The report is attached.", EmailAudience.Internal), (captured.Message.Subject, captured.Message.TextBody, captured.Message.Audience));
            Assert.Equal(new EmailCorrelation(OutgoingEmailHandler.CorrelationOwnerKey, Text(id), FixtureTenant.TenantId), captured.Message.Correlation);

            // --- The row is sent with its provider's id, and its job succeeded ---
            IReadOnlyDictionary<string, object?> row = await RowAsync(id);
            Assert.Equal(
                (nameof(OutgoingEmailState.Sent), (object?)$"capture-{captured.Ordinal}", (object?)true, (object?)1, null, null),
                (row["State"], row["ProviderMessageId"], row["ExpectsDeliveryEvents"], row["AttemptCount"], row["ErrorCode"], row["ErrorDetail"]));
            Assert.NotNull(row["SentAt"]);
            Assert.Equal((PolledJobEnd.Completed, JobStatus.Succeeded), (polled.End, polled.Status));

            // --- A second poll finds nothing to send ---
            Assert.Empty(await DispatchAsync());
            _ = Assert.Single(fixture.Sender.Captured);

            // --- One enqueued by the api, one settled sent, and its wait ---
            Assert.Equal([(1L, "api")], enqueued.GetMeasurementSnapshot().Select(static point => (point.Value, (string)point.Tags[OutboxTelemetryNames.SourceTag]!)));
            Assert.Equal(["sent"], settled.GetMeasurementSnapshot().Select(static point => (string)point.Tags[OutboxTelemetryNames.StateTag]!));
            Assert.InRange(Assert.Single(latency.GetMeasurementSnapshot()).Value, 0, 600);
        }

        [Fact]
        public async Task A_claim_sends_its_emails_with_their_attachments_read_from_the_store()
        {
            EmailEnqueueRequest attached = Request() with
            {
                Message = Request().Message with { Attachments = [new EmailAttachment("report.csv", "text/csv", "a,b\n1,2\n"u8.ToArray())] },
            };
            int[] ids = [.. await EnqueueAsync(attached, Request())];

            PolledJob[] polled = [.. await DispatchAsync()];

            // Both jobs ran in one claim; the attachment's bytes went out with its email.
            Assert.Equal(2, polled.Length);
            Assert.Equal(2, fixture.Sender.Captured.Count);
            EmailAttachment file = Assert.Single(fixture.Sender.Captured.Single(email => email.Message.Correlation!.Reference == Text(ids[0])).Message.Attachments);
            Assert.Equal(("report.csv", "text/csv", "a,b\n1,2\n"), (file.FileName, file.ContentType, System.Text.Encoding.UTF8.GetString(file.Content.Span)));
            foreach (int id in ids)
            {
                Assert.Equal(nameof(OutgoingEmailState.Sent), (await RowAsync(id))["State"]);
            }
        }

        [Fact]
        public async Task A_sandboxed_email_keeps_no_provider_id_and_expects_no_delivery_events()
        {
            fixture.Sender.OnSending(static (_, _) => new EmailSendResult(EmailSendOutcome.Sandboxed, "sandbox-1", ExpectsDeliveryEvents: true));
            int id = Assert.Single(await EnqueueAsync(Request()));

            _ = await DispatchAsync();

            IReadOnlyDictionary<string, object?> row = await RowAsync(id);
            Assert.Equal((nameof(OutgoingEmailState.Sandboxed), null, (object?)false), (row["State"], row["ProviderMessageId"], row["ExpectsDeliveryEvents"]));
            Assert.NotNull(row["SentAt"]);
            Assert.Empty(await NoticesAsync());
        }

        [Fact]
        public async Task A_sandbox_tenant_s_external_email_settles_sandboxed_and_its_internal_email_goes_out_marked()
        {
            fixture.Sandbox.IsSandbox = true;
            EmailEnqueueRequest external = Request() with { Message = Request().Message with { Audience = EmailAudience.External } };
            int[] ids = [.. await EnqueueAsync(external, Request())];

            _ = await DispatchAsync();

            // --- The external email went to the transport's sandbox channel: sandboxed, no events to wait for ---
            IReadOnlyDictionary<string, object?> sandboxed = await RowAsync(ids[0]);
            Assert.Equal(
                (nameof(OutgoingEmailState.Sandboxed), null, (object?)false),
                (sandboxed["State"], sandboxed["ProviderMessageId"], sandboxed["ExpectsDeliveryEvents"]));
            Assert.NotNull(sandboxed["SentAt"]);

            // --- The internal email went out for real, marked as a sandbox's ---
            Assert.Equal(nameof(OutgoingEmailState.Sent), (await RowAsync(ids[1]))["State"]);
            CapturedEmail marked = Assert.Single(fixture.Sender.Captured, email => email.Message.Correlation!.Reference == Text(ids[1]));
            Assert.StartsWith("[Sandbox] ", marked.Message.Subject, StringComparison.Ordinal);
        }

        [Fact]
        public async Task Rejected_emails_settle_with_the_provider_detail_and_tell_the_administrators_once_in_the_inbox()
        {
            using MetricCollector<long> settled = Collector<long>(OutboxTelemetryNames.Settled);
            using MetricCollector<double> latency = Collector<double>(OutboxTelemetryNames.Latency);
            fixture.Sender.OnSending(static (_, _) => new EmailSendResult(EmailSendOutcome.Rejected, Error: "Mailbox unavailable."));
            int[] ids = [.. await EnqueueAsync(Request(), Request())];

            PolledJob[] polled = [.. await DispatchAsync()];

            // --- Both rows rejected with the provider's detail; both jobs succeeded ---
            Assert.All(polled, static job => Assert.Equal(JobStatus.Succeeded, job.Status));
            foreach (int id in ids)
            {
                IReadOnlyDictionary<string, object?> row = await RowAsync(id);
                Assert.Equal(
                    (nameof(OutgoingEmailState.Rejected), (object?)OutgoingEmailCodes.Rejected, (object?)"Mailbox unavailable.", null),
                    (row["State"], row["ErrorCode"], row["ErrorDetail"], row["SentAt"]));
            }

            // --- One error notice to the administrator, counting both, on the first row; no delivery job ---
            IReadOnlyDictionary<string, object?> notice = Assert.Single(await NoticesAsync());
            Assert.Equal(
                ((object?)AdministratorId, (object?)CoreResources.OutgoingEmail, (object?)(long)ids[0], (object?)nameof(NotificationSeverity.Error)),
                (notice["UserId"], notice["TargetResource"], notice["TargetId"], notice["Severity"]));
            Assert.Contains("2", (string)notice["ArgumentsJson"]!, StringComparison.Ordinal);
            Assert.Empty(await fixture.RowsAsync($"SELECT [Id] FROM [core].[Jobs] WHERE [HandlerKey] = N'{Delivery}';"));

            Assert.Equal(["rejected", "rejected"], settled.GetMeasurementSnapshot().Select(static point => (string)point.Tags[OutboxTelemetryNames.StateTag]!));
            Assert.Empty(latency.GetMeasurementSnapshot());
        }

        [Fact]
        public async Task A_transient_failure_returns_the_row_to_pending_and_retries_its_job_with_backoff()
        {
            using MetricCollector<long> settled = Collector<long>(OutboxTelemetryNames.Settled);
            fixture.Sender.OnSending(static (_, _) => new EmailSendResult(EmailSendOutcome.TransientFailure, Error: "Throttled."));
            int id = Assert.Single(await EnqueueAsync(Request()));
            DateTimeOffset before = DateTimeOffset.UtcNow;

            _ = await DispatchAsync();

            // --- The row waits again, saying why; the job is due again within the first attempt's
            // backoff (full jitter up to a minute), with the same code ---
            IReadOnlyDictionary<string, object?> row = await RowAsync(id);
            Assert.Equal(
                (nameof(OutgoingEmailState.Pending), (object?)OutgoingEmailCodes.TransientFailure, (object?)"Throttled.", (object?)1),
                (row["State"], row["ErrorCode"], row["ErrorDetail"], row["AttemptCount"]));
            IReadOnlyDictionary<string, object?> job = await JobAsync(row);
            Assert.Equal((nameof(JobStatus.Pending), (object?)1, (object?)OutgoingEmailCodes.TransientFailure), (job["Status"], job["Attempts"], job["ErrorCode"]));
            DateTimeOffset due = Assert.IsType<DateTimeOffset>(job["DueAt"]);
            Assert.InRange(due, before.AddSeconds(-5), DateTimeOffset.UtcNow.AddMinutes(1).AddSeconds(5));

            // --- Nothing settled, nobody told ---
            Assert.Empty(settled.GetMeasurementSnapshot());
            Assert.Empty(await NoticesAsync());

            // --- Once due again, the next attempt sends it ---
            fixture.Sender.OnSending(static (_, ordinal) => new EmailSendResult(EmailSendOutcome.Sent, $"capture-{ordinal}"));
            await DueNowAsync(row);
            _ = await DispatchAsync();
            IReadOnlyDictionary<string, object?> sent = await RowAsync(id);
            Assert.Equal((nameof(OutgoingEmailState.Sent), (object?)2, null), (sent["State"], sent["AttemptCount"], sent["ErrorCode"]));
        }

        [Fact]
        public async Task A_transient_failure_on_the_last_attempt_fails_the_row_and_tells_the_administrators()
        {
            fixture.Sender.OnSending(static (_, _) => new EmailSendResult(EmailSendOutcome.TransientFailure, Error: "Throttled."));
            int id = Assert.Single(await EnqueueAsync(Request()));
            IReadOnlyDictionary<string, object?> pending = await RowAsync(id);
            await SqlServerFixture.ExecuteAsync(
                fixture.ConnectionString,
                string.Create(CultureInfo.InvariantCulture, $"UPDATE [core].[Jobs] SET [Attempts] = {OutgoingEmailHandler.MaxAttempts - 1} WHERE [Id] = {pending["JobId"]};"));

            PolledJob polled = Assert.Single(await DispatchAsync());

            IReadOnlyDictionary<string, object?> row = await RowAsync(id);
            Assert.Equal(
                (nameof(OutgoingEmailState.Failed), (object?)OutgoingEmailCodes.AttemptsExhausted, (object?)"Throttled."),
                (row["State"], row["ErrorCode"], row["ErrorDetail"]));
            Assert.Equal(JobStatus.Succeeded, polled.Status);
            _ = Assert.Single(await NoticesAsync());
        }

        [Fact]
        public async Task A_sender_that_throws_sent_nothing_so_each_email_is_retried()
        {
            fixture.Sender.OnSending(static (_, _) => throw new InvalidOperationException("The transport is down."));
            int id = Assert.Single(await EnqueueAsync(Request()));

            _ = await DispatchAsync();

            IReadOnlyDictionary<string, object?> row = await RowAsync(id);
            Assert.Equal(
                (nameof(OutgoingEmailState.Pending), (object?)OutgoingEmailCodes.TransientFailure, (object?)"The transport is down."),
                (row["State"], row["ErrorCode"], row["ErrorDetail"]));
            Assert.Equal(nameof(JobStatus.Pending), (await JobAsync(row))["Status"]);

            // The log names the exception's type and the count, never the message's content.
            CapturedLogEntry failed = Assert.Single(fixture.Logs.Entries, static entry => entry.EventId.Name == "OutboxSendFailed");
            Assert.Contains("System.InvalidOperationException", failed.Message, StringComparison.Ordinal);
        }

        [Fact]
        public async Task A_row_found_still_sending_is_failed_unsent_and_reported()
        {
            int id = Assert.Single(await EnqueueAsync(Request()));
            await SqlServerFixture.ExecuteAsync(
                fixture.ConnectionString,
                string.Create(CultureInfo.InvariantCulture, $"UPDATE [core].[OutgoingEmails] SET [State] = 'Sending', [AttemptCount] = 1 WHERE [Id] = {id};"));

            PolledJob polled = Assert.Single(await DispatchAsync());

            // --- Never sent again; failed as interrupted, its job succeeded, the administrators told ---
            Assert.Empty(fixture.Sender.Captured);
            IReadOnlyDictionary<string, object?> row = await RowAsync(id);
            Assert.Equal((nameof(OutgoingEmailState.Failed), (object?)OutgoingEmailCodes.Interrupted, (object?)1), (row["State"], row["ErrorCode"], row["AttemptCount"]));
            Assert.Equal(JobStatus.Succeeded, polled.Status);
            _ = Assert.Single(await NoticesAsync());
            Assert.Single(
                fixture.Logs.Entries,
                entry => entry.EventId.Name == "OutboxSendInterrupted" && entry.Message!.Contains(Text(id), StringComparison.Ordinal));
        }

        [Fact]
        public async Task A_row_settled_before_its_job_ran_is_left_as_it_is_and_its_job_succeeds()
        {
            int id = Assert.Single(await EnqueueAsync(Request()));
            await SqlServerFixture.ExecuteAsync(
                fixture.ConnectionString,
                string.Create(CultureInfo.InvariantCulture, $"UPDATE [core].[OutgoingEmails] SET [State] = 'Cancelled' WHERE [Id] = {id};"));

            PolledJob polled = Assert.Single(await DispatchAsync());

            Assert.Empty(fixture.Sender.Captured);
            IReadOnlyDictionary<string, object?> row = await RowAsync(id);
            Assert.Equal((nameof(OutgoingEmailState.Cancelled), (object?)0), (row["State"], row["AttemptCount"]));
            Assert.Equal(JobStatus.Succeeded, polled.Status);
            Assert.Empty(await NoticesAsync());
        }

        [Fact]
        public async Task An_attachment_whose_object_is_gone_rejects_its_email_unsent_and_the_others_still_go()
        {
            EmailEnqueueRequest attached = Request() with
            {
                Message = Request().Message with { Attachments = [new EmailAttachment("report.csv", "text/csv", "a,b\n"u8.ToArray())] },
            };
            int[] ids = [.. await EnqueueAsync(attached, Request())];
            string key = Assert.IsType<string>(Assert.Single(await fixture.RowsAsync(string.Create(
                CultureInfo.InvariantCulture,
                $"SELECT b.[StorageKey] FROM [core].[OutgoingEmailAttachments] AS a JOIN [core].[Blobs] AS b ON b.[Id] = a.[FileId] WHERE a.[OutgoingEmailId] = {ids[0]};")))["StorageKey"]);
            await fixture.Blobs.DeleteAsync(FixtureTenant.TenantId, [key], Token);

            _ = await DispatchAsync();

            // The email without its file is rejected after its pre-mark; the other is sent.
            IReadOnlyDictionary<string, object?> rejected = await RowAsync(ids[0]);
            Assert.Equal((nameof(OutgoingEmailState.Rejected), (object?)OutgoingEmailCodes.AttachmentMissing, (object?)1), (rejected["State"], rejected["ErrorCode"], rejected["AttemptCount"]));
            Assert.Equal(Text(ids[1]), Assert.Single(fixture.Sender.Captured).Message.Correlation!.Reference);
            Assert.Equal(nameof(OutgoingEmailState.Sent), (await RowAsync(ids[1]))["State"]);
            _ = Assert.Single(await NoticesAsync());
        }

        [Fact]
        public async Task A_pre_mark_that_runs_again_returns_the_same_rows_and_counts_the_attempt_once()
        {
            int id = Assert.Single(await EnqueueAsync(Request()));

            IReadOnlySet<int> first = await PreMarkAsync(id);
            IReadOnlySet<int> again = await PreMarkAsync(id);

            Assert.Equal([id], first);
            Assert.Equal([id], again);
            IReadOnlyDictionary<string, object?> row = await RowAsync(id);
            Assert.Equal((nameof(OutgoingEmailState.Sending), (object?)1), (row["State"], row["AttemptCount"]));

            // A row that settled since is no longer one the pre-mark returns.
            await SqlServerFixture.ExecuteAsync(
                fixture.ConnectionString,
                string.Create(CultureInfo.InvariantCulture, $"UPDATE [core].[OutgoingEmails] SET [State] = 'Failed' WHERE [Id] = {id};"));
            Assert.Empty(await PreMarkAsync(id));
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

        /// <summary>An id as invariant text.</summary>
        /// <param name="id">The id.</param>
        /// <returns>The text.</returns>
        private static string Text(int id)
        {
            return id.ToString(CultureInfo.InvariantCulture);
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

        /// <summary>Runs the pre-mark over one row on a maintenance batch of the platform's own scope.</summary>
        /// <param name="id">The row.</param>
        /// <returns>The rows the pre-mark returned as sending.</returns>
        private async Task<IReadOnlySet<int>> PreMarkAsync(int id)
        {
            await using AsyncServiceScope scope = fixture.Tenant.CreateScope(WellKnownIds.SystemUserId);
            var batch = (DataBatch)scope.ServiceProvider.GetRequiredService<ITenantDatabase>().CreateBatch(BatchPurpose.Maintenance);
            batch.TransactionMode = TransactionMode.None;
            BatchResult<IReadOnlySet<int>> marked = OutboxStatements.PreMark(batch, [id]);
            _ = await batch.ExecuteAsync(Token);
            return marked.Value;
        }

        /// <summary>Makes a row's job due now, as its backoff ended.</summary>
        /// <param name="row">The row.</param>
        /// <returns>A task that completes when the job is due.</returns>
        private Task DueNowAsync(IReadOnlyDictionary<string, object?> row)
        {
            return SqlServerFixture.ExecuteAsync(
                fixture.ConnectionString,
                string.Create(CultureInfo.InvariantCulture, $"UPDATE [core].[Jobs] SET [DueAt] = DATEADD(second, -5, SYSUTCDATETIME()) WHERE [Id] = {row["JobId"]};"));
        }

        /// <summary>Reads one outgoing email row.</summary>
        /// <param name="id">The row.</param>
        /// <returns>The row's columns.</returns>
        private async Task<IReadOnlyDictionary<string, object?>> RowAsync(int id)
        {
            return Assert.Single(await fixture.RowsAsync(string.Create(CultureInfo.InvariantCulture, $"SELECT * FROM [core].[OutgoingEmails] WHERE [Id] = {id};")));
        }

        /// <summary>Reads a row's sending job.</summary>
        /// <param name="row">The row.</param>
        /// <returns>The job's columns.</returns>
        private async Task<IReadOnlyDictionary<string, object?>> JobAsync(IReadOnlyDictionary<string, object?> row)
        {
            return Assert.Single(await fixture.RowsAsync(string.Create(CultureInfo.InvariantCulture, $"SELECT * FROM [core].[Jobs] WHERE [Id] = {row["JobId"]};")));
        }

        /// <summary>The failure notices in the inbox.</summary>
        /// <returns>The notices' columns.</returns>
        private Task<IReadOnlyList<IReadOnlyDictionary<string, object?>>> NoticesAsync()
        {
            return fixture.RowsAsync($"SELECT * FROM [core].[Notifications] WHERE [Type] = N'{OutgoingEmailHandler.FailedType}';");
        }
    }
}
