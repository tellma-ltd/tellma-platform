// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using Microsoft.IdentityModel.Tokens;
using System.Globalization;
using System.Net;
using System.Security.Claims;
using System.Text.Json;

namespace Tellma.Core.AspNetCore.Sessions
{
    /// <summary>
    ///     Redeems refresh tokens at the authority's token endpoint as the OpenID Connect scheme's own
    ///     client: the endpoint and keys from its configuration manager, its client credentials, its
    ///     back-channel. A refreshed identity token is validated as strictly as the sign-in's.
    /// </summary>
    /// <param name="oidc">The OpenID Connect scheme's options.</param>
    internal sealed class OidcTokenRedeemer(IOptionsMonitor<OpenIdConnectOptions> oidc) : ITokenRedeemer
    {
        /// <inheritdoc />
        public async Task<Redemption> RedeemAsync(string refreshToken, CancellationToken cancellationToken)
        {
            ArgumentException.ThrowIfNullOrEmpty(refreshToken);
            OpenIdConnectOptions options = oidc.Get(TellmaAuthentication.OidcScheme);

            // --- The authority's metadata: its token endpoint and signing keys ---
            OpenIdConnectConfiguration configuration;
            try
            {
                configuration = options.Configuration
                    ?? await (options.ConfigurationManager ?? throw new InvalidOperationException("The OpenID Connect scheme has no configuration manager."))
                        .GetConfigurationAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (Exception exception) when (exception is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
            {
                return Redemption.Unreachable("the identity server's metadata could not be read", exception);
            }

            if (string.IsNullOrEmpty(configuration.TokenEndpoint))
            {
                return Redemption.Unreachable("the identity server's metadata names no token endpoint");
            }

            // --- The redemption, authenticated as the scheme's confidential client ---
            Dictionary<string, string> form = new(StringComparer.Ordinal)
            {
                [OpenIdConnectParameterNames.GrantType] = OpenIdConnectGrantTypes.RefreshToken,
                [OpenIdConnectParameterNames.RefreshToken] = refreshToken,
                [OpenIdConnectParameterNames.ClientId] = options.ClientId ?? string.Empty,
            };
            if (!string.IsNullOrEmpty(options.ClientSecret))
            {
                form[OpenIdConnectParameterNames.ClientSecret] = options.ClientSecret;
            }

            string body;
            HttpStatusCode status;
            try
            {
                using HttpRequestMessage request = new(HttpMethod.Post, configuration.TokenEndpoint) { Content = new FormUrlEncodedContent(form) };
                using HttpResponseMessage response = await options.Backchannel.SendAsync(request, cancellationToken).ConfigureAwait(false);
                status = response.StatusCode;
                body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (Exception exception) when (exception is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
            {
                return Redemption.Unreachable("the identity server's token endpoint could not be reached", exception);
            }

            // --- A refusal is the authority's word on the session; a failure on its side is not ---
            int code = (int)status;
            return code is >= 400 and < 500
                ? Redemption.Refused(ErrorOf(body) ?? string.Create(CultureInfo.InvariantCulture, $"status {code}"))
                : code is < 200 or >= 300
                    ? Redemption.Unreachable(string.Create(CultureInfo.InvariantCulture, $"the token endpoint answered status {code}"))
                    : await ReadTokensAsync(body, options, configuration).ConfigureAwait(false);
        }

        /// <summary>Reads a successful token response, validating the identity token it carries.</summary>
        /// <param name="body">The response body.</param>
        /// <param name="options">The scheme's options.</param>
        /// <param name="configuration">The authority's metadata.</param>
        /// <returns>The redemption.</returns>
        private static async Task<Redemption> ReadTokensAsync(string body, OpenIdConnectOptions options, OpenIdConnectConfiguration configuration)
        {
            string? accessToken;
            string? refreshToken;
            string? idToken;
            string? tokenType;
            long expiresIn;
            try
            {
                using var document = JsonDocument.Parse(body);
                JsonElement root = document.RootElement;
                accessToken = Text(root, OpenIdConnectParameterNames.AccessToken);
                refreshToken = Text(root, OpenIdConnectParameterNames.RefreshToken);
                idToken = Text(root, OpenIdConnectParameterNames.IdToken);
                tokenType = Text(root, OpenIdConnectParameterNames.TokenType);
                expiresIn = root.TryGetProperty(OpenIdConnectParameterNames.ExpiresIn, out JsonElement value) && value.TryGetInt64(out long seconds) ? seconds : 0;
            }
            catch (Exception exception) when (exception is JsonException or InvalidOperationException)
            {
                // A body the authority's success status does not live up to is its fault, not the session's.
                return Redemption.Unreachable("the token endpoint answered a malformed body", exception);
            }

            if (accessToken is null || expiresIn <= 0)
            {
                return Redemption.Unreachable("the token endpoint answered without an access token or its lifetime");
            }

            // --- The refreshed identity token: signed by the authority, issued to this client, current ---
            ClaimsIdentity? claims = null;
            if (idToken is not null)
            {
                TokenValidationParameters parameters = options.TokenValidationParameters.Clone();
                parameters.ValidIssuer = configuration.Issuer;
                parameters.ValidAudience = options.ClientId;
                parameters.IssuerSigningKeys = configuration.SigningKeys;
                parameters.ValidateIssuer = true;
                parameters.ValidateAudience = true;
                parameters.ValidateLifetime = true;
                parameters.ValidateIssuerSigningKey = true;
                TokenValidationResult result = await new JsonWebTokenHandler { MapInboundClaims = false }
                    .ValidateTokenAsync(idToken, parameters).ConfigureAwait(false);
                if (!result.IsValid)
                {
                    return Redemption.Refused("the refreshed identity token is not valid");
                }

                claims = result.ClaimsIdentity;
            }

            return Redemption.Redeemed(new RedeemedTokens(accessToken, refreshToken, idToken, claims, tokenType, TimeSpan.FromSeconds(expiresIn)));
        }

        /// <summary>A string member of a JSON object.</summary>
        /// <param name="root">The object.</param>
        /// <param name="name">The member.</param>
        /// <returns>Its value, or null when absent, empty or not a string.</returns>
        private static string? Text(JsonElement root, string name)
        {
            return root.TryGetProperty(name, out JsonElement value) && value.ValueKind == JsonValueKind.String && value.GetString() is { Length: > 0 } text
                ? text
                : null;
        }

        /// <summary>The OAuth error code of an error response.</summary>
        /// <param name="body">The response body.</param>
        /// <returns>The code, or null when the body names none.</returns>
        private static string? ErrorOf(string body)
        {
            try
            {
                using var document = JsonDocument.Parse(body);
                return document.RootElement.ValueKind == JsonValueKind.Object
                    && Text(document.RootElement, OpenIdConnectParameterNames.Error) is string error
                    && error.Length <= 64
                    && error.All(static c => c is >= ' ' and <= '~')
                    ? error
                    : null;
            }
            catch (JsonException)
            {
                return null;
            }
        }
    }
}
