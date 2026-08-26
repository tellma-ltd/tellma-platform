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
    ///     <para>
    ///         Shared by the connector suites: a client that takes an <see cref="HttpClient" />
    ///         directly and a pipeline that accepts one through its transport are both driven by one
    ///         scripted handler.
    ///     </para>
    ///     <para>
    ///         The responder may be asynchronous, which is what lets a suite hold a response open
    ///         while it arranges something else — a second caller arriving at a single-flight gate,
    ///         a clock advanced past a timeout — instead of racing a real one.
    ///     </para>
    ///     <para>
    ///         What it recorded outlives it. Disposing the handler — which an
    ///         <see cref="HttpClient" /> does by default — leaves every recording readable, so a
    ///         suite that asserts after its client has gone sees the same thing it would have seen
    ///         before. The recorded requests carry no content and nothing unmanaged.
    ///     </para>
    /// </remarks>
    public sealed class ScriptedHttpMessageHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, string, CancellationToken, Task<HttpResponseMessage>> _responder;
        private readonly Lock _gate = new();
        private readonly List<string> _requestBodies = [];
        private readonly List<HttpRequestMessage> _requests = [];
        private readonly List<RecordedHttpRequest> _exchanges = [];

        /// <summary>Creates a handler that answers synchronously.</summary>
        /// <param name="responder">Given the request and its body, produces the response.</param>
        public ScriptedHttpMessageHandler(Func<HttpRequestMessage, string, HttpResponseMessage> responder)
        {
            ArgumentNullException.ThrowIfNull(responder);

            _responder = (request, body, _) => Task.FromResult(responder(request, body));
        }

        /// <summary>Creates a handler that answers asynchronously.</summary>
        /// <param name="responder">Given the request, its body, and the request's cancellation
        ///     token, produces the response.</param>
        public ScriptedHttpMessageHandler(
            Func<HttpRequestMessage, string, CancellationToken, Task<HttpResponseMessage>> responder)
        {
            ArgumentNullException.ThrowIfNull(responder);

            _responder = responder;
        }

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

        /// <summary>Every request seen, in arrival order, with its content headers and body.</summary>
        /// <remarks>
        ///     <see cref="Requests" /> keeps only the request headers, which is all the suites that
        ///     predate this needed. Content headers live on the body rather than the request, so a
        ///     suite asserting on a content type has to read them from here.
        /// </remarks>
        public IReadOnlyList<RecordedHttpRequest> Exchanges
        {
            get
            {
                lock (_gate)
                {
                    return [.. _exchanges];
                }
            }
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

        /// <inheritdoc />
        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            ArgumentNullException.ThrowIfNull(request);

            string body = request.Content is null
                ? string.Empty
                : await request.Content.ReadAsStringAsync(cancellationToken);

            var recorded = RecordedHttpRequest.From(request, body);

            lock (_gate)
            {
                _requestBodies.Add(body);
                _requests.Add(CloneForInspection(request));
                _exchanges.Add(recorded);
            }

            return await _responder(request, body, cancellationToken);
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

    /// <summary>One request a <see cref="ScriptedHttpMessageHandler" /> saw.</summary>
    /// <remarks>
    ///     A snapshot rather than a reference: the client disposes the request as soon as the call
    ///     returns, taking its content and headers with it.
    /// </remarks>
    /// <param name="Method">The verb.</param>
    /// <param name="RequestUri">Where it was sent.</param>
    /// <param name="Headers">Its request headers.</param>
    /// <param name="ContentHeaders">Its content headers, empty when it carried no body.</param>
    /// <param name="Body">Its body, empty when it carried none.</param>
    public sealed record RecordedHttpRequest(
        HttpMethod Method,
        Uri? RequestUri,
        IReadOnlyDictionary<string, IReadOnlyList<string>> Headers,
        IReadOnlyDictionary<string, IReadOnlyList<string>> ContentHeaders,
        string Body)
    {
        /// <summary>The first value of a request header, or null when it carried none.</summary>
        /// <param name="name">The header's name, matched case-insensitively.</param>
        /// <returns>The value.</returns>
        public string? Header(string name)
        {
            return Headers.TryGetValue(name, out IReadOnlyList<string>? values) && values.Count > 0
                ? values[0]
                : null;
        }

        /// <summary>The first value of a content header, or null when it carried none.</summary>
        /// <param name="name">The header's name, matched case-insensitively.</param>
        /// <returns>The value.</returns>
        public string? ContentHeader(string name)
        {
            return ContentHeaders.TryGetValue(name, out IReadOnlyList<string>? values) && values.Count > 0
                ? values[0]
                : null;
        }

        internal static RecordedHttpRequest From(HttpRequestMessage request, string body)
        {
            return new RecordedHttpRequest(
                request.Method,
                request.RequestUri,
                Snapshot(request.Headers),
                request.Content is null
                    ? new Dictionary<string, IReadOnlyList<string>>(StringComparer.OrdinalIgnoreCase)
                    : Snapshot(request.Content.Headers),
                body);
        }

        private static Dictionary<string, IReadOnlyList<string>> Snapshot(
            IEnumerable<KeyValuePair<string, IEnumerable<string>>> headers)
        {
            Dictionary<string, IReadOnlyList<string>> snapshot = new(StringComparer.OrdinalIgnoreCase);
            foreach (KeyValuePair<string, IEnumerable<string>> header in headers)
            {
                snapshot[header.Key] = [.. header.Value];
            }

            return snapshot;
        }
    }
}
