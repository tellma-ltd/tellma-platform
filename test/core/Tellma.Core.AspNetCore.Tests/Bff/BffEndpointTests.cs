// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using Microsoft.Net.Http.Headers;
using System.Net;
using System.Text.Json;
using System.Text.Json.Serialization;
using Tellma.Core.Abstractions.Tenancy;
using Tellma.Core.AspNetCore.Sessions;
using Tellma.Core.AspNetCore.Tests.Infrastructure;

namespace Tellma.Core.AspNetCore.Tests.Bff
{
    /// <summary>
    ///     The BFF's sign-in, landing, user document and sign-out, driven through the real pipeline and a
    ///     fake identity server's back channel.
    /// </summary>
    public sealed class BffEndpointTests
    {
        /// <summary>How the landing vectors are read.</summary>
        private static readonly JsonSerializerOptions VectorJson = new(JsonSerializerDefaults.Web)
        {
            Converters = { new JsonStringEnumConverter() },
        };

        // ------------------------------------------------------------------------------------------
        // Sign-in
        // ------------------------------------------------------------------------------------------

        [Fact]
        public async Task The_login_pushes_the_request_with_its_language_and_redirects_to_the_authorize_endpoint()
        {
            await using TellmaTestHost host = await TellmaTestHost.StartAsync();

            using HttpResponseMessage response = await host.Client.GetAsync(new Uri("/bff/login?ui_locales=ar%20en&returnUrl=%2F1%2Fhome", UriKind.Relative), TestContext.Current.CancellationToken);

            // --- The browser goes to the authorize endpoint with the pushed request's reference alone ---
            Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
            string location = response.Headers.Location!.ToString();
            Assert.StartsWith(FakeIdentityServer.AuthorizationEndpoint + "?", location, StringComparison.Ordinal);
            Assert.Contains("request_uri=" + Uri.EscapeDataString(FakeIdentityServer.RequestUri), location, StringComparison.Ordinal);
            Assert.DoesNotContain("ui_locales", location, StringComparison.Ordinal);
            Assert.Equal("no-store", response.Headers.CacheControl?.ToString());

            // --- The pushed request carries the client's credentials, PKCE, the scopes and the language ---
            IdentityRequest pushed = Assert.Single(host.IdentityServer.Requests, static request => request.Path == "/connect/par");
            Assert.Equal(TellmaTestHost.Slug, pushed.Form["client_id"]);
            Assert.Equal(TellmaTestHost.ClientSecret, pushed.Form["client_secret"]);
            Assert.Equal("code", pushed.Form["response_type"]);
            Assert.Equal("S256", pushed.Form["code_challenge_method"]);
            Assert.Equal("openid profile email offline_access tellma_api", pushed.Form["scope"]);
            Assert.Equal(TellmaTestHost.PublicOrigin + "/signin-oidc", pushed.Form["redirect_uri"]);
            Assert.Equal("ar en", pushed.Form["ui_locales"]);
            Assert.False(pushed.Form.ContainsKey("acr_values"));
        }

        [Fact]
        public async Task The_login_forwards_the_requested_assurance_and_age()
        {
            await using TellmaTestHost host = await TellmaTestHost.StartAsync();

            using HttpResponseMessage response = await host.Client.GetAsync(new Uri("/bff/login?acr_values=urn%3Atellma%3Aacr%3Amfa&max_age=0", UriKind.Relative), TestContext.Current.CancellationToken);

            Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
            IdentityRequest pushed = Assert.Single(host.IdentityServer.Requests, static request => request.Path == "/connect/par");
            Assert.Equal("urn:tellma:acr:mfa", pushed.Form["acr_values"]);
            Assert.Equal("0", pushed.Form["max_age"]);
        }

        [Fact]
        public async Task The_login_forwards_the_distribution_s_policy()
        {
            await using TellmaTestHost host = await StartWithPolicyAsync(new LoginPolicy("urn:tellma:acr:mfa", TimeSpan.FromHours(1), ["password", "passkey"]));

            using HttpResponseMessage response = await host.Client.GetAsync(new Uri("/bff/login", UriKind.Relative), TestContext.Current.CancellationToken);

            Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
            IdentityRequest pushed = Assert.Single(host.IdentityServer.Requests, static request => request.Path == "/connect/par");
            Assert.Equal("urn:tellma:acr:mfa", pushed.Form["acr_values"]);
            Assert.Equal("3600", pushed.Form["max_age"]);
            Assert.Equal("password passkey", pushed.Form["tellma_allowed_methods"]);
        }

