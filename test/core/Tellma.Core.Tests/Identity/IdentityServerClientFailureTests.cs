// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Testing;
using System.Diagnostics;
using System.Net;
using Tellma.Core.Abstractions.Access;
using Tellma.Core.Abstractions.Errors;
using Tellma.Core.Abstractions.Identity;

namespace Tellma.Core.Tests.Identity
{
    /// <summary>
    ///     How a call ends when it cannot finish: before any chunk completed it raises by cause, the
    ///     unreachable server as <see cref="DependencyUnavailableException" /> (a transport failure, a
    ///     <c>5xx</c> or thirty seconds without an answer); after one, any failure or a cancellation
    ///     answers the completed chunks' results.
    /// </summary>
    public sealed class IdentityServerClientFailureTests : IDisposable
    {
        /// <summary>How long a test waits for the client to reach a point it signals; never reached when sound.</summary>
        private static readonly TimeSpan Guard = TimeSpan.FromSeconds(30);

        /// <summary>The client over the fake server.</summary>
        private readonly IdentityClientHarness _harness = new();

        /// <summary>The test's cancellation.</summary>
        private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

        /// <summary>The ways a chunk fails at the server.</summary>
        public enum Failure
        {
            /// <summary>A <c>500</c>.</summary>
            ServerError,

            /// <summary>A <c>503</c>.</summary>
            Unavailable,

            /// <summary>A <c>400</c>.</summary>
            BadRequest,

            /// <summary>No connection.</summary>
            Transport,

            /// <summary>An answer the client cannot read.</summary>
            Unreadable,
        }

        /// <inheritdoc />
        public void Dispose()
        {
            _harness.Dispose();
        }

        // --- Before any chunk: raised by cause ---

        [Theory]
        [InlineData(Failure.ServerError)]
        [InlineData(Failure.Unavailable)]
        [InlineData(Failure.Transport)]
        public async Task An_unreachable_server_before_any_chunk_is_unavailability(Failure failure)
        {
            _harness.Server.Api = (_, _) => Fail(failure);

            DependencyUnavailableException exception = await Assert.ThrowsAsync<DependencyUnavailableException>(
                () => _harness.Client.InviteAsync(IdentityClientHarness.Invitations(1500), Cancellation));

            Assert.Equal(("identity", (TimeSpan?)TimeSpan.FromSeconds(1)), (exception.Dependency, exception.RetryAfter));
            Assert.Single(_harness.Server.ApiRequests);
            Assert.Equal(UsersTelemetryNames.OutcomeUnavailable, Assert.Single(_harness.Calls).GetStructuredStateValue("Outcome"));
        }

        [Fact]
        public async Task Thirty_seconds_without_an_answer_is_unavailability_and_not_a_tick_sooner()
        {
            TaskCompletionSource entered = new(TaskCreationOptions.RunContinuationsAsynchronously);
            _harness.Server.Api = async (_, cancellationToken) =>
            {
                entered.TrySetResult();
                await Task.Delay(Timeout.Infinite, cancellationToken);
                throw new UnreachableException();
            };

            Task<IReadOnlyList<IdentityInvitationResult>> call = _harness.Client.InviteAsync(IdentityClientHarness.Invitations(1), Cancellation);
            await entered.Task.WaitAsync(Guard, Cancellation);

            _harness.Time.Advance(TimeSpan.FromSeconds(30) - TimeSpan.FromTicks(1));
            Assert.False(call.IsCompleted);

            _harness.Time.Advance(TimeSpan.FromTicks(1));
            DependencyUnavailableException exception = await Assert.ThrowsAsync<DependencyUnavailableException>(() => call);
            _ = Assert.IsType<TimeoutException>(exception.InnerException);
        }

        [Fact]
        public async Task A_cancellation_before_any_chunk_is_raised_and_sends_nothing()
        {
            using CancellationTokenSource cancellation = new();
            await cancellation.CancelAsync();

            _ = await Assert.ThrowsAnyAsync<OperationCanceledException>(
                () => _harness.Client.InviteAsync(IdentityClientHarness.Invitations(1), cancellation.Token));

            Assert.Empty(_harness.Server.Requests);
            Assert.Equal(UsersTelemetryNames.OutcomeCancelled, Assert.Single(_harness.Calls).GetStructuredStateValue("Outcome"));
        }

        [Fact]
        public async Task A_cancellation_during_the_first_chunk_is_raised_as_itself_not_as_unavailability()
        {
            using CancellationTokenSource cancellation = new();
            _harness.Server.Api = async (_, cancellationToken) =>
            {
                await cancellation.CancelAsync();
                await Task.Delay(Timeout.Infinite, cancellationToken);
                throw new UnreachableException();
            };

            Exception exception = await Assert.ThrowsAnyAsync<OperationCanceledException>(
                () => _harness.Client.InviteAsync(IdentityClientHarness.Invitations(1500), cancellation.Token));

            Assert.IsNotType<DependencyUnavailableException>(exception);
            Assert.Single(_harness.Server.ApiRequests);
        }

        // --- After a chunk: the prefix ---

