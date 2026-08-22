// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Microsoft.Extensions.Time.Testing;
using System.Globalization;
using Tellma.Testing.Support.Http;

namespace Tellma.Connector.MarminAe.Tests.Infrastructure
{
    /// <summary>
    ///     A client and a token provider over a scripted wire, on a clock the test drives.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         Token requests and data requests are scripted separately, because almost every test
    ///         cares about one and not the other. Token requests are answered from a default that
    ///         issues a distinct, plainly numbered token each time; data requests come from a queue
    ///         the test fills, and a data request arriving with the queue empty fails the test where
    ///         it happened rather than somewhere downstream.
    ///     </para>
    ///     <para>
    ///         Nothing here sleeps or polls. A step that needs to be held open takes a task the test
    ///         completes, which is what makes the concurrency cases deterministic.
    ///     </para>
    /// </remarks>
    internal sealed class MarminAeHarness : IDisposable
    {
        /// <summary>The client id every harness authenticates with unless told otherwise.</summary>
        internal const string DefaultClientId = "org_test_0000000000000001";

        /// <summary>The client secret every harness signs with unless told otherwise.</summary>
        internal const string DefaultClientSecret = "sk_test_0000000000000001";

        /// <summary>The business profile the request-shape tests submit under.</summary>
        internal const string DefaultProfileId = "MBP-TESTPROFILE";

        /// <summary>How long the default token response says a token lives.</summary>
        internal static readonly TimeSpan DefaultTokenLifetime = TimeSpan.FromSeconds(300);

        private readonly Lock _gate = new();
        private readonly Queue<Func<HttpRequestMessage, string, CancellationToken, Task<HttpResponseMessage>>> _dataSteps = new();
        private readonly List<HttpResponseMessage> _owned = [];

        private Func<int, CancellationToken, Task<HttpResponseMessage>>? _tokenResponder;
        private int _tokenRequests;

        private MarminAeHarness(MarminAeClientOptions options, FakeTimeProvider time)
        {
            Options = options;
            Time = time;
            Handler = new ScriptedHttpMessageHandler(RespondAsync);
            HttpClient = new HttpClient(Handler, disposeHandler: false)
            {
                // The client owns its own timeout; leaving the transport's in place would race it.
                Timeout = Timeout.InfiniteTimeSpan,
            };

            TokenProvider = new MarminAeTokenProvider(HttpClient, options, time);
            Client = new MarminAeClient(HttpClient, options, TokenProvider, time);
        }

        /// <summary>The clock the client and the provider read.</summary>
        internal FakeTimeProvider Time { get; }

        /// <summary>The settings both were built with.</summary>
        internal MarminAeClientOptions Options { get; }

        /// <summary>The scripted wire.</summary>
        internal ScriptedHttpMessageHandler Handler { get; }

        /// <summary>The transport, for a test that needs to build a second client over it.</summary>
        internal HttpClient HttpClient { get; }

        /// <summary>The provider the client shares.</summary>
        internal MarminAeTokenProvider TokenProvider { get; }

        /// <summary>The client under test.</summary>
        internal MarminAeClient Client { get; }

        /// <summary>Every request the wire saw, in arrival order.</summary>
        internal IReadOnlyList<RecordedHttpRequest> Exchanges => Handler.Exchanges;

        /// <summary>The token requests, in arrival order.</summary>
        internal IReadOnlyList<RecordedHttpRequest> TokenRequests =>
            [.. Handler.Exchanges.Where(static exchange => IsTokenRequest(exchange.RequestUri))];

        /// <summary>Everything that was not a token request, in arrival order.</summary>
        internal IReadOnlyList<RecordedHttpRequest> DataRequests =>
            [.. Handler.Exchanges.Where(static exchange => !IsTokenRequest(exchange.RequestUri))];

        /// <summary>How many token requests the wire saw.</summary>
        internal int TokenRequestCount => TokenRequests.Count;

        /// <summary>The one data request the wire saw.</summary>
        internal RecordedHttpRequest SingleDataRequest => Assert.Single(DataRequests);

