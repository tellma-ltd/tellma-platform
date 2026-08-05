// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Localization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Extensions.Localization;
using Tellma.Identity.Data;
using Tellma.Identity.Infrastructure;

namespace Tellma.Identity.Areas.Identity.Pages.Manage
{
    /// <summary>
    ///     The self-service profile page for the fields the server owns: display name and locale.
    ///     A locale change takes effect for future tokens and localized email immediately, and for
    ///     the pages the user is looking at — a language setting that leaves the current session in
    ///     the old language reads as broken.
    /// </summary>
    /// <param name="userManager">The Identity user manager.</param>
    /// <param name="localizer">UI strings.</param>
    /// <param name="languages">The languages this deployment offers.</param>
    [Authorize]
    public sealed class IndexModel(
        UserManager<TellmaIdentityUser> userManager,
        IStringLocalizer<SharedResources> localizer,
        LanguageCatalog languages) : PageModel
    {
        /// <summary>The languages the picker offers.</summary>
        public IReadOnlyList<LanguageChoice> Languages => languages.Offered;

        /// <summary>The display name emitted as the <c>name</c> claim.</summary>
        [BindProperty]
        public string? DisplayName { get; set; }

        /// <summary>The preferred language emitted as the <c>locale</c> claim.</summary>
        [BindProperty]
        public string Locale { get; set; } = "en";

        /// <summary>How to address the user grammatically; null when unstated.</summary>
        [BindProperty]
        public UserGender? Gender { get; set; }

        /// <summary>An informational banner, when any.</summary>
        public string? StatusMessage { get; private set; }

        /// <summary>Loads the current profile.</summary>
        /// <returns>The page.</returns>
        public async Task<IActionResult> OnGetAsync()
        {
            TellmaIdentityUser user = (await userManager.GetUserAsync(User))!;
            DisplayName = user.DisplayName;
            Locale = user.Locale;
            Gender = user.Gender;
            StatusMessage = TempData["StatusMessage"] as string;
            return Page();
        }

        /// <summary>Saves the profile changes.</summary>
        /// <returns>The refreshed page.</returns>
        public async Task<IActionResult> OnPostAsync()
        {
            TellmaIdentityUser user = (await userManager.GetUserAsync(User))!;
            user.DisplayName = DisplayName;
            user.Locale = languages.IsOffered(Locale) ? Locale : languages.Offered[0].Culture;
            user.Gender = Gender;
            await userManager.UpdateAsync(user);

            // Apply it to the browser as well as the profile, so the change is visible where it
            // was made instead of only in the next token and the next email.
            Response.Cookies.Append(
                CookieRequestCultureProvider.DefaultCookieName,
                CookieRequestCultureProvider.MakeCookieValue(new RequestCulture(user.Locale)),
                new CookieOptions
                {
                    Expires = DateTimeOffset.UtcNow.AddYears(1),
                    IsEssential = true,
                    HttpOnly = true,
                    SameSite = SameSiteMode.Lax,
                    Secure = Request.IsHttps,
                });

            // Re-render through a redirect: the culture cookie is read by request localization at
            // the start of a request, so the saved language only takes effect on the next one.
            TempData["StatusMessage"] = localizer["Saved"].Value;
            return RedirectToPage();
        }
    }
}
