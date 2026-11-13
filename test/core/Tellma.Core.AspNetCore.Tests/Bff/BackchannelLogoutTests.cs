// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.Tokens;
using System.Net;
using System.Security.Cryptography;
using Tellma.Core.Abstractions.Tenancy;
using Tellma.Core.AspNetCore.Tests.Infrastructure;

namespace Tellma.Core.AspNetCore.Tests.Bff
{
    /// <summary>
    ///     The back-channel logout receiver: a valid logout token ends the sessions it names and tells
    ///     the listeners their handles; any other token is refused with 400 and ends nothing.
    /// </summary>
    public sealed class BackchannelLogoutTests
    {
        /// <summary>The events claim a logout token carries.</summary>
        private const string BackchannelEvent = "http://schemas.openid.net/event/backchannel-logout";

        [Fact]
        public async Task A_token_naming_a_session_ends_its_sessions_and_tells_the_listener_their_handles()
        {
            RecordingListener listener = new();
            await using TellmaTestHost host = await StartAsync(listener);
            TestSession first = host.Session(sessionId: "sid-1");
            TestSession second = host.Session(sessionId: "sid-1");
            TestSession other = host.Session(sessionId: "sid-2");

            using HttpResponseMessage response = await PostAsync(host, Token(Claims(sid: "sid-1")));

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.Equal([other.Key], host.Rows.Keys());
            (string subject, IReadOnlyList<string> handles) = Assert.Single(listener.Ended);
            Assert.Equal("sub-1", subject);
            Assert.Equal([first.Handle, second.Handle], [.. handles.Order(StringComparer.Ordinal)], StringComparer.Ordinal);
            Assert.DoesNotContain(first.Key, handles);
        }

        [Fact]
        public async Task A_token_naming_only_a_subject_ends_every_session_of_the_subject()
        {
            RecordingListener listener = new();
            await using TellmaTestHost host = await StartAsync(listener);
            _ = host.Session(subject: "sub-1", sessionId: "sid-1");
            _ = host.Session(subject: "sub-1", sessionId: "sid-2");
            TestSession other = host.Session(subject: "sub-2", sessionId: "sid-3");

            using HttpResponseMessage response = await PostAsync(host, Token(Claims(sid: null, sub: "sub-1")));

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.Equal([other.Key], host.Rows.Keys());
            Assert.Equal(2, listener.Handles.Count);
        }

        [Fact]
        public async Task A_replayed_token_answers_200_and_ends_nothing_more()
        {
            RecordingListener listener = new();
            await using TellmaTestHost host = await StartAsync(listener);
            _ = host.Session(sessionId: "sid-1");
            string token = Token(Claims(sid: "sid-1"));
            using HttpResponseMessage first = await PostAsync(host, token);

            using HttpResponseMessage replay = await PostAsync(host, token);

            Assert.Equal(HttpStatusCode.OK, first.StatusCode);
            Assert.Equal(HttpStatusCode.OK, replay.StatusCode);
            _ = Assert.Single(listener.Ended);
        }

        /// <summary>Logout tokens the receiver refuses, by what is wrong with them.</summary>
        /// <returns>Each case's name.</returns>
        public static TheoryData<string> RefusedTokens()
        {
            return ["bad issuer", "bad audience", "wrong type", "no events", "other event", "a nonce", "expired", "issued long ago", "issued in the future", "no sid or sub", "a foreign key", "a malformed subject", "not a token"];
        }

