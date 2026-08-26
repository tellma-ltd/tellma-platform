// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using System.Net;
using Tellma.Connector.MarminAe.Tests.Infrastructure;
using Tellma.Testing.Support.Http;

namespace Tellma.Connector.MarminAe.Tests.Auth
{
    /// <summary>The token cache, its refresh margin, and what happens when callers collide.</summary>
    /// <remarks>
    ///     Nothing here sleeps or polls. The clock is one the test advances, and the one case that
    ///     needs two callers in flight at once holds the response open on a task the test completes,
    ///     so the interleaving is decided rather than raced.
    /// </remarks>
    public class MarminAeTokenProviderTests
    {
        [Fact]
        public async Task Obtains_a_token_on_the_first_call()
        {
            using var harness = MarminAeHarness.Create();

            string token = await harness.TokenProvider.GetTokenAsync(TestContext.Current.CancellationToken);

            Assert.Equal("token-1", token);
            Assert.Equal(1, harness.TokenRequestCount);
        }

        [Fact]
        public async Task Sends_the_client_id_as_a_query_parameter_and_no_bearer()
        {
            using var harness = MarminAeHarness.Create();

            await harness.TokenProvider.GetTokenAsync(TestContext.Current.CancellationToken);

            RecordedHttpRequest request = Assert.Single(harness.TokenRequests);
            Assert.Equal(
                "client_id=" + MarminAeHarness.DefaultClientId, request.RequestUri?.Query.TrimStart('?'));
            Assert.Null(request.Header("Authorization"));
            Assert.NotNull(request.Header(MarminAeTokenProvider.SignatureHeaderName));
        }

        [Fact]
        public async Task Escapes_a_client_id_that_needs_escaping_in_the_query()
        {
            const string clientId = "org test/one&two";
            using var harness = MarminAeHarness.Create(clientId: clientId);

            await harness.TokenProvider.GetTokenAsync(TestContext.Current.CancellationToken);

            RecordedHttpRequest request = Assert.Single(harness.TokenRequests);
            string query = request.RequestUri?.Query.TrimStart('?') ?? string.Empty;

            // Asserted by round trip rather than against a literal: what matters is that the ampersand
            // does not start a second parameter and the slash does not start a path segment, not which
            // canonical form the URI class settled on.
            Assert.StartsWith("client_id=", query, StringComparison.Ordinal);
            Assert.Equal(clientId, Uri.UnescapeDataString(query["client_id=".Length..]));
        }

        [Fact]
        public async Task Signs_the_raw_client_id_even_when_the_query_escapes_it()
        {
            const string clientId = "org test/one&two";
            using var harness = MarminAeHarness.Create(clientId: clientId);

            await harness.TokenProvider.GetTokenAsync(TestContext.Current.CancellationToken);

            // The two halves of the request disagree about escaping on purpose: the vendor signs
            // what it was given, not what the URL had to become to carry it.
            Assert.Equal(
                MarminAeSignature.Compute(MarminAeHarness.DefaultClientSecret, clientId),
                Assert.Single(harness.TokenRequests).Header(MarminAeTokenProvider.SignatureHeaderName));
        }

        [Fact]
        public async Task Serves_the_cached_token_without_a_second_request()
        {
            using var harness = MarminAeHarness.Create();

            string first = await harness.TokenProvider.GetTokenAsync(TestContext.Current.CancellationToken);
            string second = await harness.TokenProvider.GetTokenAsync(TestContext.Current.CancellationToken);

            Assert.Equal(first, second);
            Assert.Equal(1, harness.TokenRequestCount);
        }

        [Fact]
        public async Task Keeps_serving_a_token_one_second_outside_the_refresh_margin()
        {
            // A token that lives five minutes, refreshed a minute early, is good for 239 seconds and
            // stale at 241. Both sides are asserted because a one-sided test passes just as happily
            // against a provider that never caches and against one that never refreshes.
            using var harness = MarminAeHarness.Create();
            await harness.TokenProvider.GetTokenAsync(TestContext.Current.CancellationToken);

            harness.Time.Advance(TimeSpan.FromSeconds(239));
            string token = await harness.TokenProvider.GetTokenAsync(TestContext.Current.CancellationToken);

            Assert.Equal("token-1", token);
            Assert.Equal(1, harness.TokenRequestCount);
        }

        [Fact]
        public async Task Refreshes_one_second_inside_the_refresh_margin()
        {
            using var harness = MarminAeHarness.Create();
            await harness.TokenProvider.GetTokenAsync(TestContext.Current.CancellationToken);

            harness.Time.Advance(TimeSpan.FromSeconds(241));
            string token = await harness.TokenProvider.GetTokenAsync(TestContext.Current.CancellationToken);

            Assert.Equal("token-2", token);
            Assert.Equal(2, harness.TokenRequestCount);
        }

        [Fact]
        public async Task Refreshes_after_the_token_has_expired_outright()
        {
            using var harness = MarminAeHarness.Create();
            await harness.TokenProvider.GetTokenAsync(TestContext.Current.CancellationToken);

            harness.Time.Advance(TimeSpan.FromSeconds(301));

            Assert.Equal(
                "token-2", await harness.TokenProvider.GetTokenAsync(TestContext.Current.CancellationToken));
        }

