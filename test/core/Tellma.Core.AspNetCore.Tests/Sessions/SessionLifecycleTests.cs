// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using Microsoft.IdentityModel.Tokens;
using Microsoft.Net.Http.Headers;
using System.Globalization;
using System.Net;
using System.Security.Cryptography;
using System.Text.Json;
using Tellma.Core.Abstractions.Tenancy;
using Tellma.Core.AspNetCore.Sessions;
using Tellma.Core.AspNetCore.Tests.Infrastructure;

namespace Tellma.Core.AspNetCore.Tests.Sessions
{
    /// <summary>
    ///     A session after sign-in: the sliding renewal and the cookies it re-issues, the background
    ///     requests that renew nothing, and the token refresh with its single flight, its refusals and
    ///     the outage allowance.
    /// </summary>
    public sealed class SessionLifecycleTests
    {
        // ------------------------------------------------------------------------------------------
        // Renewal
        // ------------------------------------------------------------------------------------------

        [Fact]
        public async Task A_renewal_moves_the_row_and_both_cookies_to_the_same_expiry()
        {
            await using TellmaTestHost host = await TellmaTestHost.StartAsync();
            TestSession session = host.Session(accessTokenLifetime: TimeSpan.FromDays(30));
            DateTimeOffset before = host.Rows.ExpiresAt(session.Key)!.Value;
            host.Time.Advance(TimeSpan.FromDays(4));

            using HttpResponseMessage response = await GetUserAsync(host, session.Cookie);

            // --- The row slid a full idle window from now, and the persistent cookie with it ---
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            DateTimeOffset after = host.Rows.ExpiresAt(session.Key)!.Value;
            Assert.True(after > before);
            Assert.Equal(host.Time.GetUtcNow().AddDays(7).ToUnixTimeSeconds(), after.ToUnixTimeSeconds());
            Dictionary<string, SetCookieHeaderValue> cookies = SignInFlow.Parse(response);
            Assert.Equal(after, cookies[host.SessionCookieName].Expires);

            // --- The profile cookie is re-issued, version 2, naming the same session ---
            SetCookieHeaderValue profile = cookies[TellmaAuthentication.ProfileCookieName];
            Assert.Equal(after, profile.Expires);
            using var document = JsonDocument.Parse(System.Buffers.Text.Base64Url.DecodeFromChars(profile.Value.ToString()));
            Assert.Equal(2, document.RootElement.GetProperty("v").GetInt32());
            Assert.Equal(session.Handle, document.RootElement.GetProperty("sh").GetString());
        }

        [Fact]
        public async Task A_request_early_in_the_window_renews_nothing()
        {
            await using TellmaTestHost host = await TellmaTestHost.StartAsync();
            TestSession session = host.Session();
            host.Time.Advance(TimeSpan.FromMinutes(5));

            using HttpResponseMessage response = await GetUserAsync(host, session.Cookie);

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.False(response.Headers.Contains(HeaderNames.SetCookie));
            Assert.Equal(0, host.Rows.Renewals);
        }

        /// <summary>Requests that are background activity, by how they are marked.</summary>
        /// <returns>Each case's name.</returns>
        public static TheoryData<string> BackgroundRequests()
        {
            return ["activity header", "client telemetry", "hub negotiate"];
        }

        [Theory]
        [MemberData(nameof(BackgroundRequests))]
        public async Task A_background_request_renews_neither_cookie_nor_row(string kind)
        {
            await using TellmaTestHost host = await TellmaTestHost.StartAsync();
            TestSession session = host.Session(accessTokenLifetime: TimeSpan.FromDays(30));
            DateTimeOffset before = host.Rows.ExpiresAt(session.Key)!.Value;
            host.Time.Advance(TimeSpan.FromDays(4));

            using HttpRequestMessage request = kind switch
            {
                "activity header" => TellmaTestHost.SpaRequest(HttpMethod.Get, "/bff/user", session.Cookie),
                "client telemetry" => TellmaTestHost.SpaRequest(HttpMethod.Post, "/api/client-telemetry", session.Cookie, "{}"),
                _ => TellmaTestHost.SpaRequest(HttpMethod.Post, "/1/hub/negotiate", session.Cookie),
            };
            if (kind == "activity header")
            {
                request.Headers.Add("Tellma-Activity", "background");
            }

            using HttpResponseMessage response = await host.Client.SendAsync(request, TestContext.Current.CancellationToken);

            Assert.False(SignInFlow.Parse(response).ContainsKey(host.SessionCookieName));
            Assert.False(SignInFlow.Parse(response).ContainsKey(TellmaAuthentication.ProfileCookieName));
            Assert.Equal(before, host.Rows.ExpiresAt(session.Key));
            Assert.Equal(0, host.Rows.Renewals);
        }

