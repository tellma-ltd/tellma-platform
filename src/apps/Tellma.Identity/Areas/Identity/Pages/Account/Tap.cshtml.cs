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
using Tellma.Identity.Infrastructure;
using Tellma.Identity.Services.Tap;

namespace Tellma.Identity.Areas.Identity.Pages.Account
{
    /// <summary>
    ///     Admin-assisted recovery entry: the user redeems a Temporary Access Pass to establish a
    ///     credential-flow context and is sent straight to passkey enrollment. The pass never
    ///     yields a full session — it only authorizes enrolling a new credential.
    /// </summary>
    /// <param name="tapService">Temporary Access Pass redemption.</param>
    /// <param name="userManager">The Identity user manager.</param>
    /// <param name="localizer">UI strings.</param>
    [AllowAnonymous]
    public sealed class TapModel(
        ITemporaryAccessPassService tapService,
        UserManager<TellmaIdentityUser> userManager,
        IStringLocalizer<SharedResources> localizer) : PageModel
    {
        /// <summary>The email identifying the account.</summary>
        [BindProperty]
        public string? Email { get; set; }

        /// <summary>The access pass.</summary>
        [BindProperty]
        public string? Pass { get; set; }

        /// <summary>Redeems the pass and starts credential enrollment.</summary>
        /// <returns>A redirect to passkey enrollment, or the form with a generic error.</returns>
        public async Task<IActionResult> OnPostAsync()
        {
            if (string.IsNullOrWhiteSpace(Email) || string.IsNullOrWhiteSpace(Pass))
            {
                ModelState.AddModelError(string.Empty, localizer["TapInvalid"].Value);
                return Page();
            }

            string? userId = await tapService.RedeemAsync(Email, Pass, HttpContext.RequestAborted);
            if (userId is null)
            {
                ModelState.AddModelError(string.Empty, localizer["TapInvalid"].Value);
                return Page();
            }

            // Prove ownership; the mailbox check is implicit in the operator's out-of-band delivery.
            TellmaIdentityUser user = (await userManager.FindByIdAsync(userId))!;
            if (!user.EmailConfirmed)
            {
                user.EmailConfirmed = true;
                await userManager.UpdateAsync(user);
            }

            CredentialFlowCookie.Issue(HttpContext, userId);
            return RedirectToPage("RegisterPasskey", new { returnUrl = "/Identity/Manage/Passkeys" });
        }
    }
}
