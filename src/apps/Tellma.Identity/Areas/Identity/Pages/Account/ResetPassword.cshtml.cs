// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Extensions.Options;
using Tellma.Identity.Data;
using Tellma.Identity.Data.Entities;
using Tellma.Identity.Options;
using Tellma.Identity.Services.Audit;
using Tellma.Identity.Services.Tokens;

namespace Tellma.Identity.Areas.Identity.Pages.Account
{
    /// <summary>
    ///     Completes a self-service password reset (only when passwords are enabled): the
    ///     single-use link token is redeemed, the new password set, and the security stamp bumped
    ///     so any outstanding session or token stops renewing.
    /// </summary>
    /// <param name="userManager">The Identity user manager.</param>
    /// <param name="tokens">One-time reset tokens.</param>
    /// <param name="engineOptions">The engine options (password gate).</param>
    /// <param name="auditLogger">Audit emission.</param>
    [AllowAnonymous]
    public sealed class ResetPasswordModel(
        UserManager<TellmaIdentityUser> userManager,
        IOneTimeTokenService tokens,
        IOptions<TellmaIdentityOptions> engineOptions,
        IAuditLogger auditLogger) : PageModel
    {
        /// <summary>The single-use reset token, round-tripped from the link.</summary>
        [BindProperty]
        public string? Code { get; set; }

        /// <summary>The new password.</summary>
        [BindProperty]
        public string? Password { get; set; }

        /// <summary>Whether the page has a token to work with.</summary>
        public bool IsValid { get; private set; }

        /// <summary>Renders the reset form, 404 when passwords are disabled.</summary>
        /// <param name="code">The single-use reset token.</param>
        /// <returns>The page or 404.</returns>
        public IActionResult OnGet(string? code)
        {
            if (!engineOptions.Value.EnablePasswordSignIn)
            {
                return NotFound();
            }

            Code = code;
            IsValid = !string.IsNullOrWhiteSpace(code);
            return Page();
        }

        /// <summary>Redeems the token and sets the new password.</summary>
        /// <returns>A redirect to sign in, or the page with an error.</returns>
        public async Task<IActionResult> OnPostAsync()
        {
            if (!engineOptions.Value.EnablePasswordSignIn)
            {
                return NotFound();
            }

            if (string.IsNullOrWhiteSpace(Code) || string.IsNullOrWhiteSpace(Password))
            {
                IsValid = false;
                return Page();
            }

            OneTimeTokenContext? redeemed = await tokens.RedeemAsync(
                Code, SingleUseCodePurpose.PasswordReset, HttpContext.RequestAborted);
            if (redeemed is null)
            {
                IsValid = false;
                return Page();
            }

            TellmaIdentityUser? user = await userManager.FindByIdAsync(redeemed.UserId);
            if (user is null)
            {
                IsValid = false;
                return Page();
            }

            string resetToken = await userManager.GeneratePasswordResetTokenAsync(user);
            IdentityResult result = await userManager.ResetPasswordAsync(user, resetToken, Password);
            if (!result.Succeeded)
            {
                IsValid = true;
                foreach (IdentityError error in result.Errors)
                {
                    ModelState.AddModelError(string.Empty, error.Description);
                }

                return Page();
            }

            await userManager.UpdateSecurityStampAsync(user);
            await auditLogger.LogAsync(new AuditEventEntry
            {
                Action = AuditActions.PasswordReset,
                Subject = user.Id,
                Outcome = "success",
            });

            return RedirectToPage("Login");
        }
    }
}
