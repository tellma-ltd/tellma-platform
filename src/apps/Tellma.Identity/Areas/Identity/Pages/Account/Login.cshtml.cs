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
    /// <param name="signInManager">The Identity sign-in manager (passkey assertion API).</param>
    /// <param name="emailCodes">Email one-time code issuance.</param>
    /// <param name="signInService">The engine sign-in (method evidence stamping).</param>
    /// <param name="policyService">Allow-list parsing.</param>
    /// <param name="engineOptions">The engine options (password gate).</param>
    /// <param name="auditLogger">Audit emission.</param>
    /// <param name="metrics">Identity metrics.</param>
    /// <param name="localizer">UI strings.</param>
    /// <param name="authorizeReturns">Names the pending client's callbacks in this page's policy.</param>
    [AllowAnonymous]
    public sealed class LoginModel(
        SignInManager<TellmaIdentityUser> signInManager,
        IEmailCodeService emailCodes,
        TellmaSignInService signInService,
        IAuthenticationPolicyService policyService,
        IOptions<TellmaIdentityOptions> engineOptions,
        IAuditLogger auditLogger,
        IdentityMetrics metrics,
        IStringLocalizer<SharedResources> localizer,
        AuthorizeReturnFormAction authorizeReturns) : PageModel
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

        /// <summary>
        ///     The offerable-methods list exactly as the authorize redirect sent it, round-tripped
        ///     through every post so a re-rendered page keeps offering the same methods instead of
        ///     falling back to the full catalog.
        /// </summary>
        public string? MethodsRaw { get; private set; }

        /// <summary>Whether the page is confirming an existing session (step-up).</summary>
        public bool StepUp { get; private set; }

        /// <summary>The required assurance tier from the authorize redirect, round-tripped as-is.</summary>
        public string? Tier { get; private set; }

        /// <summary>
        ///     Whether the pending request demands the aal3 tier, which only a device-bound
        ///     (non-synced) passkey reaches — shown as guidance so the user picks the right
        ///     authenticator instead of bouncing off the authorization endpoint.
        /// </summary>
        public bool RequiresDeviceBoundPasskey { get; private set; }

        /// <summary>
        ///     The external providers this page offers: configured on the deployment and allowed by
        ///     the request. Already filtered, because the same set decides which buttons render and
        ///     which providers the page's <c>form-action</c> permits — computing it twice is how a
        ///     button appears that the policy will not let the browser follow.
        /// </summary>
        public IReadOnlySet<string> OfferedExternalProviders { get; private set; } = new HashSet<string>();

        /// <summary>An informational banner, when any.</summary>
        public PageStatus? StatusMessage { get; private set; }

        /// <summary>Renders the sign-in surface, or sends an already-signed-in user onward.</summary>
        /// <param name="returnUrl">Where to return after sign-in.</param>
        /// <param name="methods">The offerable methods (space-delimited), from the authorize redirect.</param>
        /// <param name="stepUp">Whether this is a step-up confirmation.</param>
        /// <param name="tier">The required assurance tier, from the authorize redirect.</param>
        /// <returns>The page, or a redirect when there is nothing left to ask for.</returns>
        public async Task<IActionResult> OnGetAsync(string? returnUrl = null, string? methods = null, bool stepUp = false, string? tier = null)
        {
            await InitializeAsync(returnUrl, methods, stepUp, tier);

            // A signed-in user has nothing to do here — unless this is a step-up, where the session
            // exists but does not yet meet what the request demands, and the whole point is to ask
            // again. Sending them on rather than re-presenting the form keeps a stale bookmark or a
            // back-button press from looking like a signed-out state.
            if (!StepUp && User.Identity?.IsAuthenticated == true)
            {
                string fallback = Url.Page("/Manage/Index", new { area = "Identity" })!;
                return LocalRedirect(ReturnUrlValidator.Sanitize(ReturnUrl, fallback));
            }

            return Page();
        }

        /// <summary>Issues an email one-time code and advances to code entry (enumeration-safe).</summary>
        /// <param name="returnUrl">Where to return after sign-in.</param>
        /// <param name="methods">The offerable methods, round-tripped.</param>
        /// <param name="stepUp">Whether this is a step-up confirmation.</param>
        /// <param name="tier">The required assurance tier, round-tripped.</param>
        /// <returns>A redirect to the code-entry page regardless of account existence.</returns>
        public async Task<IActionResult> OnPostEmailCodeAsync(
            string? returnUrl = null, string? methods = null, bool stepUp = false, string? tier = null)
        {
            await InitializeAsync(returnUrl, methods, stepUp, tier);

            if (string.IsNullOrWhiteSpace(Email))
            {
                ModelState.AddModelError(nameof(Email), localizer["Email"].Value);
                return Page();
            }

            // Enumeration-safe: the code service rate-limits, looks the user up, and dispatches the
            // mail in the background, so this returns with comparable latency whether or not the
            // account exists. The response is identical either way.
            await emailCodes.RequestCodeAsync(
                Email,
                SingleUseCodePurpose.SignIn,
                LoginFlowCookie.GetOrCreate(HttpContext),
                HttpContext.Connection.RemoteIpAddress?.ToString(),
                HttpContext.RequestAborted);

            return RedirectToPage("EmailCode", new { email = Email, returnUrl = ReturnUrl, stepUp = StepUp, rememberMe = RememberMe });
        }

        /// <summary>Completes a passkey (WebAuthn) sign-in from the assertion credential.</summary>
        /// <param name="returnUrl">Where to return after sign-in.</param>
        /// <param name="methods">The offerable methods, round-tripped.</param>
        /// <param name="stepUp">Whether this is a step-up confirmation.</param>
        /// <param name="tier">The required assurance tier, round-tripped.</param>
        /// <returns>The post-sign-in redirect, or the page with a generic error.</returns>
        public async Task<IActionResult> OnPostPasskeyAsync(
            string? returnUrl = null, string? methods = null, bool stepUp = false, string? tier = null)
        {
            await InitializeAsync(returnUrl, methods, stepUp, tier);

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
                metrics.LoginAttempt(AuthenticationMethods.Passkey, "failure", StepUp ? "step_up" : "primary");
                ModelState.AddModelError(string.Empty, localizer["PasskeyFailed"].Value);
                return Page();
            }

            if (assertion.User.LifecycleState != UserLifecycleState.Active)
            {
                ModelState.AddModelError(string.Empty, localizer["PasskeyFailed"].Value);
                return Page();
            }

            // The tier is enforced on the credential actually presented, not merely on what the
            // user owns: a user holding both a hardware key and a synced passkey (the two-passkey
            // setup recovery guidance encourages) must not satisfy aal3 with the synced one — and
            // would otherwise loop here forever, since the authorization endpoint sees a qualifying
            // credential in the inventory and sends them straight back.
            //
            // Refusing is only right when they have something better to present. A user whose
            // every passkey is synced is signed in as usual, so the authorization endpoint can
            // answer the relying party with unmet_authentication_requirements — parking them here
            // would end the flow with the client never hearing why.
            bool deviceBound = PasskeySignals.IsDeviceBound(assertion.Passkey);
            if (RequiresDeviceBoundPasskey && !deviceBound && await HasDeviceBoundPasskeyAsync(assertion.User))
            {
                await auditLogger.LogAsync(new AuditEventEntry
                {
                    Action = AuditActions.LoginFailed,
                    Subject = assertion.User.Id,
                    IpAddress = HttpContext.Connection.RemoteIpAddress?.ToString(),
                    Outcome = "failure",
                    DetailsJson = System.Text.Json.JsonSerializer.Serialize(
                        new { method = AuthenticationMethods.Passkey, reason = "not_device_bound", tier = Tier }),
                });
                metrics.LoginAttempt(AuthenticationMethods.Passkey, "failure", StepUp ? "step_up" : "primary");
                ModelState.AddModelError(string.Empty, localizer["DeviceBoundPasskeyRequired"].Value);
                return Page();
            }

            await signInService.SignInAsync(
                assertion.User,
                new SignInEvidence(AuthenticationMethods.Passkey, deviceBound),
                isPersistent: RememberMe,
                HttpContext.RequestAborted);

            string fallback = Url.Page("/Account/Login", new { area = "Identity" })!;
            return LocalRedirect(ReturnUrlValidator.Sanitize(ReturnUrl, fallback));
        }

        /// <summary>Whether the user has registered any device-bound (non-synced) passkey.</summary>
        private async Task<bool> HasDeviceBoundPasskeyAsync(TellmaIdentityUser user)
        {
            IList<UserPasskeyInfo> passkeys = await signInManager.UserManager.GetPasskeysAsync(user);
            return passkeys.Any(PasskeySignals.IsDeviceBound);
        }

        /// <summary>Applies and validates the flow parameters.</summary>
        /// <returns>A task that completes once the response's policy has been settled.</returns>
        private async Task InitializeAsync(string? returnUrl, string? methods, bool stepUp, string? tier)
        {
            ReturnUrl = ReturnUrlValidator.IsValid(returnUrl) ? returnUrl : null;
            StepUp = stepUp;
            Tier = string.Equals(tier, AcrTiers.Aal3, StringComparison.Ordinal) ? tier : null;
            RequiresDeviceBoundPasskey = Tier is not null;

            // An unparseable list offers nothing rather than everything: the authorize endpoint
            // rejects it before redirecting, so reaching here means the two disagree, and falling
            // back to the full catalog would invite methods the tenant may have disallowed.
            IReadOnlyList<string> offered = AuthenticationMethods.All;
            if (!policyService.TryParseAllowedMethods(methods, out IReadOnlyList<string>? parsed))
            {
                offered = [];
            }
            else if (parsed is not null)
            {
                offered = parsed;
                MethodsRaw = methods;
            }

            // Passwords are off by default; the page never offers what the deployment disables.
            if (!engineOptions.Value.EnablePasswordSignIn)
            {
                offered = [.. offered.Where(static m => m != AuthenticationMethods.Password)];
            }

            Methods = offered;

            HashSet<string> providers = [];
            if (engineOptions.Value.ExternalProviders.Google.IsConfigured
                && offered.Contains(AuthenticationMethods.Google, StringComparer.Ordinal))
            {
                providers.Add("Google");
            }

            if (engineOptions.Value.ExternalProviders.Microsoft.IsConfigured
                && offered.Contains(AuthenticationMethods.Microsoft, StringComparer.Ordinal))
            {
                providers.Add("Microsoft");
            }

            OfferedExternalProviders = providers;

            // Starting a federated sign-in is a form submission answered with a redirect off this
            // origin, and form-action is enforced on this page's policy across every hop of it.
            ExternalProviderFormAction.Allow(HttpContext, providers);

            // So is finishing one: a sign-in that resumes a pending authorization is answered with
            // a redirect through the authorization endpoint and on to the client's own callback.
            // Both widenings apply to this one page, which is why they accumulate.
            await authorizeReturns.AllowAsync(HttpContext, ReturnUrl, HttpContext.RequestAborted);
            StatusMessage = StepUp ? PageStatus.Info(localizer["ConfirmItsYou"].Value) : null;
        }
    }
}
