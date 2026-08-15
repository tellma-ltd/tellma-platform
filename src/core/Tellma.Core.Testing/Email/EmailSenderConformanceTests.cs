// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using System.Globalization;
using Tellma.Core.Abstractions.Email;
using Xunit;

namespace Tellma.Core.Testing.Email
{
    /// <summary>
    ///     The executable form of the <see cref="IEmailSender" /> batch contract. Every email
    ///     transport derives from this and supplies a harness; the cases below are the invariants a
    ///     caller is entitled to rely on regardless of which transport is behind the interface.
    /// </summary>
    /// <remarks>
    ///     Cases a transport cannot express on its own wire are skipped through
    ///     <see cref="SenderCapabilities" />, and every skip reason names the transport limitation —
    ///     so a skip always reads as "not applicable", never as "not implemented".
    /// </remarks>
    public abstract class EmailSenderConformanceTests
    {
        /// <summary>The subject prefix that carries a conformance message's ordinal.</summary>
        public const string SubjectPrefix = "conformance-";

        /// <summary>Creates the harness for one test.</summary>
        /// <returns>The harness; the suite disposes it.</returns>
        protected abstract ValueTask<IEmailSenderHarness> CreateHarnessAsync();

        /// <summary>Builds the message the suite sends for one ordinal.</summary>
        /// <param name="ordinal">The message's ordinal, carried in the subject.</param>
        /// <returns>A minimal, structurally valid message.</returns>
        public static EmailMessage ConformanceMessage(int ordinal)
        {
            return new EmailMessage
            {
                To = [new EmailAddress($"recipient{ordinal.ToString(CultureInfo.InvariantCulture)}@example.com")],
                Subject = SubjectPrefix + ordinal.ToString(CultureInfo.InvariantCulture),
                TextBody = "Conformance body.",
                Audience = EmailAudience.Internal,
            };
        }

        /// <summary>Reads the ordinal a conformance message carries in its subject.</summary>
        /// <param name="subject">The message subject.</param>
        /// <param name="ordinal">The ordinal, or -1.</param>
        /// <returns>True when the subject was a conformance subject.</returns>
        public static bool TryGetOrdinal(string? subject, out int ordinal)
        {
            ordinal = -1;
            return subject is not null
                && subject.StartsWith(SubjectPrefix, StringComparison.Ordinal)
                && int.TryParse(
                    subject.AsSpan(SubjectPrefix.Length), CultureInfo.InvariantCulture, out ordinal);
        }

        /// <summary>Rule 1: one result per message, in input order, always.</summary>
        /// <returns>A task that completes when the case has run.</returns>
        [Fact]
        public async Task Reports_one_result_per_message_in_input_order()
        {
            await using IEmailSenderHarness harness = await CreateHarnessAsync();
            Assert.SkipUnless(
                harness.Capabilities.HasFlag(SenderCapabilities.ScriptedOutcomes),
                "This transport cannot be told to refuse a message, so per-message outcomes cannot be distinguished.");

            EmailMessage[] messages = [.. Enumerable.Range(0, 5).Select(ConformanceMessage)];
            for (int i = 0; i < messages.Length; i++)
            {
                harness.Script(i, i % 2 == 0 ? ScriptedReplyKind.Accept : ScriptedReplyKind.PermanentRefusal);
            }

            IReadOnlyList<EmailSendResult> results =
                await harness.Sender.SendAsync(messages, TestContext.Current.CancellationToken);

            Assert.Equal(messages.Length, results.Count);
            for (int i = 0; i < messages.Length; i++)
            {
                Assert.Equal(
                    i % 2 == 0 ? EmailSendOutcome.Sent : EmailSendOutcome.Rejected,
                    results[i].Outcome);
            }
        }

