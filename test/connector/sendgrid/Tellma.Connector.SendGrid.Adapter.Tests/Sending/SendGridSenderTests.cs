// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using System.Text.Json;
using Tellma.Connector.SendGrid.Adapter.Tests.Infrastructure;
using Tellma.Core.Abstractions.Email;
using Tellma.Core.Abstractions.Hosting;
using Tellma.Core.Testing.Email;

namespace Tellma.Connector.SendGrid.Adapter.Tests.Sending
{
    /// <summary>
    ///     The outcome table and the payload mapping — the two places where a wrong answer either
    ///     loses mail or duplicates it.
    /// </summary>
    public class SendGridSenderTests
    {
        [Fact]
        public async Task Throws_when_the_key_is_refused_and_nothing_went_out()
        {
            await using SendGridSenderHarness harness = new();
            for (int i = 0; i < 6; i++)
            {
                harness.Script(i, ScriptedReplyKind.AuthFailure);
            }

            // The operator has to fix the key, and the caller can retry the whole batch safely.
            await Assert.ThrowsAsync<SendGridRequestException>(
                () => harness.Sender.SendAsync(Messages(6), TestContext.Current.CancellationToken));

            // "Nothing went out" is the claim in the name, so it is asserted rather than assumed:
            // the batch stopped issuing at the concurrency limit instead of working through all six,
            // and every request it did make was refused.
            Assert.True(
                harness.AttemptedOrdinals.Count <= harness.MaxConcurrency,
                $"Expected at most {harness.MaxConcurrency} attempts before the batch stopped issuing, saw {harness.AttemptedOrdinals.Count}.");
            Assert.Equal(harness.AttemptedOrdinals.Count, harness.RequestBodies.Count);
        }

        [Fact]
        public async Task Reports_the_remainder_when_the_key_is_refused_after_a_success()
        {
            await using SendGridSenderHarness harness = new();
            harness.Options.MaxConcurrency = 1;
            harness.Script(0, ScriptedReplyKind.Accept);
            for (int i = 1; i < 6; i++)
            {
                harness.Script(i, ScriptedReplyKind.AuthFailure);
            }

            IReadOnlyList<EmailSendResult> results =
                await harness.Sender.SendAsync(Messages(6), TestContext.Current.CancellationToken);

            Assert.Equal(EmailSendOutcome.Sent, results[0].Outcome);
            Assert.All(results.Skip(1), static r => Assert.Equal(EmailSendOutcome.TransientFailure, r.Outcome));

            // Exactly two requests: the success, then the refusal that stopped issuing.
            Assert.Equal([0, 1], harness.AttemptedOrdinals);
        }

        [Fact]
        public async Task Short_circuits_the_unattempted_remainder_when_throttled()
        {
            await using SendGridSenderHarness harness = new();
            harness.Options.MaxConcurrency = 1;
            for (int i = 0; i < 6; i++)
            {
                harness.Script(i, ScriptedReplyKind.Throttle);
            }

            IReadOnlyList<EmailSendResult> results =
                await harness.Sender.SendAsync(Messages(6), TestContext.Current.CancellationToken);

            Assert.All(results, static r => Assert.Equal(EmailSendOutcome.TransientFailure, r.Outcome));
            Assert.Equal([0], harness.AttemptedOrdinals);
        }

        [Fact]
        public async Task Rejects_a_payload_the_provider_would_refuse_permanently()
        {
            await using SendGridSenderHarness harness = new();
            harness.Script(0, ScriptedReplyKind.PermanentRefusal);

            EmailSendResult result = Assert.Single(
                await harness.Sender.SendAsync(Messages(1), TestContext.Current.CancellationToken));

            Assert.Equal(EmailSendOutcome.Rejected, result.Outcome);
            Assert.Contains("bad address", result.Error, StringComparison.Ordinal);
        }

