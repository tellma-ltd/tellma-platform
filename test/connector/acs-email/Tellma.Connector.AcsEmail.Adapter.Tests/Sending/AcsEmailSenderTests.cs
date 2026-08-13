// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Azure;
using System.Text.Json;
using Tellma.Connector.AcsEmail.Adapter.Tests.Infrastructure;
using Tellma.Core.Abstractions.Email;
using Tellma.Core.Testing.Email;

// The SDK's own EmailMessage collides with the contract's, so only the one type this file needs from
// Azure.Communication.Email is imported.
using AcsEmailClientOptions = Azure.Communication.Email.EmailClientOptions;

namespace Tellma.Connector.AcsEmail.Adapter.Tests.Sending
{
    /// <summary>
    ///     The outcome table, the message-id stamping, and the one configuration line that keeps the
    ///     Azure SDK from retrying behind the contract's back.
    /// </summary>
    public class AcsEmailSenderTests
    {
        [Fact]
        public async Task Reports_a_queued_message_as_sent_with_the_operation_id()
        {
            // A 202 means ACS has queued the message — the same epistemic state the contract calls
            // "accepted by the transport". The operation is never polled.
            await using AcsSenderHarness harness = new();

            EmailSendResult result = Assert.Single(
                await harness.Sender.SendAsync(Messages(1), TestContext.Current.CancellationToken));

            Assert.Equal(EmailSendOutcome.Sent, result.Outcome);
            Assert.Equal("op-0", result.ProviderMessageId);
        }

        [Fact]
        public void Pins_the_retry_policy_at_zero()
        {
            // Azure.Core would otherwise retry 429 and 5xx three times with backoff, which is exactly
            // the durable retry the contract reserves for the caller. Read off the production
            // factory, so deleting that line fails here rather than passing against a copy.
            AcsEmailOptions options = new() { TimeoutSeconds = 12 };

            AcsEmailClientOptions clientOptions = AcsEmailServiceCollectionExtensions.BuildClientOptions(options);

            Assert.Equal(0, clientOptions.Retry.MaxRetries);
            Assert.Equal(TimeSpan.FromSeconds(12), clientOptions.Retry.NetworkTimeout);
        }

        [Fact]
        public async Task Rejects_a_payload_the_provider_refuses_permanently()
        {
            await using AcsSenderHarness harness = new();
            harness.Script(0, ScriptedReplyKind.PermanentRefusal);

            EmailSendResult result = Assert.Single(
                await harness.Sender.SendAsync(Messages(1), TestContext.Current.CancellationToken));

            Assert.Equal(EmailSendOutcome.Rejected, result.Outcome);
            Assert.Contains("InvalidRecipient", result.Error, StringComparison.Ordinal);
        }

        [Fact]
        public async Task Throws_when_the_credential_is_refused_and_nothing_went_out()
        {
            await using AcsSenderHarness harness = new();
            for (int i = 0; i < 6; i++)
            {
                harness.Script(i, ScriptedReplyKind.AuthFailure);
            }

            await Assert.ThrowsAsync<RequestFailedException>(
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
        public async Task Short_circuits_the_unattempted_remainder_when_throttled()
        {
            // Default ACS quotas are low, so continuing to issue against a throttling resource only
            // deepens the hole.
            await using AcsSenderHarness harness = new();
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
        public async Task Stamps_a_message_id_only_on_correlated_mail()
        {
            await using AcsSenderHarness harness = new();

            EmailMessage correlated = ConformanceMessage(0) with
            {
                Correlation = new EmailCorrelation("outbox", "42", 3),
            };

            await harness.Sender.SendAsync(
                [correlated, ConformanceMessage(1)], TestContext.Current.CancellationToken);

            List<string?> messageIds = [.. harness.RequestBodies.Select(ReadMessageIdHeader)];

            Assert.Contains(messageIds, static id => id is not null && id.StartsWith("<tlm1-", StringComparison.Ordinal));
            Assert.Contains(messageIds, static id => id is null);
        }

        [Fact]
        public async Task Never_asks_acs_to_validate_the_message_id()
        {
            // Under the lenient default an invalid or duplicate id is silently replaced, so the worst
            // case is an event that meters as uncorrelated. Strict validation would instead turn a
            // correlation nicety into a rejected email.
            await using AcsSenderHarness harness = new();

            EmailMessage correlated = ConformanceMessage(0) with
            {
                Correlation = new EmailCorrelation("outbox", "42", 3),
            };

            await harness.Sender.SendAsync([correlated], TestContext.Current.CancellationToken);

            using var body = JsonDocument.Parse(Assert.Single(harness.RequestBodies));
            JsonElement headers = body.RootElement.GetProperty("headers");
            Assert.False(headers.TryGetProperty(AcsEmailMessageMapper.ValidateMessageIdHeaderName, out _));
        }

        [Fact]
        public async Task Expects_delivery_events_only_when_a_webhook_is_configured_and_the_mail_is_correlated()
        {
            AcsEmailOptions withTokens = AcsSenderHarness.DefaultOptions();
            withTokens.Webhook.Tokens.Add("a-token");

            await using AcsSenderHarness configured = new(withTokens);
            await using AcsSenderHarness unconfigured = new();

            EmailMessage correlated = ConformanceMessage(0) with
            {
                Correlation = new EmailCorrelation("outbox", "42"),
            };

            IReadOnlyList<EmailSendResult> withWebhook = await configured.Sender.SendAsync(
                [correlated, ConformanceMessage(1)], TestContext.Current.CancellationToken);
            IReadOnlyList<EmailSendResult> withoutWebhook = await unconfigured.Sender.SendAsync(
                [correlated], TestContext.Current.CancellationToken);

            Assert.True(withWebhook[0].ExpectsDeliveryEvents);
            Assert.False(withWebhook[1].ExpectsDeliveryEvents);
            Assert.False(withoutWebhook[0].ExpectsDeliveryEvents);
        }

        [Fact]
        public async Task Quotes_a_display_name_so_it_cannot_corrupt_the_sender_header()
        {
            AcsEmailOptions options = AcsSenderHarness.DefaultOptions();
            options.From.DisplayName = "Tellma, \"ERP\"";

            await using AcsSenderHarness harness = new(options);
            await harness.Sender.SendAsync(Messages(1), TestContext.Current.CancellationToken);

            using var body = JsonDocument.Parse(Assert.Single(harness.RequestBodies));
            Assert.Equal(
                "\"Tellma, \\\"ERP\\\"\" <no-reply@tellma.com>",
                body.RootElement.GetProperty("senderAddress").GetString());
        }

        private static string? ReadMessageIdHeader(string body)
        {
            using var document = JsonDocument.Parse(body);
            return document.RootElement.TryGetProperty("headers", out JsonElement headers)
                && headers.TryGetProperty(AcsEmailMessageMapper.MessageIdHeaderName, out JsonElement messageId)
                    ? messageId.GetString()
                    : null;
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
