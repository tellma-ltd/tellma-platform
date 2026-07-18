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

namespace Tellma.Identity.Areas.Identity.Pages.Manage
{
    /// <summary>
    ///     The self-service profile page for the fields the server owns: display name and locale.
    ///     A locale change takes effect for future tokens and localized email immediately.
    /// </summary>
    /// <param name="userManager">The Identity user manager.</param>
    /// <param name="localizer">UI strings.</param>
    [Authorize]
    public sealed class IndexModel(
        UserManager<TellmaIdentityUser> userManager,
        IStringLocalizer<SharedResources> localizer) : PageModel
    {
        /// <summary>The display name emitted as the <c>name</c> claim.</summary>
        [BindProperty]
        public string? DisplayName { get; set; }

        /// <summary>The preferred language emitted as the <c>locale</c> claim.</summary>
        [BindProperty]
        public string Locale { get; set; } = "en";

        /// <summary>An informational banner, when any.</summary>
        public string? StatusMessage { get; private set; }

        /// <summary>Loads the current profile.</summary>
        /// <returns>The page.</returns>
        public async Task<IActionResult> OnGetAsync()
        {
            TellmaIdentityUser user = (await userManager.GetUserAsync(User))!;
            DisplayName = user.DisplayName;
            Locale = user.Locale;
            return Page();
        }

        /// <summary>Saves the profile changes.</summary>
        /// <returns>The refreshed page.</returns>
        public async Task<IActionResult> OnPostAsync()
        {
            TellmaIdentityUser user = (await userManager.GetUserAsync(User))!;
            user.DisplayName = DisplayName;
            user.Locale = Locale is "en" or "ar" ? Locale : "en";
            await userManager.UpdateAsync(user);

            StatusMessage = localizer["Saved"].Value;
            return Page();
        }
    }
}
