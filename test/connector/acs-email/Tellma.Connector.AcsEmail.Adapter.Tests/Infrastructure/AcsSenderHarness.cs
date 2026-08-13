// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Azure.Communication.Email;
using Azure.Core.Pipeline;
using Microsoft.Extensions.Logging.Abstractions;
using System.Globalization;
using System.Net;
using System.Text.Json;
using Tellma.Core.Abstractions.Email;
using Tellma.Core.Testing.Email;
using Tellma.Testing.Support.Http;
using Tellma.Testing.Support.Options;

namespace Tellma.Connector.AcsEmail.Adapter.Tests.Infrastructure
{
    /// <summary>
    ///     Drives the ACS sender over a scripted wire.
    /// </summary>
    /// <remarks>
    ///     The Azure pipeline is redirected through an ordinary <see cref="HttpMessageHandler" />, a
    ///     public seam on <c>ClientOptions.Transport</c> — which is what lets the same scripted
    ///     handler serve this transport and the SendGrid one.
    /// </remarks>
    internal sealed class AcsSenderHarness : IEmailSenderHarness
    {
        private readonly Lock _gate = new();
        private readonly Dictionary<int, ScriptedReplyKind> _script = [];
        private readonly List<int> _attempted = [];
        private readonly ScriptedHttpMessageHandler _handler;
        private readonly HttpClient _httpClient;

        /// <summary>Creates a harness.</summary>
        /// <param name="options">The transport options; defaults to a valid configuration.</param>
        public AcsSenderHarness(AcsEmailOptions? options = null)
        {
            Options = options ?? DefaultOptions();
            _handler = new ScriptedHttpMessageHandler(Respond);
            _httpClient = new HttpClient(_handler, disposeHandler: false);

            Sender = new AcsEmailSender(
                new StaticOptionsMonitor<AcsEmailOptions>(Options),
                BuildClient,
                NullLogger<AcsEmailSender>.Instance);
        }

        /// <summary>The options the sender reads.</summary>
        public AcsEmailOptions Options { get; }

        /// <summary>The request bodies the wire saw, in arrival order.</summary>
        public IReadOnlyList<string> RequestBodies => _handler.RequestBodies;

        /// <summary>The client options the last client was built with, for the retry-policy pin.</summary>
        public EmailClientOptions? LastClientOptions { get; private set; }

        /// <inheritdoc />
        public IEmailSender Sender { get; }

        /// <inheritdoc />
        public SenderCapabilities Capabilities =>
            SenderCapabilities.ScriptedOutcomes
            | SenderCapabilities.UpFrontAuthFailure
            | SenderCapabilities.MidBatchAuthFailure
            | SenderCapabilities.Throttling;

        /// <inheritdoc />
        public int MaxConcurrency => Options.MaxConcurrency;

        /// <inheritdoc />
        public IReadOnlyList<int> AttemptedOrdinals
        {
            get
            {
                lock (_gate)
                {
                    return [.. _attempted];
                }
            }
        }

        /// <inheritdoc />
        public void Script(int ordinal, ScriptedReplyKind reply)
        {
            lock (_gate)
            {
                _script[ordinal] = reply;
            }
        }

        /// <summary>Builds a valid configuration.</summary>
        /// <returns>The options.</returns>
        public static AcsEmailOptions DefaultOptions()
        {
            AcsEmailOptions options = new()
            {
                Endpoint = new Uri("https://tellma-test.communication.azure.com"),
                MaxConcurrency = 4,
            };

            options.From.Address = "no-reply@tellma.com";
            options.From.DisplayName = "Tellma";
            return options;
        }

        /// <inheritdoc />
        public ValueTask DisposeAsync()
        {
            _httpClient.Dispose();
            _handler.Dispose();
            return ValueTask.CompletedTask;
        }

        private EmailClient BuildClient(AcsEmailOptions options)
        {
            // The production factory, not a copy of it: a harness that re-declared the retry policy
            // would make every assertion about that policy a tautology. Only the transport is
            // substituted, which is the one thing a test has to change.
            EmailClientOptions clientOptions = AcsEmailServiceCollectionExtensions.BuildClientOptions(options);
            clientOptions.Transport = new HttpClientTransport(_httpClient);
            LastClientOptions = clientOptions;

            return new EmailClient(options.Endpoint, new StubTokenCredential(), clientOptions);
        }

        private HttpResponseMessage Respond(HttpRequestMessage request, string body)
        {
            int ordinal = ReadOrdinal(body);
            ScriptedReplyKind reply;

            lock (_gate)
            {
                if (ordinal >= 0)
                {
                    _attempted.Add(ordinal);
                }

                reply = _script.GetValueOrDefault(ordinal, ScriptedReplyKind.Accept);
            }

            return reply switch
            {
                ScriptedReplyKind.TransientRefusal => Error(HttpStatusCode.ServiceUnavailable, "ServiceUnavailable"),
                ScriptedReplyKind.PermanentRefusal => Error(HttpStatusCode.BadRequest, "InvalidRecipient"),
                ScriptedReplyKind.AuthFailure => Error(HttpStatusCode.Unauthorized, "Unauthorized"),
                ScriptedReplyKind.Throttle => Error(HttpStatusCode.TooManyRequests, "TooManyRequests"),
                ScriptedReplyKind.Accept or _ => Accepted(ordinal),
            };
        }

        private static HttpResponseMessage Accepted(int ordinal)
        {
            string operationId = $"op-{ordinal.ToString(CultureInfo.InvariantCulture)}";
            HttpResponseMessage response = ScriptedHttpMessageHandler.Respond(
                HttpStatusCode.Accepted,
                $$$"""{"id":"{{{operationId}}}","status":"NotStarted"}""");

            response.Content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("application/json");
            response.Headers.TryAddWithoutValidation("operation-location", $"https://tellma-test.communication.azure.com/emails/operations/{operationId}");
            response.Headers.TryAddWithoutValidation("x-ms-request-id", operationId);
            return response;
        }

        private static HttpResponseMessage Error(HttpStatusCode statusCode, string code)
        {
            HttpResponseMessage response = ScriptedHttpMessageHandler.Respond(
                statusCode, $$$"""{"error":{"code":"{{{code}}}","message":"scripted failure"}}""");

            response.Content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("application/json");
            return response;
        }

        private static int ReadOrdinal(string body)
        {
            if (string.IsNullOrEmpty(body))
            {
                return -1;
            }

            using var document = JsonDocument.Parse(body);
            string? subject = document.RootElement.TryGetProperty("content", out JsonElement content)
                && content.TryGetProperty("subject", out JsonElement subjectElement)
                    ? subjectElement.GetString()
                    : null;

            return EmailSenderConformanceTests.TryGetOrdinal(subject, out int ordinal) ? ordinal : -1;
        }
    }
}
