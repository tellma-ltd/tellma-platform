// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Microsoft.Extensions.Logging.Abstractions;
using System.Net;
using System.Text.Json;
using Tellma.Core.Abstractions.Email;
using Tellma.Core.Abstractions.Hosting;
using Tellma.Core.Testing.Email;
using Tellma.Testing.Support.Http;
using Tellma.Testing.Support.Options;

namespace Tellma.Connector.SendGrid.Adapter.Tests.Infrastructure
{
    /// <summary>
    ///     Drives the SendGrid sender over a scripted HTTP wire, keyed by the conformance ordinal a
    ///     message carries in its subject.
    /// </summary>
    internal sealed class SendGridSenderHarness : IEmailSenderHarness
    {
        private readonly Lock _gate = new();
        private readonly Dictionary<int, ScriptedReplyKind> _script = [];
        private readonly List<int> _attempted = [];
        private readonly ScriptedHttpMessageHandler _handler;
        private readonly SingleClientHttpClientFactory _httpClientFactory;

        /// <summary>Creates a harness.</summary>
        /// <param name="channel">Which channel the sender serves.</param>
        /// <param name="options">The transport options; defaults to a valid live configuration.</param>
        /// <param name="deployment">The deployment identity stamped into the correlation envelope.</param>
        public SendGridSenderHarness(
            SendGridChannel channel = SendGridChannel.Live,
            SendGridEmailOptions? options = null,
            DeploymentIdentity? deployment = null)
        {
            Options = options ?? DefaultOptions();
            _handler = new ScriptedHttpMessageHandler(Respond);
            _httpClientFactory = new SingleClientHttpClientFactory(_handler);

            Sender = new SendGridEmailSender(
                channel,
                new StaticOptionsMonitor<SendGridEmailOptions>(Options),
                _httpClientFactory,
                deployment ?? new DeploymentIdentity("etpharma", "Production"),
                NullLogger<SendGridEmailSender>.Instance);
        }

        /// <summary>The options the sender reads.</summary>
        public SendGridEmailOptions Options { get; }

        /// <summary>The bodies the wire saw, in arrival order.</summary>
        /// <remarks>
        ///     The handler's own record, not a second copy of it: this is read while a concurrent
        ///     batch may still be responding, so it has to be the snapshot the handler takes under
        ///     its own lock rather than a live list.
        /// </remarks>
        public IReadOnlyList<string> RequestBodies => _handler.RequestBodies;

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

        /// <summary>Builds a valid live configuration.</summary>
        /// <returns>The options.</returns>
        public static SendGridEmailOptions DefaultOptions()
        {
            SendGridEmailOptions options = new()
            {
                ApiKey = "SG.test-key",
                MaxConcurrency = 4,
            };

            options.From.Address = "no-reply@tellma.com";
            options.From.DisplayName = "Tellma";
            return options;
        }

        /// <inheritdoc />
        public ValueTask DisposeAsync()
        {
            _httpClientFactory.Dispose();
            return ValueTask.CompletedTask;
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
                ScriptedReplyKind.TransientRefusal => ScriptedHttpMessageHandler.Respond(
                    HttpStatusCode.ServiceUnavailable, /*lang=json,strict*/ """{"errors":[{"message":"try later"}]}"""),
                ScriptedReplyKind.PermanentRefusal => ScriptedHttpMessageHandler.Respond(
                    HttpStatusCode.BadRequest, /*lang=json,strict*/ """{"errors":[{"message":"bad address","field":"personalizations.0.to.0.email"}]}"""),
                ScriptedReplyKind.AuthFailure => ScriptedHttpMessageHandler.Respond(
                    HttpStatusCode.Unauthorized, /*lang=json,strict*/ """{"errors":[{"message":"permission denied"}]}"""),
                ScriptedReplyKind.Throttle => ScriptedHttpMessageHandler.Respond(
                    HttpStatusCode.TooManyRequests, /*lang=json,strict*/ """{"errors":[{"message":"rate limit"}]}"""),
                ScriptedReplyKind.Accept or _ => ScriptedHttpMessageHandler.Respond(
                    HttpStatusCode.Accepted, messageIdHeader: $"msg-{ordinal}"),
            };
        }

        private static int ReadOrdinal(string body)
        {
            using var document = JsonDocument.Parse(body);
            string? subject = document.RootElement.GetProperty("subject").GetString();
            return EmailSenderConformanceTests.TryGetOrdinal(subject, out int ordinal) ? ordinal : -1;
        }
    }
}
