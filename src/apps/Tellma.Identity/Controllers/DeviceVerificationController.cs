// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using OpenIddict.Abstractions;
using OpenIddict.Server.AspNetCore;
using System.Security.Claims;
using Tellma.Identity.Controllers.ViewModels;
using Tellma.Identity.Data;
using Tellma.Identity.Infrastructure;
using Tellma.Identity.Services.Audit;
using Tellma.Identity.Services.AuthenticationPolicy;
using static OpenIddict.Abstractions.OpenIddictConstants;

namespace Tellma.Identity.Controllers
{
    /// <summary>
    ///     The device end-user verification endpoint: the human confirms the user code shown on a
    ///     headless device, sees which client is requesting access, and approves or denies. The
    ///     user must be signed in first (the login challenge subjects the device flow to the same
    ///     authentication policy as every other flow).
    /// </summary>
    /// <param name="applicationManager">The OpenIddict application manager.</param>
    /// <param name="userManager">The Identity user manager.</param>
    /// <param name="principalFactory">Protocol principal assembly.</param>
    /// <param name="auditLogger">Audit emission.</param>
    public sealed class DeviceVerificationController(
        IOpenIddictApplicationManager applicationManager,
        UserManager<TellmaIdentityUser> userManager,
        TellmaPrincipalFactory principalFactory,
        IAuditLogger auditLogger) : Controller
    {
        /// <summary>Renders the verification form, pre-filling a user code supplied in the URI.</summary>
        /// <returns>The verification view.</returns>
        [Authorize]
        [HttpGet("connect/verify")]
        [IgnoreAntiforgeryToken]
        public async Task<IActionResult> Verify()
        {
            AuthenticateResult result = await HttpContext.AuthenticateAsync(
                OpenIddictServerAspNetCoreDefaults.AuthenticationScheme);

            string? userCode = result.Succeeded
                ? result.Properties?.GetTokenValue(OpenIddictServerAspNetCoreConstants.Tokens.UserCode)
                : null;

            return string.IsNullOrEmpty(userCode)
                ? View(new VerifyViewModel())
                : View(new VerifyViewModel
                {
                    UserCode = userCode,
                    ApplicationName = await GetApplicationNameAsync(result.Principal),
                    Scope = string.Join(' ', result.Principal!.GetScopes()),
                });
        }

        /// <summary>Approves the device authorization, issuing the device its tokens on the next poll.</summary>
        /// <returns>The sign-in result completing the device grant.</returns>
        [Authorize]
        [HttpPost("connect/verify")]
        [FormValueRequired("submit.Accept")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Accept()
        {
            TellmaIdentityUser? user = await userManager.GetUserAsync(User);
            if (user is null || user.LifecycleState != UserLifecycleState.Active)
            {
                return View("Verify", new VerifyViewModel
                {
                    Error = Errors.AccessDenied,
                    ErrorDescription = "The account cannot obtain tokens.",
                });
            }

            AuthenticateResult result = await HttpContext.AuthenticateAsync(
                OpenIddictServerAspNetCoreDefaults.AuthenticationScheme);
            if (result is not { Succeeded: true } || string.IsNullOrEmpty(result.Principal.GetClaim(Claims.ClientId)))
            {
                return View("Verify", new VerifyViewModel
                {
                    Error = Errors.InvalidToken,
                    ErrorDescription = "The user code is invalid or has expired.",
                });
            }

            string clientId = result.Principal.GetClaim(Claims.ClientId)!;
            object application = await applicationManager.FindByClientIdAsync(clientId)
                ?? throw new InvalidOperationException("The application details cannot be found.");

            // The device grant reuses the shared principal builder, so a device sign-in carries
            // the same assurance, session, and audience claims as an authorization-code sign-in.
            // Its scopes and resources come from the stored user-code principal.
            GrantRequest grant = new(
                clientId,
                [.. result.Principal.GetScopes()],
                [.. result.Principal.GetResources()],
                AllowedMethodsRaw: null);
            PrincipalResult principal = await principalFactory.CreateAsync(
                user, User, grant, application, HttpContext.RequestAborted);
            if (principal.Identity is null)
            {
                return View("Verify", new VerifyViewModel
                {
                    Error = principal.Error,
                    ErrorDescription = principal.ErrorDescription,
                });
            }

            await auditLogger.LogAsync(new AuditEventEntry
            {
                Action = AuditActions.DeviceCodeApproved,
                Subject = user.Id,
                ClientId = result.Principal.GetClaim(Claims.ClientId),
                Outcome = "success",
            });

            return SignIn(
                new ClaimsPrincipal(principal.Identity),
                new AuthenticationProperties { RedirectUri = Url.Page("/Account/DeviceApproved", new { area = TellmaIdentityConstants.AreaName }) },
                OpenIddictServerAspNetCoreDefaults.AuthenticationScheme);
        }

        /// <summary>Denies the device authorization.</summary>
        /// <returns>The forbid result rejecting the device grant.</returns>
        [Authorize]
        [HttpPost("connect/verify")]
        [FormValueRequired("submit.Deny")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Deny()
        {
            await auditLogger.LogAsync(new AuditEventEntry
            {
                Action = AuditActions.DeviceCodeDenied,
                Subject = userManager.GetUserId(User),
                Outcome = "failure",
            });

            return Forbid(
                authenticationSchemes: OpenIddictServerAspNetCoreDefaults.AuthenticationScheme,
                properties: new AuthenticationProperties { RedirectUri = Url.Page("/Account/DeviceApproved", new { area = TellmaIdentityConstants.AreaName }) });
        }

        /// <summary>Resolves the requesting client's display name for the confirmation view.</summary>
        private async Task<string?> GetApplicationNameAsync(ClaimsPrincipal? principal)
        {
            string? clientId = principal?.GetClaim(Claims.ClientId);
            if (string.IsNullOrEmpty(clientId))
            {
                return null;
            }

            object? application = await applicationManager.FindByClientIdAsync(clientId);
            return application is null ? clientId : await applicationManager.GetLocalizedDisplayNameAsync(application);
        }
    }
}
