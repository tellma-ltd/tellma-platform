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
    ///         A declared link attempt's session proves ownership by itself — the visitor is signed
    ///         in as the account they named when starting the challenge, and the provider's address
    ///         is recorded beside the link as a label, nothing more. Following an invitation, the
    ///         proof <em>is</em> the address: the provider must assert it as verified and it must be
    ///         the one invited. The verified-email requirement therefore applies to that branch
    ///         alone, where it is what stands between an unverified assertion and someone else's
    ///         account.
    ///     </para>
    ///     <para>
    ///         A link attempt declares itself: the challenge carries the initiating account, and
    ///         only that account's live session may complete it. An identity already held by a
    ///         different account is then reported as the conflict it is, rather than signing the
    ///         visitor into that account behind a page that looks like it merely connected
    ///         something. A plain sign-in carries no such mark, and completing one as a different
    ///         account is the same deliberate switch every sign-in method allows — onto a fresh
    ///         session, never a continued one.
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
        /// <summary>
        ///     The challenge-properties item that carries a link attempt's initiating account
        ///     through the provider round trip. Present only when the challenge was started with
        ///     <see cref="OnPostLink" />, so the callback can tell a link from a sign-in without
        ///     guessing from whatever session is present when the provider answers.
        /// </summary>
        public const string LinkForProperty = "LinkFor";

        /// <summary>An error to display when the flow could not complete.</summary>
        public string? Error { get; private set; }

        /// <summary>Where the visitor was headed, offered back to them when the flow refuses.</summary>
        public string? ReturnUrl { get; private set; }

        /// <summary>Starts the external sign-in challenge.</summary>
        /// <param name="provider">The external authentication scheme (Google, Microsoft).</param>
        /// <param name="returnUrl">Where to return after sign-in.</param>
        /// <returns>A challenge to the external provider.</returns>
        public IActionResult OnPost(string provider, string? returnUrl = null)
        {
            return ChallengeProvider(provider, returnUrl, linkFor: null);
        }

        /// <summary>Starts the external linking challenge for the signed-in account.</summary>
        /// <param name="provider">The external authentication scheme (Google, Microsoft).</param>
        /// <param name="returnUrl">Where to return after linking.</param>
        /// <returns>A challenge to the external provider, carrying the link intent.</returns>
        public IActionResult OnPostLink(string provider, string? returnUrl = null)
        {
            // Whose link attempt this is travels inside the challenge state rather than being
            // read back from the session later: the provider round trip can outlive the session
            // that started it, and a link completed against whatever session remains — or none —
            // is how an identity ends up attached to an account nobody chose.
            return SignedInUserId() is { } initiator
                ? ChallengeProvider(provider, returnUrl, initiator)
                : Fail();
        }

        /// <summary>Handles the provider callback: sign in an existing link, or link with ownership proof.</summary>
        /// <param name="returnUrl">Where to return after sign-in.</param>
        /// <param name="remoteError">An error reported by the provider, when any.</param>
        /// <returns>The post-sign-in redirect, or the page with a generic error.</returns>
        public async Task<IActionResult> OnGetCallbackAsync(string? returnUrl = null, string? remoteError = null)
        {
            string safeReturn = ReturnUrlValidator.Sanitize(returnUrl, Url.Page("/Account/Login", new { area = "Identity" })!);
            ReturnUrl = safeReturn;
            if (remoteError is not null)
            {
                return Fail();
            }

            ExternalLoginInfo? info = await signInManager.GetExternalLoginInfoAsync();
            if (info is null)
            {
                return Fail();
            }

            // Consumed on first read. The engine sign-in does not delete the assertion the way
            // the stock manager's sign-in does, so left alive it would remain replayable from
            // this GET for its whole cookie lifetime — able to re-mint the session it produced
            // even after a global sign-out, or to complete a refused attempt on whatever session
            // exists by the time the page is refreshed.
            await HttpContext.SignOutAsync(IdentityConstants.ExternalScheme);

            string method = MapMethod(info.LoginProvider);

            // A link attempt names its account in the challenge state; a sign-in carries no such
            // mark. The distinction cannot be read from the current session — the round trip can
            // outlive it, and step-up renders sign-in buttons to a browser that has one.
            string? linkFor = info.AuthenticationProperties is { } challenge
                && challenge.Items.TryGetValue(LinkForProperty, out string? initiator)
                    ? initiator
                    : null;

            // 1. Already linked by (provider, key): sign in as whoever holds it.
            TellmaIdentityUser? linked = await userManager.FindByLoginAsync(info.LoginProvider, info.ProviderKey);
            if (linked is not null)
            {
                // Unless this is a link attempt and the identity is already another account's.
                // The conflict outranks the holder's lifecycle: whoever holds it, the answer to
                // "connect this to me" is that it is taken, and the generic failure's advice to
                // try again could never come true.
                if (linkFor is not null && !string.Equals(linkFor, linked.Id, StringComparison.Ordinal))
                {
                    return await OwnedByAnotherAccountAsync(linkFor);
                }

                if (linked.LifecycleState != UserLifecycleState.Active)
                {
                    return Fail();
                }

                // A link attempt is completed only by the session that started it. Anything else
                // — the session ended at the provider's account chooser, or another tab changed
                // whose it is — and signing in whoever the identity resolves to would hand the
                // browser an account nobody chose, behind a page that reads like the connection
                // having worked. A sign-in, by contrast, completes for the identity's owner no
                // matter what session it lands on: switching accounts is what sign-in methods do,
                // and the engine answers a switch with a fresh session id.
                return linkFor is not null
                    && !string.Equals(SignedInUserId(), linkFor, StringComparison.Ordinal)
                        ? Fail()
                        : await CompleteSignInAsync(linked, method, safeReturn);
            }

            // 2. New external identity: link only against a proof of local ownership.
            string? providerEmail = info.Principal.FindFirstValue(ClaimTypes.Email);
            (TellmaIdentityUser? owner, OwnershipProof proof) = await ResolveOwnerAsync(providerEmail, linkFor);
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

        /// <summary>Builds the provider challenge, stamping the link initiator when there is one.</summary>
        /// <param name="provider">The external authentication scheme.</param>
        /// <param name="returnUrl">Where to return after the callback completes.</param>
        /// <param name="linkFor">The account a link attempt is for, or null for a sign-in.</param>
        /// <returns>A challenge to the external provider.</returns>
        private ChallengeResult ChallengeProvider(string provider, string? returnUrl, string? linkFor)
        {
            string safeReturn = ReturnUrlValidator.Sanitize(returnUrl, Url.Page("/Account/Login", new { area = "Identity" })!);
            string redirectUrl = Url.Page("ExternalLogin", pageHandler: "Callback", values: new { returnUrl = safeReturn })!;
            AuthenticationProperties properties = signInManager.ConfigureExternalAuthenticationProperties(provider, redirectUrl);
            if (linkFor is not null)
            {
                properties.Items[LinkForProperty] = linkFor;
            }

            return Challenge(properties, provider);
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

            /// <summary>
            ///     The declared link attempt's own session: the visitor is signed in as the
            ///     account they named when starting the challenge.
            /// </summary>
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
        /// <param name="linkFor">The account the challenge declared a link attempt for, if any.</param>
        /// <returns>The account and its proof, or null and <see cref="OwnershipProof.None" />.</returns>
        private async Task<(TellmaIdentityUser? Owner, OwnershipProof Proof)> ResolveOwnerAsync(
            string? providerEmail, string? linkFor)
        {
            // A declared link attempt is the only path on which a session proves ownership, and
            // only the session that made the declaration. An undeclared callback never links to
            // whatever session happens to be present: ambient state does not decide whose account
            // gains a credential.
            if (linkFor is not null)
            {
                return string.Equals(SignedInUserId(), linkFor, StringComparison.Ordinal)
                    ? (await userManager.GetUserAsync(User), OwnershipProof.Session)
                    : (null, OwnershipProof.None);
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
        ///     Renders the refusal to move an identity that another account already holds, and
        ///     leaves a trace of it. The message says what happened without naming that account:
        ///     the reader authenticated as this provider identity a moment ago, so the conflict is
        ///     theirs to resolve, but which local account holds it is not theirs to learn.
        /// </summary>
        /// <param name="subject">The account whose link attempt was refused.</param>
        /// <returns>The rendered refusal.</returns>
        private async Task<PageResult> OwnedByAnotherAccountAsync(string subject)
        {
            // The one refusal here that proves a cross-account collision, so the one the audit
            // stream must not lose: a run of these against one subject is somebody probing which
            // provider identities lead where.
            await auditLogger.LogAsync(new AuditEventEntry
            {
                Action = AuditActions.ExternalLoginConflict,
                Subject = subject,
                Outcome = "failure",
            });

            Error = localizer["ExternalLoginOwnedByAnotherAccount"].Value;
            return Page();
        }
    }
}