        [Theory]
        [InlineData(Failure.ServerError)]
        [InlineData(Failure.Unavailable)]
        [InlineData(Failure.BadRequest)]
        [InlineData(Failure.Transport)]
        [InlineData(Failure.Unreadable)]
        public async Task A_failed_chunk_after_a_completed_one_answers_the_completed_prefix(Failure failure)
        {
            _harness.Server.Api = (request, _) => request.Json()["users"]![0]!["email"]!.GetValue<string>() == "user-0@acme.com"
                ? Task.FromResult(FakeIdentityServer.Echo(request))
                : Fail(failure);
            List<IdentityInvitation> invitations = IdentityClientHarness.Invitations(2500);

            IReadOnlyList<IdentityInvitationResult> results = await _harness.Client.InviteAsync(invitations, Cancellation);

            // The first chunk's results, in order; the third chunk was never sent.
            Assert.Equal(invitations.Take(1000).Select(static invitation => invitation.Email), results.Select(static result => result.Email));
            Assert.Equal(2, _harness.Server.ApiRequests.Count);

            FakeLogRecord stopped = Assert.Single(_harness.Logs, static record => record.Id.Name == "IdentityChunkFailed");
            Assert.Equal(
                (LogLevel.Warning, "1000", "1500"),
                (stopped.Level, stopped.GetStructuredStateValue("Completed"), stopped.GetStructuredStateValue("Remaining")));
            Assert.NotNull(stopped.Exception);
            FakeLogRecord call = Assert.Single(_harness.Calls);
            Assert.Equal(("2500", UsersTelemetryNames.OutcomePartial), (call.GetStructuredStateValue("Count"), call.GetStructuredStateValue("Outcome")));
        }

        [Fact]
        public async Task A_timed_out_chunk_after_a_completed_one_answers_the_completed_prefix()
        {
            TaskCompletionSource entered = new(TaskCreationOptions.RunContinuationsAsynchronously);
            _harness.Server.Api = async (request, cancellationToken) =>
            {
                if (request.Json()["subs"]![0]!.GetValue<string>() == "s-0")
                {
                    return FakeIdentityServer.Echo(request);
                }

                entered.TrySetResult();
                await Task.Delay(Timeout.Infinite, cancellationToken);
                throw new UnreachableException();
            };

            Task<IReadOnlyList<IdentityDeliveryStatus>> call = _harness.Client.GetInvitationDeliveryAsync(
                [.. Enumerable.Range(0, 1200).Select(static i => $"s-{i}")],
                Cancellation);
            await entered.Task.WaitAsync(Guard, Cancellation);
            _harness.Time.Advance(TimeSpan.FromSeconds(30));

            Assert.Equal(1000, (await call).Count);
        }

        [Fact]
        public async Task A_cancellation_between_chunks_answers_the_completed_prefix_and_sends_no_more()
        {
            // The first chunk's activity stops once its results are in; the caller cancels right then.
            using CancellationTokenSource cancellation = new();
            using ActivityRecorder activities = new() { OnStopped = _ => cancellation.Cancel() };

            IReadOnlyList<IdentityInvitationResult> results = await _harness.Client.InviteAsync(IdentityClientHarness.Invitations(2500), cancellation.Token);

            Assert.Equal(1000, results.Count);
            Assert.Single(_harness.Server.ApiRequests);
            Assert.Equal(UsersTelemetryNames.OutcomePartial, Assert.Single(_harness.Calls).GetStructuredStateValue("Outcome"));
        }

        [Fact]
        public async Task A_cancellation_during_a_later_chunk_answers_the_completed_prefix()
        {
            using CancellationTokenSource cancellation = new();
            _harness.Server.Api = async (request, cancellationToken) =>
            {
                if (request.Json()["users"]![0]!["email"]!.GetValue<string>() == "user-0@acme.com")
                {
                    return FakeIdentityServer.Echo(request);
                }

                await cancellation.CancelAsync();
                await Task.Delay(Timeout.Infinite, cancellationToken);
                throw new UnreachableException();
            };

            IReadOnlyList<IdentityInvitationResult> results = await _harness.Client.InviteAsync(IdentityClientHarness.Invitations(2500), cancellation.Token);

            Assert.Equal(1000, results.Count);
            Assert.Equal(2, _harness.Server.ApiRequests.Count);
        }

        // --- The single calls ---

        [Theory]
        [InlineData(Failure.ServerError)]
        [InlineData(Failure.Transport)]
        public async Task An_unreachable_server_fails_a_service_account_call_as_unavailability(Failure failure)
        {
            _harness.Server.Api = (_, _) => Fail(failure);

            _ = await Assert.ThrowsAsync<DependencyUnavailableException>(() => _harness.Client.CreateServiceAccountAsync("Nightly import", Cancellation));
            _ = await Assert.ThrowsAsync<DependencyUnavailableException>(() => _harness.Client.DeleteServiceAccountAsync("acme-sa-1", Cancellation));
        }

        /// <summary>An answer that fails the way <paramref name="failure" /> says.</summary>
        /// <param name="failure">The failure.</param>
        /// <returns>The answer, or the transport's exception.</returns>
        private static Task<HttpResponseMessage> Fail(Failure failure)
        {
            return failure switch
            {
                Failure.ServerError => Task.FromResult(new HttpResponseMessage(HttpStatusCode.InternalServerError)),
                Failure.Unavailable => Task.FromResult(new HttpResponseMessage(HttpStatusCode.ServiceUnavailable)),
                Failure.BadRequest => Task.FromResult(new HttpResponseMessage(HttpStatusCode.BadRequest)),
                Failure.Transport => Task.FromException<HttpResponseMessage>(new HttpRequestException("Connection refused.")),
                Failure.Unreadable => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(/*lang=json,strict*/ "{\"results\":7}") }),
                _ => throw new UnreachableException(),
            };
        }
    }
}
