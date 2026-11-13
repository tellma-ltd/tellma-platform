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
using System.Collections.Concurrent;
using System.Globalization;
using System.Net;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text.Json;
using Tellma.Core.Abstractions.Tenancy;
using Tellma.Core.AspNetCore.Sessions;
using Tellma.Core.AspNetCore.Tests.Infrastructure;
using Tellma.Identity.Options;

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
            HttpResponseMessage[] responses = await Task.WhenAll(requests).WaitAsync(RealTimeLimit, TestContext.Current.CancellationToken);

            Assert.All(responses, static response => Assert.Equal(HttpStatusCode.OK, response.StatusCode));
            Assert.Equal(1, host.IdentityServer.Redemptions);
            Assert.Equal(1, host.Rows.TokensVersion(session.Key));
            foreach (HttpResponseMessage response in responses)
            {
                response.Dispose();
            }
        }

        [Fact]
        public async Task Requests_that_join_one_refresh_each_get_their_own_principal()
        {
            RecordingClaimsTransformation transformation = new();
            await using TellmaTestHost host = await TellmaTestHost.StartAsync(options => options.ConfigureServices = services =>
            {
                _ = services.AddHttpContextAccessor();
                _ = services.AddSingleton<IClaimsTransformation>(provider =>
                {
                    transformation.Accessor = provider.GetRequiredService<IHttpContextAccessor>();
                    return transformation;
                });
            });
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
            HttpResponseMessage[] responses = await Task.WhenAll(requests).WaitAsync(RealTimeLimit, TestContext.Current.CancellationToken);

            // --- One redemption served every request, and no identity reached two of them ---
            Assert.All(responses, static response => Assert.Equal(HttpStatusCode.OK, response.StatusCode));
            Assert.Equal(1, host.IdentityServer.Redemptions);
            Assert.All(
                transformation.Seen.GroupBy(static seen => seen.Identity, ReferenceEqualityComparer.Instance),
                static group => _ = Assert.Single(group.Select(static seen => seen.Request).Distinct(StringComparer.Ordinal)));
            Assert.Equal(8, transformation.Seen.Select(static seen => seen.Request).Distinct(StringComparer.Ordinal).Count());
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

        // ------------------------------------------------------------------------------------------
        // A refresh token that may be spent
        // ------------------------------------------------------------------------------------------

        [Fact]
        public void Every_retry_of_a_token_that_may_be_spent_lands_well_inside_the_identity_server_s_reuse_leeway()
        {
            TimeSpan leeway = new TellmaIdentityLifetimeOptions().RefreshTokenReuseLeeway;

            Assert.True(SessionRefresher.RetryWindow * 2 <= leeway, $"The retry window {SessionRefresher.RetryWindow} leaves less than half of the {leeway} leeway.");
            Assert.True(SessionRefresher.FlightWait <= OidcTokenRedeemer.Timeout);
        }

        [Fact]
        public async Task A_redemption_answered_after_its_deadline_is_retried_while_the_identity_server_still_accepts_the_token()
        {
            await using TellmaTestHost host = await TellmaTestHost.StartAsync();
            RotatingAuthority authority = Rotating(host);
            authority.Answers.Enqueue(RotatingAnswer.Stalled);
            TestSession session = host.Session(accessTokenLifetime: TimeSpan.FromSeconds(30));

            // --- The identity server spends the token but answers only after the deadline ---
            Task<HttpResponseMessage> pending = GetUserAsync(host, session.Cookie);
            await UntilAsync(() => host.IdentityServer.Redemptions == 1);
            await AdvanceAsync(host, TimeSpan.FromSeconds(12));
            using HttpResponseMessage first = await pending.WaitAsync(RealTimeLimit, TestContext.Current.CancellationToken);

            // --- The session's next request comes after the leeway of the token's first redemption ---
            await AdvanceAsync(host, TimeSpan.FromSeconds(23));
            using HttpResponseMessage later = await GetUserAsync(host, session.Cookie);

            Assert.Equal(HttpStatusCode.OK, first.StatusCode);
            Assert.Equal(HttpStatusCode.OK, later.StatusCode);
            Assert.Equal(0, authority.Refusals);
            Assert.Equal([session.Key], host.Rows.Keys());
            Assert.Equal(1, host.Rows.TokensVersion(session.Key));
        }

        [Fact]
        public async Task A_malformed_success_from_the_token_endpoint_is_retried_while_the_identity_server_still_accepts_the_token()
        {
            await using TellmaTestHost host = await TellmaTestHost.StartAsync();
            RotatingAuthority authority = Rotating(host);
            authority.Answers.Enqueue(RotatingAnswer.Malformed);
            TestSession session = host.Session(accessTokenLifetime: TimeSpan.FromSeconds(30));

            Task<HttpResponseMessage> pending = GetUserAsync(host, session.Cookie);
            await UntilAsync(() => host.IdentityServer.Redemptions == 1);
            await AdvanceAsync(host, TimeSpan.FromSeconds(12));
            using HttpResponseMessage first = await pending.WaitAsync(RealTimeLimit, TestContext.Current.CancellationToken);
            await AdvanceAsync(host, TimeSpan.FromSeconds(23));
            using HttpResponseMessage later = await GetUserAsync(host, session.Cookie);

            Assert.Equal(HttpStatusCode.OK, first.StatusCode);
            Assert.Equal(HttpStatusCode.OK, later.StatusCode);
            Assert.Equal(0, authority.Refusals);
            Assert.Equal(2, host.IdentityServer.Redemptions);
            Assert.Equal(1, host.Rows.TokensVersion(session.Key));
        }

        [Fact]
        public async Task A_refreshed_identity_token_signed_by_a_key_the_metadata_never_lists_is_stored_with_the_session_s_current_claims()
        {
            SymmetricSecurityKey unknown = new(RandomNumberGenerator.GetBytes(32)) { KeyId = "unknown-key" };
            TestConfigurationManager manager = new(static _ => FakeIdentityServer.Configuration());
            await using TellmaTestHost host = await TellmaTestHost.StartAsync(options => options.ConfigureServices = services => TestConfigurationManager.Install(services, manager));
            RotatingAuthority authority = Rotating(host);
            authority.Issue = refreshToken => FakeIdentityServer.Tokens(
                "sub-1", nonce: null, TimeSpan.FromHours(1), acr: "urn:tellma:acr:aal3", shape: static claims => claims["name"] = "Someone Else", key: unknown, refreshToken: refreshToken);
            TestSession session = host.Session(acr: "urn:tellma:acr:aal1", accessTokenLifetime: TimeSpan.FromSeconds(30));

            using HttpResponseMessage response = await GetUserAsync(host, session.Cookie);
            await AdvanceAsync(host, TimeSpan.FromSeconds(35));
            using HttpResponseMessage later = await GetUserAsync(host, session.Cookie);

            // --- The keys were read again once; the tokens were stored, the claims left as they were ---
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.Equal(HttpStatusCode.OK, later.StatusCode);
            Assert.Equal(1, manager.Refreshes);
            Assert.Equal(1, host.IdentityServer.Redemptions);
            Assert.Equal(0, authority.Refusals);
            Assert.Equal(1, host.Rows.TokensVersion(session.Key));
            AuthenticationTicket stored = (await host.Services.GetRequiredService<ICatalogSessionStore>()
                .RetrieveAsync(session.Key, new DefaultHttpContext(), TestContext.Current.CancellationToken))!;
            Assert.NotEqual("refresh-1", stored.Properties.GetTokenValue(OpenIdConnectParameterNames.RefreshToken));
            Assert.Equal("urn:tellma:acr:aal1", Assert.Single(stored.Principal.FindAll("acr")).Value);
            Assert.Equal("Jane Doe", Assert.Single(stored.Principal.FindAll("name")).Value);
        }

        [Fact]
        public async Task A_refreshed_identity_token_whose_keys_cannot_be_read_again_in_time_is_stored_with_the_session_s_current_claims()
        {
            SymmetricSecurityKey rolled = new(RandomNumberGenerator.GetBytes(32)) { KeyId = "rolled-key" };
            TestConfigurationManager manager = new(static _ => FakeIdentityServer.Configuration())
            {
                // The keys are read once; the read after the refresh never answers.
                Stalls = static refreshes => refreshes > 0,
            };
            await using TellmaTestHost host = await TellmaTestHost.StartAsync(options => options.ConfigureServices = services => TestConfigurationManager.Install(services, manager));
            RotatingAuthority authority = Rotating(host);
            authority.Issue = refreshToken => FakeIdentityServer.Tokens("sub-1", nonce: null, TimeSpan.FromHours(1), key: rolled, refreshToken: refreshToken);
            TestSession session = host.Session(accessTokenLifetime: TimeSpan.FromSeconds(30));

            Task<HttpResponseMessage> pending = GetUserAsync(host, session.Cookie);
            await UntilAsync(() => manager.Refreshes == 1);
            await AdvanceAsync(host, TimeSpan.FromSeconds(6));
            using HttpResponseMessage first = await pending.WaitAsync(RealTimeLimit, TestContext.Current.CancellationToken);
            await UntilAsync(() => host.Rows.TokensVersion(session.Key) == 1);
            await AdvanceAsync(host, TimeSpan.FromSeconds(29));
            using HttpResponseMessage later = await GetUserAsync(host, session.Cookie);

            Assert.Equal(HttpStatusCode.OK, first.StatusCode);
            Assert.Equal(HttpStatusCode.OK, later.StatusCode);
            Assert.Equal(1, host.IdentityServer.Redemptions);
            Assert.Equal(0, authority.Refusals);
        }

        [Fact]
        public async Task Metadata_that_is_never_read_puts_the_refresh_off_at_the_deadline_without_presenting_the_token()
        {
            TestConfigurationManager manager = new(static _ => FakeIdentityServer.Configuration()) { Stalls = static _ => true };
            await using TellmaTestHost host = await TellmaTestHost.StartAsync(options => options.ConfigureServices = services => TestConfigurationManager.Install(services, manager));
            TestSession session = host.Session(accessTokenLifetime: TimeSpan.FromSeconds(30));

            Task<HttpResponseMessage> pending = GetUserAsync(host, session.Cookie);
            await UntilAsync(() => manager.Reads == 1);
            await AdvanceUntilAsync(host, () => pending.IsCompleted, OidcTokenRedeemer.Timeout + TimeSpan.FromSeconds(1));
            using HttpResponseMessage response = await pending.WaitAsync(RealTimeLimit, TestContext.Current.CancellationToken);

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.Equal(0, host.IdentityServer.Redemptions);
            Assert.Equal([session.Key], host.Rows.Keys());
            Assert.Equal(0, host.Rows.TokensVersion(session.Key));
        }

        [Fact]
        public async Task Tokens_the_catalog_could_not_store_are_stored_by_the_next_refresh_instead_of_redeeming_the_spent_token()
        {
            await using TellmaTestHost host = await TellmaTestHost.StartAsync();
            RotatingAuthority authority = Rotating(host);
            TestSession session = host.Session(accessTokenLifetime: TimeSpan.FromSeconds(30));
            host.Rows.FailingReplacements = SessionRefresher.Retries + 1;

            // --- The redemption succeeds; every write of its tokens fails ---
            Task<HttpResponseMessage> pending = GetUserAsync(host, session.Cookie);
            await UntilAsync(() => host.Rows.ReplaceAttempts == 1);
            for (int attempt = 1; attempt <= SessionRefresher.Retries; attempt++)
            {
                // Each pause lapses, and the write is tried again.
                await AdvanceUntilAsync(host, () => host.Rows.ReplaceAttempts > attempt, TimeSpan.FromSeconds(5));
            }

            using HttpResponseMessage first = await pending.WaitAsync(RealTimeLimit, TestContext.Current.CancellationToken);
            Assert.Equal(SessionRefresher.Retries + 1, host.Rows.ReplaceAttempts);

            // --- Past the backoff and the leeway, the next request stores the tokens already in hand ---
            await AdvanceAsync(host, TimeSpan.FromSeconds(29));
            using HttpResponseMessage later = await GetUserAsync(host, session.Cookie);

            Assert.Equal(HttpStatusCode.OK, first.StatusCode);
            Assert.Equal(HttpStatusCode.OK, later.StatusCode);
            Assert.Equal(1, host.IdentityServer.Redemptions);
            Assert.Equal(0, authority.Refusals);
            Assert.Equal(1, host.Rows.TokensVersion(session.Key));
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

            // The clock moves a second at a time until the request is answered, so a wait armed after a
            // move is still released by a later one.
            Task<HttpResponseMessage> pending = GetUserAsync(host, session.Cookie);
            await UntilAsync(() => host.IdentityServer.Redemptions > 0);
            await AdvanceUntilAsync(host, () => pending.IsCompleted, OidcTokenRedeemer.Timeout + TimeSpan.FromSeconds(3));
            using HttpResponseMessage response = await pending.WaitAsync(RealTimeLimit, TestContext.Current.CancellationToken);

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

        /// <summary>How long a test waits in real time for a request the fake clock should have released.</summary>
        private static readonly TimeSpan RealTimeLimit = TimeSpan.FromSeconds(30);

        /// <summary>Answers the host's refresh grants as the platform's identity server does, with its reuse leeway.</summary>
        /// <param name="host">The host.</param>
        /// <returns>The authority.</returns>
        private static RotatingAuthority Rotating(TellmaTestHost host)
        {
            RotatingAuthority authority = new(host.Time, new TellmaIdentityLifetimeOptions().RefreshTokenReuseLeeway);
            host.IdentityServer.RefreshWithin = authority.AnswerAsync;
            return authority;
        }

        /// <summary>Moves the fake clock a second at a time, letting what each second releases run.</summary>
        /// <param name="host">The host.</param>
        /// <param name="span">How far to move it.</param>
        /// <returns>A task that completes when the clock has moved.</returns>
        private static async Task AdvanceAsync(TellmaTestHost host, TimeSpan span)
        {
            for (TimeSpan moved = TimeSpan.Zero; moved < span; moved += TimeSpan.FromSeconds(1))
            {
                host.Time.Advance(TimeSpan.FromSeconds(1));
                await Task.Delay(25, TestContext.Current.CancellationToken);
            }
        }

        /// <summary>Moves the fake clock a second at a time until a condition holds, or as far as a limit.</summary>
        /// <param name="host">The host.</param>
        /// <param name="condition">The condition.</param>
        /// <param name="limit">How far the clock may move.</param>
        /// <returns>A task that completes when the condition holds or the clock has moved the limit.</returns>
        private static async Task AdvanceUntilAsync(TellmaTestHost host, Func<bool> condition, TimeSpan limit)
        {
            for (TimeSpan moved = TimeSpan.Zero; moved < limit && !condition(); moved += TimeSpan.FromSeconds(1))
            {
                host.Time.Advance(TimeSpan.FromSeconds(1));
                await Task.Delay(25, TestContext.Current.CancellationToken);
            }
        }

        /// <summary>Waits in real time until a condition holds.</summary>
        /// <param name="condition">The condition.</param>
        /// <returns>A task that completes when it holds.</returns>
        private static async Task UntilAsync(Func<bool> condition)
        {
            using var limit = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
            limit.CancelAfter(RealTimeLimit);
            while (!condition())
            {
                await Task.Delay(10, limit.Token);
            }
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

        /// <summary>A claims transformation that records which request saw which identity.</summary>
        private sealed class RecordingClaimsTransformation : IClaimsTransformation
        {
            /// <summary>The request accessor, which names the request a principal reached.</summary>
            public IHttpContextAccessor? Accessor { get; set; }

            /// <summary>Each identity seen, with the request that saw it.</summary>
            public ConcurrentQueue<(ClaimsIdentity Identity, string Request)> Seen { get; } = new();

            /// <inheritdoc />
            public Task<ClaimsPrincipal> TransformAsync(ClaimsPrincipal principal)
            {
                string request = Accessor?.HttpContext?.TraceIdentifier ?? string.Empty;
                foreach (ClaimsIdentity identity in principal.Identities)
                {
                    Seen.Enqueue((identity, request));
                }

                return Task.FromResult(principal);
            }
        }
    }
}
