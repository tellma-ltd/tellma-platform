// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Azure.Core;
using Azure.Core.Pipeline;
using Azure.Identity;
using Microsoft.Extensions.Logging.Abstractions;
using System.Net;
using Tellma.Connector.AcsEmail.Adapter.Tests.Infrastructure;
using Tellma.Core.Abstractions.Email;
using Tellma.Testing.Support.Options;

using AcsEmailClient = Azure.Communication.Email.EmailClient;
using AcsEmailClientOptions = Azure.Communication.Email.EmailClientOptions;
using TellmaEmailMessage = Tellma.Core.Abstractions.Email.EmailMessage;

namespace Tellma.Connector.AcsEmail.Adapter.Tests.Sending
{
    /// <summary>
    ///     The failures that do not arrive as a <c>RequestFailedException</c>. Both of these once
    ///     escaped the worker loop entirely, which discarded the results of every message in the
    ///     batch — including the ones ACS had already accepted, and which the caller would then have
    ///     sent a second time.
    /// </summary>
    public class AcsEmailFailureModeTests
    {
        [Fact]
        public async Task Reports_a_network_timeout_as_transient_and_keeps_what_was_already_accepted()
        {
            // Azure.Core raises its own network timeout as a TaskCanceledException on a token the
            // caller never cancelled, which is indistinguishable by type from the caller abandoning
            // the batch and has to be told apart by asking whose token it was.
            using SlowSecondRequestHandler handler = new(TimeSpan.FromSeconds(5));
            AcsEmailOptions options = AcsSenderHarness.DefaultOptions();
            options.MaxConcurrency = 1;
            options.TimeoutSeconds = 1;

            AcsEmailSender sender = Sender(options, handler, new StubTokenCredential());

            IReadOnlyList<EmailSendResult> results = await sender.SendAsync(
                [Message("first"), Message("second")], TestContext.Current.CancellationToken);

            Assert.Equal(EmailSendOutcome.Sent, results[0].Outcome);
            Assert.Equal(EmailSendOutcome.TransientFailure, results[1].Outcome);
        }

        [Fact]
        public async Task Treats_a_credential_failure_as_an_authentication_failure()
        {
            // A token credential is this transport's only authentication mechanism, and its failures
            // derive from Exception rather than from RequestFailedException.
            using SlowSecondRequestHandler handler = new(TimeSpan.Zero);
            AcsEmailOptions options = AcsSenderHarness.DefaultOptions();
            options.MaxConcurrency = 1;

            AcsEmailSender sender = Sender(options, handler, new BrokenTokenCredential());

            AuthenticationFailedException failure = await Assert.ThrowsAnyAsync<AuthenticationFailedException>(
                () => sender.SendAsync(
                    [Message("first"), Message("second")], TestContext.Current.CancellationToken));

            Assert.Contains("no identity", failure.Message, StringComparison.OrdinalIgnoreCase);
        }

        private static AcsEmailSender Sender(
            AcsEmailOptions options, HttpMessageHandler handler, TokenCredential credential)
        {
            return new AcsEmailSender(
                new StaticOptionsMonitor<AcsEmailOptions>(options),
                current =>
                {
                    AcsEmailClientOptions clientOptions =
                        AcsEmailServiceCollectionExtensions.BuildClientOptions(current);
                    clientOptions.Transport = new HttpClientTransport(new HttpClient(handler, disposeHandler: false));
                    return new AcsEmailClient(current.Endpoint, credential, clientOptions);
                },
                NullLogger<AcsEmailSender>.Instance);
        }

        private static TellmaEmailMessage Message(string subject)
        {
            return new TellmaEmailMessage
            {
                To = [new EmailAddress("recipient@example.com")],
                Subject = subject,
                TextBody = "Body",
                Audience = EmailAudience.Internal,
            };
        }

        /// <summary>Answers the first request at once and stalls every later one.</summary>
        private sealed class SlowSecondRequestHandler(TimeSpan delay) : HttpMessageHandler
        {
            private int _seen;

            protected override async Task<HttpResponseMessage> SendAsync(
                HttpRequestMessage request, CancellationToken cancellationToken)
            {
                int ordinal = Interlocked.Increment(ref _seen) - 1;
                if (ordinal > 0 && delay > TimeSpan.Zero)
                {
                    // Long past the configured network timeout, so Azure.Core's own timer fires.
                    await Task.Delay(delay, cancellationToken);
                }

                HttpResponseMessage response = new(HttpStatusCode.Accepted)
                {
                    Content = new StringContent($$"""{"id":"op-{{ordinal}}","status":"NotStarted"}"""),
                };

                response.Content.Headers.ContentType =
                    new System.Net.Http.Headers.MediaTypeHeaderValue("application/json");
                return response;
            }
        }

        /// <summary>A credential that cannot produce a token, as a managed identity blip looks.</summary>
        private sealed class BrokenTokenCredential : TokenCredential
        {
            public override AccessToken GetToken(TokenRequestContext requestContext, CancellationToken cancellationToken)
            {
                throw new CredentialUnavailableException("No identity endpoint answered.");
            }

            public override ValueTask<AccessToken> GetTokenAsync(
                TokenRequestContext requestContext, CancellationToken cancellationToken)
            {
                throw new CredentialUnavailableException("No identity endpoint answered.");
            }
        }
    }
}