        // ------------------------------------------------------------------------------------------
        // Refresh
        // ------------------------------------------------------------------------------------------

        [Fact]
        public async Task An_access_token_near_its_expiry_is_refreshed_and_the_row_moves_a_version()
        {
            await using TellmaTestHost host = await TellmaTestHost.StartAsync();
            TestSession session = host.Session(accessTokenLifetime: TimeSpan.FromSeconds(30));

            using HttpResponseMessage response = await GetUserAsync(host, session.Cookie);

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.Equal(1, host.IdentityServer.Redemptions);
            IdentityRequest redemption = host.IdentityServer.Requests.Single(static request => request.Form.GetValueOrDefault("grant_type") == "refresh_token");
            Assert.Equal("refresh-1", redemption.Form["refresh_token"]);
            Assert.Equal(TellmaTestHost.Slug, redemption.Form["client_id"]);
            Assert.Equal(TellmaTestHost.ClientSecret, redemption.Form["client_secret"]);
            Assert.Equal(1, host.Rows.TokensVersion(session.Key));

            // The refreshed tokens serve the next request without another redemption.
            using HttpResponseMessage next = await GetUserAsync(host, session.Cookie);
            Assert.Equal(HttpStatusCode.OK, next.StatusCode);
            Assert.Equal(1, host.IdentityServer.Redemptions);
        }

        [Fact]
        public async Task Concurrent_requests_of_one_session_redeem_its_refresh_token_once()
        {
            await using TellmaTestHost host = await TellmaTestHost.StartAsync();
            TestSession session = host.Session(accessTokenLifetime: TimeSpan.FromSeconds(30));
            TaskCompletionSource gate = new(TaskCreationOptions.RunContinuationsAsynchronously);
            host.IdentityServer.Refresh = async _ =>
            {
                await gate.Task;
                return null;
            };

            Task<HttpResponseMessage>[] requests = [.. Enumerable.Range(0, 8).Select(_ => GetUserAsync(host, session.Cookie))];
            await Task.Delay(200, TestContext.Current.CancellationToken);
            gate.SetResult();
            HttpResponseMessage[] responses = await Task.WhenAll(requests);

            Assert.All(responses, static response => Assert.Equal(HttpStatusCode.OK, response.StatusCode));
            Assert.Equal(1, host.IdentityServer.Redemptions);
            Assert.Equal(1, host.Rows.TokensVersion(session.Key));
            foreach (HttpResponseMessage response in responses)
            {
                response.Dispose();
            }
        }

        [Fact]
        public async Task A_refused_refresh_ends_the_session_and_tells_the_listener()
        {
            RecordingListener listener = new();
            await using TellmaTestHost host = await TellmaTestHost.StartAsync(options =>
                options.ConfigureServices = services => services.AddSingleton<ISessionTerminationListener>(listener));
            TestSession session = host.Session(accessTokenLifetime: TimeSpan.FromSeconds(30));
            host.IdentityServer.Refresh = static _ => Task.FromResult<HttpResponseMessage?>(
                FakeIdentityServer.Json(HttpStatusCode.BadRequest, /*lang=json,strict*/ """{"error":"invalid_grant"}"""));

            using HttpResponseMessage response = await GetUserAsync(host, session.Cookie);

            Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
            Assert.Empty(host.Rows.Keys());
            Assert.Equal([session.Handle], listener.Handles);
            Assert.True(SignInFlow.Parse(response)[host.SessionCookieName].Expires < host.Time.GetUtcNow());
        }

        [Fact]
        public async Task A_session_without_a_refresh_token_ends_when_its_access_token_does()
        {
            await using TellmaTestHost host = await TellmaTestHost.StartAsync();
            TestSession session = host.Session(accessTokenLifetime: TimeSpan.FromSeconds(30), refreshToken: null);

            using HttpResponseMessage response = await GetUserAsync(host, session.Cookie);

            Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
            Assert.Empty(host.Rows.Keys());
            Assert.Equal(0, host.IdentityServer.Redemptions);
        }

