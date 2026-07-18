// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Microsoft.IdentityModel.Tokens;
using OpenIddict.Abstractions;
using System.Collections.Immutable;
using System.Security.Claims;
using System.Text.Json;
using Tellma.Identity.Data;
using Tellma.Identity.Services.Provisioning;
using Tellma.Identity.Services.Sessions;
using static OpenIddict.Abstractions.OpenIddictConstants;

namespace Tellma.Identity.Services.AuthenticationPolicy
{
    /// <summary>The outcome of building a protocol principal.</summary>
    /// <param name="Identity">The identity to sign in, when successful.</param>
    /// <param name="Error">The protocol error, when not.</param>
    /// <param name="ErrorDescription">The human-readable error description.</param>
    public sealed record PrincipalResult(ClaimsIdentity? Identity, string? Error, string? ErrorDescription);

    /// <summary>
    ///     The grant parameters the principal builder needs, decoupled from where they came from
    ///     (an authorization request or a stored device-code principal).
    /// </summary>
    /// <param name="ClientId">The requesting client.</param>
    /// <param name="Scopes">The granted scopes.</param>
    /// <param name="Resources">The explicitly requested resources, when any.</param>
    /// <param name="AllowedMethodsRaw">The tenant's method allow-list snapshot, when carried.</param>
    public sealed record GrantRequest(
        string ClientId,
        IReadOnlyCollection<string> Scopes,
        IReadOnlyCollection<string> Resources,
        string? AllowedMethodsRaw);

