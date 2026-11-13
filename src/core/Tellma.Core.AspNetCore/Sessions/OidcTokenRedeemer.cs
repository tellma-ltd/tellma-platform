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
using System.Globalization;
using System.Net;
using System.Security.Claims;
using System.Text.Json;

namespace Tellma.Core.AspNetCore.Sessions
{
    /// <summary>
    ///     Redeems refresh tokens at the authority's token endpoint as the OpenID Connect scheme's own
    ///     client: the endpoint and keys from its configuration manager, its client credentials, its
    ///     back-channel. A refreshed identity token is validated as strictly as the sign-in's, except
    ///     that one signed by a key the metadata does not list, even after reading it again, leaves the
    ///     redeemed tokens kept with their claims unverified. A failure says whether the refresh token
    ///     may have been spent, so the caller knows whether a retry must stay inside the authority's
    ///     reuse leeway.
    /// </summary>
    /// <param name="oidc">The OpenID Connect scheme's options.</param>
    /// <param name="time">The clock the redemption's deadline runs on.</param>
    internal sealed class OidcTokenRedeemer(IOptionsMonitor<OpenIdConnectOptions> oidc, TimeProvider time) : ITokenRedeemer
    {
        /// <summary>Why a refreshed identity token signed by a key the metadata does not list leaves its claims unverified.</summary>
        private const string UnknownKey = "the refreshed identity token is signed by a key the identity server's metadata does not list";

        /// <summary>
        ///     The longest one redemption may take, from reading the authority's metadata to its answer:
        ///     five seconds. A request may wait on it, so an authority that does not answer must not hold
        ///     that request for the back channel's own, much longer, timeout. A redemption it cuts short
        ///     after the refresh token was sent is retried, and the retries must reach the authority within
        ///     its reuse leeway, so this deadline is one term of the refresher's retry window.
        /// </summary>
        internal static readonly TimeSpan Timeout = TimeSpan.FromSeconds(5);

        /// <inheritdoc />
        public async Task<Redemption> RedeemAsync(string refreshToken, CancellationToken cancellationToken)
        {
            ArgumentException.ThrowIfNullOrEmpty(refreshToken);
            OpenIdConnectOptions options = oidc.Get(TellmaAuthentication.OidcScheme);

            // --- The redemption's own deadline, beside the caller's cancellation ---
            using CancellationTokenSource deadline = new(Timeout, time);
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, deadline.Token);
            CancellationToken within = linked.Token;

            // --- The authority's metadata: until it is read, the refresh token has not left this host ---
            OpenIdConnectConfiguration configuration;
            try
            {
                configuration = options.Configuration ?? await ReadConfigurationAsync(options, within).ConfigureAwait(false);
            }
            catch (OperationCanceledException exception) when (!cancellationToken.IsCancellationRequested)
            {
                return Redemption.Unreachable("the identity server's metadata was not read in time", exception);
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
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

            // Once the request may have left, a failure may have spent the refresh token: only a
            // connection never made leaves it as good as it was.
            string body;
            HttpStatusCode status;
            try
            {
                using HttpRequestMessage request = new(HttpMethod.Post, configuration.TokenEndpoint) { Content = new FormUrlEncodedContent(form) };
                using HttpResponseMessage response = await options.Backchannel.SendAsync(request, within).ConfigureAwait(false);
                status = response.StatusCode;
                body = await response.Content.ReadAsStringAsync(within).ConfigureAwait(false);
            }
            catch (OperationCanceledException exception) when (!cancellationToken.IsCancellationRequested)
            {
                return Redemption.Indeterminate("the identity server did not answer in time", exception);
            }
            catch (HttpRequestException exception) when (exception.HttpRequestError is HttpRequestError.NameResolutionError or HttpRequestError.ConnectionError or HttpRequestError.SecureConnectionError)
            {
                return Redemption.Unreachable("the identity server's token endpoint could not be reached", exception);
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                return Redemption.Indeterminate("the token endpoint's answer was cut off", exception);
            }

            // --- A refusal is the authority's word on the session; a failure on its side is not ---
            int code = (int)status;
            return code is >= 400 and < 500
                ? Redemption.Refused(ErrorOf(body) ?? string.Create(CultureInfo.InvariantCulture, $"status {code}"))
                : code is < 200 or >= 300
                    ? Redemption.Unreachable(string.Create(CultureInfo.InvariantCulture, $"the token endpoint answered status {code}"))
                    : await ReadTokensAsync(body, options, configuration, within).ConfigureAwait(false);
        }