        [Fact]
        public async Task Rejects_a_message_over_the_recipient_cap_without_a_request()
        {
            await using SendGridSenderHarness harness = new();

            EmailMessage oversized = ConformanceMessage(0) with
            {
                To = [.. Enumerable.Range(0, 1001).Select(i => new EmailAddress($"r{i}@example.com"))],
            };

            EmailSendResult result = Assert.Single(
                await harness.Sender.SendAsync([oversized], TestContext.Current.CancellationToken));

            Assert.Equal(EmailSendOutcome.Rejected, result.Outcome);
            Assert.Empty(harness.RequestBodies);
        }

        [Fact]
        public async Task Uses_the_configured_sender_when_the_message_carries_none()
        {
            await using SendGridSenderHarness harness = new();

            await harness.Sender.SendAsync(Messages(1), TestContext.Current.CancellationToken);

            using var body = JsonDocument.Parse(Assert.Single(harness.RequestBodies));
            Assert.Equal("no-reply@tellma.com", body.RootElement.GetProperty("from").GetProperty("email").GetString());
        }

        [Fact]
        public async Task Stamps_the_correlation_with_this_deployments_envelope()
        {
            await using SendGridSenderHarness harness = new(
                deployment: new DeploymentIdentity("etpharma", "Staging"));

            EmailMessage correlated = ConformanceMessage(0) with
            {
                Correlation = new EmailCorrelation("outbox", "42", 3),
            };

            await harness.Sender.SendAsync([correlated], TestContext.Current.CancellationToken);

            using var body = JsonDocument.Parse(Assert.Single(harness.RequestBodies));
            Assert.Equal(
                "etpharma-staging:outbox:3:42",
                body.RootElement.GetProperty("custom_args").GetProperty("tellma_correlation").GetString());
        }

        [Fact]
        public async Task Expects_delivery_events_only_with_both_a_verification_key_and_a_correlation()
        {
            SendGridEmailOptions withKeys = SendGridSenderHarness.DefaultOptions();
            withKeys.Webhook.VerificationKeys.Add("a-key");

            await using SendGridSenderHarness configured = new(options: withKeys);
            await using SendGridSenderHarness unconfigured = new();

            EmailMessage correlated = ConformanceMessage(0) with
            {
                Correlation = new EmailCorrelation("outbox", "42"),
            };

            IReadOnlyList<EmailSendResult> withKey =
                await configured.Sender.SendAsync([correlated, ConformanceMessage(1)], TestContext.Current.CancellationToken);
            IReadOnlyList<EmailSendResult> withoutKey =
                await unconfigured.Sender.SendAsync([correlated], TestContext.Current.CancellationToken);

            Assert.True(withKey[0].ExpectsDeliveryEvents);

            // No correlation means no event could be routed even if one arrived.
            Assert.False(withKey[1].ExpectsDeliveryEvents);

            // No verification key means no event would ever be accepted.
            Assert.False(withoutKey[0].ExpectsDeliveryEvents);
        }

        [Fact]
        public async Task Never_expects_delivery_events_on_the_sandbox_channel()
        {
            SendGridEmailOptions withKeys = SendGridSenderHarness.DefaultOptions();
            withKeys.Webhook.VerificationKeys.Add("a-key");

            await using SendGridSenderHarness harness = new(SendGridChannel.Sandbox, withKeys);

            EmailMessage correlated = ConformanceMessage(0) with
            {
                Correlation = new EmailCorrelation("outbox", "42"),
            };

            IReadOnlyList<EmailSendResult> results =
                await harness.Sender.SendAsync([correlated], TestContext.Current.CancellationToken);

            // Sandbox mode emits no events at all.
            Assert.False(Assert.Single(results).ExpectsDeliveryEvents);

            using var body = JsonDocument.Parse(Assert.Single(harness.RequestBodies));
            Assert.True(body.RootElement
                .GetProperty("mail_settings").GetProperty("sandbox_mode").GetProperty("enable").GetBoolean());
        }

        private static EmailMessage ConformanceMessage(int ordinal)
        {
            return EmailSenderConformanceTests.ConformanceMessage(ordinal);
        }

        private static EmailMessage[] Messages(int count)
        {
            return [.. Enumerable.Range(0, count).Select(ConformanceMessage)];
        }
    }
}
