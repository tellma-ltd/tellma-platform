// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using System.Collections.Concurrent;
using System.Net;
using System.Reflection;
using System.Security.Claims;
using Tellma.Core.Abstractions.Email;
using Tellma.Core.AspNetCore.Hosting;
using Tellma.Core.AspNetCore.Tests.Infrastructure;
using Tellma.Core.Email;
using Tellma.Core.Testing.Email;
using Tellma.Identity.Data;
using Tellma.Identity.Services.Email;

namespace Tellma.Core.AspNetCore.Tests.Hosting
{
    /// <summary>
    ///     The in-proc identity mode against the real engine: its protocol endpoints and pages are
    ///     reachable under its path base without a session cookie, the platform's CSRF control leaves
    ///     its forms to the engine's own antiforgery, its own sign-in and the platform's session never
    ///     stand in for each other, and its mail goes through the platform's email pipeline live,
    ///     outside any tenant.
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

        [Fact]
        public void A_missing_engine_and_an_engine_of_another_release_are_told_apart()
        {
            (Type? absentType, MethodInfo? absent) = InProcIdentity.EntryPoint("Tellma.Identity.Hosting.Absent", "AddTellmaIdentity", typeof(IServiceCollection), typeof(IConfiguration));
            Assert.Null(absentType);
            Assert.Null(absent);

            (Type? type, MethodInfo? mismatched) = InProcIdentity.EntryPoint("Tellma.Identity.Hosting.TellmaIdentityServiceCollectionExtensions", "AddTellmaIdentity", typeof(IServiceCollection), typeof(string));
            Assert.NotNull(type);
            Assert.Null(mismatched);
        }

        // ------------------------------------------------------------------------------------------
        // The engine's own sign-in and the platform's session
        // ------------------------------------------------------------------------------------------

        [Fact]
        public async Task An_engine_page_that_requires_a_sign_in_challenges_with_the_engine_s_own_scheme()
        {
            using HttpResponseMessage response = await fixture.Client.GetAsync(new Uri("/id/Identity/Manage/Index", UriKind.Relative), TestContext.Current.CancellationToken);

            // The engine's sign-in page, not the platform's 401 problem.
            Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
            Assert.Contains("/id/Identity/Account/Login", response.Headers.Location!.ToString(), StringComparison.Ordinal);
        }

        [Fact]
        public async Task A_platform_session_does_not_sign_its_user_in_to_the_engine()
        {
            using HttpRequestMessage request = new(HttpMethod.Get, "/id/Identity/Manage/Index");
            request.Headers.Add("Cookie", fixture.Test.Cookie());

            using HttpResponseMessage response = await fixture.Client.SendAsync(request, TestContext.Current.CancellationToken);

            Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
            Assert.Contains("/id/Identity/Account/Login", response.Headers.Location!.ToString(), StringComparison.Ordinal);
        }

        [Fact]
        public async Task A_form_post_to_an_engine_page_that_requires_a_sign_in_answers_the_engine_s_challenge_not_the_platform_s_refusal()
        {
            // A browser form carrying the platform's session: no Tellma-Client header, a form body.
            using HttpRequestMessage request = new(HttpMethod.Post, "/id/Identity/Manage/Index")
            {
                Content = new FormUrlEncodedContent([new KeyValuePair<string, string>("Input.DisplayName", "Jane")]),
            };
            request.Headers.Add("Cookie", fixture.Test.Cookie());
            request.Headers.Add("Sec-Fetch-Site", "same-origin");
            request.Headers.Add("Origin", "https://localhost");

            using HttpResponseMessage response = await fixture.Client.SendAsync(request, TestContext.Current.CancellationToken);

            Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
            Assert.Contains("/id/Identity/Account/Login", response.Headers.Location!.ToString(), StringComparison.Ordinal);
        }

