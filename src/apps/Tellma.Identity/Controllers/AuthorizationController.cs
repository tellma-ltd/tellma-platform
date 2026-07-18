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
        /// <summary>TempData key breaking the <c>prompt=login</c> re-authentication loop.</summary>
        private const string ReauthCompletedKey = "tellma.identity.reauth";

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

            // prompt=login forces one fresh interactive event; TempData breaks the loop when the
            // browser returns to this same (still prompt=login) request afterwards.
            bool reauthDemanded = request.HasPromptValue(PromptValues.Login) && TempData[ReauthCompletedKey] is null;

            PolicyEvaluation evaluation = policyService.Evaluate(
                request.GetAcrValues(),
                request.MaxAge is { } maxAge ? TimeSpan.FromSeconds(maxAge) : null,
                allowedMethods,
                assurance,
                reauthDemanded,
                timeProvider.GetUtcNow().ToUnixTimeSeconds());

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

                if (reauthDemanded)
                {
                    TempData[ReauthCompletedKey] = true;
                }

                return RedirectToLogin(evaluation, stepUp: assurance is not null);
            }

            TellmaIdentityUser? user = await userManager.GetUserAsync(cookie.Principal!);
            if (user is null)
            {
                // The cookie outlived the user record; force a fresh interaction.
                return RedirectToLogin(evaluation, stepUp: false);
            }

            if (user.LifecycleState != UserLifecycleState.Active)
            {
                return ForbidProtocol(Errors.AccessDenied, "The account cannot obtain tokens.");
            }

            // Consent: first-party clients are implicit; third-party clients need an explicit,
            // remembered grant.
            string? consentType = await applicationManager.GetConsentTypeAsync(application);
            if (consentType == ConsentTypes.Explicit)
            {
                bool hasConsent = await HasPermanentAuthorizationAsync(user, application, request);
                if (!hasConsent || request.HasPromptValue(PromptValues.Consent))
                {
                    return View("Consent", new ConsentViewModel
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