        /// <summary>
        ///     Rule 3: a structurally invalid message is rejected on its own, never reaches the wire,
        ///     and does not poison the rest of the batch.
        /// </summary>
        /// <returns>A task that completes when the case has run.</returns>
        [Fact]
        public async Task Rejects_an_invalid_message_without_poisoning_the_batch()
        {
            await using IEmailSenderHarness harness = await CreateHarnessAsync();

            EmailMessage valid0 = ConformanceMessage(0);
            EmailMessage noRecipients = ConformanceMessage(1) with { To = [] };
            EmailMessage valid2 = ConformanceMessage(2);
            EmailMessage blankAddress = ConformanceMessage(3) with { To = [new EmailAddress("   ")] };
            EmailMessage valid4 = ConformanceMessage(4);

            IReadOnlyList<EmailSendResult> results = await harness.Sender.SendAsync(
                [valid0, noRecipients, valid2, blankAddress, valid4], TestContext.Current.CancellationToken);

            Assert.Equal(5, results.Count);
            Assert.Equal(EmailSendOutcome.Sent, results[0].Outcome);
            Assert.Equal(EmailSendOutcome.Rejected, results[1].Outcome);
            Assert.Equal(EmailSendOutcome.Sent, results[2].Outcome);
            Assert.Equal(EmailSendOutcome.Rejected, results[3].Outcome);
            Assert.Equal(EmailSendOutcome.Sent, results[4].Outcome);

            Assert.NotNull(results[1].Error);
            Assert.NotNull(results[3].Error);
            Assert.Equal<int>([0, 2, 4], harness.AttemptedOrdinals);
        }

        /// <summary>
        ///     Rule 4: no adapter-level retries — a refused message is attempted exactly once,
        ///     whatever the refusal was.
        /// </summary>
        /// <returns>A task that completes when the case has run.</returns>
        [Fact]
        public async Task Attempts_each_message_at_most_once()
        {
            await using IEmailSenderHarness harness = await CreateHarnessAsync();
            Assert.SkipUnless(
                harness.Capabilities.HasFlag(SenderCapabilities.ScriptedOutcomes),
                "This transport cannot be told to refuse a message, so there is no refusal to retry.");

            EmailMessage[] messages = [.. Enumerable.Range(0, 4).Select(ConformanceMessage)];
            for (int i = 0; i < messages.Length; i++)
            {
                harness.Script(i, ScriptedReplyKind.TransientRefusal);
            }

            IReadOnlyList<EmailSendResult> results =
                await harness.Sender.SendAsync(messages, TestContext.Current.CancellationToken);

            Assert.All(results, static r => Assert.Equal(EmailSendOutcome.TransientFailure, r.Outcome));
            Assert.Equal(
                harness.AttemptedOrdinals.Count,
                harness.AttemptedOrdinals.Distinct().Count());
        }

        /// <summary>
        ///     Rule 2: an up-front authentication failure throws, because the caller knows nothing
        ///     went out and can safely retry the whole batch.
        /// </summary>
        /// <returns>A task that completes when the case has run.</returns>
        [Fact]
        public async Task Throws_when_the_credential_is_refused_and_nothing_was_sent()
        {
            await using IEmailSenderHarness harness = await CreateHarnessAsync();
            Assert.SkipUnless(
                harness.Capabilities.HasFlag(SenderCapabilities.UpFrontAuthFailure),
                "This transport presents no credential, so there is no authentication failure to script.");

            EmailMessage[] messages = [.. Enumerable.Range(0, 6).Select(ConformanceMessage)];
            for (int i = 0; i < messages.Length; i++)
            {
                harness.Script(i, ScriptedReplyKind.AuthFailure);
            }

            await Assert.ThrowsAnyAsync<Exception>(
                () => harness.Sender.SendAsync(messages, TestContext.Current.CancellationToken));

            // It stopped issuing rather than working through the batch: a settled value, read after
            // the send has already failed. The bound is the concurrency limit, because every request
            // in flight when the first refusal lands may still complete, but nothing new may be
            // issued after it — a batch of six against a limit of four leaves two never attempted.
            Assert.True(
                harness.AttemptedOrdinals.Count <= harness.MaxConcurrency,
                $"Expected the batch to stop issuing after the authentication failure, but {harness.AttemptedOrdinals.Count} of {messages.Length} messages were attempted at a concurrency limit of {harness.MaxConcurrency}.");
        }

