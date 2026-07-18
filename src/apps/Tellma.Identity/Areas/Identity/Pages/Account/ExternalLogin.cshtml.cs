// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Extensions.Localization;
using System.Security.Claims;
using Tellma.Identity.Data;
using Tellma.Identity.Infrastructure;
using Tellma.Identity.Services.Audit;
using Tellma.Identity.Services.AuthenticationPolicy;

namespace Tellma.Identity.Areas.Identity.Pages.Account
{
    /// <summary>
    ///     External login (Google, Microsoft). A login already linked by the provider's stable
    ///     subject <c>(LoginProvider, ProviderKey)</c> signs in directly. A new external identity
    ///     is never auto-merged by email: linking requires the provider to assert a verified email
    ///     AND proof of ownership of the local account — an authenticated session, or the
    ///     invitation/recovery credential-flow context (the single-use link is itself the proof).
    /// </summary>
    /// <param name="signInManager">The Identity sign-in manager.</param>
    /// <param name="userManager">The Identity user manager.</param>
    /// <param name="signInService">The engine sign-in (method evidence stamping).</param>
    /// <param name="auditLogger">Audit emission.</param>
    /// <param name="localizer">UI strings.</param>
    [AllowAnonymous]
    public sealed class ExternalLoginModel(
        SignInManager<TellmaIdentityUser> signInManager,
        UserManager<TellmaIdentityUser> userManager,
        TellmaSignInService signInService,
        IAuditLogger auditLogger,
        IStringLocalizer<SharedResources> localizer) : PageModel
    {
        /// <summary>An error to display when the flow could not complete.</summary>
        public string? Error { get; private set; }

        /// <summary>Starts the external-login challenge.</summary>
        /// <param name="provider">The external authentication scheme (Google, Microsoft).</param>
        /// <param name="returnUrl">Where to return after sign-in.</param>
        /// <returns>A challenge to the external provider.</returns>
        public IActionResult OnPost(string provider, string? returnUrl = null)
        {
            string safeReturn = ReturnUrlValidator.Sanitize(returnUrl, Url.Page("/Account/Login", new { area = "Identity" })!);
            string redirectUrl = Url.Page("ExternalLogin", pageHandler: "Callback", values: new { returnUrl = safeReturn })!;
            AuthenticationProperties properties = signInManager.ConfigureExternalAuthenticationProperties(provider, redirectUrl);
            return Challenge(properties, provider);
        }

        /// <summary>Handles the provider callback: sign in an existing link, or link with ownership proof.</summary>
        /// <param name="returnUrl">Where to return after sign-in.</param>
        /// <param name="remoteError">An error reported by the provider, when any.</param>
        /// <returns>The post-sign-in redirect, or the page with a generic error.</returns>
        public async Task<IActionResult> OnGetCallbackAsync(string? returnUrl = null, string? remoteError = null)
        {
            string safeReturn = ReturnUrlValidator.Sanitize(returnUrl, Url.Page("/Account/Login", new { area = "Identity" })!);
            if (remoteError is not null)
            {
                return Fail();
            }

            ExternalLoginInfo? info = await signInManager.GetExternalLoginInfoAsync();
            if (info is null)
            {
                return Fail();
            }

            string method = MapMethod(info.LoginProvider);

            // 1. Already linked by (provider, key): sign in directly.
            TellmaIdentityUser? linked = await userManager.FindByLoginAsync(info.LoginProvider, info.ProviderKey);
            if (linked is not null)
            {
                return linked.LifecycleState != UserLifecycleState.Active
                    ? Fail()
                    : await CompleteSignInAsync(linked, method, safeReturn);
            }

            // 2. New external identity: link only with a verified email AND ownership proof.
            bool emailVerified = string.Equals(
                info.Principal.FindFirstValue("email_verified"), "true", StringComparison.OrdinalIgnoreCase);
            string? providerEmail = info.Principal.FindFirstValue(ClaimTypes.Email);

            TellmaIdentityUser? owner = await ResolveOwnerAsync(providerEmail);
            if (owner is null || !emailVerified)
            {
                // Never auto-merge by email without proof — this is the pre-hijacking guard.
                return Fail();
            }

            IdentityResult link = await userManager.AddLoginAsync(owner, info);
            if (!link.Succeeded)
            {
                return Fail();
            }

            await auditLogger.LogAsync(new AuditEventEntry
            {
                Action = AuditActions.ExternalLoginLinked,
                Subject = owner.Id,
                Outcome = "success",
            });

            CredentialFlowCookie.Clear(HttpContext);
            return await CompleteSignInAsync(owner, method, safeReturn);
        }

        /// <summary>Signs the user in, recording the external method as the authentication event.</summary>
        private async Task<IActionResult> CompleteSignInAsync(TellmaIdentityUser user, string method, string returnUrl)
        {
            await signInService.SignInAsync(
                user, new SignInEvidence(method), isPersistent: false, HttpContext.RequestAborted);
            return LocalRedirect(returnUrl);
        }

        /// <summary>
        ///     Resolves the local account an external identity may link to, from the ownership
        ///     proofs the spec permits: an authenticated session, or the invitation/recovery
        ///     credential-flow context whose provider email must match.
        /// </summary>
        private async Task<TellmaIdentityUser?> ResolveOwnerAsync(string? providerEmail)
        {
            // An authenticated user is linking a new provider from Account & Security.
            if (User.Identity?.IsAuthenticated == true)
            {
                return await userManager.GetUserAsync(User);
            }

            // Only the invitation flow may link an external login (§8.4): the single-use invitation
            // link is the email-ownership proof. A recovery or bootstrap context has a passkey-only
            // exit (§10.3–§10.4), so it must never yield a signed-in session by linking a social IdP.
            CredentialFlowContext? flow = CredentialFlowCookie.Read(HttpContext);
            if (flow is not { Purpose: CredentialFlowPurpose.Invitation } || providerEmail is null)
            {
                return null;
            }

            TellmaIdentityUser? user = await userManager.FindByIdAsync(flow.UserId);
            return user is not null
                && string.Equals(user.Email, providerEmail, StringComparison.OrdinalIgnoreCase)
                ? user
                : null;
        }

        /// <summary>Maps an external provider scheme to the method vocabulary.</summary>
        private static string MapMethod(string loginProvider)
        {
            return loginProvider.Equals("Microsoft", StringComparison.OrdinalIgnoreCase)
                ? AuthenticationMethods.Microsoft
                : AuthenticationMethods.Google;
        }

        /// <summary>Renders the generic external-login failure.</summary>
        private PageResult Fail()
        {
            Error = localizer["PasskeyFailed"].Value;
            return Page();
        }
    }
}
