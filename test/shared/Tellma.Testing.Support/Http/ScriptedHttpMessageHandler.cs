// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using System.Net;

namespace Tellma.Testing.Support.Http
{
    /// <summary>
    ///     An <see cref="HttpMessageHandler" /> that answers from a script and records every request
    ///     body it saw.
    /// </summary>
    /// <remarks>
    ///     Shared by the SendGrid and ACS suites: SendGrid's client takes an <see cref="HttpClient" />
    ///     directly, and the Azure pipeline accepts one through its transport, so one scripted
    ///     handler drives both wires.
    /// </remarks>
    /// <param name="responder">Given the request and its body, produces the response.</param>
    public sealed class ScriptedHttpMessageHandler(
        Func<HttpRequestMessage, string, HttpResponseMessage> responder) : HttpMessageHandler
    {
        private readonly Lock _gate = new();
        private readonly List<string> _requestBodies = [];
        private readonly List<HttpRequestMessage> _requests = [];

        /// <summary>Every request body seen, in arrival order.</summary>
        public IReadOnlyList<string> RequestBodies
        {
            get
            {
                lock (_gate)
                {
                    return [.. _requestBodies];
                }
            }
        }

        /// <summary>Every request seen, in arrival order, with headers intact.</summary>
        public IReadOnlyList<HttpRequestMessage> Requests
        {
            get
            {
                lock (_gate)
                {
                    return [.. _requests];
                }
            }
        }

        /// <inheritdoc />
        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            ArgumentNullException.ThrowIfNull(request);

            string body = request.Content is null
                ? string.Empty
                : await request.Content.ReadAsStringAsync(cancellationToken);

            lock (_gate)
            {
                _requestBodies.Add(body);
                _requests.Add(CloneForInspection(request));
            }

            return responder(request, body);
        }

        /// <summary>Builds a response with a status code and an optional body.</summary>
        /// <param name="statusCode">The status.</param>
        /// <param name="body">The response body.</param>
        /// <param name="messageIdHeader">A value for the message-id response header, when the
        ///     provider returns one.</param>
        /// <param name="messageIdHeaderName">The message-id header's name.</param>
        /// <returns>The response.</returns>
        public static HttpResponseMessage Respond(
            HttpStatusCode statusCode,
            string? body = null,
            string? messageIdHeader = null,
            string messageIdHeaderName = "X-Message-Id")
        {
            HttpResponseMessage response = new(statusCode)
            {
                Content = new StringContent(body ?? string.Empty),
            };

            if (messageIdHeader is not null)
            {
                response.Headers.TryAddWithoutValidation(messageIdHeaderName, messageIdHeader);
            }

            return response;
        }

        private static HttpRequestMessage CloneForInspection(HttpRequestMessage request)
        {
            // The original is disposed by the client once the call returns, so a shallow copy of the
            // parts a test asserts on is kept instead.
            HttpRequestMessage clone = new(request.Method, request.RequestUri);
            foreach (KeyValuePair<string, IEnumerable<string>> header in request.Headers)
            {
                clone.Headers.TryAddWithoutValidation(header.Key, header.Value);
            }

            return clone;
        }
    }
}