        /// <summary>
        ///     Rule 2, the other half: once a message has gone out, an authentication failure is
        ///     reported per message instead of thrown — throwing would force the caller to choose
        ///     between duplicating sent mail and dropping unsent mail.
        /// </summary>
        /// <returns>A task that completes when the case has run.</returns>
        [Fact]
        public async Task Reports_instead_of_throwing_when_the_credential_is_refused_after_a_success()
        {
            await using IEmailSenderHarness harness = await CreateHarnessAsync();
            Assert.SkipUnless(
                harness.Capabilities.HasFlag(SenderCapabilities.MidBatchAuthFailure),
                "This transport authenticates once, before anything is sent, so a mid-batch credential failure cannot occur.");

            EmailMessage[] messages = [.. Enumerable.Range(0, 20).Select(ConformanceMessage)];
            harness.Script(0, ScriptedReplyKind.Accept);
            for (int i = 1; i < messages.Length; i++)
            {
                harness.Script(i, ScriptedReplyKind.AuthFailure);
            }

            IReadOnlyList<EmailSendResult> results =
                await harness.Sender.SendAsync(messages, TestContext.Current.CancellationToken);

            Assert.Equal(messages.Length, results.Count);
            Assert.Equal(EmailSendOutcome.Sent, results[0].Outcome);
            Assert.True(
                harness.AttemptedOrdinals.Count < messages.Length,
                "Expected the batch to stop issuing after the authentication failure.");

            // The messages that were actually refused, not just the backfilled remainder: an
            // adapter that translated a 401 to Rejected would drop mail a key rotation would have
            // fixed, and asserting only on an unattempted slot would never notice.
            IReadOnlyList<int> refused = [.. harness.AttemptedOrdinals.Where(static ordinal => ordinal != 0)];
            Assert.NotEmpty(refused);
            foreach (int ordinal in refused)
            {
                Assert.Equal(EmailSendOutcome.TransientFailure, results[ordinal].Outcome);
            }

            // And the remainder that never went out, which must also be retryable rather than lost.
            Assert.Equal(EmailSendOutcome.TransientFailure, results[^1].Outcome);
        }

        /// <summary>
        ///     A throttling response short-circuits the unattempted remainder: hammering a throttling
        ///     endpoint helps no one.
        /// </summary>
        /// <returns>A task that completes when the case has run.</returns>
        [Fact]
        public async Task Short_circuits_the_remainder_when_throttled()
        {
            await using IEmailSenderHarness harness = await CreateHarnessAsync();
            Assert.SkipUnless(
                harness.Capabilities.HasFlag(SenderCapabilities.Throttling),
                "This transport has no throttling signal distinct from an ordinary transient refusal.");

            EmailMessage[] messages = [.. Enumerable.Range(0, 20).Select(ConformanceMessage)];
            for (int i = 0; i < messages.Length; i++)
            {
                harness.Script(i, ScriptedReplyKind.Throttle);
            }

            IReadOnlyList<EmailSendResult> results =
                await harness.Sender.SendAsync(messages, TestContext.Current.CancellationToken);

            Assert.Equal(messages.Length, results.Count);
            Assert.All(results, static r => Assert.Equal(EmailSendOutcome.TransientFailure, r.Outcome));
            Assert.True(
                harness.AttemptedOrdinals.Count < messages.Length,
                $"Expected the throttling response to stop the batch, but {harness.AttemptedOrdinals.Count} of {messages.Length} messages were attempted.");
        }

        /// <summary>
        ///     Rule 5's exemption: when the caller abandons the batch, cancellation propagates rather
        ///     than being reported as a per-message outcome.
        /// </summary>
        /// <returns>A task that completes when the case has run.</returns>
        [Fact]
        public async Task Propagates_cancellation()
        {
            await using IEmailSenderHarness harness = await CreateHarnessAsync();

            EmailMessage[] messages = [.. Enumerable.Range(0, 3).Select(ConformanceMessage)];
            using CancellationTokenSource cancelled = new();
            await cancelled.CancelAsync();

            await Assert.ThrowsAnyAsync<OperationCanceledException>(
                () => harness.Sender.SendAsync(messages, cancelled.Token));

            Assert.Empty(harness.AttemptedOrdinals);
        }
    }
}
