// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Microsoft.Extensions.DependencyInjection;
using Tellma.Core.Abstractions.Email;
using Tellma.Core.Email.Tests.Infrastructure;

namespace Tellma.Core.Email.Tests.Routing
{
    /// <summary>
    ///     The routing matrix is the whole point of the router: which channel a message goes out on,
    ///     and what the caller is told about it.
    /// </summary>
    public class RouterMatrixTests
    {
        [Theory]
        [InlineData(EmailAudience.Internal)]
        [InlineData(EmailAudience.External)]
        public async Task Passes_a_live_tenants_mail_through_untouched(EmailAudience audience)
        {
            FakeEmailSender live = new();
            FakeEmailSender sandbox = new();
            await using EmailTestHost host = Build(live, sandbox);
            host.Sandbox.IsSandbox = false;

            EmailMessage message = EmailTestMessages.Message(audience);
            IReadOnlyList<EmailSendResult> results = await SendAsync(host, [message]);

            Assert.Equal(EmailSendOutcome.Sent, Assert.Single(results).Outcome);
            Assert.Same(message, Assert.Single(live.AllMessages));
            Assert.Empty(sandbox.Batches);
        }

        [Fact]
        public async Task Marks_a_sandbox_tenants_internal_mail_and_sends_it_for_real()
        {
            FakeEmailSender live = new();
            FakeEmailSender sandbox = new();
            await using EmailTestHost host = Build(live, sandbox);
            host.Sandbox.IsSandbox = true;

            IReadOnlyList<EmailSendResult> results =
                await SendAsync(host, [EmailTestMessages.Message(EmailAudience.Internal)]);

            // Real mail to real staff, visibly test-originated.
            Assert.Equal(EmailSendOutcome.Sent, Assert.Single(results).Outcome);
            Assert.StartsWith("[Sandbox] ", Assert.Single(live.AllMessages).Subject, StringComparison.Ordinal);
            Assert.Empty(sandbox.Batches);
        }

        [Fact]
        public async Task Routes_a_sandbox_tenants_external_mail_to_the_sandbox_channel()
        {
            FakeEmailSender live = new();
            FakeEmailSender sandbox = new();
            await using EmailTestHost host = Build(live, sandbox);
            host.Sandbox.IsSandbox = true;

            IReadOnlyList<EmailSendResult> results =
                await SendAsync(host, [EmailTestMessages.Message(EmailAudience.External)]);

            EmailSendResult result = Assert.Single(results);
            Assert.Equal(EmailSendOutcome.Sandboxed, result.Outcome);
            Assert.False(result.ExpectsDeliveryEvents);
            Assert.Single(sandbox.AllMessages);
            Assert.Empty(live.Batches);

            // The sandbox channel is not the marking channel: nothing is delivered, so nothing needs
            // to warn a recipient.
            Assert.Equal("Subject", sandbox.AllMessages.Single().Subject);
        }

        [Fact]
        public async Task Withholds_a_sandbox_tenants_external_mail_when_the_transport_has_no_sandbox_channel()
        {
            FakeEmailSender live = new();
            await using EmailTestHost host = Build(live, sandbox: null);
            host.Sandbox.IsSandbox = true;

            IReadOnlyList<EmailSendResult> results =
                await SendAsync(host, [EmailTestMessages.Message(EmailAudience.External)]);

            EmailSendResult result = Assert.Single(results);
            Assert.Equal(EmailSendOutcome.Sandboxed, result.Outcome);
            Assert.Null(result.ProviderMessageId);

            // No wire activity at all — that is what "withheld" means.
            Assert.Empty(live.Batches);
        }

        [Fact]
        public async Task Rewrites_only_success_on_the_sandbox_channel()
        {
            FakeEmailSender live = new();
            FakeEmailSender sandbox = new()
            {
                Responder = static messages =>
                [
                    new EmailSendResult(EmailSendOutcome.Sent, "provider-1", ExpectsDeliveryEvents: true),
                    new EmailSendResult(EmailSendOutcome.Rejected, Error: "bad address"),
                    new EmailSendResult(EmailSendOutcome.TransientFailure, Error: "throttled"),
                ],
            };

            await using EmailTestHost host = Build(live, sandbox);
            host.Sandbox.IsSandbox = true;

            IReadOnlyList<EmailSendResult> results = await SendAsync(host, [
                EmailTestMessages.Message(EmailAudience.External, "a"),
                EmailTestMessages.Message(EmailAudience.External, "b"),
                EmailTestMessages.Message(EmailAudience.External, "c"),
            ]);

            // A sandbox-channel success becomes Sandboxed and can never expect delivery events;
            // failures are still failures.
            Assert.Equal(EmailSendOutcome.Sandboxed, results[0].Outcome);
            Assert.Equal("provider-1", results[0].ProviderMessageId);
            Assert.False(results[0].ExpectsDeliveryEvents);
            Assert.Equal(EmailSendOutcome.Rejected, results[1].Outcome);
            Assert.Equal(EmailSendOutcome.TransientFailure, results[2].Outcome);
        }

