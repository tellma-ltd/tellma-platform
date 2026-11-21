// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using System.Diagnostics.Metrics;
using Tellma.Core.Abstractions.Access;
using Tellma.Core.Abstractions.Data;
using Tellma.Core.Abstractions.Email;
using Tellma.Core.Abstractions.Jobs;
using Tellma.Core.Abstractions.Notifications;
using Tellma.Core.Abstractions.TableTypes;
using Tellma.Core.Outbox;
using Tellma.Core.Tests.Access;

namespace Tellma.Core.Tests.Outbox
{
    /// <summary>
    ///     The sending job's rules with no database: how each result of the email sender settles its
    ///     row and ends its job, the row's message rebuilt from its stored columns and correlated with
    ///     it, a provider's detail cut to fit its column, and the failure notice's type; and the claims
    ///     that end before anything is written: a host with no sender, a host stopping or a lease lost
    ///     before the pre-mark, and rows whose jobs are being cancelled.
    /// </summary>
    public sealed class OutgoingEmailHandlerTests
    {
        [Fact]
        public async Task A_host_with_no_email_sender_fails_the_claim_before_it_reads_or_writes_anything()
        {
            // The runner and the database refuse every call, so a load or a write fails the test.
            await using ServiceProvider services = new ServiceCollection().AddMetrics().BuildServiceProvider();
            JobItem<OutgoingEmail> item = Item(1);

            InvalidOperationException refused = await Assert.ThrowsAsync<InvalidOperationException>(
                () => Handler(services, Refusing.Of<IGuardedBatchRunner>()).ExecuteAsync(new JobBatch<OutgoingEmail> { Items = [item], Batch = Refusing.Of<IDataBatch>() }));

            Assert.Contains("No email sender is registered", refused.Message, StringComparison.Ordinal);
            Assert.Null(item.Mark);
        }

        [Theory]
        [InlineData(JobCancellationReason.HostStopping)]
        [InlineData(JobCancellationReason.LeaseLost)]
        public async Task A_claim_whose_host_stops_or_whose_lease_is_lost_ends_before_the_pre_mark_with_every_item_unmarked(JobCancellationReason reason)
        {
            // The attachments load answers none; the sender and the database refuse every call, so a
            // pre-mark or a send fails the test.
            await using ServiceProvider services = new ServiceCollection().AddMetrics().AddSingleton(Refusing.Of<IEmailSender>()).BuildServiceProvider();
            JobItem<OutgoingEmail> stopped = Item(1);
            JobItem<OutgoingEmail> other = Item(2);
            Assert.True(stopped.TrySetCancellationReason(reason));

            _ = await Assert.ThrowsAsync<OperationCanceledException>(
                () => Handler(services, new NoAttachments()).ExecuteAsync(new JobBatch<OutgoingEmail> { Items = [other, stopped], Batch = Refusing.Of<IDataBatch>() }));

            Assert.Null(stopped.Mark);
            Assert.Null(other.Mark);
        }

        [Fact]
        public async Task Pending_rows_whose_jobs_are_being_cancelled_are_left_unmarked_and_nothing_is_sent_or_written()
        {
            await using ServiceProvider services = new ServiceCollection().AddMetrics().AddSingleton(Refusing.Of<IEmailSender>()).BuildServiceProvider();
            JobItem<OutgoingEmail> first = Item(1);
            JobItem<OutgoingEmail> second = Item(2);
            Assert.True(first.TrySetCancellationReason(JobCancellationReason.CancelRequested));
            Assert.True(second.TrySetCancellationReason(JobCancellationReason.CancelRequested));

            await Handler(services, Refusing.Of<IGuardedBatchRunner>()).ExecuteAsync(new JobBatch<OutgoingEmail> { Items = [first, second], Batch = Refusing.Of<IDataBatch>() });

            // Unmarked, each job ends cancelled and its row stays pending.
            Assert.Null(first.Mark);
            Assert.Null(second.Mark);
        }
        [Fact]
        public void A_sent_email_settles_sent_with_its_provider_id_cut_to_its_column_and_its_job_succeeds()
        {
            JobItem<OutgoingEmail> item = Item(7);
            string provider = new('p', OutgoingEmail.ProviderMessageIdLength + 20);

            var verdict = Verdict.Of(item, new EmailSendResult(EmailSendOutcome.Sent, provider, ExpectsDeliveryEvents: true));
            verdict.Mark();

            OutgoingEmailOutcomeList outcome = Assert.IsType<OutgoingEmailOutcomeList>(verdict.Outcome);
            Assert.Equal((7, OutgoingEmailState.Sent, true, null), (outcome.Id, outcome.State, outcome.ExpectsDeliveryEvents, outcome.ErrorCode));
            Assert.Equal(provider[..OutgoingEmail.ProviderMessageIdLength], outcome.ProviderMessageId);
            Assert.False(verdict.Notifies);
            Assert.Equal(JobItemMarkKind.Succeeded, item.Mark?.Kind);
        }

