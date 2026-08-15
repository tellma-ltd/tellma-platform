// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Microsoft.Extensions.Time.Testing;
using Tellma.Core.Abstractions.Email;
using Tellma.Core.Testing.Email;

namespace Tellma.Core.Testing.Tests.Email
{
    /// <summary>
    ///     Untested test infrastructure produces false green everywhere downstream, so the capturing
    ///     sender answers for itself.
    /// </summary>
    public class CapturingEmailSenderTests
    {
        [Fact]
        public async Task Captures_every_message_with_its_result_and_lifetime_ordinal()
        {
            FakeTimeProvider time = new(new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero));
            CapturingEmailSender sender = new(time);

            await sender.SendAsync([Message("a"), Message("b")], TestContext.Current.CancellationToken);
            await sender.SendAsync([Message("c")], TestContext.Current.CancellationToken);

            Assert.Equal(["a", "b", "c"], sender.Captured.Select(static c => c.Message.Subject));
            Assert.Equal([0, 1, 2], sender.Captured.Select(static c => c.Ordinal));
            Assert.All(sender.Captured, c => Assert.Equal(time.GetUtcNow(), c.Timestamp));
            Assert.All(sender.Captured, static c => Assert.Equal(EmailSendOutcome.Sent, c.Result.Outcome));
        }

        [Fact]
        public async Task Scripts_results_by_lifetime_ordinal_so_a_retry_can_be_expressed()
        {
            CapturingEmailSender sender = new();
            sender.OnSending(static (_, ordinal) => ordinal == 0
                ? new EmailSendResult(EmailSendOutcome.TransientFailure, Error: "first attempt fails")
                : new EmailSendResult(EmailSendOutcome.Sent, "id"));

            EmailSendResult first = Assert.Single(
                await sender.SendAsync([Message("a")], TestContext.Current.CancellationToken));
            EmailSendResult retry = Assert.Single(
                await sender.SendAsync([Message("a")], TestContext.Current.CancellationToken));

            // "Fail the first attempt, accept the second" is a statement about attempts, not batches.
            Assert.Equal(EmailSendOutcome.TransientFailure, first.Outcome);
            Assert.Equal(EmailSendOutcome.Sent, retry.Outcome);
        }

        [Fact]
        public async Task Rejects_an_invalid_message_like_any_other_transport()
        {
            CapturingEmailSender sender = new();

            IReadOnlyList<EmailSendResult> results = await sender.SendAsync(
                [Message("valid"), Message("invalid") with { To = [] }],
                TestContext.Current.CancellationToken);

            Assert.Equal(EmailSendOutcome.Sent, results[0].Outcome);
            Assert.Equal(EmailSendOutcome.Rejected, results[1].Outcome);
        }

        [Fact]
        public async Task Completes_a_wait_as_soon_as_the_predicate_holds()
        {
            CapturingEmailSender sender = new();

            Task<IReadOnlyList<CapturedEmail>> waiting = sender.WaitForAsync(
                static captured => captured.Any(static c => c.Message.Subject == "expected"),
                TimeSpan.FromSeconds(30));

            Assert.False(waiting.IsCompleted);

            await sender.SendAsync([Message("other")], TestContext.Current.CancellationToken);
            Assert.False(waiting.IsCompleted);

            await sender.SendAsync([Message("expected")], TestContext.Current.CancellationToken);

            IReadOnlyList<CapturedEmail> captured = await waiting;
            Assert.Contains(captured, static c => c.Message.Subject == "expected");
        }

        [Fact]
        public async Task Returns_immediately_when_the_predicate_already_holds()
        {
            CapturingEmailSender sender = new();
            await sender.SendAsync([Message("already-there")], TestContext.Current.CancellationToken);

            IReadOnlyList<CapturedEmail> captured = await sender.WaitForAsync(
                static c => c.Count == 1, TimeSpan.FromSeconds(1));

            Assert.Single(captured);
        }

        [Fact]
        public async Task Times_out_rather_than_hanging_a_suite()
        {
            CapturingEmailSender sender = new();

            await Assert.ThrowsAsync<TimeoutException>(
                () => sender.WaitForAsync(static _ => false, TimeSpan.FromMilliseconds(50)));
        }

        [Fact]
        public async Task Keeps_the_script_and_the_ordinal_across_a_clear()
        {
            CapturingEmailSender sender = new();
            await sender.SendAsync([Message("a")], TestContext.Current.CancellationToken);

            sender.Clear();
            await sender.SendAsync([Message("b")], TestContext.Current.CancellationToken);

            // Clearing narrows what a later assertion sees; it does not restart the scenario.
            CapturedEmail captured = Assert.Single(sender.Captured);
            Assert.Equal("b", captured.Message.Subject);
            Assert.Equal(1, captured.Ordinal);
        }

        [Fact]
        public async Task Captures_safely_from_several_senders_at_once()
        {
            CapturingEmailSender sender = new();

            // Task.Run behind a gate, not a bare Select over an async method: SendAsync completes
            // synchronously, so the lazy sequence would run all 32 calls on this one thread and the
            // test would still pass with the lock removed. Releasing the gate queues every
            // continuation at once, so the calls genuinely overlap.
            TaskCompletionSource gate = new(TaskCreationOptions.RunContinuationsAsynchronously);
            Task[] senders = [.. Enumerable.Range(0, 32).Select(i => Task.Run(
                async () =>
                {
                    await gate.Task;
                    await sender.SendAsync([Message($"m{i}")], TestContext.Current.CancellationToken);
                },
                TestContext.Current.CancellationToken))];

            gate.SetResult();
            await Task.WhenAll(senders);

            Assert.Equal(32, sender.Captured.Count);
            Assert.Equal(32, sender.Captured.Select(static c => c.Ordinal).Distinct().Count());
        }

        private static EmailMessage Message(string subject)
        {
            return new EmailMessage
            {
                To = [new EmailAddress("recipient@example.com")],
                Subject = subject,
                TextBody = "Body",
                Audience = EmailAudience.Internal,
            };
        }
    }
}
