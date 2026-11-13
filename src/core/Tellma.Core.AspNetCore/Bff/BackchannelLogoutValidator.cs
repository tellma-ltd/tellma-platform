// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Protocols;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using Microsoft.IdentityModel.Tokens;
using System.Buffers.Text;
using System.Text.Json;
using Tellma.Core.Tenancy;

namespace Tellma.Core.AspNetCore.Bff
{
    /// <summary>What a back-channel logout token said, or why it was refused.</summary>
    /// <param name="Status">Whether it was valid, refused or could not be checked.</param>
    /// <param name="Sid">The identity server session it ends; null when it names only a subject.</param>
    /// <param name="Subject">The subject it ends, if it names one.</param>
    /// <param name="Reason">Why it was refused or could not be checked, for the log; never the token.</param>
    internal sealed record LogoutTokenResult(LogoutTokenStatus Status, string? Sid, string? Subject, string? Reason)
    {
        /// <summary>A refused token.</summary>
        /// <param name="reason">Why.</param>
        /// <returns>The result.</returns>
        public static LogoutTokenResult Invalid(string reason)
        {
            return new LogoutTokenResult(LogoutTokenStatus.Invalid, null, null, reason);
        }
    }

    /// <summary>Whether a back-channel logout token was valid.</summary>
    internal enum LogoutTokenStatus
    {
        /// <summary>Valid: its sessions end.</summary>
        Valid,

        /// <summary>Refused: it is not a logout token of this distribution's identity server.</summary>
        Invalid,

        /// <summary>Not checked: the identity server's keys could not be read.</summary>
        Unavailable,
    }

    /// <summary>
    ///     Validates back-channel logout tokens with the sign-in scheme's own configuration manager: the
    ///     signature against the identity server's keys, the issuer, the audience (the distribution's
    ///     client), the <c>logout+jwt</c> type, an issue time within five minutes, the back-channel logout
    ///     event, no nonce, and a session or a subject to end.
    /// </summary>
    /// <param name="oidc">The sign-in scheme's options.</param>
    /// <param name="time">The clock.</param>
    internal sealed class BackchannelLogoutValidator(IOptionsMonitor<OpenIdConnectOptions> oidc, TimeProvider time)
    {
        /// <summary>The event a logout token must carry.</summary>
        public const string LogoutEvent = "http://schemas.openid.net/event/backchannel-logout";

        /// <summary>The type a logout token's header must state.</summary>
        public const string LogoutTokenType = "logout+jwt";

        /// <summary>How far a logout token's issue time may be from now, either way.</summary>
        public static readonly TimeSpan IssuedAtTolerance = TimeSpan.FromMinutes(5);

        /// <summary>The longest session id the catalog holds.</summary>
        private const int MaxSidLength = 128;

