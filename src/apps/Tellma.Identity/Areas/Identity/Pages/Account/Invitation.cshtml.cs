// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Tellma.Identity.Data;
using Tellma.Identity.Data.Entities;
using Tellma.Identity.Infrastructure;
using Tellma.Identity.Services.Audit;
using Tellma.Identity.Services.Invitations;
using Tellma.Identity.Services.Tokens;

namespace Tellma.Identity.Areas.Identity.Pages.Account
{
    /// <summary>
    ///     The invitation accept flow. Opening the single-use link proves control of the mailbox,
    ///     so the page confirms the user's email and establishes a credential-flow context, then
    ///     sends the user to enroll a passkey (or link an external login / set a password). An
    ///     existing user with credentials goes straight through.
    /// </summary>
    /// <param name="tokens">One-time invitation tokens.</param>
    /// <param name="userManager">The Identity user manager.</param>
    /// <param name="auditLogger">Audit emission.</param>
    /// <param name="returnUrls">Re-checks the stored destination against the client's registration.</param>
    [AllowAnonymous]
    public sealed class InvitationModel(
        IOneTimeTokenService tokens,
        UserManager<TellmaIdentityUser> userManager,
        IAuditLogger auditLogger,
        InvitationReturnUrlValidator returnUrls) : PageModel
    {
        /// <summary>Whether the invitation link resolved to a pending user.</summary>
        public bool IsValid { get; private set; }

        /// <summary>The validated post-accept destination.</summary>
        public string? ReturnUrl { get; private set; }

        /// <summary>Redeems the invitation link and prepares credential enrollment.</summary>
        /// <param name="code">The single-use invitation token.</param>
        /// <returns>The page (invalid or ready-to-enroll), or a redirect for an existing user.</returns>
        public async Task<IActionResult> OnGetAsync(string? code)
        {
            OneTimeTokenContext? redeemed = code is null
                ? null
                : await tokens.RedeemAsync(code, SingleUseCodePurpose.Invitation, HttpContext.RequestAborted);
            if (redeemed is null)
            {
                // A link that was already used is the one case worth telling apart, because it is
                // overwhelmingly the legitimate recipient coming back to the only address they
                // kept — the email — after finishing setup and not bookmarking the app. Sending
                // them on to sign in is the whole point of having recorded where they were going.
                //
                // It does mean "used" is distinguishable from "expired" or "forged", which are
                // still identical to each other. The trade is deliberate: the token's secret is
                // verified before this answers, so only someone who already held the link learns
                // anything, and consuming it proved control of the mailbox in the first place.
                if (await ConsumedRedirectAsync(code) is { } onward)
                {
                    return onward;
                }

                // Enumeration-safe: an invalid, expired, or unknown link looks identical.
                IsValid = false;
                return Page();
            }

            TellmaIdentityUser? user = await userManager.FindByIdAsync(redeemed.UserId);
            if (user is null)
            {
                IsValid = false;
                return Page();
            }

            // Opening the link proves mailbox control.
            if (!user.EmailConfirmed)
            {
                user.EmailConfirmed = true;
                await userManager.UpdateAsync(user);
            }

            await auditLogger.LogAsync(new AuditEventEntry
            {
                Action = AuditActions.InvitationAccepted,
                Subject = user.Id,
                Outcome = "success",
            });

            // Re-checked rather than trusted because it was checked once at issuance: a
            // registration can change in the days an invitation is valid for, and the answer that
            // matters is the one true when the user actually arrives.
            ReturnUrl = await returnUrls.IsAllowedAsync(
                redeemed.ReturnUrl, redeemed.CreatedByClientId, HttpContext.RequestAborted)
                ? redeemed.ReturnUrl
                : null;

            // An existing user who already has a credential is only new to this distribution; the
            // membership is recorded by the caller and any existing passkey already works.
            if (await HasCredentialAsync(user))
            {
                return RedirectToPage("Login", new { returnUrl = ReturnUrl });
            }

            // Scope the upcoming credential ceremony to this user without a session, carrying the
            // destination with it: the enrollment page must not take an absolute address from its
            // own query string, where anything could have put it.
            CredentialFlowCookie.Issue(HttpContext, user.Id, CredentialFlowPurpose.Invitation, ReturnUrl);

            // This page carries the enrollment form, and a browser checks form-action against the
            // policy of the document the form is in — not the policy on the response that ends up
            // redirecting. Naming the destination here is therefore the only thing that lets the
            // browser follow the enrollment through to the tenant; without it the submission
            // succeeds server-side and the page simply never moves.
            AllowFormActionTo(ReturnUrl);
            IsValid = true;
            return Page();
        }

        /// <summary>
        ///     Sends the holder of an already-used invitation on to sign in, when the link is
        ///     genuinely one that was redeemed and the account it belongs to can now get in.
        /// </summary>
        /// <param name="code">The single-use invitation token.</param>
        /// <returns>The redirect, or null when there is nothing better to offer than the
        ///     generic page.</returns>
        private async Task<IActionResult?> ConsumedRedirectAsync(string? code)
        {
            if (code is null
                || await tokens.FindConsumedAsync(
                    code, SingleUseCodePurpose.Invitation, HttpContext.RequestAborted) is not { } consumed)
            {
                return null;
            }

            // Only for an account that can actually get in. One that was invited, had its link
            // consumed, and still holds no credential has nothing to sign in with, and sending it
            // to a login page would be a dead end wearing a different hat.
            TellmaIdentityUser? user = await userManager.FindByIdAsync(consumed.UserId);
            if (user is null || !await HasCredentialAsync(user))
            {
                return null;
            }

            // Re-checked against the client's registration for the same reason the redeem path
            // does it: the invitation may be days old and registrations change. An invitation that
            // named nowhere still gets the sign-in page — that alone is the difference between a
            // way back in and a dead end.
            string? destination = await returnUrls.IsAllowedAsync(
                consumed.ReturnUrl, consumed.CreatedByClientId, HttpContext.RequestAborted)
                ? consumed.ReturnUrl
                : null;

            return RedirectToPage("Login", new { returnUrl = destination });
        }

        /// <summary>Widens this response's form-action policy to an off-origin destination.</summary>
        private void AllowFormActionTo(string? destination)
        {
            if (destination is not null && !ReturnUrlValidator.IsValid(destination))
            {
                CspFormAction.Allow(HttpContext, [destination]);
            }
        }

        /// <summary>Whether a user holds any credential they could sign in with.</summary>
        private async Task<bool> HasCredentialAsync(TellmaIdentityUser user)
        {
            return (await userManager.GetPasskeysAsync(user)).Count > 0
                || await userManager.HasPasswordAsync(user)
                || (await userManager.GetLoginsAsync(user)).Count > 0;
        }
    }
}
