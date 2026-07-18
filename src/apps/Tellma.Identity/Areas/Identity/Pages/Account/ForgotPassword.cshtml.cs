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
using Tellma.Identity.Services.Email;
using Tellma.Identity.Services.Tokens;

namespace Tellma.Identity.Areas.Identity.Pages.Account
{
    /// <summary>
    ///     Self-service password reset request (only when passwords are enabled). The response is
    ///     always the same whether or not the account exists (enumeration-safe); a matching active
    ///     user is emailed a single-use reset link.
    /// </summary>
    /// <param name="userManager">The Identity user manager.</param>
    /// <param name="tokens">One-time reset tokens.</param>
    /// <param name="emailQueue">The background mail dispatch queue.</param>
    /// <param name="templates">Localized message construction.</param>
    /// <param name="engineOptions">The engine options (password gate, link base).</param>
    /// <param name="auditLogger">Audit emission.</param>
    [AllowAnonymous]
    public sealed class ForgotPasswordModel(
        UserManager<TellmaIdentityUser> userManager,
        IOneTimeTokenService tokens,
        IEmailDispatcher emailQueue,
        EmailTemplateService templates,
        IOptions<TellmaIdentityOptions> engineOptions,
        IAuditLogger auditLogger) : PageModel
    {
        /// <summary>The email the reset was requested for.</summary>
        [BindProperty]
        public string? Email { get; set; }

        /// <summary>Whether the request has been submitted (drives the generic confirmation).</summary>
        public bool Submitted { get; private set; }

        /// <summary>Renders the request form, 404 when passwords are disabled.</summary>
        /// <returns>The page or 404.</returns>
        public IActionResult OnGet()
        {
            return engineOptions.Value.EnablePasswordSignIn ? Page() : NotFound();
        }

        /// <summary>Issues a reset link (enumeration-safe) and shows the generic confirmation.</summary>
        /// <returns>The confirmation page or 404.</returns>
        public async Task<IActionResult> OnPostAsync()
        {
            if (!engineOptions.Value.EnablePasswordSignIn)
            {
                return NotFound();
            }

            Submitted = true;
            if (string.IsNullOrWhiteSpace(Email))
            {
                return Page();
            }

            TellmaIdentityUser? user = await userManager.FindByEmailAsync(Email);
            if (user is not null && user.LifecycleState == UserLifecycleState.Active)
            {
                string token = await tokens.IssueAsync(
                    user.Id, SingleUseCodePurpose.PasswordReset, TimeSpan.FromHours(1), null, null, HttpContext.RequestAborted);
                string prefix = engineOptions.Value.PathBase;
                string link = new Uri(engineOptions.Value.Issuer!, $"{prefix}/Identity/Account/ResetPassword?code={Uri.EscapeDataString(token)}").AbsoluteUri;

                emailQueue.Enqueue([templates.PasswordReset(user, link)]);
                await auditLogger.LogAsync(new AuditEventEntry
                {
                    Action = AuditActions.PasswordResetRequested,
                    Subject = user.Id,
                    Outcome = "success",
                });
            }

            return Page();
        }
    }
}