        /// <summary>Validates a logout token.</summary>
        /// <param name="token">The token, as posted.</param>
        /// <param name="cancellationToken">Cancels the validation.</param>
        /// <returns>What it said, or why it was refused.</returns>
        public async Task<LogoutTokenResult> ValidateAsync(string token, CancellationToken cancellationToken)
        {
            ArgumentNullException.ThrowIfNull(token);
            OpenIdConnectOptions options = oidc.Get(TellmaAuthentication.OidcScheme);

            // --- The signature, issuer, audience and type, against the identity server's metadata ---
            TokenValidationResult result;
            try
            {
                OpenIdConnectConfiguration configuration = await ConfigurationAsync(options, refresh: false, cancellationToken).ConfigureAwait(false);
                result = await ValidateSignatureAsync(token, options, configuration).ConfigureAwait(false);
                if (!result.IsValid && result.Exception is SecurityTokenSignatureKeyNotFoundException && options.ConfigurationManager is not null)
                {
                    // The identity server may have rolled its keys since they were read: read them again, once.
                    configuration = await ConfigurationAsync(options, refresh: true, cancellationToken).ConfigureAwait(false);
                    result = await ValidateSignatureAsync(token, options, configuration).ConfigureAwait(false);
                }
            }
            catch (Exception exception) when (exception is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
            {
                return new LogoutTokenResult(LogoutTokenStatus.Unavailable, null, null, "the identity server's metadata could not be read");
            }

            if (!result.IsValid || result.SecurityToken is not JsonWebToken jwt)
            {
                return LogoutTokenResult.Invalid(result.Exception?.GetType().Name ?? "the token is not a signed JWT");
            }

            // --- The logout token's own rules, over its payload ---
            JsonDocument payload;
            try
            {
                payload = JsonDocument.Parse(Base64Url.DecodeFromChars(jwt.EncodedPayload));
            }
            catch (Exception exception) when (exception is JsonException or FormatException)
            {
                return LogoutTokenResult.Invalid("the payload is not JSON");
            }

            using (payload)
            {
                return Inspect(payload.RootElement);
            }
        }

        /// <summary>Applies the logout token's own rules to its payload.</summary>
        /// <param name="payload">The payload.</param>
        /// <returns>What it said, or why it was refused.</returns>
        private LogoutTokenResult Inspect(JsonElement payload)
        {
            if (payload.ValueKind != JsonValueKind.Object)
            {
                return LogoutTokenResult.Invalid("the payload is not an object");
            }

            // --- Issued within five minutes of now, either way ---
            if (!payload.TryGetProperty(JwtRegisteredClaimNames.Iat, out JsonElement iat) || !iat.TryGetInt64(out long issuedAt))
            {
                return LogoutTokenResult.Invalid("the token has no issue time");
            }

            long now = time.GetUtcNow().ToUnixTimeSeconds();
            if (Math.Abs(now - issuedAt) > (long)IssuedAtTolerance.TotalSeconds)
            {
                return LogoutTokenResult.Invalid("the token's issue time is more than five minutes from now");
            }

            // --- The back-channel logout event, and no nonce, which only an identity token carries ---
            if (!payload.TryGetProperty("events", out JsonElement events) || events.ValueKind != JsonValueKind.Object || !events.TryGetProperty(LogoutEvent, out _))
            {
                return LogoutTokenResult.Invalid("the token carries no back-channel logout event");
            }

            if (payload.TryGetProperty(JwtRegisteredClaimNames.Nonce, out _))
            {
                return LogoutTokenResult.Invalid("the token carries a nonce");
            }

            // --- A session or a subject to end, each in a shape the catalog holds ---
            string? sid = Text(payload, JwtRegisteredClaimNames.Sid);
            string? subject = Text(payload, JwtRegisteredClaimNames.Sub);
            if (sid is null && subject is null)
            {
                return LogoutTokenResult.Invalid("the token names neither a session nor a subject");
            }

            if (sid is { Length: > MaxSidLength })
            {
                return LogoutTokenResult.Invalid("the token's session id is too long");
            }

            if (subject is not null)
            {
                try
                {
                    TenantMembershipDirectory.ValidateSubject(subject, nameof(subject));
                }
                catch (ArgumentException)
                {
                    return LogoutTokenResult.Invalid("the token's subject is malformed");
                }
            }

            return new LogoutTokenResult(LogoutTokenStatus.Valid, sid, subject, null);
        }

        /// <summary>Validates the token's signature, issuer, audience, type and, when it states one, expiry.</summary>
        /// <param name="token">The token.</param>
        /// <param name="options">The sign-in scheme's options.</param>
        /// <param name="configuration">The identity server's metadata.</param>
        /// <returns>The validation's result.</returns>
        private static Task<TokenValidationResult> ValidateSignatureAsync(string token, OpenIdConnectOptions options, OpenIdConnectConfiguration configuration)
        {
            TokenValidationParameters parameters = new()
            {
                ValidIssuer = configuration.Issuer,
                ValidAudience = options.ClientId,
                IssuerSigningKeys = configuration.SigningKeys,
                ValidTypes = [LogoutTokenType],
                ValidateIssuer = true,
                ValidateAudience = true,
                ValidateIssuerSigningKey = true,
                RequireSignedTokens = true,
                ValidateLifetime = true,
                RequireExpirationTime = false,
                ClockSkew = IssuedAtTolerance,
            };
            return new JsonWebTokenHandler { MapInboundClaims = false }.ValidateTokenAsync(token, parameters);
        }

        /// <summary>The identity server's metadata, as the sign-in scheme reads it.</summary>
        /// <param name="options">The sign-in scheme's options.</param>
        /// <param name="refresh">Whether to read it again rather than use what is cached.</param>
        /// <param name="cancellationToken">Cancels the read.</param>
        /// <returns>The metadata.</returns>
        private static async Task<OpenIdConnectConfiguration> ConfigurationAsync(OpenIdConnectOptions options, bool refresh, CancellationToken cancellationToken)
        {
            if (options.Configuration is { } fixedConfiguration)
            {
                return fixedConfiguration;
            }

            IConfigurationManager<OpenIdConnectConfiguration> manager = options.ConfigurationManager
                ?? throw new InvalidOperationException("The sign-in scheme has no configuration manager.");
            if (refresh)
            {
                manager.RequestRefresh();
            }

            return await manager.GetConfigurationAsync(cancellationToken).ConfigureAwait(false);
        }

        /// <summary>A non-empty string member of a JSON object.</summary>
        /// <param name="payload">The object.</param>
        /// <param name="name">The member.</param>
        /// <returns>Its value, or null when absent, empty or not a string.</returns>
        private static string? Text(JsonElement payload, string name)
        {
            return payload.TryGetProperty(name, out JsonElement value) && value.ValueKind == JsonValueKind.String && value.GetString() is { Length: > 0 } text
                ? text
                : null;
        }
    }
}
