// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Microsoft.AspNetCore;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Extensions;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using OpenIddict.Abstractions;
using OpenIddict.Server.AspNetCore;
using System.Globalization;
using System.Security.Claims;
using Tellma.Identity.Controllers.ViewModels;
using Tellma.Identity.Data;
using Tellma.Identity.Infrastructure;
using Tellma.Identity.Options;
using Tellma.Identity.Services.Audit;
using Tellma.Identity.Services.AuthenticationPolicy;
using static OpenIddict.Abstractions.OpenIddictConstants;

namespace Tellma.Identity.Controllers
{
    /// <summary>
    ///     The authorization endpoint (pass-through): checks the SSO session cookie, enforces the
    ///     tenant's authentication policy (allowed methods, requested assurance, freshness),
    ///     drives the login/step-up UI, handles consent, and signs the protocol principal in.
    /// </summary>
    /// <param name="applicationManager">The OpenIddict application manager.</param>
    /// <param name="policyService">The authentication-policy engine.</param>
    /// <param name="principalFactory">Protocol principal assembly.</param>
    /// <param name="authorizationManager">The OpenIddict authorization manager.</param>
    /// <param name="userManager">The Identity user manager.</param>
    /// <param name="engineOptions">The engine options (route prefix).</param>
    /// <param name="auditLogger">Audit emission.</param>
    /// <param name="timeProvider">The clock.</param>
    public sealed class AuthorizationController(
        IOpenIddictApplicationManager applicationManager,
        IAuthenticationPolicyService policyService,
        TellmaPrincipalFactory principalFactory,
        IOpenIddictAuthorizationManager authorizationManager,
        UserManager<TellmaIdentityUser> userManager,
        IOptions<TellmaIdentityOptions> engineOptions,
        IAuditLogger auditLogger,
        TimeProvider timeProvider) : Controller
    {
        /// <summary>
        ///     The single TempData key carrying the pending re-authentication marker that breaks
        ///     the <c>prompt=login</c> / <c>max_age</c> loop. One entry, never one per request:
        ///     TempData survives until it is read, and the marker of an abandoned login is keyed
        ///     to an attempt that never comes back, so a per-request key would accumulate in the
        ///     browser's cookie for the whole browsing session. The attempt it belongs to travels
        ///     in the value instead, so a marker left by a different attempt is recognized and
        ///     ignored rather than mistaken for this one's.
        /// </summary>
        private const string ReauthMarkerKey = "tellma.identity.reauth";

        /// <summary>Handles the (PAR-resolved) authorization request.</summary>
        /// <returns>The protocol response, a login redirect, or the consent form.</returns>
        [HttpGet("connect/authorize")]
        [HttpPost("connect/authorize")]
        [IgnoreAntiforgeryToken]
        public async Task<IActionResult> Authorize()
        {
            OpenIddictRequest request = HttpContext.GetOpenIddictServerRequest()
                ?? throw new InvalidOperationException("The OpenID Connect request cannot be retrieved.");

            // OpenIddict defers manager-based validation (client existence, PAR, redirect_uri) to
            // the final sign-in in pass-through mode. Enforce the two guards that decide whether
            // the interaction may even begin up front, so an unknown client or a BFF bypassing
            // PAR gets a clean error instead of failing deep in the sign-in.
            object? application = string.IsNullOrEmpty(request.ClientId)
                ? null
                : await applicationManager.FindByClientIdAsync(request.ClientId);
            if (application is null)
            {
                return ForbidProtocol(Errors.InvalidClient, "The specified client is unknown.");
            }

            if (await applicationManager.HasRequirementAsync(application, Requirements.Features.PushedAuthorizationRequests)
                && string.IsNullOrEmpty(request.RequestUri))
            {
                return ForbidProtocol(Errors.InvalidRequest, "This client must use pushed authorization requests.");
            }

            // The tenant's method allow-list rides the pushed request; unknown vocabulary is a
            // protocol error, not something to silently ignore.
            string? allowedMethodsRaw = (string?)request[TellmaParameters.AllowedMethods];
            if (!policyService.TryParseAllowedMethods(allowedMethodsRaw, out IReadOnlyList<string>? allowedMethods))
            {
                return ForbidProtocol(Errors.InvalidRequest,
                    "The tellma_allowed_methods parameter contains an unknown method.");
            }

            AuthenticateResult cookie = await HttpContext.AuthenticateAsync(IdentityConstants.ApplicationScheme);
            AssuranceResult? assurance = cookie.Succeeded ? policyService.ReadAssurance(cookie.Principal) : null;

            // A demand for a fresh interactive event — prompt=login, or a max_age bound the login
            // round trip itself cannot beat — is one-shot per authorization attempt. The marker
            // records which attempt sent the browser to the login page and *when*, and only an
            // interactive event at or after that instant, for that same attempt, discharges the
            // demand. Two things keep another attempt's marker out of it. The attempt id is
            // per-attempt random — the PAR request_uri, else the PKCE code_challenge, which
            // RequireProofKeyForCodeExchange makes mandatory for every authorization-code client —
            // so a marker left behind by an abandoned attempt, or planted by a third party who
            // navigated the browser to this URL, does not match and is discarded. And it is
            // anchored in time, so even a matching marker cannot be met by evidence predating the
            // redirect. The policy engine decides what discharges the demand, because only it
            // knows which evidence carries the requested tier.
            string attemptId = request.ClientId
                + ":" + (request.RequestUri ?? request.CodeChallenge ?? request.State ?? string.Empty);
            long? interactionSince = ReadReauthMarker(attemptId);

            // max_age=0 is OpenID Connect's equivalent of prompt=login: a demand for a fresh
            // event outright, not an elapsed-seconds comparison a same-second sign-in would meet
            // without re-authenticating.
            bool reauthDemanded = request.HasPromptValue(PromptValues.Login) || request.MaxAge == 0;

            TimeSpan? maxAge = request.MaxAge is { } seconds ? TimeSpan.FromSeconds(seconds) : null;
            long nowUnixSeconds = timeProvider.GetUtcNow().ToUnixTimeSeconds();
            PolicyEvaluation evaluation = policyService.Evaluate(
                request.GetAcrValues(), maxAge, allowedMethods, assurance, reauthDemanded, nowUnixSeconds, interactionSince);

            if (evaluation.Outcome == PolicyOutcome.Unsatisfiable)
            {
                return ForbidProtocol(Errors.UnmetAuthenticationRequirements,
                    "No allowed authentication method can satisfy the requested assurance level.");
            }

            if (evaluation.Outcome == PolicyOutcome.InteractionRequired)
            {
                // A non-interactive client cannot be sent to the login UI.
                if (request.HasPromptValue(PromptValues.None))
                {
                    return ForbidProtocol(Errors.LoginRequired, "The user is not signed in.");
                }

                // When the only method the login page can offer is the passkey ceremony and the
                // known user holds no credential that ceremony could satisfy, more interaction
                // cannot help: fail closed with the protocol error instead of parking the browser
                // on a page whose only control leads nowhere. (This covers the aal3 tier, which
                // needs a device-bound credential specifically, and any tier whose allow-list has
                // narrowed to the passkey alone. An anonymous user reaches the check on the second
                // pass, once a first sign-in identifies them.)
                if (evaluation.OfferableMethods is [AuthenticationMethods.Passkey] && cookie.Succeeded
                    && await userManager.GetUserAsync(cookie.Principal!) is { } knownUser)
                {
                    IList<UserPasskeyInfo> passkeys = await userManager.GetPasskeysAsync(knownUser);
                    bool reachable = evaluation.RequiredTier == AcrTiers.Aal3
                        ? passkeys.Any(PasskeySignals.IsDeviceBound)
                        : passkeys.Count > 0;
                    if (!reachable)
                    {
                        return ForbidProtocol(Errors.UnmetAuthenticationRequirements,
                            "The requested assurance requires a passkey the user has not registered.");
                    }
                }

                // Record when this attempt sent the browser to authenticate, so the event it
                // produces can be recognized as this attempt's on return — but only when there is
                // a freshness demand for it to discharge, so an ordinary login redirect writes no
                // cookie state at all.
                if (reauthDemanded || maxAge is not null)
                {
                    WriteReauthMarker(attemptId, nowUnixSeconds);
                }

                return RedirectToLogin(evaluation, stepUp: assurance is not null);
            }

            TellmaIdentityUser? user = await userManager.GetUserAsync(cookie.Principal!);
            if (user is null)
            {
                // The cookie outlived the user record: interaction is required, which a
                // non-interactive client must hear as the protocol error.
                if (request.HasPromptValue(PromptValues.None))
                {
                    return ForbidProtocol(Errors.LoginRequired, "The user is not signed in.");
                }

                // Force a fresh interaction carrying the same method and tier context an
                // anonymous visitor would get (the Satisfied evaluation in hand has no
                // offerable-methods list to give the login page) — unless no method can satisfy
                // the request at all, which is the protocol error, not a login page offering
                // methods the tenant disallowed.
                PolicyEvaluation fresh = policyService.Evaluate(
                    request.GetAcrValues(), maxAge, allowedMethods, null, false, nowUnixSeconds, interactionSince);
                if (fresh.Outcome == PolicyOutcome.Unsatisfiable)
                {
                    return ForbidProtocol(Errors.UnmetAuthenticationRequirements,
                        "No allowed authentication method can satisfy the requested assurance level.");
                }

                if (reauthDemanded || maxAge is not null)
                {
                    WriteReauthMarker(attemptId, nowUnixSeconds);
                }

                return RedirectToLogin(fresh, stepUp: false);
            }

            if (user.LifecycleState != UserLifecycleState.Active)
            {
                return ForbidProtocol(Errors.AccessDenied, "The account cannot obtain tokens.");
            }

            // Consent: first-party clients are implicit; third-party clients need an explicit,
            // remembered grant. A non-interactive client cannot be shown the consent form.
            string? consentType = await applicationManager.GetConsentTypeAsync(application);
            if (consentType == ConsentTypes.Explicit)
            {
                bool hasConsent = await HasPermanentAuthorizationAsync(user, application, request);
                if (!hasConsent || request.HasPromptValue(PromptValues.Consent))
                {
                    return request.HasPromptValue(PromptValues.None)
                        ? ForbidProtocol(Errors.ConsentRequired, "Interactive consent is required.")
                        : View("Consent", new ConsentViewModel
                        {
                            ApplicationName = await applicationManager.GetLocalizedDisplayNameAsync(application),
                            Scope = request.Scope ?? string.Empty,
                        });
                }
            }

            return await SignInProtocolAsync(user, cookie.Principal!, request, application);
        }

        /// <summary>Handles the consent form's Accept button.</summary>
        /// <returns>The protocol response.</returns>
        [Authorize]
        [HttpPost("connect/authorize")]
        [FormValueRequired("submit.Accept")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Accept()
        {
            OpenIddictRequest request = HttpContext.GetOpenIddictServerRequest()
                ?? throw new InvalidOperationException("The OpenID Connect request cannot be retrieved.");

            TellmaIdentityUser? user = await userManager.GetUserAsync(User);
            if (user is null || user.LifecycleState != UserLifecycleState.Active)
            {
                return ForbidProtocol(Errors.AccessDenied, "The account cannot obtain tokens.");
            }

            object application = await applicationManager.FindByClientIdAsync(request.ClientId!)
                ?? throw new InvalidOperationException("The application details cannot be found.");

            await auditLogger.LogAsync(new AuditEventEntry
            {
                Action = AuditActions.ConsentGranted,
                Subject = user.Id,
                ClientId = request.ClientId,
                Outcome = "success",
            });

            return await SignInProtocolAsync(user, User, request, application);
        }

        /// <summary>Handles the consent form's Deny button.</summary>
        /// <returns>The protocol error response.</returns>
        [Authorize]
        [HttpPost("connect/authorize")]
        [FormValueRequired("submit.Deny")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Deny()
        {
            OpenIddictRequest? request = HttpContext.GetOpenIddictServerRequest();

            await auditLogger.LogAsync(new AuditEventEntry
            {
                Action = AuditActions.ConsentDenied,
                Subject = userManager.GetUserId(User),
                ClientId = request?.ClientId,
                Outcome = "failure",
            });

            return ForbidProtocol(Errors.AccessDenied, "The authorization was denied by the user.");
        }

        /// <summary>
        ///     Reads — and consumes — the pending re-authentication marker when it belongs to this
        ///     authorization attempt. A marker written by any other attempt is discarded rather
        ///     than credited, which is what stops an abandoned attempt from discharging a later
        ///     demand for a fresh interactive event.
        /// </summary>
        /// <param name="attemptId">This attempt's identity.</param>
        /// <returns>When this attempt sent the browser to authenticate, or null.</returns>
        private long? ReadReauthMarker(string attemptId)
        {
            // Peeked, not read: reading consumes, and a marker belonging to a different attempt —
            // a second tab part-way through its own login — must be left where its owner can still
            // find it rather than eaten on its behalf.
            if (TempData.Peek(ReauthMarkerKey) is not string marker)
            {
                return null;
            }

            // The timestamp is the tail after the last separator; the attempt id may itself contain
            // one, since `state` is whatever the client chose.
            int separator = marker.LastIndexOf('|');
            if (separator <= 0 || !string.Equals(marker[..separator], attemptId, StringComparison.Ordinal))
            {
                return null;
            }

            // It is this attempt's, and the demand it discharges is one-shot.
            TempData.Remove(ReauthMarkerKey);
            return long.TryParse(
                marker[(separator + 1)..], NumberStyles.Integer, CultureInfo.InvariantCulture, out long redirectedAt)
                ? redirectedAt
                : null;
        }

        /// <summary>
        ///     Records that this attempt sent the browser to authenticate, replacing any marker a
        ///     previous attempt left behind.
        /// </summary>
        /// <param name="attemptId">This attempt's identity.</param>
        /// <param name="nowUnixSeconds">The instant of the redirect.</param>
        private void WriteReauthMarker(string attemptId, long nowUnixSeconds)
        {
            // Stored as a string: TempData's serializer accepts no 64-bit integer.
            TempData[ReauthMarkerKey] = attemptId + "|" + nowUnixSeconds.ToString(CultureInfo.InvariantCulture);
        }

        /// <summary>Builds the protocol principal and completes the authorization.</summary>
        private async Task<IActionResult> SignInProtocolAsync(
            TellmaIdentityUser user, ClaimsPrincipal cookiePrincipal, OpenIddictRequest request, object application)
        {
            PrincipalResult result = await principalFactory.CreateAsync(
                user, cookiePrincipal, request, application, HttpContext.RequestAborted);
            return result.Identity is null
                ? ForbidProtocol(result.Error!, result.ErrorDescription!)
                : SignIn(new ClaimsPrincipal(result.Identity), OpenIddictServerAspNetCoreDefaults.AuthenticationScheme);
        }

        /// <summary>Whether a valid permanent authorization already covers the requested scopes.</summary>
        private async Task<bool> HasPermanentAuthorizationAsync(
            TellmaIdentityUser user, object application, OpenIddictRequest request)
        {
            string clientObjectId = (await applicationManager.GetIdAsync(application))!;
            IAsyncEnumerator<object> authorizations = authorizationManager.FindAsync(
                subject: user.Id,
                client: clientObjectId,
                status: Statuses.Valid,
                type: AuthorizationTypes.Permanent,
                scopes: request.GetScopes(),
                HttpContext.RequestAborted).GetAsyncEnumerator(HttpContext.RequestAborted);
            await using (authorizations)
            {
                return await authorizations.MoveNextAsync();
            }
        }

        /// <summary>
        ///     Redirects to the login UI with the policy context the page needs to offer only the
        ///     right methods. The query is advisory UI state — enforcement happens back here, on
        ///     the pushed request, when the browser returns.
        /// </summary>
        private RedirectResult RedirectToLogin(PolicyEvaluation evaluation, bool stepUp)
        {
            // Rebuild the exact authorize URL (query for GET, form for POST) as the return target.
            string returnUrl = Request.HasFormContentType
                ? Request.PathBase + Request.Path + QueryString.Create(Request.Form)
                : Request.PathBase + Request.Path + Request.QueryString;

            string prefix = engineOptions.Value.PathBase;
            QueryBuilder query = new()
            {
                { "returnUrl", returnUrl },
            };
            if (evaluation.OfferableMethods is { } methods)
            {
                query.Add("methods", string.Join(' ', methods));
            }

            if (stepUp)
            {
                query.Add("stepUp", "true");
            }

            // The aal3 tier travels to the page so it can tell the user a device-bound passkey is
            // needed; a synced passkey would only bounce back here.
            if (evaluation.RequiredTier == AcrTiers.Aal3)
            {
                query.Add("tier", evaluation.RequiredTier);
            }

            return Redirect(prefix + "/Identity/Account/Login" + query.ToQueryString());
        }

        /// <summary>Returns a protocol error through the OpenIddict response pipeline.</summary>
        private ForbidResult ForbidProtocol(string error, string description)
        {
            return Forbid(
                authenticationSchemes: OpenIddictServerAspNetCoreDefaults.AuthenticationScheme,
                properties: new AuthenticationProperties(new Dictionary<string, string?>
                {
                    [OpenIddictServerAspNetCoreConstants.Properties.Error] = error,
                    [OpenIddictServerAspNetCoreConstants.Properties.ErrorDescription] = description,
                }));
        }
    }
}
