// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using Tellma.Core.Abstractions.Errors;
using Tellma.Core.Abstractions.Identity;
using Tellma.Core.Composition;

namespace Tellma.Core.Identity
{
    // Spec 0018 §2.2: the transport and the token. One HTTP exchange at a time, each under its own
    // 30-second timeout on the injected clock; a client_credentials token for the tellma_identity
    // scope, cached until 60 seconds before it expires under a single-flight gate; one 401 on an API
    // call discards the token, fetches a fresh one and retries that call once. Every failure leaves
    // as one of three exceptions: DependencyUnavailableException (unreachable), InvalidOperation-
    // Exception (refused or unreadable), or the caller's own OperationCanceledException.

    /// <summary>
    ///     The identity-server client's transport: the service token, its cache, and the exchanges with
    ///     the token endpoint and the management API, each classified into the client's failure kinds.
    ///     Thread-safe; one per client.
    /// </summary>
    /// <param name="httpClients">Creates the named <c>Tellma.Identity</c> client for each exchange.</param>
    /// <param name="settings">The options the exchanges read: the authority and the service client's credentials.</param>
    /// <param name="time">The clock of the token's expiry and of each exchange's timeout.</param>
    internal sealed class IdentityTransport(
        IHttpClientFactory httpClients,
        Func<IdentityServerClientOptions> settings,
        TimeProvider time) : IDisposable
    {
        /// <summary>The name of the <see cref="HttpClient" /> every exchange uses.</summary>
        public const string HttpClientName = "Tellma.Identity";

        /// <summary>The dependency name a <see cref="DependencyUnavailableException" /> carries.</summary>
        public const string Dependency = "identity";

        /// <summary>The scope the service token asks for: the identity server's management API.</summary>
        public const string Scope = "tellma_identity";

        /// <summary>The token endpoint, relative to the authority.</summary>
        public const string TokenRoute = "connect/token";

        /// <summary>How long one exchange may take before the identity server counts as unreachable.</summary>
        public static readonly TimeSpan ExchangeTimeout = TimeSpan.FromSeconds(30);

        /// <summary>How long before its expiry a cached token is replaced, so no call carries one that lapses on the way.</summary>
        public static readonly TimeSpan RefreshMargin = TimeSpan.FromSeconds(60);

        /// <summary>What an unreachable identity server tells the edge to wait before a retry.</summary>
        public static readonly TimeSpan RetryAfter = TimeSpan.FromSeconds(1);

        /// <summary>The media type every exchange accepts and every body is sent as.</summary>
        private static readonly MediaTypeWithQualityHeaderValue Json = new("application/json");

        /// <summary>The <c>User-Agent</c> of every exchange: <c>Tellma.Core/&lt;version&gt;</c>.</summary>
        private static readonly ProductInfoHeaderValue UserAgent = CreateUserAgent();

        /// <summary>Admits one token fetch at a time; the others wait for its token.</summary>
        private readonly SemaphoreSlim _gate = new(1, 1);

        /// <summary>The cached token, or null when none is held.</summary>
        private volatile CachedToken? _token;

        /// <inheritdoc />
        public void Dispose()
        {
            _gate.Dispose();
        }

        // ------------------------------------------------------------------------------------------
        // The management API
        // ------------------------------------------------------------------------------------------

        /// <summary>
        ///     Sends one management API request with the service token, retrying it once with a fresh
        ///     token after a <c>401</c>, and reads its JSON answer.
        /// </summary>
        /// <typeparam name="TResponse">The answer's shape.</typeparam>
        /// <param name="request">Creates the request; called once per attempt, since a request is sent only once.</param>
        /// <param name="responseType">The answer's metadata; null to read no body.</param>
        /// <param name="notFoundIsSuccess">Whether a <c>404</c> answers success, as a delete of an account that does not exist does.</param>
        /// <param name="cancellationToken">The caller's cancellation.</param>
        /// <returns>The answer; null when <paramref name="responseType" /> is null or the server answered <c>404</c>.</returns>
        /// <exception cref="DependencyUnavailableException">The identity server is unreachable.</exception>
        /// <exception cref="InvalidOperationException">The identity server refused the call or answered what cannot be read.</exception>
        /// <exception cref="OperationCanceledException">The caller cancelled.</exception>
        public async Task<TResponse?> SendAsync<TResponse>(
            Func<Uri, HttpRequestMessage> request,
            JsonTypeInfo<TResponse>? responseType,
            bool notFoundIsSuccess,
            CancellationToken cancellationToken)
            where TResponse : class
        {
            IdentityServerClientOptions options = Settings();
            Uri authority = BaseOf(options.Authority!);

            // --- The first attempt, with the cached token or a fresh one ---
            CachedToken token = await GetTokenAsync(options, authority, cancellationToken).ConfigureAwait(false);
            Exchange<TResponse> exchange = await ExchangeAsync(request(authority), token.Value, responseType, cancellationToken).ConfigureAwait(false);

            // --- One 401: the token was revoked or rotated early; discard it, fetch one, retry once ---
            if (exchange.Status == HttpStatusCode.Unauthorized)
            {
                Discard(token);
                token = await GetTokenAsync(options, authority, cancellationToken).ConfigureAwait(false);
                exchange = await ExchangeAsync(request(authority), token.Value, responseType, cancellationToken).ConfigureAwait(false);
                if (exchange.Status == HttpStatusCode.Unauthorized)
                {
                    throw new InvalidOperationException(
                        $"The identity server refused the service client '{options.ClientId}' twice with 401 Unauthorized, a fresh token included; the client's registration or its scope is wrong.");
                }
            }

            // --- Everything but success is the server's refusal or its outage ---
            return exchange.IsSuccess
                ? exchange.Body
                : exchange.Status == HttpStatusCode.NotFound && notFoundIsSuccess
                ? null
                : exchange.Status >= HttpStatusCode.InternalServerError
                ? throw Unavailable(new HttpRequestException($"The identity server answered {Describe(exchange.Status)}.", null, exchange.Status))
                : throw new InvalidOperationException(
                    $"The identity server refused the call with {Describe(exchange.Status)}; the distribution's request or registration is wrong, and no retry fixes it.");
        }

        /// <summary>One API exchange: the request sent with the token, under its own timeout, and its answer read.</summary>
        /// <typeparam name="TResponse">The answer's shape.</typeparam>
        /// <param name="request">The request.</param>
        /// <param name="accessToken">The bearer token.</param>
        /// <param name="responseType">The answer's metadata; null to read no body.</param>
        /// <param name="cancellationToken">The caller's cancellation.</param>
        /// <returns>The status and, on success, the body.</returns>
        private async Task<Exchange<TResponse>> ExchangeAsync<TResponse>(
            HttpRequestMessage request,
            string accessToken,
            JsonTypeInfo<TResponse>? responseType,
            CancellationToken cancellationToken)
            where TResponse : class
        {
            using (request)
            {
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
                return await RunAsync(
                    request,
                    async (response, token) =>
                    {
                        TResponse? body = response.IsSuccessStatusCode && responseType is not null
                            ? await ReadAsync(response, responseType, token).ConfigureAwait(false)
                            : null;
                        return new Exchange<TResponse>(response.StatusCode, response.IsSuccessStatusCode, body);
                    },
                    cancellationToken).ConfigureAwait(false);
            }
        }

        // ------------------------------------------------------------------------------------------
        // The token
        // ------------------------------------------------------------------------------------------

        /// <summary>
        ///     The service token: the cached one while it is fresh, otherwise one fetch that every
        ///     concurrent caller awaits.
        /// </summary>
        /// <param name="options">The options of the call.</param>
        /// <param name="authority">The authority, with a trailing slash.</param>
        /// <param name="cancellationToken">The caller's cancellation.</param>
        /// <returns>The token.</returns>
        private async Task<CachedToken> GetTokenAsync(IdentityServerClientOptions options, Uri authority, CancellationToken cancellationToken)
        {
            // --- Fresh: no lock ---
            if (Fresh() is CachedToken cached)
            {
                return cached;
            }

            // --- Stale or absent: one fetch at a time, and whoever waited reads its token ---
            await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                if (Fresh() is CachedToken fetched)
                {
                    return fetched;
                }

                CachedToken token = await FetchTokenAsync(options, authority, cancellationToken).ConfigureAwait(false);
                _token = token;
                return token;
            }
            finally
            {
                _ = _gate.Release();
            }
        }