        [Fact]
        public async Task Reassembles_a_mixed_batch_in_input_order()
        {
            FakeEmailSender live = new();
            FakeEmailSender sandbox = new();
            await using EmailTestHost host = Build(live, sandbox);
            host.Sandbox.IsSandbox = true;

            EmailMessage[] messages =
            [
                EmailTestMessages.Message(EmailAudience.External, "external-0"),
                EmailTestMessages.Message(EmailAudience.Internal, "internal-1"),
                EmailTestMessages.Message(EmailAudience.External, "external-2"),
                EmailTestMessages.Message(EmailAudience.Internal, "internal-3"),
            ];

            IReadOnlyList<EmailSendResult> results = await SendAsync(host, messages);

            Assert.Equal(4, results.Count);
            Assert.Equal(EmailSendOutcome.Sandboxed, results[0].Outcome);
            Assert.Equal(EmailSendOutcome.Sent, results[1].Outcome);
            Assert.Equal(EmailSendOutcome.Sandboxed, results[2].Outcome);
            Assert.Equal(EmailSendOutcome.Sent, results[3].Outcome);

            Assert.Equal(2, live.AllMessages.Count());
            Assert.Equal(2, sandbox.AllMessages.Count());
        }

        [Fact]
        public async Task Passes_the_delivery_event_expectation_through_untouched_on_the_live_channel()
        {
            FakeEmailSender live = new()
            {
                Responder = static _ => [new EmailSendResult(EmailSendOutcome.Sent, "id", ExpectsDeliveryEvents: true)],
            };

            await using EmailTestHost host = Build(live, sandbox: null);

            IReadOnlyList<EmailSendResult> results =
                await SendAsync(host, [EmailTestMessages.Message(EmailAudience.External)]);

            Assert.True(Assert.Single(results).ExpectsDeliveryEvents);
        }

        [Fact]
        public async Task Throws_when_the_transport_fails_before_anything_was_handled()
        {
            FakeEmailSender live = new() { ThrowOnSend = new InvalidOperationException("no credentials") };
            await using EmailTestHost host = Build(live, sandbox: null);

            await Assert.ThrowsAsync<InvalidOperationException>(
                () => SendAsync(host, [EmailTestMessages.Message(EmailAudience.Internal)]));
        }

        [Fact]
        public async Task Reports_instead_of_throwing_when_a_later_partition_fails()
        {
            // The live partition runs first and succeeds; the sandbox partition then throws. Throwing
            // now would force the caller to choose between duplicating handled mail and dropping the
            // rest, so the failed partition becomes per-message transient failures.
            FakeEmailSender live = new();
            FakeEmailSender sandbox = new() { ThrowOnSend = new InvalidOperationException("trap is down") };
            await using EmailTestHost host = Build(live, sandbox);
            host.Sandbox.IsSandbox = true;

            IReadOnlyList<EmailSendResult> results = await SendAsync(host, [
                EmailTestMessages.Message(EmailAudience.Internal, "internal"),
                EmailTestMessages.Message(EmailAudience.External, "external"),
            ]);

            Assert.Equal(EmailSendOutcome.Sent, results[0].Outcome);
            Assert.Equal(EmailSendOutcome.TransientFailure, results[1].Outcome);
            Assert.Contains("trap is down", results[1].Error, StringComparison.Ordinal);
        }

        [Fact]
        public async Task Returns_an_empty_result_list_for_an_empty_batch()
        {
            await using EmailTestHost host = Build(new FakeEmailSender(), sandbox: null);

            Assert.Empty(await SendAsync(host, []));
        }

        private static EmailTestHost Build(FakeEmailSender live, FakeEmailSender? sandbox)
        {
            return EmailTestHost.Build(
                new Dictionary<string, string?> { ["Email:Provider"] = "fake" },
                configure: services => services.AddFakeTransport("fake", live, sandbox));
        }

        private static async Task<IReadOnlyList<EmailSendResult>> SendAsync(
            EmailTestHost host, IReadOnlyList<EmailMessage> messages)
        {
            using IServiceScope scope = host.Services.CreateScope();
            IEmailSender sender = scope.ServiceProvider.GetRequiredService<IEmailSender>();
            return await sender.SendAsync(messages, TestContext.Current.CancellationToken);
        }
    }
}
