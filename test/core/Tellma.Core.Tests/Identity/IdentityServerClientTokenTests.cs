// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using System.Diagnostics;
using System.Net;
using System.Text;
using System.Text.Json.Nodes;
using Tellma.Core.Abstractions.Errors;
using Tellma.Core.Abstractions.Identity;

namespace Tellma.Core.Tests.Identity
{
    /// <summary>
    ///     The service token: cached until sixty seconds before it expires (a day at most), fetched by
    ///     one flight that every concurrent caller awaits, its failure shared and its run untouched by
    ///     a caller that gives up, discarded and refetched once on a <c>401</c> without discarding a
    ///     fresher one, and every token-endpoint failure classified.
    /// </summary>
    public sealed class IdentityServerClientTokenTests : IDisposable
    {
        /// <summary>How long a test waits for the client to reach a point it signals; never reached when sound.</summary>
        private static readonly TimeSpan Guard = TimeSpan.FromSeconds(30);

        /// <summary>The client over the fake server.</summary>
        private readonly IdentityClientHarness _harness = new();

        /// <summary>The test's cancellation.</summary>
        private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

        /// <inheritdoc />
        public void Dispose()
        {
            _harness.Dispose();
        }

        // --- The cache ---

        [Fact]
        public async Task A_token_is_reused_until_sixty_seconds_before_it_expires()
        {
            _ = await Invite();

            // An hour's token, issued at the start: reused up to the tick before its refresh time...
            _harness.Time.Advance(TimeSpan.FromSeconds(3600 - 60) - TimeSpan.FromTicks(1));
            _ = await Invite();
            Assert.Single(_harness.Server.TokenRequests);

            // ...and replaced at it.
            _harness.Time.Advance(TimeSpan.FromTicks(1));
            _ = await Invite();
            Assert.Equal(2, _harness.Server.TokenRequests.Count);
            Assert.Equal(["Bearer token-1", "Bearer token-1", "Bearer token-2"], Bearers());
        }

        [Fact]
        public async Task A_token_without_a_lifetime_is_used_for_one_call()
        {
            using IdentityClientHarness harness = new(expiresIn: null);

            _ = await harness.Client.InviteAsync(IdentityClientHarness.Invitations(1), Cancellation);
            _ = await harness.Client.InviteAsync(IdentityClientHarness.Invitations(1), Cancellation);

            Assert.Equal(2, harness.Server.TokenRequests.Count);
        }

        [Fact]
        public async Task Concurrent_callers_without_a_token_share_one_fetch()
        {
            // The first caller's fetch is held at the server; the others start while it is held.
            TaskCompletionSource fetching = new(TaskCreationOptions.RunContinuationsAsynchronously);
            TaskCompletionSource issue = new(TaskCreationOptions.RunContinuationsAsynchronously);
            _harness.Server.Token = async (_, number, cancellationToken) =>
            {
                fetching.TrySetResult();
                await issue.Task.WaitAsync(cancellationToken);
                return FakeIdentityServer.TokenAnswer("token-" + number, 3600);
            };

            List<Task<IReadOnlyList<IdentityInvitationResult>>> calls = [.. Enumerable.Range(0, 8).Select(i => _harness.Client.InviteAsync(
                [new IdentityInvitation($"user-{i}@acme.com", null, null, null, null)],
                Cancellation))];
            await fetching.Task.WaitAsync(Guard, Cancellation);

            // Every caller is waiting for the one fetch: none has fetched another or reached the API.
            Assert.Single(_harness.Server.TokenRequests);
            Assert.Empty(_harness.Server.ApiRequests);
            Assert.DoesNotContain(calls, static call => call.IsCompleted);

            issue.SetResult();
            _ = await Task.WhenAll(calls);

            Assert.Single(_harness.Server.TokenRequests);
            Assert.Equal(Enumerable.Repeat("Bearer token-1", 8), Bearers());
        }