        /// <summary>Forgets a token the server refused, unless a fresher one has already replaced it.</summary>
        /// <param name="token">The refused token.</param>
        private void Discard(CachedToken token)
        {
            // A concurrent caller may have replaced it already; discarding that one would cost a fetch.
            _ = Interlocked.CompareExchange(ref _token, null, token);
        }

        /// <summary>The cached token while it is fresh.</summary>
        /// <returns>The token, or null when none is held or it is due for replacement.</returns>
        private CachedToken? Fresh()
        {
            CachedToken? token = _token;
            return token is not null && time.GetUtcNow() < token.RefreshAt ? token : null;
        }

        /// <summary>Fetches a <c>client_credentials</c> token for the management scope.</summary>
        /// <param name="options">The options of the call.</param>
        /// <param name="authority">The authority, with a trailing slash.</param>
        /// <param name="cancellationToken">The caller's cancellation.</param>
        /// <returns>The token and when to replace it.</returns>
        private async Task<CachedToken> FetchTokenAsync(IdentityServerClientOptions options, Uri authority, CancellationToken cancellationToken)
        {
            // No resource parameter: the server sets the audience to the management scope's own.
            // The secret travels in the form body (client_secret_post), never in a header.
            DateTimeOffset requested = time.GetUtcNow();
            using HttpRequestMessage request = new(HttpMethod.Post, new Uri(authority, TokenRoute))
            {
                Content = new FormUrlEncodedContent(
                [
                    new KeyValuePair<string, string>("grant_type", "client_credentials"),
                    new KeyValuePair<string, string>("scope", Scope),
                    new KeyValuePair<string, string>("client_id", options.ClientId),
                    new KeyValuePair<string, string>("client_secret", options.ServiceClientSecret!),
                ]),
            };

            return await RunAsync(
                request,
                async (response, token) =>
                {
                    // --- Refusals and outages ---
                    if (response.StatusCode >= HttpStatusCode.InternalServerError)
                    {
                        throw Unavailable(new HttpRequestException($"The identity server's token endpoint answered {Describe(response.StatusCode)}.", null, response.StatusCode));
                    }

                    if (!response.IsSuccessStatusCode)
                    {
                        string? error = await ReadErrorAsync(response, token).ConfigureAwait(false);
                        throw new InvalidOperationException(
                            $"The identity server's token endpoint refused the service client '{options.ClientId}' with {Describe(response.StatusCode)}"
                            + (error is null ? string.Empty : $" ({error})")
                            + "; its secret, its registration or its tellma_identity scope is wrong.");
                    }

                    // --- The token, cached until the margin before its expiry ---
                    TokenResponse body = await ReadAsync(response, IdentityJson.TypeInfo<TokenResponse>(), token).ConfigureAwait(false);
                    if (string.IsNullOrWhiteSpace(body.AccessToken) || !string.Equals(body.TokenType, "Bearer", StringComparison.OrdinalIgnoreCase))
                    {
                        throw new InvalidOperationException("The identity server's token endpoint answered no bearer access token.");
                    }

                    // Measured from the request, so the token is replaced no later than the margin
                    // before the server's own expiry; a token without a lifetime is used once.
                    DateTimeOffset refreshAt = body.ExpiresIn is long seconds and > 0
                        ? requested + TimeSpan.FromSeconds(seconds) - RefreshMargin
                        : requested;
                    return new CachedToken(body.AccessToken, refreshAt);
                },
                cancellationToken).ConfigureAwait(false);
        }

