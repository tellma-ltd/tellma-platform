// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using Microsoft.IdentityModel.Tokens;
using System.Collections.Concurrent;
using System.Net;
using System.Text;
using System.Text.Json;

namespace Tellma.Core.AspNetCore.Tests.Infrastructure
{
    /// <summary>A request the identity server received over the back channel.</summary>
    /// <param name="Path">The endpoint's path.</param>
    /// <param name="Form">The form's fields.</param>
    internal sealed record IdentityRequest(string Path, IReadOnlyDictionary<string, string> Form);

    /// <summary>
    ///     The identity server's back channel, as the sign-in scheme reaches it: the pushed
    ///     authorization endpoint and the token endpoint, both recording what they receive. The code
    ///     grant answers tokens for <see cref="Subject" />; the refresh grant answers what
    ///     <see cref="Refresh" /> scripts, tokens by default.
    /// </summary>
    internal sealed class FakeIdentityServer : HttpMessageHandler
    {
        /// <summary>The pushed authorization endpoint.</summary>
        public const string ParEndpoint = TellmaTestHost.Issuer + "/connect/par";

        /// <summary>The token endpoint.</summary>
        public const string TokenEndpoint = TellmaTestHost.Issuer + "/connect/token";

        /// <summary>The authorization endpoint.</summary>
        public const string AuthorizationEndpoint = TellmaTestHost.Issuer + "/connect/authorize";

        /// <summary>The end-session endpoint.</summary>
        public const string EndSessionEndpoint = TellmaTestHost.Issuer + "/connect/endsession";

        /// <summary>The request URI every pushed request is answered with.</summary>
        public const string RequestUri = "urn:ietf:params:oauth:request_uri:test";

        /// <summary>The identity server's metadata, with the test signing key.</summary>
        /// <returns>A fresh copy.</returns>
        public static OpenIdConnectConfiguration Configuration()
        {
            OpenIdConnectConfiguration configuration = new()
            {
                Issuer = TellmaTestHost.Issuer,
                AuthorizationEndpoint = AuthorizationEndpoint,
                TokenEndpoint = TokenEndpoint,
                PushedAuthorizationRequestEndpoint = ParEndpoint,
                EndSessionEndpoint = EndSessionEndpoint,
            };
            configuration.SigningKeys.Add(TellmaTestHost.SigningKey);
            return configuration;
        }

        /// <summary>Every request received, in order.</summary>
        public ConcurrentQueue<IdentityRequest> Requests { get; } = new();

        /// <summary>The subject the code grant signs in.</summary>
        public string Subject { get; set; } = "sub-1";

        /// <summary>The identity server session the code grant signs into.</summary>
        public string SessionId { get; set; } = "sid-1";

        /// <summary>The access token's lifetime in each answer.</summary>
        public TimeSpan AccessTokenLifetime { get; set; } = TimeSpan.FromHours(1);

        /// <summary>Changes the claims of the identity token the code grant answers; null leaves them as they are.</summary>
        public Action<IDictionary<string, object>>? ShapeIdToken { get; set; }

        /// <summary>The refresh grant's answer for a request; null answers fresh tokens.</summary>
        public Func<IReadOnlyDictionary<string, string>, Task<HttpResponseMessage?>>? Refresh { get; set; }

        /// <summary>Whether the refresh grant never answers, until the request is cancelled.</summary>
        public bool StallRefresh { get; set; }

        /// <summary>How many refresh grants were received.</summary>
        public int Redemptions => Requests.Count(static request => request.Form.TryGetValue("grant_type", out string? grant) && grant == "refresh_token");

        /// <summary>An answer of the token endpoint with fresh tokens.</summary>
        /// <param name="subject">The subject.</param>
        /// <param name="nonce">The nonce the identity token carries, if any.</param>
        /// <param name="lifetime">The access token's lifetime.</param>
        /// <param name="acr">The authentication context class, if any.</param>
        /// <param name="sid">The identity server session the identity token names.</param>
        /// <param name="shape">Changes the identity token's claims; null leaves them as they are.</param>
        /// <param name="key">The identity token's signing key; the test key by default.</param>
        /// <param name="refreshToken">The refresh token answered; a fresh one by default.</param>
        /// <returns>The response.</returns>
        public static HttpResponseMessage Tokens(
            string subject,
            string? nonce,
            TimeSpan lifetime,
            string? acr = null,
            string sid = "sid-1",
            Action<IDictionary<string, object>>? shape = null,
            SecurityKey? key = null,
            string? refreshToken = null)
        {
            Dictionary<string, object> claims = new(StringComparer.Ordinal)
            {
                ["sub"] = subject,
                ["sid"] = sid,
                ["aud"] = TellmaTestHost.Slug,
                ["azp"] = TellmaTestHost.Slug,
                ["name"] = "Jane Doe",
                ["email"] = "jane@example.com",
                ["locale"] = "en",
            };
            if (nonce is not null)
            {
                claims["nonce"] = nonce;
            }

            if (acr is not null)
            {
                claims["acr"] = acr;
            }

            shape?.Invoke(claims);
            string idToken = TellmaTestHost.Token(claims, type: "JWT", key);
            string body = JsonSerializer.Serialize(new Dictionary<string, object>
            {
                ["access_token"] = "access-" + Guid.NewGuid().ToString("N"),
                ["refresh_token"] = refreshToken ?? ("refresh-" + Guid.NewGuid().ToString("N")),
                ["id_token"] = idToken,
                ["token_type"] = "Bearer",
                ["expires_in"] = (int)lifetime.TotalSeconds,
            });
            return Json(HttpStatusCode.OK, body);
        }