        [Fact]
        public async Task Obtains_a_fresh_token_after_invalidate()
        {
            using var harness = MarminAeHarness.Create();
            await harness.TokenProvider.GetTokenAsync(TestContext.Current.CancellationToken);

            harness.TokenProvider.Invalidate();

            Assert.Equal(
                "token-2", await harness.TokenProvider.GetTokenAsync(TestContext.Current.CancellationToken));
            Assert.Equal(2, harness.TokenRequestCount);
        }

        [Fact]
        public async Task Joins_concurrent_callers_onto_one_token_request()
        {
            const int callers = 8;
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

            // Launched synchronously and collected without awaiting: an async method runs on the
            // calling thread up to its first real await, and the gate above guarantees none of them
            // can get past it. By the time the loop returns, every caller is provably parked — so a
            // provider that did not single-flight would already have issued eight requests.
            List<Task<string>> calls = [];
            for (int index = 0; index < callers; index++)
            {
                calls.Add(harness.TokenProvider.GetTokenAsync(TestContext.Current.CancellationToken));
            }

            await arrived.Task;
            release.SetResult();
            string[] tokens = await Task.WhenAll(calls);

            Assert.Equal(1, harness.TokenRequestCount);
            Assert.Equal(callers, tokens.Length);
            Assert.Single(tokens.Distinct(StringComparer.Ordinal));
        }

        [Fact]
        public async Task Serves_a_later_caller_from_the_cache_the_shared_request_filled()
        {
            using var harness = MarminAeHarness.Create();

            Task<string> first = harness.TokenProvider.GetTokenAsync(TestContext.Current.CancellationToken);
            Task<string> second = harness.TokenProvider.GetTokenAsync(TestContext.Current.CancellationToken);
            await Task.WhenAll(first, second);

            await harness.TokenProvider.GetTokenAsync(TestContext.Current.CancellationToken);

            Assert.Equal(1, harness.TokenRequestCount);
        }

        [Fact]
        public async Task Does_not_cache_a_token_request_that_failed()
        {
            using var harness = MarminAeHarness.Create();
            harness.OnToken((ordinal, _) => Task.FromResult(
                ordinal == 1
                    ? MarminAeResponses.Json(HttpStatusCode.InternalServerError, "{}")
                    : MarminAeResponses.Token(
                        "token-recovered", MarminAeHarness.DefaultTokenLifetime)));

            // The failure mode this guards against is a provider that parks the faulted task in its
            // field, which makes one bad refresh permanent for the life of the process.
            await Assert.ThrowsAsync<MarminAeRequestException>(
                () => harness.TokenProvider.GetTokenAsync(TestContext.Current.CancellationToken));

            Assert.Equal(
                "token-recovered",
                await harness.TokenProvider.GetTokenAsync(TestContext.Current.CancellationToken));
            Assert.Equal(2, harness.TokenRequestCount);
        }

        [Fact]
        public async Task Surfaces_the_token_endpoints_failure_to_every_joined_caller()
        {
            TaskCompletionSource arrived = new(TaskCreationOptions.RunContinuationsAsynchronously);
            TaskCompletionSource release = new(TaskCreationOptions.RunContinuationsAsynchronously);

            using var harness = MarminAeHarness.Create();
            harness.OnToken(async (_, _) =>
            {
                arrived.TrySetResult();
                await release.Task;

                return MarminAeResponses.Json(HttpStatusCode.Unauthorized, /*lang=json,strict*/ """{"errors":{"message":"bad signature"}}""");
            });

            List<Task<string>> calls =
            [
                harness.TokenProvider.GetTokenAsync(TestContext.Current.CancellationToken),
                harness.TokenProvider.GetTokenAsync(TestContext.Current.CancellationToken),
                harness.TokenProvider.GetTokenAsync(TestContext.Current.CancellationToken),
            ];

            await arrived.Task;
            release.SetResult();

            foreach (Task<string> call in calls)
            {
                MarminAeRequestException exception =
                    await Assert.ThrowsAsync<MarminAeRequestException>(() => call);
                Assert.Equal(401, exception.StatusCode);
                Assert.Equal("bad signature", exception.Detail?.Message);
            }

            Assert.Equal(1, harness.TokenRequestCount);
        }

        [Fact]
        public async Task Surfaces_a_token_response_with_no_token_as_a_typed_failure()
        {
            using var harness = MarminAeHarness.Create();
            harness.OnToken((_, _) => Task.FromResult(
                MarminAeResponses.Json(HttpStatusCode.OK, /*lang=json,strict*/ """{"expires_at":1778000000}""")));

            MarminAeRequestException exception = await Assert.ThrowsAsync<MarminAeRequestException>(
                () => harness.TokenProvider.GetTokenAsync(TestContext.Current.CancellationToken));

            Assert.Contains("no access token", exception.Message, StringComparison.Ordinal);
        }