        [Fact]
        public async Task A_failed_fetch_fails_every_caller_awaiting_it_with_one_request()
        {
            // The first caller's fetch is held at the server and then refused with a 503.
            TaskCompletionSource fetching = new(TaskCreationOptions.RunContinuationsAsynchronously);
            TaskCompletionSource answer = new(TaskCreationOptions.RunContinuationsAsynchronously);
            _harness.Server.Token = async (_, number, cancellationToken) =>
            {
                if (number > 1)
                {
                    return FakeIdentityServer.TokenAnswer("token-" + number, 3600);
                }

                fetching.TrySetResult();
                await answer.Task.WaitAsync(cancellationToken);
                return new HttpResponseMessage(HttpStatusCode.ServiceUnavailable);
            };

            List<Task<IReadOnlyList<IdentityInvitationResult>>> calls = [.. Enumerable.Range(0, 8).Select(_ => Invite())];
            await fetching.Task.WaitAsync(Guard, Cancellation);
            answer.SetResult();

            // Every caller fails with the one fetch's outage; none fetched again.
            foreach (Task<IReadOnlyList<IdentityInvitationResult>> call in calls)
            {
                _ = await Assert.ThrowsAsync<DependencyUnavailableException>(() => call.WaitAsync(Guard, Cancellation));
            }

            Assert.Single(_harness.Server.TokenRequests);
            Assert.Empty(_harness.Server.ApiRequests);

            // The failed flight is over: the next call starts a fresh one.
            _ = await Invite();
            Assert.Equal(2, _harness.Server.TokenRequests.Count);
        }

        [Fact]
        public async Task A_hung_token_endpoint_fails_every_caller_at_thirty_seconds()
        {
            TaskCompletionSource fetching = new(TaskCreationOptions.RunContinuationsAsynchronously);
            _harness.Server.Token = async (_, _, cancellationToken) =>
            {
                fetching.TrySetResult();
                await Task.Delay(Timeout.Infinite, cancellationToken);
                throw new UnreachableException();
            };

            List<Task<IReadOnlyList<IdentityInvitationResult>>> calls = [.. Enumerable.Range(0, 3).Select(_ => Invite())];
            await fetching.Task.WaitAsync(Guard, Cancellation);

            _harness.Time.Advance(TimeSpan.FromSeconds(30) - TimeSpan.FromTicks(1));
            Assert.DoesNotContain(calls, static call => call.IsCompleted);

            // One timeout, at thirty seconds, fails all three: none waits for a fetch of its own.
            _harness.Time.Advance(TimeSpan.FromTicks(1));
            foreach (Task<IReadOnlyList<IdentityInvitationResult>> call in calls)
            {
                DependencyUnavailableException exception = await Assert.ThrowsAsync<DependencyUnavailableException>(() => call.WaitAsync(Guard, Cancellation));
                _ = Assert.IsType<TimeoutException>(exception.InnerException);
            }

            Assert.Single(_harness.Server.TokenRequests);
        }

        [Fact]
        public async Task The_starting_caller_s_cancellation_ends_only_its_own_wait()
        {
            // The fetch the first caller starts is held at the server until released.
            TaskCompletionSource fetching = new(TaskCreationOptions.RunContinuationsAsynchronously);
            TaskCompletionSource issue = new(TaskCreationOptions.RunContinuationsAsynchronously);
            _harness.Server.Token = async (_, number, cancellationToken) =>
            {
                fetching.TrySetResult();
                await issue.Task.WaitAsync(cancellationToken);
                return FakeIdentityServer.TokenAnswer("token-" + number, 3600);
            };
            using CancellationTokenSource starter = new();

            Task<IReadOnlyList<IdentityInvitationResult>> first = _harness.Client.InviteAsync(IdentityClientHarness.Invitations(1), starter.Token);
            await fetching.Task.WaitAsync(Guard, Cancellation);
            List<Task<IReadOnlyList<IdentityInvitationResult>>> others = [Invite(), Invite()];

            // The starter gives up: it alone is cancelled, and the fetch goes on.
            await starter.CancelAsync();
            _ = await Assert.ThrowsAnyAsync<OperationCanceledException>(() => first.WaitAsync(Guard, Cancellation));
            Assert.DoesNotContain(others, static call => call.IsCompleted);

            issue.SetResult();
            _ = await Task.WhenAll(others).WaitAsync(Guard, Cancellation);

            Assert.Single(_harness.Server.TokenRequests);
            Assert.Equal(["Bearer token-1", "Bearer token-1"], Bearers());
        }

