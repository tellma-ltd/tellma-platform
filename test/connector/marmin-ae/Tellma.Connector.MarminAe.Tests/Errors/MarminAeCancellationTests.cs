// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using System.Net;
using Tellma.Connector.MarminAe.Tests.Infrastructure;

namespace Tellma.Connector.MarminAe.Tests.Errors
{
    /// <summary>Giving up, and being given up on, told apart.</summary>
    /// <remarks>
    ///     They are different failures with different remedies — one is the caller's decision and
    ///     the other is a transient condition worth retrying — and a client that reports them
    ///     identically makes that decision impossible.
    /// </remarks>
    public class MarminAeCancellationTests
    {
        [Fact]
        public async Task Reports_a_request_that_outlived_the_timeout_as_a_timeout()
        {
            TaskCompletionSource arrived = new(TaskCreationOptions.RunContinuationsAsynchronously);

            using var harness = MarminAeHarness.Create(timeout: TimeSpan.FromSeconds(30));
            harness.Enqueue(async (_, _, token) =>
            {
                arrived.TrySetResult();
                await Task.Delay(Timeout.Infinite, token);

                return MarminAeResponses.Json(HttpStatusCode.OK, MarminAeOperations.DocumentBody);
            });

            Task call = harness.Client.GetDocumentAsync(
                MarminAeDocumentKind.SalesInvoice,
                MarminAeOperations.DocumentId,
                TestContext.Current.CancellationToken);

            await arrived.Task;
            harness.Time.Advance(TimeSpan.FromSeconds(31));

            await Assert.ThrowsAsync<TimeoutException>(() => call);
        }

        [Fact]
        public async Task Leaves_the_callers_own_token_uncancelled_when_it_times_out()
        {
            TaskCompletionSource arrived = new(TaskCreationOptions.RunContinuationsAsynchronously);
            using CancellationTokenSource caller = new();

            using var harness = MarminAeHarness.Create(timeout: TimeSpan.FromSeconds(30));
            harness.Enqueue(async (_, _, token) =>
            {
                arrived.TrySetResult();
                await Task.Delay(Timeout.Infinite, token);

                return MarminAeResponses.Json(HttpStatusCode.OK, MarminAeOperations.DocumentBody);
            });

            Task call = harness.Client.GetDocumentAsync(
                MarminAeDocumentKind.SalesInvoice, MarminAeOperations.DocumentId, caller.Token);

            await arrived.Task;
            harness.Time.Advance(TimeSpan.FromSeconds(31));
            await Assert.ThrowsAsync<TimeoutException>(() => call);

            Assert.False(caller.IsCancellationRequested);
        }

        [Fact]
        public async Task Surfaces_caller_cancellation_as_cancellation()
        {
            TaskCompletionSource arrived = new(TaskCreationOptions.RunContinuationsAsynchronously);
            using CancellationTokenSource caller = new();

            using var harness = MarminAeHarness.Create();
            harness.Enqueue(async (_, _, token) =>
            {
                arrived.TrySetResult();
                await Task.Delay(Timeout.Infinite, token);

                return MarminAeResponses.Json(HttpStatusCode.OK, MarminAeOperations.DocumentBody);
            });

            Task call = harness.Client.GetDocumentAsync(
                MarminAeDocumentKind.SalesInvoice, MarminAeOperations.DocumentId, caller.Token);

            await arrived.Task;
            await caller.CancelAsync();

            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => call);
        }

        [Fact]
        public async Task Cancels_the_request_that_was_in_flight()
        {
            TaskCompletionSource arrived = new(TaskCreationOptions.RunContinuationsAsynchronously);
            TaskCompletionSource<bool> observed = new(TaskCreationOptions.RunContinuationsAsynchronously);
            using CancellationTokenSource caller = new();

            using var harness = MarminAeHarness.Create();
            harness.Enqueue(async (_, _, token) =>
            {
                arrived.TrySetResult();
                try
                {
                    await Task.Delay(Timeout.Infinite, token);
                }
                catch (OperationCanceledException)
                {
                    // The point of the test: the abandonment reaches the wire rather than merely
                    // being reported to the caller while the request runs on.
                    observed.TrySetResult(true);
                    throw;
                }

                return MarminAeResponses.Json(HttpStatusCode.OK, MarminAeOperations.DocumentBody);
            });

            Task call = harness.Client.GetDocumentAsync(
                MarminAeDocumentKind.SalesInvoice, MarminAeOperations.DocumentId, caller.Token);

            await arrived.Task;
            await caller.CancelAsync();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => call);

            Assert.True(await observed.Task);
        }

        [Fact]
        public async Task Does_not_re_authenticate_after_the_caller_gave_up()
        {
            using CancellationTokenSource caller = new();
            await caller.CancelAsync();

            using var harness = MarminAeHarness.Create();

            await Assert.ThrowsAnyAsync<OperationCanceledException>(
                () => harness.Client.GetDocumentAsync(
                    MarminAeDocumentKind.SalesInvoice, MarminAeOperations.DocumentId, caller.Token));

            Assert.Empty(harness.DataRequests);
        }

        [Fact]
        public async Task Leaves_no_cancelled_token_request_parked_in_the_cache()
        {
            using CancellationTokenSource caller = new();
            TaskCompletionSource arrived = new(TaskCreationOptions.RunContinuationsAsynchronously);
            TaskCompletionSource release = new(TaskCreationOptions.RunContinuationsAsynchronously);

            using var harness = MarminAeHarness.Create();
            harness.OnToken(async (ordinal, _) =>
            {
                arrived.TrySetResult();
                await release.Task;

                return MarminAeResponses.Token(
                    "token-" + ordinal.ToString(System.Globalization.CultureInfo.InvariantCulture),
                    MarminAeHarness.DefaultTokenLifetime);
            });

            Task<string> abandoned = harness.TokenProvider.GetTokenAsync(caller.Token);
            await arrived.Task;
            await caller.CancelAsync();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => abandoned);

            // The fetch itself was never cancelled — one caller giving up must not take the token
            // every other caller is waiting on with it.
            release.SetResult();

            Assert.Equal(
                "token-1", await harness.TokenProvider.GetTokenAsync(TestContext.Current.CancellationToken));
            Assert.Equal(1, harness.TokenRequestCount);
        }
    }
}
