// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json.Nodes;
using Tellma.Core.Abstractions.Api;
using Tellma.Distro.Acme.TestSupport;

namespace Tellma.Core.AspNetCore.IntegrationTests.Infrastructure
{
    /// <summary>
    ///     The single-page application's calls to the web surface, as a browser sends them from the
    ///     public origin: same-origin fetch metadata, the origin, the suite's client header
    ///     <c>test/1</c> and, when signed in, the session cookie.
    /// </summary>
    internal sealed class SpaClient : IDisposable
    {
        /// <summary>The client header every request carries unless a test removes or alters it.</summary>
        public const string ClientHeaderValue = "test/1";

        private readonly HttpClient _http;

        /// <summary>Initializes a client of a host.</summary>
        /// <param name="host">The host the requests go to, through its test server or over loopback to its Kestrel port.</param>
        /// <param name="cookie">The session cookie's header value; null for a caller with no session.</param>
        public SpaClient(AcmeHost host, string? cookie)
        {
            ArgumentNullException.ThrowIfNull(host);
            Host = host;
            Cookie = cookie;
            _http = new HttpClient(host.CreateHandler()) { BaseAddress = host.Address };
        }

        /// <summary>The host the requests go to.</summary>
        public AcmeHost Host { get; }

        /// <summary>The session cookie's header value; null for a caller with no session.</summary>
        public string? Cookie { get; }

        /// <summary>The test's cancellation.</summary>
        private static CancellationToken Token => TestContext.Current.CancellationToken;

        /// <summary>Posts a JSON body to a tenant's web surface.</summary>
        /// <param name="tenantId">The tenant.</param>
        /// <param name="route">The route after <c>api/web/</c>, such as <c>centers/query</c>.</param>
        /// <param name="body">The body; an empty object when null.</param>
        /// <param name="configure">Changes the request before it is sent, such as a header a case adds or removes; null for none.</param>
        /// <param name="cancellationToken">Aborts the request as a closed tab does; the test's own cancellation when null.</param>
        /// <returns>The answer, read whole.</returns>
        public async Task<ApiResponse> PostAsync(
            int tenantId, string route, JsonNode? body = null, Action<HttpRequestMessage>? configure = null, CancellationToken? cancellationToken = null)
        {
            using HttpRequestMessage request = Request(HttpMethod.Post, Path(tenantId, route));
            request.Content = new StringContent((body ?? new JsonObject()).ToJsonString(), Encoding.UTF8, "application/json");
            configure?.Invoke(request);
            return await SendAsync(request, cancellationToken ?? Token);
        }

        /// <summary>Gets a path of the host, with the browser's headers and no body.</summary>
        /// <param name="path">The path.</param>
        /// <param name="configure">Changes the request before it is sent; null for none.</param>
        /// <returns>The answer, read whole.</returns>
        public async Task<ApiResponse> GetAsync(string path, Action<HttpRequestMessage>? configure = null)
        {
            using HttpRequestMessage request = Request(HttpMethod.Get, path);
            configure?.Invoke(request);
            return await SendAsync(request, Token);
        }

        /// <summary>A route of a tenant's web surface.</summary>
        /// <param name="tenantId">The tenant.</param>
        /// <param name="route">The route after <c>api/web/</c>.</param>
        /// <returns>The path.</returns>
        public static string Path(int tenantId, string route)
        {
            return string.Create(CultureInfo.InvariantCulture, $"/{tenantId}/api/web/{route}");
        }

        /// <inheritdoc />
        public void Dispose()
        {
            _http.Dispose();
        }

        /// <summary>A request carrying the headers a browser at the public origin sends.</summary>
        /// <param name="method">The method.</param>
        /// <param name="path">The path.</param>
        /// <returns>The request; the caller disposes it.</returns>
        private HttpRequestMessage Request(HttpMethod method, string path)
        {
            HttpRequestMessage request = new(method, new Uri(path, UriKind.Relative));
            _ = request.Headers.TryAddWithoutValidation("Sec-Fetch-Site", "same-origin");
            _ = request.Headers.TryAddWithoutValidation("Origin", AcmeMigrator.PublicOrigin);
            _ = request.Headers.TryAddWithoutValidation(TellmaHeaders.Client, ClientHeaderValue);
            if (Cookie is not null)
            {
                _ = request.Headers.TryAddWithoutValidation("Cookie", Cookie);
            }

            return request;
        }

        /// <summary>Sends a request and reads its answer whole.</summary>
        /// <param name="request">The request.</param>
        /// <param name="cancellationToken">Aborts the request.</param>
        /// <returns>The answer.</returns>
        private async Task<ApiResponse> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            using HttpResponseMessage response = await _http.SendAsync(request, cancellationToken);
            byte[] body = await response.Content.ReadAsByteArrayAsync(cancellationToken);
            return new ApiResponse(response.StatusCode, response.Headers, response.Content.Headers, Encoding.UTF8.GetString(body), body, Host.Warnings);
        }
    }

    /// <summary>An answer of the web surface, read whole.</summary>
    /// <param name="Status">The status.</param>
    /// <param name="Headers">The response's headers.</param>
    /// <param name="ContentHeaders">The body's headers.</param>
    /// <param name="Text">The body's text, read as UTF-8.</param>
    /// <param name="Body">The body's bytes, for an answer that is a file rather than text.</param>
    /// <param name="Warnings">The host's warnings, for a failed assertion to report.</param>
    internal sealed record ApiResponse(HttpStatusCode Status, HttpResponseHeaders Headers, HttpContentHeaders ContentHeaders, string Text, ReadOnlyMemory<byte> Body, WarningLog Warnings)
    {
        /// <summary>The body as JSON; null when it is empty or not JSON.</summary>
        public JsonNode? Json
        {
            get
            {
                if (string.IsNullOrWhiteSpace(Text))
                {
                    return null;
                }

                try
                {
                    return JsonNode.Parse(Text);
                }
                catch (System.Text.Json.JsonException)
                {
                    return null;
                }
            }
        }

        /// <summary>The problem's code; null when the body is no problem.</summary>
        public string? Code => Json?["code"]?.GetValue<string>();

        /// <summary>One header's values joined, as the server wrote them; null when absent.</summary>
        /// <param name="name">The header's name.</param>
        /// <returns>The value.</returns>
        public string? Header(string name)
        {
            return Headers.NonValidated.TryGetValues(name, out HeaderStringValues values) ? string.Join(",", values)
                : ContentHeaders.NonValidated.TryGetValues(name, out HeaderStringValues content) ? string.Join(",", content)
                : null;
        }

        /// <summary>Asserts the status, and answers the body as JSON.</summary>
        /// <param name="status">The expected status.</param>
        /// <returns>The body; an empty object for an empty body.</returns>
        public JsonNode Expect(HttpStatusCode status)
        {
            Assert.True(Status == status, $"Expected {(int)status}, answered {this}");
            return Json ?? new JsonObject();
        }

        /// <summary>Asserts the answer is a problem of a status and a code, and answers its body.</summary>
        /// <param name="status">The expected status.</param>
        /// <param name="code">The expected problem code.</param>
        /// <returns>The problem's body.</returns>
        public JsonNode ExpectProblem(HttpStatusCode status, string code)
        {
            Assert.True(Status == status && Code == code, $"Expected {(int)status} {code}, answered {this}");
            return Json!;
        }

        /// <inheritdoc />
        public override string ToString()
        {
            return string.Create(CultureInfo.InvariantCulture, $"{(int)Status} {Text}\n{Warnings}");
        }
    }
}