        /// <summary>Reads the authority's metadata, waiting for it no longer than the redemption's deadline.</summary>
        /// <param name="options">The scheme's options.</param>
        /// <param name="cancellationToken">The redemption's deadline.</param>
        /// <returns>The metadata.</returns>
        /// <exception cref="InvalidOperationException">The scheme has no configuration manager.</exception>
        /// <exception cref="OperationCanceledException">The deadline passed first.</exception>
        private static Task<OpenIdConnectConfiguration> ReadConfigurationAsync(OpenIdConnectOptions options, CancellationToken cancellationToken)
        {
            IConfigurationManager<OpenIdConnectConfiguration> manager = options.ConfigurationManager
                ?? throw new InvalidOperationException("The OpenID Connect scheme has no configuration manager.");

            // The manager's first read does not heed a token, so only the wait is bounded: the read
            // finishes on its own and fills the manager's cache, its failure observed here.
            Task<OpenIdConnectConfiguration> read = manager.GetConfigurationAsync(CancellationToken.None);
            _ = read.ContinueWith(static task => _ = task.Exception, CancellationToken.None, TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
            return read.WaitAsync(cancellationToken);
        }

        /// <summary>Reads a successful token response, validating the identity token it carries.</summary>
        /// <param name="body">The response body.</param>
        /// <param name="options">The scheme's options.</param>
        /// <param name="configuration">The authority's metadata.</param>
        /// <param name="cancellationToken">The redemption's deadline.</param>
        /// <returns>The redemption.</returns>
        private static async Task<Redemption> ReadTokensAsync(string body, OpenIdConnectOptions options, OpenIdConnectConfiguration configuration, CancellationToken cancellationToken)
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
                // A body the authority's success status does not live up to is its fault, not the
                // session's; it answered, so it may have spent the refresh token.
                return Redemption.Indeterminate("the token endpoint answered a malformed body", exception);
            }

            if (accessToken is null || expiresIn <= 0)
            {
                return Redemption.Indeterminate("the token endpoint answered without an access token or its lifetime");
            }

            // --- The refreshed identity token: signed by the authority, issued to this client, current ---
            ClaimsIdentity? claims = null;
            string? unverified = null;
            if (idToken is not null)
            {
                TokenValidationResult result = await ValidateIdTokenAsync(idToken, options, configuration).ConfigureAwait(false);
                if (!result.IsValid && result.Exception is SecurityTokenSignatureKeyNotFoundException)
                {
                    (result, unverified) = await RevalidateAsync(idToken, options, cancellationToken).ConfigureAwait(false);
                }

                if (unverified is null)
                {
                    if (!result.IsValid)
                    {
                        return Redemption.Refused("the refreshed identity token is not valid");
                    }

                    claims = result.ClaimsIdentity;
                }
            }

            return Redemption.Redeemed(new RedeemedTokens(accessToken, refreshToken, idToken, claims, tokenType, TimeSpan.FromSeconds(expiresIn), unverified));
        }

        /// <summary>
        ///     Validates a refreshed identity token again after reading the authority's keys again, once:
        ///     the authority may have rolled its keys since they were read. Whatever comes of it the
        ///     tokens are kept, since the refresh token they replace may be spent, so keys still unknown
        ///     or unreadable leave the claims unverified rather than the redemption failed.
        /// </summary>
        /// <param name="idToken">The identity token.</param>
        /// <param name="options">The scheme's options.</param>
        /// <param name="cancellationToken">The redemption's deadline.</param>
        /// <returns>The validation's result, and why the claims are unverified when they are.</returns>
        private static async Task<(TokenValidationResult Result, string? Unverified)> RevalidateAsync(string idToken, OpenIdConnectOptions options, CancellationToken cancellationToken)
        {
            if (options.ConfigurationManager is null)
            {
                return (new TokenValidationResult { IsValid = false }, UnknownKey);
            }

            OpenIdConnectConfiguration configuration;
            try
            {
                options.ConfigurationManager.RequestRefresh();
                configuration = await ReadConfigurationAsync(options, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception)
            {
                return (new TokenValidationResult { IsValid = false }, "the identity server's metadata could not be read again");
            }

            TokenValidationResult result = await ValidateIdTokenAsync(idToken, options, configuration).ConfigureAwait(false);
            return !result.IsValid && result.Exception is SecurityTokenSignatureKeyNotFoundException ? (result, UnknownKey) : (result, null);
        }

        /// <summary>Validates a refreshed identity token: signed by the authority, issued to this client, current.</summary>
        /// <param name="idToken">The identity token.</param>
        /// <param name="options">The scheme's options.</param>
        /// <param name="configuration">The authority's metadata.</param>
        /// <returns>The validation's result.</returns>
        private static Task<TokenValidationResult> ValidateIdTokenAsync(string idToken, OpenIdConnectOptions options, OpenIdConnectConfiguration configuration)
        {
            TokenValidationParameters parameters = options.TokenValidationParameters.Clone();
            parameters.ValidIssuer = configuration.Issuer;
            parameters.ValidAudience = options.ClientId;
            parameters.IssuerSigningKeys = configuration.SigningKeys;
            parameters.ValidateIssuer = true;
            parameters.ValidateAudience = true;
            parameters.ValidateLifetime = true;
            parameters.ValidateIssuerSigningKey = true;
            return new JsonWebTokenHandler { MapInboundClaims = false }.ValidateTokenAsync(idToken, parameters);
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
