// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using System.Net;
using Tellma.Core.Abstractions.Email;
using Tellma.Core.AspNetCore.Tests.Infrastructure;

namespace Tellma.Core.AspNetCore.Tests.Hosting
{
    /// <summary>
    ///     The in-proc identity mode against the real engine: its protocol endpoints and pages are
    ///     reachable under its path base without a session cookie, and the platform's CSRF control
    ///     leaves its forms to the engine's own antiforgery.
    /// </summary>
    public sealed class InProcIdentityTests(InProcIdentityTests.Host fixture) : IClassFixture<InProcIdentityTests.Host>
    {
        [Fact]
        public async Task The_authorize_endpoint_reaches_the_engine_without_a_session()
        {
            using HttpResponseMessage response = await fixture.Client.GetAsync(new Uri("/id/connect/authorize", UriKind.Relative), TestContext.Current.CancellationToken);

            // The engine's own refusal of a request that names no client (invalid_client), not the
            // platform's session challenge, which answers a problem document.
            Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
            Assert.NotEqual("application/problem+json", response.Content.Headers.ContentType?.MediaType);
            Assert.Contains("frame-ancestors 'none'", response.Headers.GetValues("Content-Security-Policy").Single(), StringComparison.Ordinal);
        }

        [Fact]
        public async Task The_token_endpoint_answers_the_protocol_s_own_error_to_a_form_from_no_browser()
        {
            using FormUrlEncodedContent content = new([new KeyValuePair<string, string>("grant_type", "urn:tellma:unknown")]);
            using HttpResponseMessage response = await fixture.Client.PostAsync(new Uri("/id/connect/token", UriKind.Relative), content, TestContext.Current.CancellationToken);

            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
            string body = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
            Assert.Contains("\"error\"", body, StringComparison.Ordinal);
        }

        [Fact]
        public async Task The_discovery_document_names_the_issuer_under_the_path_base()
        {
            using HttpResponseMessage response = await fixture.Client.GetAsync(new Uri("/id/.well-known/openid-configuration", UriKind.Relative), TestContext.Current.CancellationToken);

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            string body = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
            Assert.Contains($"\"issuer\": \"{TellmaTestHost.PublicOrigin}/id\"", body.Replace("\":\"", "\": \"", StringComparison.Ordinal), StringComparison.Ordinal);
        }

        [Fact]
        public async Task An_engine_page_is_served_without_a_session()
        {
            using HttpResponseMessage response = await fixture.Client.GetAsync(new Uri("/id/Identity/Account/Login", UriKind.Relative), TestContext.Current.CancellationToken);

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        }

        [Fact]
        public async Task An_engine_form_post_is_left_to_the_engine_s_antiforgery()
        {
            // A browser form: same-origin provenance, no Tellma-Client header, a form body.
            using HttpRequestMessage request = new(HttpMethod.Post, "/id/Identity/Account/Login")
            {
                Content = new FormUrlEncodedContent([new KeyValuePair<string, string>("Input.Email", "jane@example.com")]),
            };
            request.Headers.Add("Sec-Fetch-Site", "same-origin");
            request.Headers.Add("Origin", TellmaTestHost.PublicOrigin);

            using HttpResponseMessage response = await fixture.Client.SendAsync(request, TestContext.Current.CancellationToken);

            // The engine's antiforgery refuses the form without its token; the platform's CSRF
            // control, which would answer 403 or 415, never sees it.
            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        }

        /// <summary>One in-proc host for the suite, its database-bound background services removed.</summary>
        public sealed class Host : IAsyncLifetime
        {
            private TellmaTestHost? _host;

            /// <summary>The started host.</summary>
            internal TellmaTestHost Test => _host ?? throw new InvalidOperationException("The host has not started.");

            /// <summary>A client over TLS, which the engine's protocol endpoints require.</summary>
            internal HttpClient Client { get; private set; } = null!;

            /// <inheritdoc />
            public async ValueTask InitializeAsync()
            {
                _host = await TellmaTestHost.StartAsync(static options =>
                {
                    // --- The in-proc mode, in Development with the engine's development keys ---
                    options.Environment = "Development";
                    _ = options.Settings.Remove("Tellma:Identity:Authority");
                    options.Settings["Tellma:Identity:Mode"] = "InProc";
                    options.Settings["Tellma:Identity:ServiceClientSecret"] = "test-service-secret";
                    options.Settings["TellmaIdentity:Keys:Signing:Source"] = "DevelopmentSelfSigned";
                    options.Settings["TellmaIdentity:Keys:Encryption:Source"] = "DevelopmentSelfSigned";
                    options.Settings["TellmaIdentity:Development:AllowDevelopmentCertificates"] = "true";

                    // --- The engine's store seeding and scheduler need its database, which this suite has none of ---
                    options.ConfigureServices = static services =>
                    {
                        // The distribution's email pipeline, which the engine sends through; nothing is sent here.
                        _ = services.AddSingleton<IEmailSender, UnusedEmailSender>();

                        foreach (ServiceDescriptor descriptor in services
                            .Where(static descriptor => descriptor.ServiceType == typeof(IHostedService)
                                && descriptor.ImplementationType?.Name is "IdentitySeedHostedService" or "QuartzHostedService")
                            .ToList())
                        {
                            _ = services.Remove(descriptor);
                        }
                    };
                });
                Client = _host.CreateHttpsClient();
            }

            /// <inheritdoc />
            public async ValueTask DisposeAsync()
            {
                Client?.Dispose();
                if (_host is not null)
                {
                    await _host.DisposeAsync();
                }
            }
        }

        /// <summary>An email sender no test reaches: the suite sends no mail.</summary>
        private sealed class UnusedEmailSender : IEmailSender
        {
            /// <inheritdoc />
            public Task<IReadOnlyList<EmailSendResult>> SendAsync(IReadOnlyList<EmailMessage> messages, CancellationToken cancellationToken)
            {
                throw new InvalidOperationException("The in-proc suite sends no mail.");
            }
        }
    }
}