        [Fact]
        public void A_sandboxed_email_keeps_no_provider_id_and_expects_no_delivery_events()
        {
            JobItem<OutgoingEmail> item = Item(7);

            var verdict = Verdict.Of(item, new EmailSendResult(EmailSendOutcome.Sandboxed, "sandbox-1", ExpectsDeliveryEvents: true));
            verdict.Mark();

            OutgoingEmailOutcomeList outcome = Assert.IsType<OutgoingEmailOutcomeList>(verdict.Outcome);
            Assert.Equal((OutgoingEmailState.Sandboxed, null, false), (outcome.State, outcome.ProviderMessageId, outcome.ExpectsDeliveryEvents));
            Assert.False(verdict.Notifies);
            Assert.Equal(JobItemMarkKind.Succeeded, item.Mark?.Kind);
        }

        [Fact]
        public void A_rejected_email_settles_rejected_with_the_provider_detail_and_its_job_succeeds_with_a_notice()
        {
            JobItem<OutgoingEmail> item = Item(7);

            var verdict = Verdict.Of(item, new EmailSendResult(EmailSendOutcome.Rejected, Error: "Mailbox unavailable."));
            verdict.Mark();

            OutgoingEmailOutcomeList outcome = Assert.IsType<OutgoingEmailOutcomeList>(verdict.Outcome);
            Assert.Equal((OutgoingEmailState.Rejected, OutgoingEmailCodes.Rejected, "Mailbox unavailable."), (outcome.State, outcome.ErrorCode, outcome.ErrorDetail));
            Assert.True(verdict.Notifies);
            Assert.Equal(JobItemMarkKind.Succeeded, item.Mark?.Kind);
        }

        [Theory]
        [InlineData(1)]
        [InlineData(11)]
        public void A_transient_failure_with_attempts_left_returns_the_row_to_pending_and_retries_its_job(int attempts)
        {
            JobItem<OutgoingEmail> item = Item(7, attempts);

            var verdict = Verdict.Of(item, new EmailSendResult(EmailSendOutcome.TransientFailure, Error: "Throttled."));
            verdict.Mark();

            // The row records why it waits; the job records the same code and keeps its backoff.
            OutgoingEmailOutcomeList outcome = Assert.IsType<OutgoingEmailOutcomeList>(verdict.Outcome);
            Assert.Equal((OutgoingEmailState.Pending, OutgoingEmailCodes.TransientFailure, "Throttled."), (outcome.State, outcome.ErrorCode, outcome.ErrorDetail));
            Assert.False(verdict.Notifies);
            JobItemMark mark = Assert.IsType<JobItemMark>(item.Mark);
            Assert.Equal((JobItemMarkKind.Retry, null, OutgoingEmailCodes.TransientFailure), (mark.Kind, mark.RetryAfter, mark.Error?.Code));
        }

