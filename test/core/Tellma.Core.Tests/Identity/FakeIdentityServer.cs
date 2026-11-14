// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using System.Collections.Concurrent;
using System.Net;
using System.Text;
using System.Text.Json.Nodes;

namespace Tellma.Core.Tests.Identity
{
    /// <summary>One request the fake identity server received, with its body read.</summary>
    /// <param name="Method">The method.</param>
    /// <param name="Uri">The absolute request URI.</param>
    /// <param name="Authorization">The Authorization header, or null.</param>
    /// <param name="Accept">The Accept header.</param>
    /// <param name="UserAgent">The User-Agent header.</param>
    /// <param name="ContentType">The body's media type, or null without a body.</param>
    /// <param name="Body">The body's text; empty without a body.</param>
    internal sealed record RecordedRequest(
        HttpMethod Method,
        Uri Uri,
        string? Authorization,
        string Accept,
        string UserAgent,
        string? ContentType,
        string Body)
    {
        /// <summary>Whether the request went to the token endpoint.</summary>
        public bool IsToken => Uri.AbsolutePath.EndsWith("/connect/token", StringComparison.Ordinal);

        /// <summary>The body parsed as JSON.</summary>
        /// <returns>The body's root node.</returns>
        public JsonObject Json()
        {
            return JsonNode.Parse(Body)!.AsObject();
        }

        /// <summary>The body parsed as a form.</summary>
        /// <returns>The form's fields, by name.</returns>
        public Dictionary<string, string> Form()
        {
            return Body.Split('&').Select(static pair => pair.Split('=', 2)).ToDictionary(
                static pair => Uri.UnescapeDataString(pair[0]),
                static pair => Uri.UnescapeDataString(pair[1].Replace('+', ' ')),
                StringComparer.Ordinal);
        }
    }

    /// <summary>
    ///     A fake identity server behind an <see cref="HttpMessageHandler" />: it records every request
    ///     and answers the token endpoint and the management API through replaceable handlers, whose
    ///     defaults issue <c>token-1</c>, <c>token-2</c>, … for an hour and echo every bulk request.
    ///     It is also the <see cref="IHttpClientFactory" /> the client under test creates clients from.
    /// </summary>
    internal sealed class FakeIdentityServer : HttpMessageHandler, IHttpClientFactory
    {
        /// <summary>How many tokens the server issued.</summary>
        private int _tokens;

        /// <summary>Creates the server with the default handlers.</summary>
        public FakeIdentityServer()
        {
            Token = static (_, number, _) => Task.FromResult(TokenAnswer("token-" + number, 3600));
            Api = static (request, _) => Task.FromResult(Echo(request));
        }

        /// <summary>Every request received, in arrival order.</summary>
        public ConcurrentQueue<RecordedRequest> Requests { get; } = new();

        /// <summary>The names of the HTTP clients created.</summary>
        public ConcurrentQueue<string> ClientNames { get; } = new();

        /// <summary>Answers a token request; given the request and the token's ordinal, from 1.</summary>
        public Func<RecordedRequest, int, CancellationToken, Task<HttpResponseMessage>> Token { get; set; }

        /// <summary>Answers a management API request.</summary>
        public Func<RecordedRequest, CancellationToken, Task<HttpResponseMessage>> Api { get; set; }

        /// <summary>The token requests received.</summary>
        public List<RecordedRequest> TokenRequests => [.. Requests.Where(static request => request.IsToken)];

        /// <summary>The management API requests received.</summary>
        public List<RecordedRequest> ApiRequests => [.. Requests.Where(static request => !request.IsToken)];

        /// <inheritdoc />
        public HttpClient CreateClient(string name)
        {
            ClientNames.Enqueue(name);
            return new HttpClient(this, disposeHandler: false) { Timeout = Timeout.InfiniteTimeSpan };
        }

        /// <summary>A JSON answer.</summary>
        /// <param name="status">The status.</param>
        /// <param name="json">The body.</param>
        /// <returns>The answer.</returns>
        public static HttpResponseMessage JsonAnswer(HttpStatusCode status, JsonNode json)
        {
            return new HttpResponseMessage(status) { Content = new StringContent(json.ToJsonString(), Encoding.UTF8, "application/json") };
        }

        /// <summary>A token endpoint's success.</summary>
        /// <param name="accessToken">The token.</param>
        /// <param name="expiresIn">Its lifetime in seconds; omitted when null.</param>
        /// <returns>The answer.</returns>
        public static HttpResponseMessage TokenAnswer(string accessToken, long? expiresIn)
        {
            JsonObject body = new() { ["access_token"] = accessToken, ["token_type"] = "Bearer" };
            if (expiresIn is long seconds)
            {
                body["expires_in"] = seconds;
            }

            return JsonAnswer(HttpStatusCode.OK, body);
        }

        /// <summary>
        ///     The management API's echo: each invited address answered as invited with the subject
        ///     <c>sub-&lt;address&gt;</c>, each subject's invitation as sent, a created service account,
        ///     and a deleted one.
        /// </summary>
        /// <param name="request">The request.</param>
        /// <returns>The answer.</returns>
        public static HttpResponseMessage Echo(RecordedRequest request)
        {
            string path = request.Uri.AbsolutePath;

            // --- The bulk invite and the bulk delivery status: one result per item, in order ---
            if (path.EndsWith("/invitations", StringComparison.Ordinal))
            {
                JsonArray results = [.. request.Json()["users"]!.AsArray().Select(static user => (JsonNode)new JsonObject
                {
                    ["email"] = user!["email"]!.GetValue<string>(),
                    ["sub"] = "sub-" + user["email"]!.GetValue<string>(),
                    ["status"] = "Invited",
                })];
                return JsonAnswer(HttpStatusCode.OK, new JsonObject { ["results"] = results });
            }

            if (path.EndsWith("/invitations/delivery-status", StringComparison.Ordinal))
            {
                JsonArray results = [.. request.Json()["subs"]!.AsArray().Select(static sub => (JsonNode)new JsonObject
                {
                    ["sub"] = sub!.GetValue<string>(),
                    ["state"] = "Sent",
                    ["expectsDeliveryEvents"] = true,
                    ["sentUtc"] = "2026-11-14T12:00:00Z",
                })];
                return JsonAnswer(HttpStatusCode.OK, new JsonObject { ["results"] = results });
            }

            // --- The service accounts ---
            return request.Method == HttpMethod.Delete
                ? new HttpResponseMessage(HttpStatusCode.NoContent)
                : JsonAnswer(HttpStatusCode.Created, new JsonObject { ["clientId"] = "acme-sa-1", ["clientSecret"] = "sa-secret-3b9e" });
        }

        /// <inheritdoc />
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            // --- Record the request as it arrived ---
            string body = request.Content is null ? string.Empty : await request.Content.ReadAsStringAsync(cancellationToken);
            RecordedRequest recorded = new(
                request.Method,
                request.RequestUri!,
                request.Headers.Authorization?.ToString(),
                request.Headers.Accept.ToString(),
                request.Headers.UserAgent.ToString(),
                request.Content?.Headers.ContentType?.MediaType,
                body);
            Requests.Enqueue(recorded);

            // --- Answer it ---
            return recorded.IsToken
                ? await Token(recorded, Interlocked.Increment(ref _tokens), cancellationToken)
                : await Api(recorded, cancellationToken);
        }
    }
}