        [Fact]
        public async Task An_unreachable_identity_server_keeps_the_session_within_the_outage_allowance()
        {
            await using TellmaTestHost host = await TellmaTestHost.StartAsync();
            TestSession session = host.Session(accessTokenLifetime: TimeSpan.FromSeconds(30));
            host.IdentityServer.Refresh = static _ => Task.FromResult<HttpResponseMessage?>(new HttpResponseMessage(HttpStatusCode.ServiceUnavailable));

            using HttpResponseMessage response = await GetUserAsync(host, session.Cookie);

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.Equal([session.Key], host.Rows.Keys());
            Assert.Equal(0, host.Rows.TokensVersion(session.Key));
        }

        [Fact]
        public async Task An_outage_past_the_allowance_ends_the_session()
        {
            await using TellmaTestHost host = await TellmaTestHost.StartAsync();
            TestSession session = host.Session(accessTokenLifetime: TimeSpan.FromSeconds(30));
            host.IdentityServer.Refresh = static _ => Task.FromResult<HttpResponseMessage?>(new HttpResponseMessage(HttpStatusCode.ServiceUnavailable));
            host.Time.Advance(TimeSpan.FromHours(2));

            using HttpResponseMessage response = await GetUserAsync(host, session.Cookie);

            Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
            Assert.Empty(host.Rows.Keys());
        }