        [Theory]
        [MemberData(nameof(RefusedTokens))]
        public async Task A_token_that_fails_validation_is_refused_and_ends_nothing(string vector)
        {
            RecordingListener listener = new();
            await using TellmaTestHost host = await StartAsync(listener);
            TestSession session = host.Session(sessionId: "sid-1");
            DateTime now = DateTime.UtcNow;
            string token = vector switch
            {
                "bad issuer" => Token(Claims(sid: "sid-1"), issuer: "https://other.test"),
                "bad audience" => Token(Claims(sid: "sid-1", audience: "other-client")),
                "wrong type" => Token(Claims(sid: "sid-1"), type: "JWT"),
                "no events" => Token(Claims(sid: "sid-1", withEvents: false)),
                "other event" => Token(Claims(sid: "sid-1", events: new Dictionary<string, object> { ["http://schemas.openid.net/event/other"] = new Dictionary<string, object>() })),
                "a nonce" => Token(Claims(sid: "sid-1", nonce: "n-1")),
                "expired" => Token(Claims(sid: "sid-1"), issuedAt: now.AddMinutes(-30), expires: now.AddMinutes(-20)),
                "issued long ago" => Token(Claims(sid: "sid-1"), issuedAt: now.AddMinutes(-30), expires: now.AddMinutes(30)),
                "issued in the future" => Token(Claims(sid: "sid-1"), issuedAt: now.AddMinutes(30), expires: now.AddMinutes(40)),
                "no sid or sub" => Token(Claims(sid: null, sub: null)),
                "a foreign key" => Token(Claims(sid: "sid-1"), key: new SymmetricSecurityKey(RandomNumberGenerator.GetBytes(32)) { KeyId = "test-key" }),
                "a malformed subject" => Token(Claims(sid: null, sub: "bad subject\n")),
                _ => "not.a.token",
            };

            using HttpResponseMessage response = await PostAsync(host, token);

            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
            Assert.Equal([session.Key], host.Rows.Keys());
            Assert.Empty(listener.Ended);
        }

        [Fact]
        public async Task A_request_without_a_token_is_refused()
        {
            await using TellmaTestHost host = await StartAsync(new RecordingListener());

            using FormUrlEncodedContent content = new([]);
            using HttpResponseMessage response = await host.Client.PostAsync(new Uri("/bff/backchannel-logout", UriKind.Relative), content, TestContext.Current.CancellationToken);

            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        }

        /// <summary>Starts a host with a recording listener.</summary>
        /// <param name="listener">The listener.</param>
        /// <returns>The host.</returns>
        private static Task<TellmaTestHost> StartAsync(RecordingListener listener)
        {
            return TellmaTestHost.StartAsync(options =>
                options.ConfigureServices = services => services.AddSingleton<ISessionTerminationListener>(listener));
        }

        /// <summary>Posts a logout token as the identity server does: a form, from no browser.</summary>
        /// <param name="host">The host.</param>
        /// <param name="token">The token.</param>
        /// <returns>The response.</returns>
        private static async Task<HttpResponseMessage> PostAsync(TellmaTestHost host, string token)
        {
            using FormUrlEncodedContent content = new([new KeyValuePair<string, string>("logout_token", token)]);
            return await host.Client.PostAsync(new Uri("/bff/backchannel-logout", UriKind.Relative), content, TestContext.Current.CancellationToken);
        }

        /// <summary>A logout token's claims.</summary>
        /// <param name="sid">The session id, if any.</param>
        /// <param name="sub">The subject, if any.</param>
        /// <param name="audience">The audience.</param>
        /// <param name="withEvents">Whether the token carries an events claim.</param>
        /// <param name="events">The events claim; the back-channel logout event by default.</param>
        /// <param name="nonce">A nonce, which a logout token must not carry.</param>
        /// <returns>The claims.</returns>
        private static Dictionary<string, object> Claims(
            string? sid, string? sub = "sub-1", string audience = TellmaTestHost.Slug, bool withEvents = true, object? events = null, string? nonce = null)
        {
            Dictionary<string, object> claims = new(StringComparer.Ordinal)
            {
                ["aud"] = audience,
                ["jti"] = Guid.NewGuid().ToString("N"),
            };
            if (withEvents)
            {
                claims["events"] = events ?? new Dictionary<string, object> { [BackchannelEvent] = new Dictionary<string, object>() };
            }

            if (sid is not null)
            {
                claims["sid"] = sid;
            }

            if (sub is not null)
            {
                claims["sub"] = sub;
            }

            if (nonce is not null)
            {
                claims["nonce"] = nonce;
            }

            return claims;
        }

        /// <summary>A signed logout token.</summary>
        /// <param name="claims">The claims.</param>
        /// <param name="type">The type header.</param>
        /// <param name="issuer">The issuer.</param>
        /// <param name="key">The signing key.</param>
        /// <param name="issuedAt">When it was issued.</param>
        /// <param name="expires">When it expires.</param>
        /// <returns>The token.</returns>
        private static string Token(
            Dictionary<string, object> claims,
            string type = "logout+jwt",
            string issuer = TellmaTestHost.Issuer,
            SecurityKey? key = null,
            DateTime? issuedAt = null,
            DateTime? expires = null)
        {
            return FakeIdentityServer.LogoutToken(claims, type, issuer, key, issuedAt, expires);
        }
    }
}