        [Fact]
        public void A_transient_failure_on_the_last_attempt_fails_the_row_and_succeeds_its_job_with_a_notice()
        {
            JobItem<OutgoingEmail> item = Item(7, OutgoingEmailHandler.MaxAttempts);

            var verdict = Verdict.Of(item, new EmailSendResult(EmailSendOutcome.TransientFailure, Error: "Throttled."));
            verdict.Mark();

            OutgoingEmailOutcomeList outcome = Assert.IsType<OutgoingEmailOutcomeList>(verdict.Outcome);
            Assert.Equal((OutgoingEmailState.Failed, OutgoingEmailCodes.AttemptsExhausted, "Throttled."), (outcome.State, outcome.ErrorCode, outcome.ErrorDetail));
            Assert.True(verdict.Notifies);
            Assert.Equal(JobItemMarkKind.Succeeded, item.Mark?.Kind);
        }

        [Fact]
        public void A_row_found_still_sending_fails_as_interrupted_and_its_job_succeeds_with_a_notice()
        {
            JobItem<OutgoingEmail> item = Item(7);

            var verdict = Verdict.Interrupted(item);
            verdict.Mark();

            OutgoingEmailOutcomeList outcome = Assert.IsType<OutgoingEmailOutcomeList>(verdict.Outcome);
            Assert.Equal((OutgoingEmailState.Failed, OutgoingEmailCodes.Interrupted, null), (outcome.State, outcome.ErrorCode, outcome.ErrorDetail));
            Assert.True(verdict.IsInterrupted);
            Assert.True(verdict.Notifies);
            Assert.Equal(JobItemMarkKind.Succeeded, item.Mark?.Kind);
        }

        [Fact]
        public void An_unsendable_row_is_rejected_with_its_code_and_a_settled_row_is_left_as_it_is()
        {
            var refused = Verdict.Refused(Item(7), OutgoingEmailCodes.AttachmentMissing);
            var unsent = Verdict.Unsent(Item(8));

            Assert.Equal((OutgoingEmailState.Rejected, OutgoingEmailCodes.AttachmentMissing), (refused.Outcome?.State, refused.Outcome?.ErrorCode));
            Assert.True(refused.Notifies);
            Assert.Null(unsent.Outcome);
            Assert.False(unsent.Notifies);
        }

        [Fact]
        public void An_outcome_the_email_pipeline_does_not_define_is_refused()
        {
            _ = Assert.Throws<InvalidOperationException>(() => Verdict.Of(Item(7), new EmailSendResult((EmailSendOutcome)99)));
        }

        [Fact]
        public void A_detail_is_kept_whole_when_it_fits_cut_before_a_whole_character_when_not_and_dropped_when_blank()
        {
            string fits = new('d', OutgoingEmail.ErrorDetailLength);
            string split = new string('d', OutgoingEmail.ErrorDetailLength - 1) + "\U0001F600" + "tail";

            Assert.Null(OutgoingEmailHandler.Detail(null));
            Assert.Null(OutgoingEmailHandler.Detail("  "));
            Assert.Equal(fits, OutgoingEmailHandler.Detail(fits));
            Assert.Equal(fits, OutgoingEmailHandler.Detail(fits + "more"));
            Assert.Equal(new string('d', OutgoingEmail.ErrorDetailLength - 1), OutgoingEmailHandler.Detail(split));
        }

        [Fact]
        public void A_message_is_rebuilt_from_the_stored_columns_and_correlated_with_its_row_and_tenant()
        {
            OutgoingEmail row = Row(7);
            row.From = "Acme <noreply@acme.example>";
            row.Cc = "Bob <bob@example.com>, cy@example.com";
            row.HtmlBody = "<p>Hello</p>";
            row.Audience = EmailAudience.External;
            EmailAttachment file = new("a.txt", "text/plain", new byte[] { 1, 2, 3 });

            EmailMessage message = OutgoingEmailHandler.Message(row, [file], tenantId: 5);

            Assert.Equal(new EmailAddress("noreply@acme.example", "Acme"), message.From);
            Assert.Null(message.ReplyTo);
            Assert.Equal([new EmailAddress("ada@example.com", "Ada")], message.To);
            Assert.Equal([new EmailAddress("bob@example.com", "Bob"), new EmailAddress("cy@example.com")], message.Cc);
            Assert.Empty(message.Bcc);
            Assert.Equal(("Hello", "Hello there.", "<p>Hello</p>", EmailAudience.External), (message.Subject, message.TextBody, message.HtmlBody, message.Audience));
            Assert.Same(file, Assert.Single(message.Attachments));
            Assert.Equal(new EmailCorrelation(OutgoingEmailHandler.CorrelationOwnerKey, "7", 5), message.Correlation);
        }