        /// <summary>Reads the OAuth error code of a refusal, when its body carries one.</summary>
        /// <param name="response">The refusal.</param>
        /// <param name="cancellationToken">The exchange's cancellation.</param>
        /// <returns>The error code, or null when the body carries none or cannot be read.</returns>
        private static async Task<string?> ReadErrorAsync(HttpResponseMessage response, CancellationToken cancellationToken)
        {
            try
            {
                TokenErrorResponse? error = await response.Content
                    .ReadFromJsonAsync(IdentityJson.TypeInfo<TokenErrorResponse>(), cancellationToken).ConfigureAwait(false);

                // Only a well-formed code is quoted (RFC 6749 §5.2's character set), never free text.
                return error?.Error is string code && code.Length is > 0 and <= 64 && code.All(static c => c is >= '\x20' and <= '\x7e' and not '"' and not '\\')
                    ? code
                    : null;
            }
            catch (JsonException)
            {
                return null;
            }
            catch (NotSupportedException)
            {
                // A body that is not JSON at all.
                return null;
            }
        }

        // ------------------------------------------------------------------------------------------
        // One exchange
        // ------------------------------------------------------------------------------------------

        /// <summary>
        ///     Sends a request under its own timeout and hands the answer to <paramref name="read" />,
        ///     mapping a transport failure or a timeout to <see cref="DependencyUnavailableException" />,
        ///     an unreadable body to <see cref="InvalidOperationException" />, and the caller's
        ///     cancellation to itself.
        /// </summary>
        /// <typeparam name="T">What the exchange yields.</typeparam>
        /// <param name="request">The request.</param>
        /// <param name="read">Reads the answer under the exchange's cancellation.</param>
        /// <param name="cancellationToken">The caller's cancellation.</param>
        /// <returns>What <paramref name="read" /> yields.</returns>
        private async Task<T> RunAsync<T>(
            HttpRequestMessage request,
            Func<HttpResponseMessage, CancellationToken, Task<T>> read,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            request.Headers.Accept.Add(Json);
            request.Headers.UserAgent.Add(UserAgent);

            // The timeout runs on the injected clock, so the suites move it; the client's own
            // HttpClient.Timeout is infinite.
            using CancellationTokenSource timeout = new(ExchangeTimeout, time);
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeout.Token);
            try
            {
                HttpClient client = httpClients.CreateClient(HttpClientName);
                using HttpResponseMessage response = await client
                    .SendAsync(request, HttpCompletionOption.ResponseHeadersRead, linked.Token).ConfigureAwait(false);
                return await read(response, linked.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (OperationCanceledException exception) when (timeout.IsCancellationRequested)
            {
                throw Unavailable(new TimeoutException(
                    string.Create(CultureInfo.InvariantCulture, $"The identity server did not answer within {ExchangeTimeout.TotalSeconds} seconds."),
                    exception));
            }
            catch (HttpRequestException exception)
            {
                // No response at all: DNS, connection, TLS or a broken stream.
                throw Unavailable(exception);
            }
            catch (IOException exception)
            {
                // The body broke off mid-read.
                throw Unavailable(exception);
            }
            catch (JsonException exception)
            {
                throw new InvalidOperationException("The identity server answered a body the client cannot read.", exception);
            }
        }

        /// <summary>Reads a JSON body, refusing an empty one.</summary>
        /// <typeparam name="T">The body's shape.</typeparam>
        /// <param name="response">The answer.</param>
        /// <param name="type">The body's metadata.</param>
        /// <param name="cancellationToken">The exchange's cancellation.</param>
        /// <returns>The body.</returns>
        private static async Task<T> ReadAsync<T>(HttpResponseMessage response, JsonTypeInfo<T> type, CancellationToken cancellationToken)
            where T : class
        {
            try
            {
                return await response.Content.ReadFromJsonAsync(type, cancellationToken).ConfigureAwait(false)
                    ?? throw new InvalidOperationException("The identity server answered an empty body.");
            }
            catch (NotSupportedException exception)
            {
                // A content type that is not JSON, such as a proxy's HTML error page.
                throw new InvalidOperationException("The identity server answered a body that is not JSON.", exception);
            }
        }

        // ------------------------------------------------------------------------------------------
        // Helpers
        // ------------------------------------------------------------------------------------------

        /// <summary>The options of a call, refused when the host could not supply its credentials.</summary>
        /// <returns>The options.</returns>
        /// <exception cref="InvalidOperationException">The authority, the client id or the secret is missing.</exception>
        private IdentityServerClientOptions Settings()
        {
            IdentityServerClientOptions options = settings();
            const string Prefix = IdentityServerClientOptions.SectionPath + ":";

            // Startup validation refuses these everywhere but the migrator in in-proc Development,
            // which has no engine to ask for the secret the web host generates for its boot.
            return options.Authority is null
                ? throw new InvalidOperationException($"{Prefix}{nameof(IdentityServerClientOptions.Authority)} is not set, so the identity server cannot be called.")
                : string.IsNullOrWhiteSpace(options.ClientId)
                ? throw new InvalidOperationException("The identity server's service client id is not set, so the identity server cannot be called.")
                : string.IsNullOrWhiteSpace(options.ServiceClientSecret)
                ? throw new InvalidOperationException(
                    $"{Prefix}{nameof(IdentityServerClientOptions.ServiceClientSecret)} is not set, so the identity server cannot be called; "
                    + "in in-proc Development only the web host holds the secret it generates for its boot.")
                : options;
        }

        /// <summary>The authority as a base URI: with a trailing slash, so relative routes keep its path.</summary>
        /// <param name="authority">The authority.</param>
        /// <returns>The base URI.</returns>
        internal static Uri BaseOf(Uri authority)
        {
            string text = authority.AbsoluteUri;
            return text.EndsWith('/') ? authority : new Uri(text + "/", UriKind.Absolute);
        }

        /// <summary>The failure of an unreachable identity server.</summary>
        /// <param name="inner">What made it unreachable.</param>
        /// <returns>The exception.</returns>
        private static DependencyUnavailableException Unavailable(Exception inner)
        {
            return new DependencyUnavailableException(Dependency, RetryAfter, inner);
        }

        /// <summary>A status code as text, such as <c>403 Forbidden</c>.</summary>
        /// <param name="status">The status.</param>
        /// <returns>The text.</returns>
        private static string Describe(HttpStatusCode status)
        {
            return string.Create(CultureInfo.InvariantCulture, $"{(int)status} {status}");
        }

        /// <summary>Creates the <c>User-Agent</c> product, its version reduced to what a header token allows.</summary>
        /// <returns>The product.</returns>
        private static ProductInfoHeaderValue CreateUserAgent()
        {
            string version = DeploymentVersionsReader.InformationalVersion(typeof(IdentityTransport).Assembly);

            // A product version is an HTTP token: an informational version's build metadata may
            // carry characters it refuses, so only the leading token-safe part is kept.
            int end = 0;
            while (end < version.Length && (char.IsAsciiLetterOrDigit(version[end]) || version[end] is '.' or '-' or '+' or '_'))
            {
                end++;
            }

            return new ProductInfoHeaderValue("Tellma.Core", end > 0 ? version[..end] : "0.0.0");
        }

        /// <summary>
        ///     A service token and when it is due for replacement. A class rather than a record, so no
        ///     generated member prints the token, and compared by reference, so a refused token is
        ///     discarded only while it is still the cached one.
        /// </summary>
        /// <param name="value">The access token; never logged.</param>
        /// <param name="refreshAt">When the token stops being reused: the margin before its expiry.</param>
        private sealed class CachedToken(string value, DateTimeOffset refreshAt)
        {
            /// <summary>The access token; never logged.</summary>
            public string Value { get; } = value;

            /// <summary>When the token stops being reused: the margin before its expiry.</summary>
            public DateTimeOffset RefreshAt { get; } = refreshAt;
        }

        /// <summary>The outcome of one API exchange.</summary>
        /// <typeparam name="TResponse">The body's shape.</typeparam>
        /// <param name="Status">The status code.</param>
        /// <param name="IsSuccess">Whether the status is a success.</param>
        /// <param name="Body">The body on success, when one was read.</param>
        private readonly record struct Exchange<TResponse>(HttpStatusCode Status, bool IsSuccess, TResponse? Body)
            where TResponse : class;
    }
}
