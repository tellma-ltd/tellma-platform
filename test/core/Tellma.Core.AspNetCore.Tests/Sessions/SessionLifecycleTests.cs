// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Net.Http.Headers;
using System.Net;
using System.Text.Json;
using Tellma.Core.Abstractions.Tenancy;
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
            return ["activity header", "client telemetry"];
        }

        [Theory]
        [MemberData(nameof(BackgroundRequests))]
        public async Task A_background_request_renews_neither_cookie_nor_row(string kind)
        {
            await using TellmaTestHost host = await TellmaTestHost.StartAsync();
            TestSession session = host.Session(accessTokenLifetime: TimeSpan.FromDays(30));
            DateTimeOffset before = host.Rows.ExpiresAt(session.Key)!.Value;
            host.Time.Advance(TimeSpan.FromDays(4));

            using HttpRequestMessage request = kind == "activity header"
                ? TellmaTestHost.SpaRequest(HttpMethod.Get, "/bff/user", session.Cookie)
                : TellmaTestHost.SpaRequest(HttpMethod.Post, "/api/client-telemetry", session.Cookie, "{}");
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