        [Fact]
        public async Task A_lifetime_beyond_a_day_is_reused_for_a_day_at_most()
        {
            _harness.Server.Token = static (_, number, _) => Task.FromResult(FakeIdentityServer.TokenAnswer("token-" + number, 99999999999999));

            _ = await Invite();
            _harness.Time.Advance(TimeSpan.FromDays(1) - TimeSpan.FromSeconds(60) - TimeSpan.FromTicks(1));
            _ = await Invite();
            Assert.Single(_harness.Server.TokenRequests);

            _harness.Time.Advance(TimeSpan.FromTicks(1));
            _ = await Invite();
            Assert.Equal(["Bearer token-1", "Bearer token-1", "Bearer token-2"], Bearers());
        }

        [Fact]
        public async Task A_failed_fetch_is_not_cached_and_the_next_call_fetches_again()
        {
            _harness.Server.Token = static (_, number, _) => Task.FromResult(number == 1
                ? new HttpResponseMessage(HttpStatusCode.ServiceUnavailable)
                : FakeIdentityServer.TokenAnswer("token-" + number, 3600));

            _ = await Assert.ThrowsAsync<DependencyUnavailableException>(Invite);
            _ = await Invite();

            Assert.Equal(["Bearer token-2"], Bearers());
        }

        // --- A 401 ---

        [Fact]
        public async Task One_401_discards_the_token_and_retries_once_with_a_fresh_one()
        {
            _harness.Server.Api = static (request, _) => Task.FromResult(request.Authorization == "Bearer token-1"
                ? new HttpResponseMessage(HttpStatusCode.Unauthorized)
                : FakeIdentityServer.Echo(request));

            IReadOnlyList<IdentityInvitationResult> results = await Invite();

            Assert.Single(results);
            Assert.Equal(["Bearer token-1", "Bearer token-2"], Bearers());

            // The fresh token is the cached one now.
            _ = await Invite();
            Assert.Equal(2, _harness.Server.TokenRequests.Count);
        }

        [Fact]
        public async Task A_second_401_is_a_refusal_and_is_not_retried_again()
        {
            _harness.Server.Api = static (_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.Unauthorized));

            InvalidOperationException exception = await Assert.ThrowsAsync<InvalidOperationException>(Invite);

            Assert.Contains("acme-svc", exception.Message, StringComparison.Ordinal);
            Assert.Equal(["Bearer token-1", "Bearer token-2"], Bearers());
            Assert.Equal(2, _harness.Server.TokenRequests.Count);
        }

        [Fact]
        public async Task A_401_answered_after_a_concurrent_caller_replaced_the_token_does_not_discard_the_replacement()
        {
            // --- token-1 is cached, then revoked: the server refuses it from now on ---
            _ = await Invite();
            TaskCompletionSource[] arrived = [new(TaskCreationOptions.RunContinuationsAsynchronously), new(TaskCreationOptions.RunContinuationsAsynchronously)];
            TaskCompletionSource[] refuse = [new(TaskCreationOptions.RunContinuationsAsynchronously), new(TaskCreationOptions.RunContinuationsAsynchronously)];
            TaskCompletionSource retried = new(TaskCreationOptions.RunContinuationsAsynchronously);
            int refused = 0;
            _harness.Server.Api = async (request, cancellationToken) =>
            {
                if (request.Authorization == "Bearer token-1")
                {
                    int i = Interlocked.Increment(ref refused) - 1;
                    arrived[i].SetResult();
                    await refuse[i].Task.WaitAsync(cancellationToken);
                    return new HttpResponseMessage(HttpStatusCode.Unauthorized);
                }

                retried.TrySetResult();
                return FakeIdentityServer.Echo(request);
            };

            // --- Two callers send token-1 and both are held until refused ---
            Task<IReadOnlyList<IdentityInvitationResult>> first = Invite();
            Task<IReadOnlyList<IdentityInvitationResult>> second = Invite();
            await Task.WhenAll(arrived.Select(static signal => signal.Task)).WaitAsync(Guard, Cancellation);

            // --- The first is refused, fetches token-2 and retries with it; only then the second ---
            refuse[0].SetResult();
            await retried.Task.WaitAsync(Guard, Cancellation);
            refuse[1].SetResult();
            _ = await Task.WhenAll(first, second);

            // The second caller's 401 was for token-1, so it kept token-2 rather than fetch a third.
            Assert.Equal(2, _harness.Server.TokenRequests.Count);
            Assert.Equal(["Bearer token-1", "Bearer token-1", "Bearer token-1", "Bearer token-2", "Bearer token-2"], Bearers());
        }