        [Fact]
        public async Task Surfaces_a_token_response_that_is_not_json_as_a_typed_failure()
        {
            using var harness = MarminAeHarness.Create();
            harness.OnToken((_, _) => Task.FromResult(
                MarminAeResponses.Json(HttpStatusCode.OK, "<html>gateway</html>")));

            MarminAeRequestException exception = await Assert.ThrowsAsync<MarminAeRequestException>(
                () => harness.TokenProvider.GetTokenAsync(TestContext.Current.CancellationToken));

            Assert.IsAssignableFrom<System.Text.Json.JsonException>(exception.InnerException);
        }

        [Fact]
        public async Task Reports_a_token_request_that_outlives_the_timeout_as_a_timeout()
        {
            TaskCompletionSource arrived = new(TaskCreationOptions.RunContinuationsAsynchronously);
            using var harness = MarminAeHarness.Create(timeout: TimeSpan.FromSeconds(30));
            harness.OnToken(async (_, cancellationToken) =>
            {
                arrived.TrySetResult();
                await Task.Delay(Timeout.Infinite, cancellationToken);

                return MarminAeResponses.Json(HttpStatusCode.OK, "{}");
            });

            Task<string> call = harness.TokenProvider.GetTokenAsync(TestContext.Current.CancellationToken);
            await arrived.Task;
            harness.Time.Advance(TimeSpan.FromSeconds(31));

            // The same exception a data request raises when it runs out of time: which leg of a call
            // was in flight must not decide what the caller catches.
            TimeoutException exception = await Assert.ThrowsAsync<TimeoutException>(() => call);
            Assert.Contains("did not complete within", exception.Message, StringComparison.Ordinal);
        }

        [Fact]
        public async Task Discards_only_the_token_that_was_refused()
        {
            using var harness = MarminAeHarness.Create();
            string first = await harness.TokenProvider.GetTokenAsync(TestContext.Current.CancellationToken);

            harness.TokenProvider.Invalidate("some-other-token");

            Assert.Equal(
                first, await harness.TokenProvider.GetTokenAsync(TestContext.Current.CancellationToken));
            Assert.Equal(1, harness.TokenRequestCount);

            harness.TokenProvider.Invalidate(first);

            Assert.Equal(
                "token-2", await harness.TokenProvider.GetTokenAsync(TestContext.Current.CancellationToken));
            Assert.Equal(2, harness.TokenRequestCount);
        }

        [Fact]
        public async Task Does_not_let_a_straggling_refusal_discard_the_replacement_token()
        {
            // Two calls depart carrying the same token and both come back refused. By the time the
            // second refusal lands, the first has already obtained a replacement — and discarding it
            // would start a third fetch, once per straggler, against an endpoint that allows five a
            // minute.
            using var harness = MarminAeHarness.Create();
            harness.Enqueue(MarminAeResponses.Json(HttpStatusCode.Unauthorized, "{}"))
                   .EnqueueMany(
                       2, () => MarminAeResponses.Json(HttpStatusCode.OK, MarminAeOperations.DocumentBody));

            // The first refusal lands, discards token-1, and installs token-2.
            await harness.Client.GetDocumentAsync(
                MarminAeDocumentKind.SalesInvoice, "doc-1", TestContext.Current.CancellationToken);
            Assert.Equal(2, harness.TokenRequestCount);

            // The straggler arrives afterwards, still naming token-1. Discarding unconditionally
            // here would throw token-2 away and start a third fetch.
            harness.TokenProvider.Invalidate("token-1");

            await harness.Client.GetDocumentAsync(
                MarminAeDocumentKind.SalesInvoice, "doc-2", TestContext.Current.CancellationToken);

            Assert.Equal(2, harness.TokenRequestCount);
            Assert.Equal("Bearer token-2", harness.DataRequests[^1].Header("Authorization"));
        }

        [Fact]
        public async Task Rebuilding_the_provider_with_a_rotated_secret_signs_with_the_new_one()
        {
            const string rotated = "sk_test_rotated_0000000002";
            using var harness = MarminAeHarness.Create();
            await harness.TokenProvider.GetTokenAsync(TestContext.Current.CancellationToken);

            // Nothing derived from the credentials is retained on the transport, which is what lets
            // a host swap in rotated ones and have the very next call use them.
            MarminAeClientOptions rotatedOptions = harness.Options with { ClientSecret = rotated };
            MarminAeTokenProvider provider = new(harness.HttpClient, rotatedOptions, harness.Time);
            await provider.GetTokenAsync(TestContext.Current.CancellationToken);

            IReadOnlyList<RecordedHttpRequest> requests = harness.TokenRequests;
            Assert.Equal(2, requests.Count);
            Assert.NotEqual(
                requests[0].Header(MarminAeTokenProvider.SignatureHeaderName),
                requests[1].Header(MarminAeTokenProvider.SignatureHeaderName));
            Assert.Equal(
                MarminAeSignature.Compute(rotated, MarminAeHarness.DefaultClientId),
                requests[1].Header(MarminAeTokenProvider.SignatureHeaderName));
        }
    }
}