        [Fact]
        public async Task A_caller_cannot_weaken_the_distribution_s_policy()
        {
            await using TellmaTestHost host = await StartWithPolicyAsync(new LoginPolicy("urn:tellma:acr:mfa", TimeSpan.FromHours(1), ["passkey"]));

            using HttpResponseMessage response = await host.Client.GetAsync(
                new Uri("/bff/login?acr_values=urn%3Atellma%3Aacr%3Apwd&max_age=999999", UriKind.Relative), TestContext.Current.CancellationToken);

            Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
            IdentityRequest pushed = Assert.Single(host.IdentityServer.Requests, static request => request.Path == "/connect/par");
            Assert.Equal("urn:tellma:acr:mfa", pushed.Form["acr_values"]);
            Assert.Equal("3600", pushed.Form["max_age"]);
            Assert.Equal("passkey", pushed.Form["tellma_allowed_methods"]);
        }

        [Fact]
        public async Task A_caller_may_ask_for_a_more_recent_sign_in_than_the_policy()
        {
            await using TellmaTestHost host = await StartWithPolicyAsync(new LoginPolicy(null, TimeSpan.FromHours(1), null));

            using HttpResponseMessage response = await host.Client.GetAsync(
                new Uri("/bff/login?acr_values=urn%3Atellma%3Aacr%3Apwd&max_age=60", UriKind.Relative), TestContext.Current.CancellationToken);

            Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
            IdentityRequest pushed = Assert.Single(host.IdentityServer.Requests, static request => request.Path == "/connect/par");
            Assert.Equal("urn:tellma:acr:pwd", pushed.Form["acr_values"]);
            Assert.Equal("60", pushed.Form["max_age"]);
            Assert.False(pushed.Form.ContainsKey("tellma_allowed_methods"));
        }

        [Theory]
        [InlineData("?returnUrl=https%3A%2F%2Fevil.example%2F")]
        [InlineData("?returnUrl=%2F%2Fevil.example")]
        [InlineData("?returnUrl=%2F%5Cevil.example")]
        [InlineData("?max_age=-1")]
        [InlineData("?max_age=soon")]
        [InlineData("?ui_locales=%3Cscript%3E")]
        [InlineData("?returnUrl=%2Fa&returnUrl=%2Fb")]
        public async Task The_login_refuses_a_parameter_it_cannot_trust(string query)
        {
            await using TellmaTestHost host = await TellmaTestHost.StartAsync();

            using HttpResponseMessage response = await host.Client.GetAsync(new Uri("/bff/login" + query, UriKind.Relative), TestContext.Current.CancellationToken);

            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
            Assert.Equal("bad-request", (await Problem.ReadAsync(response)).Code);
            Assert.DoesNotContain(host.IdentityServer.Requests, static request => request.Path == "/connect/par");
        }

        [Fact]
        public async Task A_sign_in_persists_the_ticket_and_the_cookie_expires_with_the_row()
        {
            await using TellmaTestHost host = await TellmaTestHost.StartAsync();

            SignInResult result = await SignInFlow.SignInAsync(host, "?returnUrl=%2F1%2Fhome");

            // --- The callback returns where the login was asked to ---
            Assert.Equal(HttpStatusCode.Redirect, result.Callback.StatusCode);
            Assert.Equal("/1/home", result.Callback.Headers.Location!.ToString());

            // --- One row, whose expiry is the persistent cookie's ---
            string key = Assert.Single(host.Rows.Keys());
            SetCookieHeaderValue session = result.Cookies[host.SessionCookieName];
            Assert.True(session.HttpOnly);
            Assert.True(session.Secure);
            Assert.Equal(Microsoft.Net.Http.Headers.SameSiteMode.Lax, session.SameSite);
            Assert.Equal(host.Rows.ExpiresAt(key), session.Expires);
            Assert.Equal(host.Time.GetUtcNow().AddDays(7).ToUnixTimeSeconds(), session.Expires!.Value.ToUnixTimeSeconds());

            // --- The cookie carries a reference, never the ticket's claims ---
            Assert.DoesNotContain("sub-1", session.Value.ToString(), StringComparison.Ordinal);

            // --- The profile cookie names the session by its handle, and expires with it ---
            SetCookieHeaderValue profile = result.Cookies[TellmaAuthentication.ProfileCookieName];
            Assert.False(profile.HttpOnly);
            Assert.Equal(session.Expires, profile.Expires);
            using JsonDocument document = ProfileDocument(profile.Value.ToString());
            Assert.Equal(2, document.RootElement.GetProperty("v").GetInt32());
            Assert.Equal("sub-1", document.RootElement.GetProperty("sub").GetString());
            Assert.Equal(SessionKeys.HandleOf(key), document.RootElement.GetProperty("sh").GetString());
            Assert.Equal("Jane Doe", document.RootElement.GetProperty("name").GetString());
        }

