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
    ///     subject <c>(LoginProvider, ProviderKey)</c> signs in directly. A new external identity is
    ///     never auto-merged by email: linking requires proof that the visitor owns the local
    ///     account, and the two proofs it accepts are trusted for different reasons.
    ///     <para>
    ///         An authenticated session proves ownership by itself — the visitor is already signed in
    ///         as the account they are attaching the identity to, and the provider's address is
    ///         recorded beside the link as a label, nothing more. Following an invitation, the proof
    ///         <em>is</em> the address: the provider must assert it as verified and it must be the
    ///         one invited. The verified-email requirement therefore applies to that branch alone,
    ///         where it is what stands between an unverified assertion and someone else's account.
    ///     </para>
    ///     <para>
    ///         A session is never exchanged for another here. An identity already held by a
    ///         different account is reported as the conflict it is, rather than signing the visitor
    ///         into that account behind a page that looks like it merely connected something.
    ///     </para>
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

            // 1. Already linked by (provider, key): sign in as whoever holds it.
            TellmaIdentityUser? linked = await userManager.FindByLoginAsync(info.LoginProvider, info.ProviderKey);
            if (linked is not null)
            {
                if (linked.LifecycleState != UserLifecycleState.Active)
                {
                    return Fail();
                }

                // Unless this browser is already signed in as someone else. A session belongs to
                // the person who established it, and this callback is not a place to change whose
                // it is: someone connecting a provider from their account pages, with an identity
                // that turns out to belong to another account, would be signed into that account
                // and returned to the same pages — which reads exactly like the connection having
                // worked. Everything they did next would land on an account they did not choose
                // and cannot see they are on.
                return SignedInUserId() is { } current
                    && !string.Equals(current, linked.Id, StringComparison.Ordinal)
                        ? OwnedByAnotherAccount()
                        : await CompleteSignInAsync(linked, method, safeReturn);
            }

            // 2. New external identity: link only against a proof of local ownership.
            string? providerEmail = info.Principal.FindFirstValue(ClaimTypes.Email);
            (TellmaIdentityUser? owner, OwnershipProof proof) = await ResolveOwnerAsync(providerEmail);
            if (owner is null)
            {
                // Never auto-merge by email without proof — this is the pre-hijacking guard. Saying
                // so plainly costs nothing: the only person who can read the message is whoever just
                // signed in to that provider account, so it tells them about themselves.
                return NotLinked();
            }

            // Only where the address is the proof. A provider that asserts no verification cannot be
            // taken at its word about an invited mailbox, so that link is refused; the same provider
            // links fine from a signed-in session, where the session is the proof.
            //
            // Case-insensitively, and that matters: the claim is mapped from a JSON boolean and
            // arrives rendered by the framework as "True".
            bool emailVerified = string.Equals(
                info.Principal.FindFirstValue("email_verified"), "true", StringComparison.OrdinalIgnoreCase);
            if (proof == OwnershipProof.InvitationEmail && !emailVerified)
            {
                return Fail();
            }

            // Record which account was linked, not which provider it came from. The store keeps
            // one free-text field per link and the framework fills it with the scheme's display
            // name — "Google" — which the account page already knows from the scheme itself. The
            // address is the only thing here that a user with two Google accounts needs to see,
            // and this is the sole slot Identity offers to put it in.
            UserLoginInfo credited = new(
                info.LoginProvider, info.ProviderKey, providerEmail ?? info.ProviderDisplayName);

            IdentityResult link = await userManager.AddLoginAsync(owner, credited);
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

        /// <summary>What proved that the visitor owns the local account being linked to.</summary>
        private enum OwnershipProof
        {
            /// <summary>Nothing did, and no account was resolved.</summary>
            None,

            /// <summary>An authenticated session: the visitor is signed in as that account.</summary>
            Session,

            /// <summary>An invitation link, whose proof is the address the provider asserts.</summary>
            InvitationEmail,
        }

        /// <summary>
        ///     Resolves the local account an external identity may link to, and how that account's
        ///     ownership was proved — the two proofs are trusted for different reasons, so the
        ///     caller has to know which one it got.
        /// </summary>
        /// <param name="providerEmail">The address the provider asserts, when it asserts one.</param>
        /// <returns>The account and its proof, or null and <see cref="OwnershipProof.None" />.</returns>
        private async Task<(TellmaIdentityUser? Owner, OwnershipProof Proof)> ResolveOwnerAsync(string? providerEmail)
        {
            // An authenticated user is linking a new provider from Account & Security.
            if (User.Identity?.IsAuthenticated == true)
            {
                return (await userManager.GetUserAsync(User), OwnershipProof.Session);
            }

            // Only the invitation flow may link an external login (§8.4): the single-use invitation
            // link is the email-ownership proof. A recovery or bootstrap context has a passkey-only
            // exit (§10.3–§10.4), so it must never yield a signed-in session by linking a social IdP.
            CredentialFlowContext? flow = CredentialFlowCookie.Read(HttpContext);
            if (flow is not { Purpose: CredentialFlowPurpose.Invitation } || providerEmail is null)
            {
                return (null, OwnershipProof.None);
            }

            TellmaIdentityUser? user = await userManager.FindByIdAsync(flow.UserId);
            return user is not null
                && string.Equals(user.Email, providerEmail, StringComparison.OrdinalIgnoreCase)
                ? (user, OwnershipProof.InvitationEmail)
                : (null, OwnershipProof.None);
        }

        /// <summary>The account this browser is signed in as, when it is signed in at all.</summary>
        /// <returns>The user id, or null for an anonymous visitor.</returns>
        private string? SignedInUserId()
        {
            return User.Identity?.IsAuthenticated == true ? userManager.GetUserId(User) : null;
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
            Error = localizer["ExternalLoginFailed"].Value;
            return Page();
        }

        /// <summary>Renders the refusal to attach an external identity to an unproved account.</summary>
        private PageResult NotLinked()
        {
            Error = localizer["ExternalLoginNotLinked"].Value;
            return Page();
        }

        /// <summary>
        ///     Renders the refusal to move an identity that another account already holds. Says
        ///     what happened without naming that account: the reader authenticated as this provider
        ///     identity a moment ago, so the conflict is theirs to resolve, but which local account
        ///     holds it is not theirs to learn.
        /// </summary>
        private PageResult OwnedByAnotherAccount()
        {
            Error = localizer["ExternalLoginOwnedByAnotherAccount"].Value;
            return Page();
        }
    }
}
