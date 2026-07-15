// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Extensions.Localization;
using Microsoft.Extensions.Options;
using Tellma.Identity.Data;
using Tellma.Identity.Data.Entities;
using Tellma.Identity.Infrastructure;
using Tellma.Identity.Options;
using Tellma.Identity.Services.Audit;
using Tellma.Identity.Services.AuthenticationPolicy;
using Tellma.Identity.Services.EmailCodes;

namespace Tellma.Identity.Areas.Identity.Pages.Account
{
    /// <summary>
    ///     The central sign-in page. It offers only the methods the pending request allows
    ///     (advisory UI state — enforcement happens at the authorization endpoint on the pushed
    ///     request when the browser returns) and doubles as the step-up/re-authentication surface.
    /// </summary>
    /// <param name="userManager">The Identity user manager.</param>
    /// <param name="signInManager">The Identity sign-in manager (passkey assertion API).</param>
    /// <param name="emailCodes">Email one-time code issuance.</param>
    /// <param name="signInService">The engine sign-in (method evidence stamping).</param>
    /// <param name="policyService">Allow-list parsing.</param>
    /// <param name="engineOptions">The engine options (password gate).</param>
    /// <param name="auditLogger">Audit emission.</param>
    /// <param name="localizer">UI strings.</param>
    [AllowAnonymous]
    public sealed class LoginModel(
        UserManager<TellmaIdentityUser> userManager,
        SignInManager<TellmaIdentityUser> signInManager,
        IEmailCodeService emailCodes,
        TellmaSignInService signInService,
        IAuthenticationPolicyService policyService,
        IOptions<TellmaIdentityOptions> engineOptions,
        IAuditLogger auditLogger,
        IStringLocalizer<SharedResources> localizer) : PageModel
    {
        /// <summary>The email the user typed.</summary>
        [BindProperty]
        public string? Email { get; set; }

        /// <summary>The passkey assertion credential JSON posted by the browser ceremony.</summary>
        [BindProperty]
        public string? Credential { get; set; }

        /// <summary>Whether the SSO cookie should persist across browser sessions.</summary>
        [BindProperty]
        public bool RememberMe { get; set; }

        /// <summary>The validated local return URL.</summary>
        public string? ReturnUrl { get; private set; }

        /// <summary>The methods this page may offer.</summary>
        public IReadOnlyList<string> Methods { get; private set; } = AuthenticationMethods.All;

        /// <summary>Whether the page is confirming an existing session (step-up).</summary>
        public bool StepUp { get; private set; }

        /// <summary>The external providers configured on this deployment (offered when allowed).</summary>
        public IReadOnlySet<string> ConfiguredExternalProviders { get; private set; } = new HashSet<string>();

        /// <summary>An informational banner, when any.</summary>
        public string? StatusMessage { get; private set; }

        /// <summary>Renders the sign-in surface.</summary>
        /// <param name="returnUrl">Where to return after sign-in.</param>
        /// <param name="methods">The offerable methods (space-delimited), from the authorize redirect.</param>
        /// <param name="stepUp">Whether this is a step-up confirmation.</param>
        public void OnGet(string? returnUrl = null, string? methods = null, bool stepUp = false)
        {
            Initialize(returnUrl, methods, stepUp);
        }

        /// <summary>Issues an email one-time code and advances to code entry (enumeration-safe).</summary>
        /// <param name="returnUrl">Where to return after sign-in.</param>
        /// <param name="methods">The offerable methods, round-tripped.</param>
        /// <param name="stepUp">Whether this is a step-up confirmation.</param>
        /// <returns>A redirect to the code-entry page regardless of account existence.</returns>
        public async Task<IActionResult> OnPostEmailCodeAsync(string? returnUrl = null, string? methods = null, bool stepUp = false)
        {
            Initialize(returnUrl, methods, stepUp);

            if (string.IsNullOrWhiteSpace(Email))
            {
                ModelState.AddModelError(nameof(Email), localizer["Email"].Value);
                return Page();
            }

            string flowBinding = LoginFlowCookie.GetOrCreate(HttpContext);
            TellmaIdentityUser? user = await userManager.FindByEmailAsync(Email);
            if (user is not null && user.LifecycleState == UserLifecycleState.Active)
            {
                await emailCodes.IssueAsync(
                    user,
                    SingleUseCodePurpose.SignIn,
                    flowBinding,
                    HttpContext.Connection.RemoteIpAddress?.ToString(),
                    HttpContext.RequestAborted);
            }

            // Identical response whether or not the account exists.
            return RedirectToPage("EmailCode", new { email = Email, returnUrl = ReturnUrl, stepUp = StepUp, rememberMe = RememberMe });
        }

        /// <summary>Completes a passkey (WebAuthn) sign-in from the assertion credential.</summary>
        /// <param name="returnUrl">Where to return after sign-in.</param>
        /// <param name="stepUp">Whether this is a step-up confirmation.</param>
        /// <returns>The post-sign-in redirect, or the page with a generic error.</returns>
        public async Task<IActionResult> OnPostPasskeyAsync(string? returnUrl = null, bool stepUp = false)
        {
            Initialize(returnUrl, methods: null, stepUp);

            if (string.IsNullOrWhiteSpace(Credential))
            {
                ModelState.AddModelError(string.Empty, localizer["PasskeyFailed"].Value);
                return Page();
            }

            // Validate the assertion without letting the framework sign in (PasskeySignInAsync
            // bypasses our method-evidence stamping); the engine sign-in records the method and
            // derives assurance from the credential's device-bound signal.
            PasskeyAssertionResult<TellmaIdentityUser> assertion =
                await signInManager.PerformPasskeyAssertionAsync(Credential);
            if (!assertion.Succeeded || assertion.User is null || assertion.Passkey is null)
            {
                await auditLogger.LogAsync(new AuditEventEntry { Action = AuditActions.LoginFailed, Outcome = "failure" });
                ModelState.AddModelError(string.Empty, localizer["PasskeyFailed"].Value);
                return Page();
            }

            if (assertion.User.LifecycleState != UserLifecycleState.Active)
            {
                ModelState.AddModelError(string.Empty, localizer["PasskeyFailed"].Value);
                return Page();
            }

            // Device-bound (hardware, non-synced) is the backup-ELIGIBILITY signal: a credential
            // that cannot be backed up. Backup state alone would misclassify a syncable passkey
            // that simply has not synced yet, over-asserting the aal3 tier.
            bool deviceBound = !assertion.Passkey.IsBackupEligible;
            await signInService.SignInAsync(
                assertion.User,
                new SignInEvidence(AuthenticationMethods.Passkey, deviceBound),
                isPersistent: RememberMe,
                HttpContext.RequestAborted);

            return LocalRedirect(ReturnUrlValidator.Sanitize(ReturnUrl, "/Identity/Account/Login"));
        }

        /// <summary>Applies and validates the flow parameters.</summary>
        private void Initialize(string? returnUrl, string? methods, bool stepUp)
        {
            ReturnUrl = ReturnUrlValidator.IsValid(returnUrl) ? returnUrl : null;
            StepUp = stepUp;

            IReadOnlyList<string> offered = AuthenticationMethods.All;
            if (policyService.TryParseAllowedMethods(methods, out IReadOnlyList<string>? parsed) && parsed is not null)
            {
                offered = parsed;
            }

            // Passwords are off by default; the page never offers what the deployment disables.
            if (!engineOptions.Value.EnablePasswordSignIn)
            {
                offered = [.. offered.Where(static m => m != AuthenticationMethods.Password)];
            }

            Methods = offered;

            HashSet<string> providers = [];
            if (engineOptions.Value.ExternalProviders.Google.IsConfigured)
            {
                providers.Add("Google");
            }

            if (engineOptions.Value.ExternalProviders.Microsoft.IsConfigured)
            {
                providers.Add("Microsoft");
            }

            ConfiguredExternalProviders = providers;
            StatusMessage = StepUp ? localizer["ConfirmItsYou"].Value : null;
        }
    }
}