        [Fact]
        public async Task A_signed_in_cookie_authenticates_the_user_document()
        {
            await using TellmaTestHost host = await TellmaTestHost.StartAsync();
            SignInResult result = await SignInFlow.SignInAsync(host);

            using HttpRequestMessage request = TellmaTestHost.SpaRequest(HttpMethod.Get, "/bff/user", result.SessionCookie(host));
            using HttpResponseMessage response = await host.Client.SendAsync(request, TestContext.Current.CancellationToken);

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            using var user = JsonDocument.Parse(await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
            Assert.Equal("sub-1", user.RootElement.GetProperty("sub").GetString());
        }

        [Fact]
        public async Task A_sign_in_over_an_existing_session_ends_that_session()
        {
            RecordingListener listener = new();
            await using TellmaTestHost host = await TellmaTestHost.StartAsync(options =>
                options.ConfigureServices = services => services.AddSingleton<ISessionTerminationListener>(listener));
            TestSession previous = host.Session();

            SignInResult result = await SignInFlow.SignInAsync(host, cookie: previous.Cookie);

            // --- A new key, the old row gone, its handle told ---
            Assert.Equal(HttpStatusCode.Redirect, result.Callback.StatusCode);
            string key = Assert.Single(host.Rows.Keys());
            Assert.NotEqual(previous.Key, key);
            Assert.Equal([previous.Handle], listener.Handles);
            Assert.NotEqual(previous.Cookie, result.SessionCookie(host));
        }

        /// <summary>Identity tokens a sign-in refuses, by what is wrong with them.</summary>
        /// <returns>Each case's name.</returns>
        public static TheoryData<string> RefusedIdentityTokens()
        {
            return ["no session", "no subject", "a subject with a space"];
        }

        [Theory]
        [MemberData(nameof(RefusedIdentityTokens))]
        public async Task A_sign_in_whose_identity_token_breaks_the_platform_s_rules_stores_no_session(string kind)
        {
            await using TellmaTestHost host = await TellmaTestHost.StartAsync();
            host.IdentityServer.ShapeIdToken = kind switch
            {
                "no session" => static claims => claims.Remove("sid"),
                "no subject" => static claims => claims.Remove("sub"),
                _ => static claims => claims["sub"] = "sub 1",
            };

            SignInResult result = await SignInFlow.SignInAsync(host, "?returnUrl=%2F1%2Fhome");

            Assert.NotEqual(HttpStatusCode.Redirect, result.Callback.StatusCode);
            Assert.Empty(host.Rows.Keys());
            Assert.False(result.Cookies.ContainsKey(host.SessionCookieName));
        }

        [Fact]
        public async Task A_sign_in_keeps_only_the_claims_the_platform_reads()
        {
            await using TellmaTestHost host = await TellmaTestHost.StartAsync();
            long now = host.Time.GetUtcNow().ToUnixTimeSeconds();
            host.IdentityServer.ShapeIdToken = claims =>
            {
                claims["acr"] = "urn:tellma:acr:mfa";
                claims["amr"] = new[] { "pwd", "otp" };
                claims["auth_time"] = now;
                claims["tellma_acr_auth_time"] = now;
                claims["idp"] = "local";
                claims["custom"] = "value";
                claims["groups"] = new[] { "admins" };
            };

            _ = await SignInFlow.SignInAsync(host);

            // --- The stored principal holds the kept claims, the assurance among them, and nothing else ---
            string key = Assert.Single(host.Rows.Keys());
            AuthenticationTicket stored = (await host.Services.GetRequiredService<ICatalogSessionStore>()
                .RetrieveAsync(key, new DefaultHttpContext(), TestContext.Current.CancellationToken))!;
            HashSet<string> types = [.. stored.Principal.Claims.Select(static claim => claim.Type)];
            Assert.Equal(
                ["acr", "amr", "auth_time", "email", "locale", "name", "sid", "sub", "tellma_acr_auth_time"],
                [.. types.Order(StringComparer.Ordinal)]);
            Assert.Equal("urn:tellma:acr:mfa", stored.Principal.FindFirst("acr")?.Value);
            Assert.Equal(["otp", "pwd"], [.. stored.Principal.FindAll("amr").Select(static claim => claim.Value).Order(StringComparer.Ordinal)]);
            Assert.Equal("sid-1", stored.Principal.FindFirst("sid")?.Value);
        }

        // ------------------------------------------------------------------------------------------
        // Landing
        // ------------------------------------------------------------------------------------------

        /// <summary>The launch-landing vectors the client's launch redirect reads too.</summary>
        /// <returns>Each vector's name.</returns>
        public static TheoryData<string> LandingVectors()
        {
            return [.. Load().Select(static vector => vector.Name)];
        }

        [Theory]
        [MemberData(nameof(LandingVectors))]
        public async Task A_sign_in_to_the_root_lands_where_the_launch_redirect_would(string name)
        {
            LandingVector vector = Load().Single(vector => vector.Name == name);
            await using TellmaTestHost host = await TellmaTestHost.StartAsync();
            host.Memberships.Memberships["sub-1"] = [.. vector.Tenants.Select(static tenant => new TenantMembership(
                new TenantDescriptor(tenant.Id, tenant.Name, tenant.Name2, tenant.Name3, tenant.Languages, tenant.Category, tenant.State, tenant.LiveTenantId),
                tenant.IsActive,
                DateTimeOffset.UnixEpoch))];

            SignInResult result = await SignInFlow.SignInAsync(host);

            Assert.Equal(HttpStatusCode.Redirect, result.Callback.StatusCode);
            Assert.Equal(vector.Expected is int tenantId ? $"/{tenantId}" : "/", result.Callback.Headers.Location!.ToString());
        }

        [Fact]
        public async Task A_sign_in_with_its_own_return_address_skips_the_landing()
        {
            await using TellmaTestHost host = await TellmaTestHost.StartAsync();
            host.Memberships.Memberships["sub-1"] = [Membership(17)];

            SignInResult result = await SignInFlow.SignInAsync(host, "?returnUrl=%2F1000%2Freports");

            Assert.Equal("/1000/reports", result.Callback.Headers.Location!.ToString());
        }

        [Fact]
        public async Task A_landing_that_fails_returns_to_the_root_and_is_logged()
        {
            CapturingLoggerProvider capture = new();
            await using TellmaTestHost host = await TellmaTestHost.StartAsync(options =>
                options.ConfigureServices = services => services.AddSingleton<ILoggerProvider>(capture));
            host.Memberships.Failure = new InvalidOperationException("The catalog is down.");

            SignInResult result = await SignInFlow.SignInAsync(host);

            // The sign-in completes: the session is stored, and the root decides.
            Assert.Equal(HttpStatusCode.Redirect, result.Callback.StatusCode);
            Assert.Equal("/", result.Callback.Headers.Location!.ToString());
            _ = Assert.Single(host.Rows.Keys());
            Assert.Contains(capture.Events, static captured => captured.Level == LogLevel.Warning && captured.Message.Contains("landing", StringComparison.OrdinalIgnoreCase));
        }

        // ------------------------------------------------------------------------------------------
        // The user document
        // ------------------------------------------------------------------------------------------

        [Fact]
        public async Task The_user_document_lists_the_user_and_the_memberships()
        {
            await using TellmaTestHost host = await TellmaTestHost.StartAsync();
            host.Memberships.Memberships["sub-1"] =
            [
                new TenantMembership(new TenantDescriptor(17, "Acme", "أكمي", null, ["en", "ar"], TenantCategory.Live, TenantState.Active, null), true, DateTimeOffset.UnixEpoch),
                new TenantMembership(new TenantDescriptor(18, "Acme Training", null, null, ["en"], TenantCategory.Sandbox, TenantState.ReadOnly, 17), false, DateTimeOffset.UnixEpoch),
            ];

            using HttpRequestMessage request = TellmaTestHost.SpaRequest(HttpMethod.Get, "/bff/user", host.Cookie(acr: "urn:tellma:acr:pwd"));
            using HttpResponseMessage response = await host.Client.SendAsync(request, TestContext.Current.CancellationToken);

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.Equal("no-store", response.Headers.CacheControl?.ToString());
            string body = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
            Assert.Equal(
                """{"sub":"sub-1","name":"Jane Doe","email":"jane@example.com","locale":"en","acr":"urn:tellma:acr:pwd","tenants":[""" +
                """{"id":17,"name":"Acme","name2":"أكمي","name3":null,"languages":["en","ar"],"category":"Live","state":"Active","liveTenantId":null,"isActive":true},""" +
                """{"id":18,"name":"Acme Training","name2":null,"name3":null,"languages":["en"],"category":"Sandbox","state":"ReadOnly","liveTenantId":17,"isActive":false}]}""",
                body);
        }

        [Fact]
        public async Task The_user_document_answers_401_without_a_session()
        {
            await using TellmaTestHost host = await TellmaTestHost.StartAsync();

            using HttpRequestMessage request = TellmaTestHost.SpaRequest(HttpMethod.Get, "/bff/user", cookie: null);
            using HttpResponseMessage response = await host.Client.SendAsync(request, TestContext.Current.CancellationToken);

            Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
            Assert.Equal("unauthenticated", (await Problem.ReadAsync(response)).Code);
        }

        [Fact]
        public async Task The_user_document_refuses_a_cookie_whose_row_is_gone()
        {
            await using TellmaTestHost host = await TellmaTestHost.StartAsync();
            TestSession session = host.Session();
            await host.Services.GetRequiredService<ICatalogSessionStore>().RevokeBySubjectAsync("sub-1", TestContext.Current.CancellationToken);

            using HttpRequestMessage request = TellmaTestHost.SpaRequest(HttpMethod.Get, "/bff/user", session.Cookie);
            using HttpResponseMessage response = await host.Client.SendAsync(request, TestContext.Current.CancellationToken);

            Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        }

        // ------------------------------------------------------------------------------------------
        // Sign-out
        // ------------------------------------------------------------------------------------------

        [Fact]
        public async Task A_local_sign_out_ends_this_session_alone_and_tells_the_listener_its_handle()
        {
            RecordingListener listener = new();
            await using TellmaTestHost host = await TellmaTestHost.StartAsync(options =>
                options.ConfigureServices = services => services.AddSingleton<ISessionTerminationListener>(listener));
            TestSession session = host.Session();
            TestSession other = host.Session();
            List<string>? rowsWhenTold = null;
            listener.During = (_, _) => rowsWhenTold = [.. host.Rows.Keys()];

            using HttpRequestMessage request = TellmaTestHost.SpaRequest(HttpMethod.Post, "/bff/logout", session.Cookie, /*lang=json,strict*/ """{"scope":"local"}""");
            using HttpResponseMessage response = await host.Client.SendAsync(request, TestContext.Current.CancellationToken);

            // --- The answer: where to go, nothing cached, both cookies cleared ---
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.Equal(/*lang=json,strict*/ """{"redirectUrl":"/signed-out"}""", await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
            Assert.Equal("application/json", response.Content.Headers.ContentType?.MediaType);
            Assert.Equal("\"cache\"", response.Headers.GetValues("Clear-Site-Data").Single());
            Dictionary<string, SetCookieHeaderValue> cookies = SignInFlow.Parse(response);
            Assert.True(cookies[host.SessionCookieName].Expires < host.Time.GetUtcNow());
            Assert.True(cookies[TellmaAuthentication.ProfileCookieName].Expires < host.Time.GetUtcNow());

            // --- This row gone, the other kept; the listener told the handle, never the key ---
            Assert.Equal([other.Key], host.Rows.Keys());
            (string subject, IReadOnlyList<string> handles) = Assert.Single(listener.Ended);
            Assert.Equal("sub-1", subject);
            Assert.Equal([session.Handle], handles);
            Assert.DoesNotContain(session.Key, handles);
            Assert.DoesNotContain(host.IdentityServer.Requests, static request => request.Path.Contains("endsession", StringComparison.Ordinal));

            // --- The listener was told while the row still existed ---
            Assert.NotNull(rowsWhenTold);
            Assert.Contains(session.Key, rowsWhenTold);
        }

        [Fact]
        public async Task A_global_sign_out_hands_back_the_identity_server_s_end_session_address()
        {
            await using TellmaTestHost host = await TellmaTestHost.StartAsync();
            TestSession session = host.Session();

            using HttpRequestMessage request = TellmaTestHost.SpaRequest(HttpMethod.Post, "/bff/logout", session.Cookie);
            using HttpResponseMessage response = await host.Client.SendAsync(request, TestContext.Current.CancellationToken);

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
            string redirect = body.RootElement.GetProperty("redirectUrl").GetString()!;
            Assert.StartsWith(FakeIdentityServer.EndSessionEndpoint + "?", redirect, StringComparison.Ordinal);
            Assert.Contains("post_logout_redirect_uri=" + Uri.EscapeDataString(TellmaTestHost.PublicOrigin + "/signout-callback-oidc"), redirect, StringComparison.Ordinal);
            Assert.Empty(host.Rows.Keys());
        }

        [Fact]
        public async Task A_global_sign_out_names_the_session_s_identity_token_and_clears_the_cache()
        {
            await using TellmaTestHost host = await TellmaTestHost.StartAsync();
            SignInResult signIn = await SignInFlow.SignInAsync(host);
            string key = Assert.Single(host.Rows.Keys());
            AuthenticationTicket stored = (await host.Services.GetRequiredService<ICatalogSessionStore>()
                .RetrieveAsync(key, new DefaultHttpContext(), TestContext.Current.CancellationToken))!;
            string idToken = stored.Properties.GetTokenValue(OpenIdConnectParameterNames.IdToken)!;

            using HttpRequestMessage request = TellmaTestHost.SpaRequest(HttpMethod.Post, "/bff/logout", signIn.SessionCookie(host), /*lang=json,strict*/ """{"scope":"global"}""");
            using HttpResponseMessage response = await host.Client.SendAsync(request, TestContext.Current.CancellationToken);

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
            string redirect = body.RootElement.GetProperty("redirectUrl").GetString()!;
            Assert.Contains("id_token_hint=" + Uri.EscapeDataString(idToken), redirect, StringComparison.Ordinal);
            Assert.Equal("\"cache\"", response.Headers.GetValues("Clear-Site-Data").Single());
            Assert.Empty(host.Rows.Keys());
        }

        /// <summary>Ways a global sign-out cannot reach the identity server's end-session endpoint.</summary>
        /// <returns>Each case's name.</returns>
        public static TheoryData<string> UnreachableEndSessions()
        {
            return ["metadata unreadable", "no end-session endpoint"];
        }

        [Theory]
        [MemberData(nameof(UnreachableEndSessions))]
        public async Task A_global_sign_out_that_cannot_reach_the_identity_server_signs_out_locally_and_is_logged(string kind)
        {
            CapturingLoggerProvider capture = new();
            TestConfigurationManager manager = new(_ =>
            {
                if (kind == "metadata unreadable")
                {
                    return null;
                }

                OpenIdConnectConfiguration configuration = FakeIdentityServer.Configuration();
                configuration.EndSessionEndpoint = null;
                return configuration;
            });
            await using TellmaTestHost host = await TellmaTestHost.StartAsync(options => options.ConfigureServices = services =>
            {
                _ = services.AddSingleton<ILoggerProvider>(capture);
                TestConfigurationManager.Install(services, manager);
            });
            TestSession session = host.Session();

            using HttpRequestMessage request = TellmaTestHost.SpaRequest(HttpMethod.Post, "/bff/logout", session.Cookie);
            using HttpResponseMessage response = await host.Client.SendAsync(request, TestContext.Current.CancellationToken);

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.Equal(/*lang=json,strict*/ """{"redirectUrl":"/signed-out"}""", await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
            Assert.Empty(host.Rows.Keys());
            Assert.Contains(capture.Events, static captured => captured.Level == LogLevel.Warning && captured.Message.Contains("end-session address", StringComparison.Ordinal));
        }

        [Fact]
        public async Task A_sign_out_without_the_client_header_is_refused_as_cross_site()
        {
            await using TellmaTestHost host = await TellmaTestHost.StartAsync();
            TestSession session = host.Session();

            using HttpRequestMessage request = new(HttpMethod.Post, "/bff/logout");
            request.Headers.Add("Cookie", session.Cookie);
            using HttpResponseMessage response = await host.Client.SendAsync(request, TestContext.Current.CancellationToken);

            Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
            _ = Assert.Single(host.Rows.Keys());
        }

        [Theory]
        [InlineData(/*lang=json,strict*/ """{"scope":"everywhere"}""")]
        [InlineData(/*lang=json,strict*/ """{"scope":1}""")]
        [InlineData("""[]""")]
        [InlineData("""{""")]
        public async Task A_sign_out_with_a_scope_it_does_not_know_is_refused(string json)
        {
            await using TellmaTestHost host = await TellmaTestHost.StartAsync();
            TestSession session = host.Session();

            using HttpRequestMessage request = TellmaTestHost.SpaRequest(HttpMethod.Post, "/bff/logout", session.Cookie, json);
            using HttpResponseMessage response = await host.Client.SendAsync(request, TestContext.Current.CancellationToken);

            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
            _ = Assert.Single(host.Rows.Keys());
        }

        // ------------------------------------------------------------------------------------------
        // Helpers
        // ------------------------------------------------------------------------------------------

        /// <summary>Starts a host whose distribution states a login policy.</summary>
        /// <param name="policy">The policy.</param>
        /// <returns>The started host.</returns>
        private static Task<TellmaTestHost> StartWithPolicyAsync(LoginPolicy policy)
        {
            return TellmaTestHost.StartAsync(options => options.ConfigureServices = services =>
                services.AddSingleton<IAuthenticationPolicyProvider>(new StubPolicyProvider(policy)));
        }

        /// <summary>An active live membership.</summary>
        /// <param name="tenantId">The tenant.</param>
        /// <returns>The membership.</returns>
        private static TenantMembership Membership(int tenantId)
        {
            return new TenantMembership(new TenantDescriptor(tenantId, "Acme", null, null, ["en"], TenantCategory.Live, TenantState.Active, null), true, DateTimeOffset.UnixEpoch);
        }

        /// <summary>The profile cookie's JSON.</summary>
        /// <param name="value">The cookie's value.</param>
        /// <returns>The document.</returns>
        private static JsonDocument ProfileDocument(string value)
        {
            return JsonDocument.Parse(System.Buffers.Text.Base64Url.DecodeFromChars(value));
        }

        /// <summary>Reads the shared vector file copied beside the suite.</summary>
        /// <returns>The vectors.</returns>
        private static List<LandingVector> Load()
        {
            string path = Path.Combine(AppContext.BaseDirectory, "Bff", "launch-landing-vectors.json");
            return JsonSerializer.Deserialize<List<LandingVector>>(File.ReadAllText(path), VectorJson)
                ?? throw new InvalidOperationException("The landing vectors are empty.");
        }

        /// <summary>A distribution's login policy, the same for every sign-in.</summary>
        /// <param name="policy">The policy.</param>
        private sealed class StubPolicyProvider(LoginPolicy policy) : IAuthenticationPolicyProvider
        {
            /// <inheritdoc />
            public Task<LoginPolicy> GetLoginPolicyAsync(int? tenantId, CancellationToken cancellationToken = default)
            {
                return Task.FromResult(policy);
            }
        }

        /// <summary>One vector: the memberships and the tenant the landing picks.</summary>
        /// <param name="Name">What the vector shows.</param>
        /// <param name="Tenants">The memberships, as the user document lists them.</param>
        /// <param name="Expected">The tenant landed on, or null for the picker.</param>
        private sealed record LandingVector(string Name, IReadOnlyList<VectorTenant> Tenants, int? Expected);

        /// <summary>One membership of a vector, in the user document's shape.</summary>
        /// <param name="Id">The tenant id.</param>
        /// <param name="Name">The first name.</param>
        /// <param name="Name2">The second name.</param>
        /// <param name="Name3">The third name.</param>
        /// <param name="Languages">The language codes.</param>
        /// <param name="Category">The category.</param>
        /// <param name="State">The state.</param>
        /// <param name="LiveTenantId">A sandbox's live tenant.</param>
        /// <param name="IsActive">Whether the membership is active.</param>
        private sealed record VectorTenant(
            int Id,
            string Name,
            string? Name2,
            string? Name3,
            IReadOnlyList<string> Languages,
            TenantCategory Category,
            TenantState State,
            int? LiveTenantId,
            bool IsActive);
    }
}