        /// <summary>Builds a harness.</summary>
        /// <param name="baseAddress">The API root; the sandbox host by default.</param>
        /// <param name="timeout">The per-request timeout.</param>
        /// <param name="skew">The token refresh margin.</param>
        /// <param name="clientId">The client id.</param>
        /// <param name="clientSecret">The client secret.</param>
        /// <returns>The harness.</returns>
        internal static MarminAeHarness Create(
            Uri? baseAddress = null,
            TimeSpan? timeout = null,
            TimeSpan? skew = null,
            string clientId = DefaultClientId,
            string clientSecret = DefaultClientSecret)
        {
            // A fixed instant rather than the wall clock: every expiry assertion in the suite is
            // arithmetic against this, and a test that reads the real time is a test that fails at
            // midnight.
            FakeTimeProvider time = new(new DateTimeOffset(2026, 5, 7, 9, 0, 0, TimeSpan.Zero));

            MarminAeClientOptions options = new()
            {
                BaseAddress = baseAddress ?? MarminAeClientOptions.SandboxBaseAddress,
                ClientId = clientId,
                ClientSecret = clientSecret,
                Timeout = timeout ?? TimeSpan.FromSeconds(30),
                TokenExpirySkew = skew ?? TimeSpan.FromSeconds(60),
            };

            return new MarminAeHarness(options, time);
        }

        /// <summary>Answers every token request from a script of the test's own.</summary>
        /// <param name="responder">Given the one-based request ordinal, produces the response.</param>
        /// <returns>This harness.</returns>
        internal MarminAeHarness OnToken(Func<int, CancellationToken, Task<HttpResponseMessage>> responder)
        {
            ArgumentNullException.ThrowIfNull(responder);

            lock (_gate)
            {
                _tokenResponder = responder;
            }

            return this;
        }

        /// <summary>Adds the next data response.</summary>
        /// <param name="response">The response.</param>
        /// <returns>This harness.</returns>
        internal MarminAeHarness Enqueue(HttpResponseMessage response)
        {
            ArgumentNullException.ThrowIfNull(response);

            lock (_gate)
            {
                _owned.Add(response);
                _dataSteps.Enqueue((_, _, _) => Task.FromResult(response));
            }

            return this;
        }

        /// <summary>Adds the next data response, computed when the request arrives.</summary>
        /// <param name="responder">Given the request, its body, and its token, produces the
        ///     response.</param>
        /// <returns>This harness.</returns>
        internal MarminAeHarness Enqueue(
            Func<HttpRequestMessage, string, CancellationToken, Task<HttpResponseMessage>> responder)
        {
            ArgumentNullException.ThrowIfNull(responder);

            lock (_gate)
            {
                _dataSteps.Enqueue(responder);
            }

            return this;
        }

        /// <summary>Adds the same data response several times over.</summary>
        /// <param name="count">How many times.</param>
        /// <param name="factory">Produces each one.</param>
        /// <returns>This harness.</returns>
        internal MarminAeHarness EnqueueMany(int count, Func<HttpResponseMessage> factory)
        {
            ArgumentNullException.ThrowIfNull(factory);

            for (int index = 0; index < count; index++)
            {
                Enqueue(factory());
            }

            return this;
        }

        /// <inheritdoc />
        public void Dispose()
        {
            HttpClient.Dispose();
            Handler.Dispose();

            foreach (HttpResponseMessage response in _owned)
            {
                response.Dispose();
            }
        }

        private static bool IsTokenRequest(Uri? requestUri)
        {
            return requestUri is not null
                && requestUri.AbsolutePath.EndsWith('/' + MarminAeRoutes.TokenPath, StringComparison.Ordinal);
        }

        private Task<HttpResponseMessage> RespondAsync(
            HttpRequestMessage request, string body, CancellationToken cancellationToken)
        {
            if (IsTokenRequest(request.RequestUri))
            {
                Func<int, CancellationToken, Task<HttpResponseMessage>>? responder;
                int ordinal;
                lock (_gate)
                {
                    ordinal = ++_tokenRequests;
                    responder = _tokenResponder;
                }

                return responder is null
                    ? Task.FromResult(DefaultToken(ordinal))
                    : responder(ordinal, cancellationToken);
            }

            Func<HttpRequestMessage, string, CancellationToken, Task<HttpResponseMessage>> step;
            lock (_gate)
            {
                if (_dataSteps.Count == 0)
                {
                    // Surfaced here rather than as a mysterious downstream failure: an unscripted
                    // request is nearly always the test discovering a call it did not expect.
                    throw new InvalidOperationException(
                        string.Create(
                            CultureInfo.InvariantCulture,
                            $"The wire was asked for {request.Method} {request.RequestUri} with no response scripted for it."));
                }

                step = _dataSteps.Dequeue();
            }

            return step(request, body, cancellationToken);
        }

        private HttpResponseMessage DefaultToken(int ordinal)
        {
            HttpResponseMessage response = MarminAeResponses.Token(
                string.Create(CultureInfo.InvariantCulture, $"token-{ordinal}"), DefaultTokenLifetime);

            lock (_gate)
            {
                _owned.Add(response);
            }

            return response;
        }
    }
}