        [Fact]
        public async Task A_refresh_replaces_the_assurance_name_and_language_and_stores_the_new_tokens()
        {
            await using TellmaTestHost host = await TellmaTestHost.StartAsync();
            TestSession session = host.Session(acr: "urn:tellma:acr:pwd", accessTokenLifetime: TimeSpan.FromSeconds(30));
            long acrTime = host.Time.GetUtcNow().ToUnixTimeSeconds();
            host.IdentityServer.Refresh = _ => Task.FromResult<HttpResponseMessage?>(FakeIdentityServer.Tokens(
                "sub-1",
                nonce: null,
                TimeSpan.FromHours(1),
                acr: "urn:tellma:acr:mfa",
                shape: claims =>
                {
                    claims["name"] = "Jane Smith";
                    claims["locale"] = "ar";
                    claims["tellma_acr_auth_time"] = acrTime;
                },
                refreshToken: "refresh-2"));

            using HttpResponseMessage response = await GetUserAsync(host, session.Cookie);

            // --- The request already reads the refreshed claims ---
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            using var user = JsonDocument.Parse(await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
            Assert.Equal("Jane Smith", user.RootElement.GetProperty("name").GetString());
            Assert.Equal("ar", user.RootElement.GetProperty("locale").GetString());
            Assert.Equal("urn:tellma:acr:mfa", user.RootElement.GetProperty("acr").GetString());

            // --- The row holds the new tokens and the refreshed principal, the old claims replaced ---
            AuthenticationTicket stored = (await host.Services.GetRequiredService<ICatalogSessionStore>()
                .RetrieveAsync(session.Key, new DefaultHttpContext(), TestContext.Current.CancellationToken))!;
            Assert.Equal("refresh-2", stored.Properties.GetTokenValue(OpenIdConnectParameterNames.RefreshToken));
            Assert.NotEqual("access-1", stored.Properties.GetTokenValue(OpenIdConnectParameterNames.AccessToken));
            Assert.Equal("urn:tellma:acr:mfa", Assert.Single(stored.Principal.FindAll("acr")).Value);
            Assert.Equal(acrTime.ToString(CultureInfo.InvariantCulture), Assert.Single(stored.Principal.FindAll("tellma_acr_auth_time")).Value);
            Assert.Equal("Jane Smith", Assert.Single(stored.Principal.FindAll("name")).Value);
            Assert.Equal("ar", Assert.Single(stored.Principal.FindAll("locale")).Value);

            // --- The profile cookie is re-issued with the new name ---
            SetCookieHeaderValue profile = SignInFlow.Parse(response)[TellmaAuthentication.ProfileCookieName];
            using var document = JsonDocument.Parse(System.Buffers.Text.Base64Url.DecodeFromChars(profile.Value.ToString()));
            Assert.Equal("Jane Smith", document.RootElement.GetProperty("name").GetString());
        }

        [Fact]
        public async Task A_refresh_naming_another_subject_ends_the_session()
        {
            await using TellmaTestHost host = await TellmaTestHost.StartAsync();
            TestSession session = host.Session(accessTokenLifetime: TimeSpan.FromSeconds(30));
            host.IdentityServer.Refresh = static _ => Task.FromResult<HttpResponseMessage?>(FakeIdentityServer.Tokens("sub-2", nonce: null, TimeSpan.FromHours(1)));

            using HttpResponseMessage response = await GetUserAsync(host, session.Cookie);

            Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
            Assert.Empty(host.Rows.Keys());
        }

        [Fact]
        public async Task A_refreshed_identity_token_issued_to_another_client_ends_the_session()
        {
            await using TellmaTestHost host = await TellmaTestHost.StartAsync();
            TestSession session = host.Session(accessTokenLifetime: TimeSpan.FromSeconds(30));
            host.IdentityServer.Refresh = static _ => Task.FromResult<HttpResponseMessage?>(FakeIdentityServer.Tokens(
                "sub-1", nonce: null, TimeSpan.FromHours(1), shape: static claims => claims["aud"] = "another-client"));

            using HttpResponseMessage response = await GetUserAsync(host, session.Cookie);

            Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
            Assert.Empty(host.Rows.Keys());
        }

        [Fact]
        public async Task A_malformed_success_from_the_token_endpoint_puts_the_refresh_off()
        {
            await using TellmaTestHost host = await TellmaTestHost.StartAsync();
            TestSession session = host.Session(accessTokenLifetime: TimeSpan.FromSeconds(30));
            host.IdentityServer.Refresh = static _ => Task.FromResult<HttpResponseMessage?>(FakeIdentityServer.Json(HttpStatusCode.OK, "<html></html>"));

            using HttpResponseMessage response = await GetUserAsync(host, session.Cookie);

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.Equal([session.Key], host.Rows.Keys());
            Assert.Equal(0, host.Rows.TokensVersion(session.Key));
        }

        [Fact]
        public async Task A_refreshed_identity_token_signed_by_a_rolled_key_is_accepted_once_the_keys_are_read_again()
        {
            SymmetricSecurityKey rolled = new(RandomNumberGenerator.GetBytes(32)) { KeyId = "rolled-key" };
            TestConfigurationManager manager = new(refreshes =>
            {
                // The metadata lists the rolled key only once it has been read again.
                OpenIdConnectConfiguration configuration = FakeIdentityServer.Configuration();
                if (refreshes > 0)
                {
                    configuration.SigningKeys.Add(rolled);
                }

                return configuration;
            });
            await using TellmaTestHost host = await TellmaTestHost.StartAsync(options => options.ConfigureServices = services => TestConfigurationManager.Install(services, manager));
            TestSession session = host.Session(accessTokenLifetime: TimeSpan.FromSeconds(30));
            host.IdentityServer.Refresh = _ => Task.FromResult<HttpResponseMessage?>(FakeIdentityServer.Tokens("sub-1", nonce: null, TimeSpan.FromHours(1), key: rolled));

            using HttpResponseMessage response = await GetUserAsync(host, session.Cookie);

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.Equal(1, manager.Refreshes);
            Assert.Equal(1, host.Rows.TokensVersion(session.Key));
        }

        [Fact]
        public async Task A_refreshed_identity_token_signed_by_a_key_the_metadata_never_lists_puts_the_refresh_off()
        {
            SymmetricSecurityKey unknown = new(RandomNumberGenerator.GetBytes(32)) { KeyId = "unknown-key" };
            TestConfigurationManager manager = new(static _ => FakeIdentityServer.Configuration());
            await using TellmaTestHost host = await TellmaTestHost.StartAsync(options => options.ConfigureServices = services => TestConfigurationManager.Install(services, manager));
            TestSession session = host.Session(accessTokenLifetime: TimeSpan.FromSeconds(30));
            host.IdentityServer.Refresh = _ => Task.FromResult<HttpResponseMessage?>(FakeIdentityServer.Tokens("sub-1", nonce: null, TimeSpan.FromHours(1), key: unknown));

            using HttpResponseMessage response = await GetUserAsync(host, session.Cookie);

            // Stale metadata is not the session's fault: it is kept, on its current tokens.
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.Equal(1, manager.Refreshes);
            Assert.Equal([session.Key], host.Rows.Keys());
            Assert.Equal(0, host.Rows.TokensVersion(session.Key));
        }

        [Fact]
        public async Task A_refresh_put_off_is_not_tried_again_until_its_backoff_lapses()
        {
            await using TellmaTestHost host = await TellmaTestHost.StartAsync();
            TestSession session = host.Session(accessTokenLifetime: TimeSpan.FromSeconds(30));
            host.IdentityServer.Refresh = static _ => Task.FromResult<HttpResponseMessage?>(new HttpResponseMessage(HttpStatusCode.ServiceUnavailable));

            using HttpResponseMessage first = await GetUserAsync(host, session.Cookie);
            using HttpResponseMessage second = await GetUserAsync(host, session.Cookie);

            // --- Within the backoff the session is served on its tokens without asking again ---
            Assert.Equal(HttpStatusCode.OK, first.StatusCode);
            Assert.Equal(HttpStatusCode.OK, second.StatusCode);
            Assert.Equal(1, host.IdentityServer.Redemptions);

            // --- Past it, the next request tries again ---
            host.Time.Advance(SessionRefresher.DeferralBackoff + TimeSpan.FromSeconds(1));
            using HttpResponseMessage third = await GetUserAsync(host, session.Cookie);
            Assert.Equal(HttpStatusCode.OK, third.StatusCode);
            Assert.Equal(2, host.IdentityServer.Redemptions);
        }

        [Fact]
        public async Task An_identity_server_that_does_not_answer_is_given_up_on_at_the_redemption_s_deadline()
        {
            await using TellmaTestHost host = await TellmaTestHost.StartAsync();
            TestSession session = host.Session(accessTokenLifetime: TimeSpan.FromSeconds(30));
            host.IdentityServer.StallRefresh = true;

            Task<HttpResponseMessage> pending = GetUserAsync(host, session.Cookie);
            while (host.IdentityServer.Redemptions == 0)
            {
                await Task.Delay(10, TestContext.Current.CancellationToken);
            }

            host.Time.Advance(OidcTokenRedeemer.Timeout + TimeSpan.FromSeconds(1));
            using HttpResponseMessage response = await pending;

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.Equal([session.Key], host.Rows.Keys());
            Assert.Equal(0, host.Rows.TokensVersion(session.Key));
        }

        [Fact]
        public async Task A_session_past_its_absolute_lifetime_is_refused()
        {
            await using TellmaTestHost host = await TellmaTestHost.StartAsync(options =>
            {
                options.Settings["Tellma:Session:IdleLifetime"] = "7.00:00:00";
                options.Settings["Tellma:Session:AbsoluteLifetime"] = "10.00:00:00";
            });
            TestSession session = host.Session(accessTokenLifetime: TimeSpan.FromDays(30));
            host.Time.Advance(TimeSpan.FromDays(5));
            using HttpResponseMessage renewed = await GetUserAsync(host, session.Cookie);
            Assert.Equal(HttpStatusCode.OK, renewed.StatusCode);

            // The renewal was capped at the absolute lifetime, ten days from the sign-in.
            Assert.Equal(host.Time.GetUtcNow().AddDays(5).ToUnixTimeSeconds(), host.Rows.ExpiresAt(session.Key)!.Value.ToUnixTimeSeconds());
            host.Time.Advance(TimeSpan.FromDays(5));

            using HttpResponseMessage response = await GetUserAsync(host, session.Cookie);

            Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        }

        /// <summary>Reads the user document as the SPA does.</summary>
        /// <param name="host">The host.</param>
        /// <param name="cookie">The session cookie.</param>
        /// <returns>The response.</returns>
        private static async Task<HttpResponseMessage> GetUserAsync(TellmaTestHost host, string cookie)
        {
            using HttpRequestMessage request = TellmaTestHost.SpaRequest(HttpMethod.Get, "/bff/user", cookie);
            return await host.Client.SendAsync(request, TestContext.Current.CancellationToken);
        }
    }
}
