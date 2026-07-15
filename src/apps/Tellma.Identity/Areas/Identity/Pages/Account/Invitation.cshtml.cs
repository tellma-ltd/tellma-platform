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
    [AllowAnonymous]
    public sealed class InvitationModel(
        IOneTimeTokenService tokens,
        UserManager<TellmaIdentityUser> userManager,
        IAuditLogger auditLogger) : PageModel
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
                // Enumeration-safe: an invalid, expired, or already-used link looks identical.
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

            ReturnUrl = ReturnUrlValidator.IsValid(redeemed.ReturnUrl) ? redeemed.ReturnUrl : null;

            // An existing user who already has a credential is only new to this distribution; the
            // membership is recorded by the caller and any existing passkey already works.
            bool hasCredential = (await userManager.GetPasskeysAsync(user)).Count > 0
                || await userManager.HasPasswordAsync(user)
                || (await userManager.GetLoginsAsync(user)).Count > 0;
            if (hasCredential)
            {
                return RedirectToPage("Login", new { returnUrl = ReturnUrl });
            }

            // Scope the upcoming credential ceremony to this user without a session.
            CredentialFlowCookie.Issue(HttpContext, user.Id);
            IsValid = true;
            return Page();
        }
    }
}
