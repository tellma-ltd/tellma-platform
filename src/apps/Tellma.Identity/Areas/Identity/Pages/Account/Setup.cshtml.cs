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
using System.Security.Cryptography;
using System.Text;
using Tellma.Identity.Data;
using Tellma.Identity.Infrastructure;
using Tellma.Identity.Options;
using Tellma.Identity.Services.Audit;

namespace Tellma.Identity.Areas.Identity.Pages.Account
{
    /// <summary>
    ///     Break-glass bootstrap: the seeded administrator enters the one-time setup token
    ///     (delivered out-of-band by provisioning) to establish a credential-flow context and
    ///     enroll a passkey. The token is single-use by state — it stops working the moment the
    ///     admin has any credential.
    /// </summary>
    /// <param name="userManager">The Identity user manager.</param>
    /// <param name="options">The engine options (the setup-token hash and admin email).</param>
    /// <param name="auditLogger">Audit emission.</param>
    /// <param name="localizer">UI strings.</param>
    [AllowAnonymous]
    public sealed class SetupModel(
        UserManager<TellmaIdentityUser> userManager,
        IOptions<TellmaIdentityOptions> options,
        IAuditLogger auditLogger,
        IStringLocalizer<SharedResources> localizer) : PageModel
    {
        /// <summary>The one-time setup token.</summary>
        [BindProperty]
        public string? Token { get; set; }

        /// <summary>Verifies the setup token and starts credential enrollment for the admin.</summary>
        /// <returns>A redirect to passkey enrollment, or the form with a generic error.</returns>
        public async Task<IActionResult> OnPostAsync()
        {
            TellmaIdentityBootstrapOptions bootstrap = options.Value.Seed.Bootstrap;
            if (string.IsNullOrWhiteSpace(Token)
                || string.IsNullOrWhiteSpace(bootstrap.AdminEmail)
                || string.IsNullOrWhiteSpace(bootstrap.SetupTokenSha256))
            {
                return Fail();
            }

            // Constant-time compare against the configured token hash.
            byte[] expected = Convert.FromHexString(bootstrap.SetupTokenSha256);
            byte[] actual = SHA256.HashData(Encoding.UTF8.GetBytes(Token));
            if (!CryptographicOperations.FixedTimeEquals(expected, actual))
            {
                await auditLogger.LogAsync(new AuditEventEntry { Action = AuditActions.SetupTokenFailed, Outcome = "failure" });
                return Fail();
            }

            TellmaIdentityUser? user = await userManager.FindByEmailAsync(bootstrap.AdminEmail);
            if (user is null)
            {
                return Fail();
            }

            // Single-use by state: once the admin has any credential the token is dead.
            bool hasCredential = (await userManager.GetPasskeysAsync(user)).Count > 0
                || await userManager.HasPasswordAsync(user)
                || (await userManager.GetLoginsAsync(user)).Count > 0;
            if (hasCredential)
            {
                return Fail();
            }

            await auditLogger.LogAsync(new AuditEventEntry
            {
                Action = AuditActions.SetupTokenUsed,
                Subject = user.Id,
                Outcome = "success",
            });

            CredentialFlowCookie.Issue(HttpContext, user.Id, CredentialFlowPurpose.Recovery);
            return RedirectToPage("RegisterPasskey", new { returnUrl = Url.Page("/Manage/Passkeys", new { area = "Identity" }) });
        }

        /// <summary>Renders the generic setup failure.</summary>
        private PageResult Fail()
        {
            ModelState.AddModelError(string.Empty, localizer["SetupInvalid"].Value);
            return Page();
        }
    }
}