        // --- The token endpoint's failures ---

        [Theory]
        [InlineData(HttpStatusCode.BadRequest)]
        [InlineData(HttpStatusCode.Unauthorized)]
        [InlineData(HttpStatusCode.Forbidden)]
        public async Task A_token_endpoint_4xx_is_a_refusal_quoting_only_the_error_code(HttpStatusCode status)
        {
            _harness.Server.Token = (_, _, _) => Task.FromResult(FakeIdentityServer.JsonAnswer(
                status,
                new JsonObject { ["error"] = "invalid_client", ["error_description"] = "The secret " + IdentityClientHarness.Secret + " is wrong." }));

            InvalidOperationException exception = await Assert.ThrowsAsync<InvalidOperationException>(Invite);

            Assert.Contains("invalid_client", exception.Message, StringComparison.Ordinal);
            Assert.DoesNotContain(IdentityClientHarness.Secret, exception.ToString(), StringComparison.Ordinal);
            Assert.Empty(_harness.Server.ApiRequests);
        }

        [Theory]
        [InlineData(/*lang=json,strict*/ """{"error":"bad \"code\" here"}""")]
        [InlineData(/*lang=json,strict*/ """{"error":""}""")]
        [InlineData("<html>Bad request</html>")]
        [InlineData("")]
        public async Task A_token_refusal_with_a_free_text_or_unreadable_error_quotes_none(string body)
        {
            _harness.Server.Token = (_, _, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.BadRequest)
            {
                Content = new StringContent(body, Encoding.UTF8, "application/json"),
            });

            InvalidOperationException exception = await Assert.ThrowsAsync<InvalidOperationException>(Invite);

            Assert.DoesNotContain("code", exception.Message, StringComparison.Ordinal);
            Assert.DoesNotContain("html", exception.Message, StringComparison.Ordinal);
        }

        [Theory]
        [InlineData(/*lang=json,strict*/ """{"access_token":"","token_type":"Bearer","expires_in":3600}""")]
        [InlineData(/*lang=json,strict*/ """{"access_token":"token-x","token_type":"mac","expires_in":3600}""")]
        [InlineData(/*lang=json,strict*/ """{"token_type":"Bearer"}""")]
        [InlineData(/*lang=json,strict*/ """{"access_token":"token-x","token_type":"Bearer","expires_in":"soon"}""")]
        public async Task A_token_answer_without_a_bearer_token_is_a_refusal(string body)
        {
            _harness.Server.Token = (_, _, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(body, Encoding.UTF8, "application/json"),
            });

            _ = await Assert.ThrowsAsync<InvalidOperationException>(Invite);
            Assert.Empty(_harness.Server.ApiRequests);
        }

        [Fact]
        public async Task A_token_endpoint_5xx_or_transport_failure_is_unavailability()
        {
            _harness.Server.Token = static (_, number, _) => number == 1
                ? Task.FromResult(new HttpResponseMessage(HttpStatusCode.BadGateway))
                : throw new HttpRequestException("Connection refused.");

            DependencyUnavailableException gateway = await Assert.ThrowsAsync<DependencyUnavailableException>(Invite);
            DependencyUnavailableException refused = await Assert.ThrowsAsync<DependencyUnavailableException>(Invite);

            Assert.All([gateway, refused], static exception => Assert.Equal(("identity", (TimeSpan?)TimeSpan.FromSeconds(1)), (exception.Dependency, exception.RetryAfter)));
            Assert.Equal(HttpStatusCode.BadGateway, Assert.IsType<HttpRequestException>(gateway.InnerException).StatusCode);
        }

        /// <summary>Invites one user.</summary>
        /// <returns>The results.</returns>
        private Task<IReadOnlyList<IdentityInvitationResult>> Invite()
        {
            return _harness.Client.InviteAsync(IdentityClientHarness.Invitations(1), Cancellation);
        }

        /// <summary>The Authorization header of every management API request, in arrival order.</summary>
        /// <returns>The headers.</returns>
        private List<string?> Bearers()
        {
            return [.. _harness.Server.ApiRequests.Select(static request => request.Authorization)];
        }
    }
}
