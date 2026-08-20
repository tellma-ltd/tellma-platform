// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;
using OpenIddict.Server;
using Tellma.Identity.Options;

namespace Tellma.Identity.Services.BackchannelLogout
{
    /// <summary>
    ///     Mints OIDC Back-Channel Logout 1.0 <c>logout_token</c>s, signed with the server's
    ///     active signing credential (the same asymmetric-first selection OpenIddict applies to
    ///     its own tokens), so relying parties validate them against the published JWKS — and
    ///     overlap rotation keeps working, since old keys stay published.
    /// </summary>
    /// <param name="serverOptions">The OpenIddict server options (signing credentials).</param>
    /// <param name="engineOptions">The engine options (issuer).</param>
    /// <param name="timeProvider">The clock.</param>
    public sealed class LogoutTokenFactory(
        IOptionsMonitor<OpenIddictServerOptions> serverOptions,
        IOptions<TellmaIdentityOptions> engineOptions,
        TimeProvider timeProvider)
    {
        /// <summary>The OIDC back-channel logout event URI.</summary>
        public const string LogoutEvent = "http://schemas.openid.net/event/backchannel-logout";

        /// <summary>Creates a signed logout token for one relying party.</summary>
        /// <param name="clientId">The relying party (the token's audience).</param>
        /// <param name="subject">The user whose session ended.</param>
        /// <param name="sid">The terminated session's identifier.</param>
        /// <returns>The compact-serialized JWT.</returns>
        public string Create(string clientId, string subject, string sid)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(clientId);
            ArgumentException.ThrowIfNullOrWhiteSpace(subject);
            ArgumentException.ThrowIfNullOrWhiteSpace(sid);

            // The same selection rule OpenIddict uses for its identity tokens: the credential
            // list is pre-sorted with the newest-expiring certificate first.
            SigningCredentials credentials = serverOptions.CurrentValue.SigningCredentials
                .First(static c => c.Key is AsymmetricSecurityKey);

            DateTime now = timeProvider.GetUtcNow().UtcDateTime;
            JsonWebTokenHandler handler = new() { SetDefaultTimesOnTokenCreation = false };

            return handler.CreateToken(new SecurityTokenDescriptor
            {
                Issuer = engineOptions.Value.Issuer!.AbsoluteUri,
                Audience = clientId,
                IssuedAt = now,
                Expires = now.AddMinutes(2),
                SigningCredentials = credentials,
                TokenType = "logout+jwt",
                Claims = new Dictionary<string, object>
                {
                    ["sub"] = subject,
                    ["sid"] = sid,
                    ["jti"] = Guid.NewGuid().ToString("N"),
                    ["events"] = new Dictionary<string, object> { [LogoutEvent] = new Dictionary<string, object>() },
                },
            });
        }
    }
}
