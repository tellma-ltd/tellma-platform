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
using Tellma.Identity.Data.Entities;
using Tellma.Identity.Infrastructure;
using Tellma.Identity.Services.Audit;
using Tellma.Identity.Services.AuthenticationPolicy;
using Tellma.Identity.Services.EmailCodes;

namespace Tellma.Identity.Areas.Identity.Pages.Account
{
    /// <summary>
    ///     Email one-time-code entry: the universal device floor and the recovery bootstrap. The
    ///     code is single-use, ten-minute, and bound to the browser flow that requested it;
    ///     failures render one generic message (enumeration-safe).
    /// </summary>
    /// <param name="userManager">The Identity user manager.</param>
    /// <param name="emailCodes">Code verification and re-issuance.</param>
    /// <param name="signInService">The engine sign-in (method evidence stamping).</param>
    /// <param name="auditLogger">Audit emission.</param>
    /// <param name="localizer">UI strings.</param>
    [AllowAnonymous]
    public sealed class EmailCodeModel(
        UserManager<TellmaIdentityUser> userManager,
        IEmailCodeService emailCodes,
        TellmaSignInService signInService,
        IAuditLogger auditLogger,
        IStringLocalizer<SharedResources> localizer) : PageModel
    {
        /// <summary>The submitted code.</summary>
        [BindProperty]
        public string? Code { get; set; }

        /// <summary>The email the code was requested for.</summary>
        public string? Email { get; private set; }

        /// <summary>The validated local return URL.</summary>
        public string? ReturnUrl { get; private set; }

        /// <summary>Whether this entry is part of a step-up confirmation.</summary>
        public bool StepUp { get; private set; }

        /// <summary>Whether the SSO cookie should persist ("remember me").</summary>
        public bool RememberMe { get; private set; }

        /// <summary>Renders the code-entry form.</summary>
        /// <param name="email">The email the code was requested for.</param>
        /// <param name="returnUrl">Where to return after sign-in.</param>
        /// <param name="stepUp">Whether this is a step-up confirmation.</param>
        /// <param name="rememberMe">Whether the SSO cookie should persist.</param>
        public void OnGet(string? email = null, string? returnUrl = null, bool stepUp = false, bool rememberMe = false)
        {
            Initialize(email, returnUrl, stepUp, rememberMe);
        }

        /// <summary>Verifies the code and signs the user in.</summary>
        /// <param name="email">The email the code was requested for.</param>
        /// <param name="returnUrl">Where to return after sign-in.</param>
        /// <param name="stepUp">Whether this is a step-up confirmation.</param>
        /// <param name="rememberMe">Whether the SSO cookie should persist.</param>
        /// <returns>The post-sign-in redirect, or the form with a generic error.</returns>
        public async Task<IActionResult> OnPostAsync(string? email = null, string? returnUrl = null, bool stepUp = false, bool rememberMe = false)
        {
            Initialize(email, returnUrl, stepUp, rememberMe);

            if (string.IsNullOrWhiteSpace(Email) || string.IsNullOrWhiteSpace(Code))
            {
                ModelState.AddModelError(string.Empty, localizer["InvalidCode"]);
                return Page();
            }

            TellmaIdentityUser? user = await userManager.FindByEmailAsync(Email);
            EmailCodeVerificationResult result = EmailCodeVerificationResult.Invalid;
            if (user is not null && user.LifecycleState == UserLifecycleState.Active)
            {
                result = await emailCodes.VerifyAsync(
                    user,
                    SingleUseCodePurpose.SignIn,
                    LoginFlowCookie.Get(HttpContext),
                    Code,
                    HttpContext.RequestAborted);
            }

            if (result != EmailCodeVerificationResult.Success)
            {
                await auditLogger.LogAsync(new AuditEventEntry
                {
                    Action = AuditActions.LoginFailed,
                    Subject = user?.Id,
                    IpAddress = HttpContext.Connection.RemoteIpAddress?.ToString(),
                    Outcome = "failure",
                });

                // One generic message for every failure mode.
                ModelState.AddModelError(string.Empty, localizer["InvalidCode"]);
                return Page();
            }

            await signInService.SignInAsync(
                user!,
                new SignInEvidence(AuthenticationMethods.EmailCode),
                isPersistent: RememberMe,
                HttpContext.RequestAborted);

            return LocalRedirect(ReturnUrlValidator.Sanitize(ReturnUrl, "/Identity/Account/Login"));
        }

        /// <summary>Issues a fresh code (enumeration-safe) and re-renders the form.</summary>
        /// <param name="email">The email the code was requested for.</param>
        /// <param name="returnUrl">Where to return after sign-in.</param>
        /// <param name="stepUp">Whether this is a step-up confirmation.</param>
        /// <param name="rememberMe">Whether the SSO cookie should persist.</param>
        /// <returns>The refreshed form.</returns>
        public async Task<IActionResult> OnPostResendAsync(string? email = null, string? returnUrl = null, bool stepUp = false, bool rememberMe = false)
        {
            Initialize(email, returnUrl, stepUp, rememberMe);

            if (!string.IsNullOrWhiteSpace(Email))
            {
                await emailCodes.RequestCodeAsync(
                    Email,
                    SingleUseCodePurpose.SignIn,
                    LoginFlowCookie.GetOrCreate(HttpContext),
                    HttpContext.Connection.RemoteIpAddress?.ToString(),
                    HttpContext.RequestAborted);
            }

            return Page();
        }

        /// <summary>Applies and validates the flow parameters.</summary>
        private void Initialize(string? email, string? returnUrl, bool stepUp, bool rememberMe)
        {
            Email = email;
            ReturnUrl = ReturnUrlValidator.IsValid(returnUrl) ? returnUrl : null;
            StepUp = stepUp;
            RememberMe = rememberMe;
        }
    }
}
