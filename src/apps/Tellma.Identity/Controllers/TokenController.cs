// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Microsoft.AspNetCore;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.IdentityModel.Tokens;
using OpenIddict.Abstractions;
using OpenIddict.Server.AspNetCore;
using System.Globalization;
using System.Security.Claims;
using System.Text.Json.Nodes;
using Tellma.Identity.Data;
using Tellma.Identity.Services.AuthenticationPolicy;
using static OpenIddict.Abstractions.OpenIddictConstants;

namespace Tellma.Identity.Controllers
{
    /// <summary>
    ///     The token endpoint (pass-through): one action dispatching on grant type. OpenIddict has
    ///     already authenticated the client and validated grant, scope, and resource permissions
    ///     before the action runs; this controller shapes the principal each grant issues.
    /// </summary>
    /// <param name="applicationManager">The OpenIddict application manager.</param>
    /// <param name="scopeManager">The OpenIddict scope manager.</param>
    /// <param name="userManager">The Identity user manager.</param>
    /// <param name="policyService">The authentication-policy engine (refresh-time re-checks).</param>
    /// <param name="timeProvider">The clock (freshness re-checks).</param>
    public sealed class TokenController(
        IOpenIddictApplicationManager applicationManager,
        IOpenIddictScopeManager scopeManager,
        UserManager<TellmaIdentityUser> userManager,
        IAuthenticationPolicyService policyService,
        TimeProvider timeProvider) : Controller
    {
        /// <summary>Handles every grant the token endpoint accepts.</summary>
        /// <returns>The protocol response.</returns>
        [HttpPost("connect/token")]
        [IgnoreAntiforgeryToken]
        [Produces("application/json")]
        public async Task<IActionResult> Exchange()
        {
            OpenIddictRequest request = HttpContext.GetOpenIddictServerRequest()
                ?? throw new InvalidOperationException("The OpenID Connect request cannot be retrieved.");

            // Dispatch on grant type. Authorization code and device code share one branch: both
            // redeem a stored user principal without the refresh-time policy re-evaluation.
            return true switch
            {
                _ when request.IsClientCredentialsGrantType() => await ExchangeClientCredentialsAsync(request),
                _ when request.IsAuthorizationCodeGrantType() || request.IsDeviceCodeGrantType()
                    => await ExchangeUserGrantAsync(request, isRefresh: false),
                _ when request.IsRefreshTokenGrantType() => await ExchangeUserGrantAsync(request, isRefresh: true),
                _ when request.IsTokenExchangeGrantType() => await ExchangeTokenAsync(request),
                _ => ForbidGrant(Errors.UnsupportedGrantType, "The specified grant type is not supported."),
            };
        }

        /// <summary>
        ///     Token exchange (RFC 8693): a trusted backend obtains a token acting for a user (or
        ///     down-scoping its own). The exchanged token can never widen scope — the requested
        ///     scopes must be a subset of the subject token's — and when the actor differs from the
        ///     subject, the delegation is recorded in the standard <c>act</c> claim.
        /// </summary>
        private async Task<IActionResult> ExchangeTokenAsync(OpenIddictRequest request)
        {
            AuthenticateResult result = await HttpContext.AuthenticateAsync(
                OpenIddictServerAspNetCoreDefaults.AuthenticationScheme);
            ClaimsPrincipal subject = result.Principal
                ?? throw new InvalidOperationException("The subject token principal cannot be retrieved.");

            ClaimsPrincipal? actor = result.Properties?.GetParameter<ClaimsPrincipal>(
                OpenIddictServerAspNetCoreConstants.Properties.ActorTokenPrincipal);

            // Down-scoping only: the exchanged token must never gain scopes the subject lacked.
            HashSet<string> subjectScopes = [.. subject.GetScopes()];
            string[] requestedScopes = [.. request.GetScopes()];
            if (requestedScopes.Length > 0 && requestedScopes.Any(scope => !subjectScopes.Contains(scope)))
            {
                return ForbidGrant(Errors.InvalidScope, "The exchanged token cannot widen the subject token's scopes.");
            }

            ClaimsIdentity identity = new(TokenValidationParameters.DefaultAuthenticationType, Claims.Name, Claims.Role);
            identity.SetClaim(Claims.Subject, subject.GetClaim(Claims.Subject));

            // When the subject is a user, re-check the lifecycle gate and refresh profile claims.
            string subjectId = subject.GetClaim(Claims.Subject)!;
            TellmaIdentityUser? user = await userManager.FindByIdAsync(subjectId);
            if (user is not null)
            {
                if (user.LifecycleState != UserLifecycleState.Active)
                {
                    return ForbidGrant(Errors.InvalidGrant, "The account cannot obtain tokens.");
                }

                identity.SetClaim(Claims.Email, user.Email)
                        .SetClaim(Claims.Name, user.DisplayName)
                        .SetClaim(Claims.PreferredUsername, user.Email)
                        .SetClaim(Claims.Locale, user.Locale);
                identity.SetClaim(Claims.EmailVerified, user.EmailConfirmed);

                // Carry the assurance and session claims from the subject token unchanged. The
                // numeric ones are copied as numbers: OpenIddict refuses to sign in a principal
                // whose auth_time is not of a numeric claim type, so copying it as the string the
                // reader returns would turn every delegation into a 500.
                foreach (string claimType in (string[])
                    [Claims.AuthenticationContextReference, TellmaClaims.Sid])
                {
                    if (subject.GetClaim(claimType) is { } value)
                    {
                        identity.SetClaim(claimType, value);
                    }
                }

                foreach (string claimType in (string[])[Claims.AuthenticationTime, TellmaClaims.AcrAuthTime])
                {
                    if (long.TryParse(
                        subject.GetClaim(claimType), NumberStyles.Integer, CultureInfo.InvariantCulture, out long seconds))
                    {
                        identity.SetClaim(claimType, seconds);
                    }
                }

                identity.SetClaims(Claims.AuthenticationMethodReference, [.. subject.GetClaims(Claims.AuthenticationMethodReference)]);
                identity.SetClaims(TellmaClaims.Methods, [.. subject.GetClaims(TellmaClaims.Methods)]);
            }

            // Record the delegation when the actor differs from the subject.
            string? actorSubject = actor?.GetClaim(Claims.Subject);
            if (!string.IsNullOrEmpty(actorSubject)
                && !string.Equals(actorSubject, subjectId, StringComparison.Ordinal))
            {
                identity.SetClaim(Claims.Actor, new JsonObject { [Claims.Subject] = actorSubject });
            }

            identity.SetScopes(requestedScopes.Length > 0 ? requestedScopes : subject.GetScopes());

            List<string> resources = [.. request.GetResources()];
            if (resources.Count == 0)
            {
                await foreach (string resource in scopeManager.ListResourcesAsync(identity.GetScopes(), HttpContext.RequestAborted))
                {
                    resources.Add(resource);
                }
            }

            identity.SetResources(resources);
            identity.SetDestinations(TellmaClaimDestinations.Resolve);

            return SignIn(new ClaimsPrincipal(identity), OpenIddictServerAspNetCoreDefaults.AuthenticationScheme);
        }

        /// <summary>
        ///     Authorization-code, device-code, and refresh-token redemption: OpenIddict has
        ///     already validated the presented token (and PKCE / rotation); this branch reloads
        ///     the user, re-checks the lifecycle gate, and — at refresh — re-evaluates the tenant's
        ///     current authentication policy (the short access-token lifetime is the re-evaluation
        ///     point), so a security-stamp bump, a disabled method, or a tightened assurance
        ///     requirement stops minting tokens within one access-token lifetime.
        /// </summary>
        private async Task<IActionResult> ExchangeUserGrantAsync(OpenIddictRequest request, bool isRefresh)
        {
            // The principal stored inside the presented token, with scopes, resources, assurance
            // claims, and the backing authorization id intact.
            AuthenticateResult result = await HttpContext.AuthenticateAsync(
                OpenIddictServerAspNetCoreDefaults.AuthenticationScheme);
            ClaimsPrincipal stored = result.Principal
                ?? throw new InvalidOperationException("The grant principal cannot be retrieved.");

            TellmaIdentityUser? user = await userManager.FindByIdAsync(stored.GetClaim(Claims.Subject)!);
            if (user is null || user.LifecycleState != UserLifecycleState.Active)
            {
                return ForbidGrant(Errors.InvalidGrant, "The account cannot obtain tokens.");
            }

            AssuranceResult? effectiveAssurance = null;
            if (isRefresh)
            {
                (IActionResult? rejection, effectiveAssurance) = await ReevaluatePolicyAsync(request, stored, user);
                if (rejection is not null)
                {
                    return rejection;
                }
            }

            // Clone the stored identity and refresh the volatile profile claims from the store,
            // so a name/locale change surfaces without a fresh interactive login.
            ClaimsIdentity identity = new(
                stored.Claims, TokenValidationParameters.DefaultAuthenticationType, Claims.Name, Claims.Role);
            identity.SetClaim(Claims.Email, user.Email)
                    .SetClaim(Claims.Name, user.DisplayName)
                    .SetClaim(Claims.PreferredUsername, user.Email)
                    .SetClaim(Claims.Locale, user.Locale);
            identity.SetClaim(Claims.EmailVerified, user.EmailConfirmed);

            if (effectiveAssurance is not null)
            {
                // The refreshed tokens carry only the assurance the current allow-list permits —
                // a since-disallowed method's contribution is filtered out of the claims here,
                // mirroring the authorization-time filter — and the tier's freshness is re-dated
                // with it, since filtering can lower the tier and the pair must stay consistent.
                identity.SetClaim(Claims.AuthenticationContextReference, effectiveAssurance.Acr);
                identity.SetClaim(TellmaClaims.AcrAuthTime, policyService.TierAuthTime(effectiveAssurance));
                identity.SetClaims(Claims.AuthenticationMethodReference, [.. effectiveAssurance.Amr]);
                identity.SetClaims(TellmaClaims.Methods, [.. effectiveAssurance.Methods]);
            }

            // A pushed allow-list becomes the rotated token's stored snapshot, so a tightening
            // pushed once holds for later refreshes that omit the parameter.
            if (isRefresh && (string?)request[TellmaParameters.AllowedMethods] is { Length: > 0 } pushedAllowedMethods)
            {
                identity.SetClaim(TellmaClaims.AllowedMethods, pushedAllowedMethods);
            }

            identity.SetDestinations(TellmaClaimDestinations.Resolve);

            return SignIn(new ClaimsPrincipal(identity), OpenIddictServerAspNetCoreDefaults.AuthenticationScheme);
        }

        /// <summary>
        ///     Re-evaluates the authentication policy at refresh. Enforcement lives at the
        ///     authority (not the resource server) because <c>amr</c> is too coarse to carry the
        ///     allow-list's granularity.
        /// </summary>
        /// <returns>
        ///     A rejection, or the assurance filtered to the current allow-list — what the rotated
        ///     token's claims must carry (null when the stored principal has no method evidence).
        /// </returns>
        private async Task<(IActionResult? Rejection, AssuranceResult? EffectiveAssurance)> ReevaluatePolicyAsync(
            OpenIddictRequest request, ClaimsPrincipal stored, TellmaIdentityUser user)
        {
            // Security stamp: "sign out everywhere" (UpdateSecurityStampAsync) bumps the stamp,
            // so a token minted before the bump can no longer renew.
            string? storedStamp = stored.GetClaim(TellmaClaimDestinations.SecurityStampClaimType);
            if (storedStamp is not null)
            {
                string currentStamp = await userManager.GetSecurityStampAsync(user);
                if (!string.Equals(storedStamp, currentStamp, StringComparison.Ordinal))
                {
                    return (ForbidGrant(Errors.InvalidGrant, "The session was terminated."), null);
                }
            }

            // The current allow-list: the refresh request may carry an updated tellma_allowed_methods
            // (the confidential BFF pushes the tenant's current policy), else the snapshot stored
            // at authorization time. Unknown vocabulary is a protocol error (mirroring the
            // authorization endpoint) — never silently ignored, which would skip enforcement and
            // persist the malformed value as the next snapshot. A since-disallowed method stops
            // counting toward the grant's assurance; when no used method remains permitted,
            // renewal stops.
            string? currentAllowedRaw = (string?)request[TellmaParameters.AllowedMethods]
                ?? stored.GetClaim(TellmaClaims.AllowedMethods);
            if (!policyService.TryParseAllowedMethods(currentAllowedRaw, out IReadOnlyList<string>? allowedMethods))
            {
                return (ForbidGrant(Errors.InvalidRequest,
                    "The tellma_allowed_methods parameter contains an unknown method."), null);
            }

            AssuranceResult? assurance = policyService.ReadAssurance(stored);
            if (allowedMethods is not null && assurance is not null)
            {
                assurance = policyService.FilterAssurance(assurance, allowedMethods);
                if (assurance is null)
                {
                    return (ForbidGrant(Errors.InvalidGrant, "No method used to authenticate remains permitted."), null);
                }
            }

            // Assurance: if the refresh request restates an acr_values requirement, the assurance
            // the still-allowed methods support must satisfy it.
            IReadOnlyList<string> requestedAcr = [.. request.GetAcrValues()];
            if (requestedAcr.Count > 0)
            {
                PolicyEvaluation evaluation = policyService.Evaluate(
                    requestedAcr,
                    maxAge: null,
                    allowedMethods,
                    assurance,
                    forceInteraction: false,
                    timeProvider.GetUtcNow().ToUnixTimeSeconds());
                if (evaluation.Outcome != PolicyOutcome.Satisfied)
                {
                    return (ForbidGrant(Errors.UnmetAuthenticationRequirements, "The authentication no longer meets the requested assurance."), null);
                }
            }

            return (null, assurance);
        }

        /// <summary>Returns a protocol error at the token endpoint.</summary>
        private ForbidResult ForbidGrant(string error, string description)
        {
            return Forbid(
                authenticationSchemes: OpenIddictServerAspNetCoreDefaults.AuthenticationScheme,
                properties: new AuthenticationProperties(new Dictionary<string, string?>
                {
                    [OpenIddictServerAspNetCoreConstants.Properties.Error] = error,
                    [OpenIddictServerAspNetCoreConstants.Properties.ErrorDescription] = description,
                }));
        }

        /// <summary>
        ///     Client credentials: machine callers (service accounts, distribution backends, the
        ///     control plane). The subject is the client id; no refresh token is ever issued.
        /// </summary>
        private async Task<IActionResult> ExchangeClientCredentialsAsync(OpenIddictRequest request)
        {
            // The client secret was already validated by OpenIddict; a missing application here
            // is an integrity violation, not a protocol error.
            object application = await applicationManager.FindByClientIdAsync(request.ClientId!)
                ?? throw new InvalidOperationException("The application details cannot be found.");

            ClaimsIdentity identity = new(TokenValidationParameters.DefaultAuthenticationType, Claims.Name, Claims.Role);
            identity.SetClaim(Claims.Subject, request.ClientId);
            identity.SetClaim(Claims.Name, await applicationManager.GetDisplayNameAsync(application));
            identity.SetScopes(request.GetScopes());

            IActionResult? failure = await SetMachineAudiencesAsync(identity, request);
            if (failure is not null)
            {
                return failure;
            }

            // Machine tokens carry every claim in the access token only.
            identity.SetDestinations(static _ => [Destinations.AccessToken]);

            return SignIn(new ClaimsPrincipal(identity), OpenIddictServerAspNetCoreDefaults.AuthenticationScheme);
        }

        /// <summary>
        ///     Derives the audiences of a machine token. Explicit <c>resource</c> parameters win
        ///     (already validated against the client's resource permissions); otherwise audiences
        ///     come from the requested scopes' fixed platform resources. The per-distribution
        ///     <c>tellma_api</c> scope has no unambiguous audience for a machine caller, so it
        ///     always requires an explicit resource.
        /// </summary>
        private async Task<IActionResult?> SetMachineAudiencesAsync(ClaimsIdentity identity, OpenIddictRequest request)
        {
            List<string> resources = [.. request.GetResources()];
            if (resources.Count == 0 && request.HasScope(TellmaIdentityConstants.ApiScope))
            {
                return Forbid(
                    authenticationSchemes: OpenIddictServerAspNetCoreDefaults.AuthenticationScheme,
                    properties: new AuthenticationProperties(new Dictionary<string, string?>
                    {
                        [OpenIddictServerAspNetCoreConstants.Properties.Error] = Errors.InvalidTarget,
                        [OpenIddictServerAspNetCoreConstants.Properties.ErrorDescription] =
                            "The tellma_api scope requires an explicit 'resource' parameter naming the target API.",
                    }));
            }

            // Platform-scope audiences are unioned in unconditionally (mirroring the interactive
            // path); tellma_api is excluded because its scope entity aggregates every
            // distribution's audience — the target API must be named explicitly.
            System.Collections.Immutable.ImmutableArray<string> platformScopes =
                [.. identity.GetScopes().Where(static scope => scope != TellmaIdentityConstants.ApiScope)];
            await foreach (string resource in scopeManager.ListResourcesAsync(platformScopes, HttpContext.RequestAborted))
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