    /// <summary>
    ///     Builds the <see cref="ClaimsIdentity" /> every interactive grant signs in with — the
    ///     single place claims, assurance, audiences, the backing authorization, and session
    ///     registration are assembled, shared by the authorize and device-verification paths so
    ///     they cannot drift.
    /// </summary>
    /// <param name="applicationManager">The OpenIddict application manager.</param>
    /// <param name="authorizationManager">The OpenIddict authorization manager.</param>
    /// <param name="scopeManager">The OpenIddict scope manager.</param>
    /// <param name="policyService">Assurance derivation.</param>
    /// <param name="sessionRegistry">The sid registry.</param>
    public sealed class TellmaPrincipalFactory(
        IOpenIddictApplicationManager applicationManager,
        IOpenIddictAuthorizationManager authorizationManager,
        IOpenIddictScopeManager scopeManager,
        IAuthenticationPolicyService policyService,
        ISessionRegistry sessionRegistry)
    {
        /// <summary>Builds the principal for an interactive authorization request.</summary>
        /// <param name="user">The authenticated user.</param>
        /// <param name="cookiePrincipal">The SSO-cookie principal carrying the session evidence.</param>
        /// <param name="request">The (PAR-resolved) protocol request.</param>
        /// <param name="application">The requesting client's application object.</param>
        /// <param name="cancellationToken">Aborts the operation.</param>
        /// <returns>The identity, or a protocol error.</returns>
        public Task<PrincipalResult> CreateAsync(
            TellmaIdentityUser user,
            ClaimsPrincipal cookiePrincipal,
            OpenIddictRequest request,
            object application,
            CancellationToken cancellationToken)
        {
            ArgumentNullException.ThrowIfNull(request);

            return CreateAsync(
                user,
                cookiePrincipal,
                new GrantRequest(
                    request.ClientId!,
                    [.. request.GetScopes()],
                    [.. request.GetResources()],
                    (string?)request[TellmaParameters.AllowedMethods]),
                application,
                cancellationToken);
        }

        /// <summary>Builds the principal for a grant whose parameters come from a stored principal.</summary>
        /// <param name="user">The authenticated user.</param>
        /// <param name="cookiePrincipal">The SSO-cookie principal carrying the session evidence.</param>
        /// <param name="grant">The grant's client, scopes, resources, and allow-list.</param>
        /// <param name="application">The requesting client's application object.</param>
        /// <param name="cancellationToken">Aborts the operation.</param>
        /// <returns>The identity, or a protocol error.</returns>
        public async Task<PrincipalResult> CreateAsync(
            TellmaIdentityUser user,
            ClaimsPrincipal cookiePrincipal,
            GrantRequest grant,
            object application,
            CancellationToken cancellationToken)
        {
            ArgumentNullException.ThrowIfNull(user);
            ArgumentNullException.ThrowIfNull(cookiePrincipal);
            ArgumentNullException.ThrowIfNull(grant);
            ArgumentNullException.ThrowIfNull(application);

            AssuranceResult? assurance = policyService.ReadAssurance(cookiePrincipal);
            string? sid = cookiePrincipal.FindFirst(TellmaClaims.Sid)?.Value;
            if (assurance is null || sid is null)
            {
                // A session cookie predating the engine's evidence model cannot mint tokens.
                return new PrincipalResult(null, Errors.LoginRequired, "The session must be re-established.");
            }

            ClaimsIdentity identity = new(
                TokenValidationParameters.DefaultAuthenticationType, Claims.Name, Claims.Role);

            identity.SetClaim(Claims.Subject, user.Id)
                    .SetClaim(Claims.Email, user.Email)
                    .SetClaim(Claims.Name, user.DisplayName)
                    .SetClaim(Claims.PreferredUsername, user.Email)
                    .SetClaim(Claims.Locale, user.Locale)
                    .SetClaim(Claims.AuthenticationContextReference, assurance.Acr)
                    .SetClaim(Claims.AuthenticationTime, assurance.AuthTime)
                    .SetClaim(TellmaClaims.Sid, sid);
            identity.SetClaim(Claims.EmailVerified, user.EmailConfirmed);
            identity.SetClaims(Claims.AuthenticationMethodReference, [.. assurance.Amr]);
            identity.SetClaims(TellmaClaims.Methods, [.. assurance.Methods]);

            // The allow-list snapshot (server-side only) lets the refresh path re-enforce the
            // tenant's policy without a database read.
            if (!string.IsNullOrWhiteSpace(grant.AllowedMethodsRaw))
            {
                identity.SetClaim(TellmaClaims.AllowedMethods, grant.AllowedMethodsRaw);
            }

            // The security stamp (server-side only) lets refresh detect sign-out-everywhere.
            string? securityStamp = cookiePrincipal.FindFirst(TellmaClaimDestinations.SecurityStampClaimType)?.Value;
            if (securityStamp is not null)
            {
                identity.SetClaim(TellmaClaimDestinations.SecurityStampClaimType, securityStamp);
            }

            // The device-bound passkey signal (server-side only) rides the encrypted grant
            // principal so the refresh path re-derives the aal3 tier instead of silently
            // downgrading a hardware-key session to aal2.
            string? passkeyDeviceBound = cookiePrincipal.FindFirst(SignInClaims.PasskeyDeviceBound)?.Value;
            if (passkeyDeviceBound is not null)
            {
                identity.SetClaim(SignInClaims.PasskeyDeviceBound, passkeyDeviceBound);
            }

            identity.SetScopes(grant.Scopes);

            string? audienceError = await SetAudiencesAsync(identity, grant, application, cancellationToken);
            if (audienceError is not null)
            {
                return new PrincipalResult(null, Errors.InvalidTarget, audienceError);
            }

            // Attach (or create) the permanent authorization backing this grant; it anchors
            // consent memory and lets global logout revoke the client's refresh tokens.
            string clientObjectId = (await applicationManager.GetIdAsync(application, cancellationToken))!;
            object? authorization = null;
            await foreach (object candidate in authorizationManager.FindAsync(
                subject: user.Id,
                client: clientObjectId,
                status: Statuses.Valid,
                type: AuthorizationTypes.Permanent,
                scopes: identity.GetScopes(),
                cancellationToken))
            {
                authorization = candidate;
            }

            authorization ??= await authorizationManager.CreateAsync(
                identity: identity,
                subject: user.Id,
                client: clientObjectId,
                type: AuthorizationTypes.Permanent,
                scopes: identity.GetScopes(),
                cancellationToken);

            string authorizationId = (await authorizationManager.GetIdAsync(authorization, cancellationToken))!;
            identity.SetAuthorizationId(authorizationId);

            await sessionRegistry.RegisterClientAsync(sid, grant.ClientId, authorizationId, cancellationToken);

            identity.SetDestinations(TellmaClaimDestinations.Resolve);
            return new PrincipalResult(identity, null, null);
        }

        /// <summary>
        ///     Derives token audiences: explicit <c>resource</c> parameters win (already
        ///     validated against the client's permissions); a browser client's distribution
        ///     audience derives from its registered origin; fixed platform audiences come from
        ///     the scope registry.
        /// </summary>
        private async Task<string?> SetAudiencesAsync(
            ClaimsIdentity identity, GrantRequest grant, object application, CancellationToken cancellationToken)
        {
            List<string> resources = [.. grant.Resources];
            if (resources.Count == 0 && identity.HasScope(TellmaIdentityConstants.ApiScope))
            {
                ImmutableDictionary<string, JsonElement> properties =
                    await applicationManager.GetPropertiesAsync(application, cancellationToken);
                string? origin = TellmaClientProperties.Get(properties, TellmaClientProperties.Origin);
                if (origin is null)
                {
                    return "The tellma_api scope requires an explicit 'resource' parameter for this client.";
                }

                resources.Add(origin);
            }

            // Platform-scope audiences are unioned in unconditionally: a token granted
            // tellma_identity or tellma_control_plane must carry that audience even when an
            // explicit resource was also named, or the platform API would reject it.
            ImmutableArray<string> platformScopes =
                [.. identity.GetScopes().Where(static scope => scope != TellmaIdentityConstants.ApiScope)];
            await foreach (string resource in scopeManager.ListResourcesAsync(platformScopes, cancellationToken))
            {
                if (!resources.Contains(resource, StringComparer.Ordinal))
                {
                    resources.Add(resource);
                }
            }

            identity.SetResources(resources);
            return null;
        }
    }
}