        /// <summary>A JSON answer.</summary>
        /// <param name="status">The status.</param>
        /// <param name="body">The body.</param>
        /// <returns>The response.</returns>
        public static HttpResponseMessage Json(HttpStatusCode status, string body)
        {
            return new HttpResponseMessage(status) { Content = new StringContent(body, Encoding.UTF8, "application/json") };
        }

        /// <summary>The nonce of the latest pushed authorization request.</summary>
        /// <returns>The nonce, or null.</returns>
        public string? LatestNonce()
        {
            return Requests.LastOrDefault(static request => request.Path == "/connect/par")?.Form.GetValueOrDefault("nonce");
        }

        /// <inheritdoc />
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            ArgumentNullException.ThrowIfNull(request);

            // --- The request, recorded with its form ---
            Dictionary<string, string> form = new(StringComparer.Ordinal);
            if (request.Content is not null)
            {
                string text = await request.Content.ReadAsStringAsync(cancellationToken);
                foreach (KeyValuePair<string, Microsoft.Extensions.Primitives.StringValues> pair in Microsoft.AspNetCore.WebUtilities.QueryHelpers.ParseQuery(text))
                {
                    form[pair.Key] = pair.Value.ToString();
                }
            }

            string path = request.RequestUri!.AbsolutePath;
            Requests.Enqueue(new IdentityRequest(path, form));

            // --- An identity server that does not answer, until the caller gives up ---
            if (StallRefresh && form.GetValueOrDefault("grant_type") == "refresh_token")
            {
                await Task.Delay(Timeout.Infinite, cancellationToken);
            }

            // --- The endpoints ---
            return path switch
            {
                "/connect/par" => Json(HttpStatusCode.Created, JsonSerializer.Serialize(new { request_uri = RequestUri, expires_in = 60 })),
                "/connect/token" when form.GetValueOrDefault("grant_type") == "authorization_code" => Tokens(Subject, LatestNonce(), AccessTokenLifetime, sid: SessionId, shape: ShapeIdToken),
                "/connect/token" when form.GetValueOrDefault("grant_type") == "refresh_token" =>
                    (Refresh is null ? null : await Refresh(form)) ?? Tokens(Subject, nonce: null, AccessTokenLifetime),
                _ => new HttpResponseMessage(HttpStatusCode.NotFound),
            };
        }

        /// <summary>A logout token from the test issuer.</summary>
        /// <param name="claims">The token's claims.</param>
        /// <param name="type">The token's type header.</param>
        /// <param name="issuer">The issuer.</param>
        /// <param name="key">The signing key; the test key by default.</param>
        /// <param name="issuedAt">When it was issued; now by default.</param>
        /// <param name="expires">When it expires; two minutes after it was issued by default.</param>
        /// <returns>The token.</returns>
        public static string LogoutToken(
            IDictionary<string, object> claims,
            string type = "logout+jwt",
            string issuer = TellmaTestHost.Issuer,
            SecurityKey? key = null,
            DateTime? issuedAt = null,
            DateTime? expires = null)
        {
            DateTime issued = issuedAt ?? DateTime.UtcNow;
            return new JsonWebTokenHandler { SetDefaultTimesOnTokenCreation = false }.CreateToken(new SecurityTokenDescriptor
            {
                Issuer = issuer,
                Claims = claims,
                TokenType = type,
                IssuedAt = issued,
                Expires = expires ?? issued.AddMinutes(2),
                SigningCredentials = new SigningCredentials(key ?? TellmaTestHost.SigningKey, SecurityAlgorithms.HmacSha256),
            });
        }
    }
}
