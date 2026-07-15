// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Extensions.Localization;
using Tellma.Identity.Data;
using Tellma.Identity.Infrastructure;
using Tellma.Identity.Services.Audit;
using Tellma.Identity.Services.AuthenticationPolicy;

namespace Tellma.Identity.Areas.Identity.Pages.Account
{
    /// <summary>
    ///     The shared passkey-enrollment ceremony, used by invitation accept, recovery, the dev
    ///     bootstrap, and "add another passkey". When enrollment is part of a sign-in flow
    ///     (invitation/recovery), completing the WebAuthn attestation also signs the user in.
    /// </summary>
    /// <param name="signInManager">The Identity sign-in manager (attestation API).</param>
    /// <param name="userManager">The Identity user manager.</param>
    /// <param name="signInService">The engine sign-in (method evidence stamping).</param>
    /// <param name="auditLogger">Audit emission.</param>
    /// <param name="localizer">UI strings.</param>
    [AllowAnonymous]
    public sealed class RegisterPasskeyModel(
        SignInManager<TellmaIdentityUser> signInManager,
        UserManager<TellmaIdentityUser> userManager,
        TellmaSignInService signInService,
        IAuditLogger auditLogger,
        IStringLocalizer<SharedResources> localizer) : PageModel
    {
        /// <summary>The attestation credential JSON posted by the browser ceremony.</summary>
        [BindProperty]
        public string? Credential { get; set; }

        /// <summary>An informational banner, when any.</summary>
        public string? StatusMessage { get; private set; }

        /// <summary>The validated local return URL.</summary>
        public string? ReturnUrl { get; private set; }

        /// <summary>Renders the enrollment page, ensuring a ceremony user can be resolved.</summary>
        /// <param name="returnUrl">Where to continue after enrollment.</param>
        /// <returns>The page, or a redirect to sign in when no ceremony user exists.</returns>
        public async Task<IActionResult> OnGetAsync(string? returnUrl = null)
        {
            ReturnUrl = ReturnUrlValidator.IsValid(returnUrl) ? returnUrl : null;

            return await ResolveUserAsync() is null
                ? RedirectToPage("Login")
                : Page();
        }

        /// <summary>Completes the attestation, stores the passkey, and signs the user in when unauthenticated.</summary>
        /// <param name="returnUrl">Where to continue after enrollment.</param>
        /// <returns>The post-enrollment redirect, or the page with a generic error.</returns>
        public async Task<IActionResult> OnPostAsync(string? returnUrl = null)
        {
            ReturnUrl = ReturnUrlValidator.IsValid(returnUrl) ? returnUrl : null;

            TellmaIdentityUser? user = await ResolveUserAsync();
            if (user is null)
            {
                return RedirectToPage("Login");
            }

            if (string.IsNullOrWhiteSpace(Credential))
            {
                ModelState.AddModelError(string.Empty, localizer["PasskeyFailed"].Value);
                return Page();
            }

            PasskeyAttestationResult attestation = await signInManager.PerformPasskeyAttestationAsync(Credential);
            if (!attestation.Succeeded || attestation.Passkey is null)
            {
                ModelState.AddModelError(string.Empty, localizer["PasskeyFailed"].Value);
                return Page();
            }

            IdentityResult stored = await userManager.AddOrUpdatePasskeyAsync(user, attestation.Passkey);
            if (!stored.Succeeded)
            {
                ModelState.AddModelError(string.Empty, localizer["PasskeyFailed"].Value);
                return Page();
            }

            await auditLogger.LogAsync(new AuditEventEntry
            {
                Action = AuditActions.PasskeyEnrolled,
                Subject = user.Id,
                Outcome = "success",
            });

            // Unauthenticated enrollment (invitation/recovery) completes the sign-in; the WebAuthn
            // ceremony is itself the authentication event. A device-bound (non-synced) credential
            // raises the assurance tier.
            if (User.Identity?.IsAuthenticated != true)
            {
                bool deviceBound = PasskeySignals.IsDeviceBound(attestation.Passkey);
                await signInService.SignInAsync(
                    user,
                    new SignInEvidence(AuthenticationMethods.Passkey, deviceBound),
                    isPersistent: false,
                    HttpContext.RequestAborted);
                CredentialFlowCookie.Clear(HttpContext);
            }

            string fallback = Url.Page("/Manage/Passkeys", new { area = "Identity" })!;
            return LocalRedirect(ReturnUrlValidator.Sanitize(ReturnUrl, fallback));
        }

        /// <summary>Resolves the user this ceremony acts for (authenticated or flow-scoped).</summary>
        private async Task<TellmaIdentityUser?> ResolveUserAsync()
        {
            if (User.Identity?.IsAuthenticated == true)
            {
                return await userManager.GetUserAsync(User);
            }

            string? userId = CredentialFlowCookie.GetUserId(HttpContext);
            return userId is null ? null : await userManager.FindByIdAsync(userId);
        }
    }
}