        [Fact]
        public async Task An_engine_sign_in_alone_does_not_authenticate_the_platform_s_session_surfaces()
        {
            // --- A sign-in cookie the engine itself accepts ---
            CookieAuthenticationOptions engine = fixture.Test.Services.GetRequiredService<IOptionsMonitor<CookieAuthenticationOptions>>().Get(IdentityConstants.ApplicationScheme);
            DateTimeOffset now = fixture.Test.Time.GetUtcNow();
            ClaimsPrincipal principal = new(new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, "sub-1"), new Claim("sub", "sub-1")], IdentityConstants.ApplicationScheme));
            string value = engine.TicketDataFormat.Protect(new AuthenticationTicket(
                principal,
                new AuthenticationProperties { IssuedUtc = now, ExpiresUtc = now.AddHours(1) },
                IdentityConstants.ApplicationScheme));
            string cookie = $"{engine.Cookie.Name}={value}";
            await using (AsyncServiceScope scope = fixture.Test.Services.CreateAsyncScope())
            {
                DefaultHttpContext probe = new() { RequestServices = scope.ServiceProvider };
                probe.Request.Headers.Cookie = cookie;
                AuthenticateResult accepted = await probe.AuthenticateAsync(IdentityConstants.ApplicationScheme);
                Assert.True(accepted.Succeeded);
            }

            // --- It is not a session of the platform's ---
            using HttpRequestMessage request = TellmaTestHost.SpaRequest(HttpMethod.Get, "/bff/user", cookie);
            using HttpResponseMessage response = await fixture.Client.SendAsync(request, TestContext.Current.CancellationToken);

            Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        }

        // ------------------------------------------------------------------------------------------
        // The engine's mail
        // ------------------------------------------------------------------------------------------

        [Fact]
        public async Task The_engine_s_sign_in_code_goes_through_the_email_pipeline_live_outside_any_tenant()
        {
            // --- A sign-in code, composed by the engine and queued as its sign-in path queues it ---
            string reference = "code-" + Guid.NewGuid().ToString("N");
            EmailMessage message;
            await using (AsyncServiceScope scope = fixture.Test.Services.CreateAsyncScope())
            {
                message = scope.ServiceProvider.GetRequiredService<EmailTemplateService>()
                    .SignInCode(new TellmaIdentityUser { Email = "jane@example.com", DisplayName = "Jane Doe" }, "123456", reference);
            }

            fixture.Test.Services.GetRequiredService<IEmailDispatcher>().Enqueue([message]);

            // --- The engine's worker sent it, in a scope bound to no tenant, through the router to the transport ---
            IReadOnlyList<CapturedEmail> captured = await fixture.Capture.WaitForAsync(
                all => all.Any(email => email.Message.Correlation?.Reference == reference),
                TimeSpan.FromSeconds(30));
            CapturedEmail sent = captured.Single(email => email.Message.Correlation?.Reference == reference);
            Assert.Equal("identity", sent.Message.Correlation!.OwnerKey);
            Assert.Null(sent.Message.Correlation.TenantId);
            Assert.DoesNotContain("[Sandbox]", sent.Message.Subject, StringComparison.Ordinal);
            Assert.Contains("123456", sent.Message.TextBody, StringComparison.Ordinal);

            // --- The outcome the engine recorded is a live send ---
            EmailSendResult recorded = await fixture.Recorder.WaitForAsync(reference);
            Assert.Equal(EmailSendOutcome.Sent, recorded.Outcome);
        }

        [Fact]
        public async Task Any_other_send_outside_a_tenant_still_fails()
        {
            await using AsyncServiceScope scope = fixture.Test.Services.CreateAsyncScope();
            IEmailSender sender = scope.ServiceProvider.GetRequiredService<IEmailSender>();
            EmailMessage message = new()
            {
                To = [new EmailAddress("jane@example.com")],
                Subject = "Hello",
                TextBody = "Hello.",
                Audience = EmailAudience.External,
            };

            _ = await Assert.ThrowsAsync<InvalidOperationException>(() => sender.SendAsync([message], TestContext.Current.CancellationToken));
            Assert.DoesNotContain(fixture.Capture.Captured, email => ReferenceEquals(email.Message, message));
        }

        /// <summary>One in-proc host for the suite, its database-bound background services removed.</summary>
        public sealed class Host : IAsyncLifetime
        {
            private TellmaTestHost? _host;

            /// <summary>The started host.</summary>
            internal TellmaTestHost Test => _host ?? throw new InvalidOperationException("The host has not started.");

            /// <summary>A client over TLS, which the engine's protocol endpoints require.</summary>
            internal HttpClient Client { get; private set; } = null!;

            /// <summary>The email pipeline's transport: every message the router sent on.</summary>
            internal CapturingEmailSender Capture { get; private set; } = null!;

            /// <summary>The send outcomes the engine recorded.</summary>
            internal RecordingDispatchRecorder Recorder { get; } = new();

            /// <inheritdoc />
            public async ValueTask InitializeAsync()
            {
                _host = await TellmaTestHost.StartAsync(options =>
                {
                    // --- The in-proc mode, in Development with the engine's development keys ---
                    options.Environment = "Development";
                    _ = options.Settings.Remove("Tellma:Identity:Authority");
                    options.Settings["Tellma:Identity:Mode"] = "InProc";
                    options.Settings["Tellma:Identity:ServiceClientSecret"] = "test-service-secret";
                    options.Settings["TellmaIdentity:Keys:Signing:Source"] = "DevelopmentSelfSigned";
                    options.Settings["TellmaIdentity:Keys:Encryption:Source"] = "DevelopmentSelfSigned";
                    options.Settings["TellmaIdentity:Development:AllowDevelopmentCertificates"] = "true";
                    options.Settings["Email:Provider"] = CapturingEmailServiceCollectionExtensions.TransportName;

                    // --- The engine's store seeding and scheduler need its database, which this suite has none of ---
                    options.ConfigureServices = services =>
                    {
                        // The distribution's email pipeline, which the engine sends through, with a
                        // capturing transport behind the router; the engine's claim and record of
                        // each send, which need its database, stand in for by a recorder.
                        _ = services.AddTellmaEmail();
                        Capture = services.AddCapturingEmail(CapturingEmailMode.Transport);
                        services.RemoveAll<IEmailDispatchRecorder>();
                        _ = services.AddSingleton<IEmailDispatchRecorder>(Recorder);

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

        /// <summary>The engine's claim and record of each send, without its database: every message is claimed, every outcome kept.</summary>
        internal sealed class RecordingDispatchRecorder : IEmailDispatchRecorder
        {
            /// <summary>The outcomes recorded, by the message's correlation reference.</summary>
            private readonly ConcurrentDictionary<string, TaskCompletionSource<EmailSendResult>> _outcomes = new(StringComparer.Ordinal);

            /// <summary>Waits for the outcome recorded for a message.</summary>
            /// <param name="reference">The message's correlation reference.</param>
            /// <returns>The outcome.</returns>
            public Task<EmailSendResult> WaitForAsync(string reference)
            {
                return Pending(reference).Task.WaitAsync(TimeSpan.FromSeconds(30));
            }

            /// <inheritdoc />
            public Task<IReadOnlyList<EmailMessage>> ClaimAsync(IReadOnlyList<EmailMessage> messages, CancellationToken cancellationToken)
            {
                return Task.FromResult(messages);
            }

            /// <inheritdoc />
            public Task RecordAsync(IReadOnlyList<EmailMessage> messages, IReadOnlyList<EmailSendResult> results, CancellationToken cancellationToken)
            {
                for (int i = 0; i < messages.Count; i++)
                {
                    if (messages[i].Correlation?.Reference is string reference)
                    {
                        _ = Pending(reference).TrySetResult(results[i]);
                    }
                }

                return Task.CompletedTask;
            }

            /// <summary>The outcome's completion for a reference, created on first use.</summary>
            /// <param name="reference">The message's correlation reference.</param>
            /// <returns>The completion.</returns>
            private TaskCompletionSource<EmailSendResult> Pending(string reference)
            {
                return _outcomes.GetOrAdd(reference, static _ => new TaskCompletionSource<EmailSendResult>(TaskCreationOptions.RunContinuationsAsynchronously));
            }
        }
    }
}