        [Fact]
        public void A_single_address_column_holding_several_addresses_does_not_parse()
        {
            OutgoingEmail row = Row(7);
            row.ReplyTo = "a@example.com, b@example.com";

            _ = Assert.Throws<FormatException>(() => OutgoingEmailHandler.Message(row, [], tenantId: 5));
        }

        [Fact]
        public void The_failure_notice_is_an_error_on_the_outgoing_email_kept_to_the_inbox()
        {
            NotificationTypeDescriptor descriptor = OutgoingEmailHandler.FailedDescriptor;

            Assert.Equal(
                ("core.email.failed", "jobs", false, CoreResources.OutgoingEmail, NotificationSeverity.Error, true, true),
                (descriptor.Key, descriptor.Category, descriptor.Mutable, descriptor.TargetResource, descriptor.Severity, descriptor.Toast, descriptor.Inbox));
        }

        /// <summary>
        ///     A handler of tenant 1 whose database and administrator directory refuse every call, so a
        ///     test that reaches either fails.
        /// </summary>
        /// <param name="services">The job scope's services, which the sender is resolved from.</param>
        /// <param name="runner">The runner of the attachments' load.</param>
        /// <returns>The handler.</returns>
        private static OutgoingEmailHandler Handler(ServiceProvider services, IGuardedBatchRunner runner)
        {
            return new OutgoingEmailHandler(
                runner,
                Refusing.Of<ITenantDatabase>(),
                services,
                new FixedContextAccessor(AccessRulesHarness.Context(userId: null)),
                Refusing.Of<IAdministratorDirectory>(),
                new OutboxMetrics(services.GetRequiredService<IMeterFactory>()),
                NullLoggerFactory.Instance);
        }

        /// <summary>A pending row to Ada.</summary>
        /// <param name="id">The row's id.</param>
        /// <returns>The row.</returns>
        private static OutgoingEmail Row(int id)
        {
            return new OutgoingEmail
            {
                Id = id,
                State = OutgoingEmailState.Pending,
                To = "Ada <ada@example.com>",
                Subject = "Hello",
                TextBody = "Hello there.",
            };
        }

        /// <summary>The item of a pending row's job.</summary>
        /// <param name="id">The row's id.</param>
        /// <param name="attempts">The job's attempts, this one included.</param>
        /// <returns>The item.</returns>
        private static JobItem<OutgoingEmail> Item(int id, int attempts = 1)
        {
            return new JobItem<OutgoingEmail>
            {
                Job = new Job { Id = id + 100, HandlerKey = OutgoingEmailService.SendHandlerKey, Attempts = attempts },
                Item = Row(id),
                Progress = new SilentProgress(),
            };
        }

        /// <summary>A runner whose every read answers that the rows carry no attachments.</summary>
        private sealed class NoAttachments : IGuardedBatchRunner
        {
            /// <inheritdoc />
            public Task<TResult> RunAsync<TResult>(
                BatchPurpose purpose,
                Func<ConnectedUser, IDataBatch, ValueTask> compose,
                Func<ConnectedUser, BatchOutcome, TResult> read,
                CancellationToken cancellationToken = default)
            {
                return Task.FromResult((TResult)(object)Array.Empty<DispatchAttachment>());
            }
        }

        /// <summary>A progress that reports nowhere.</summary>
        private sealed class SilentProgress : IJobProgress
        {
            /// <inheritdoc />
            public void Report(byte? percent, JobMessage? message, object? state)
            {
            }

            /// <inheritdoc />
            public Task FlushAsync(CancellationToken cancellationToken = default)
            {
                return Task.CompletedTask;
            }

            /// <inheritdoc />
            public void Append(IDataBatch batch, byte? percent, JobMessage? message, object? state)
            {
            }
        }
    }
}
