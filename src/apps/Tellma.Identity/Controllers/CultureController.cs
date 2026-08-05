// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Localization;
using Microsoft.AspNetCore.Mvc;
using Tellma.Identity.Infrastructure;

namespace Tellma.Identity.Controllers
{
    /// <summary>
    ///     Switches the language for the rest of the browser session.
    ///     <para>
    ///         A query-string culture applies to one response, which is useless mid-flow: an
    ///         authorization request bounces through several redirects and would revert at the
    ///         first hop. Writing the standard culture cookie makes the choice stick across every
    ///         later request, including redirect targets.
    ///     </para>
    /// </summary>
    /// <param name="languages">The languages this deployment offers.</param>
    [AllowAnonymous]
    public sealed class CultureController(LanguageCatalog languages) : Controller
    {
        /// <summary>Applies a language choice and returns to the page it was made on.</summary>
        /// <param name="culture">The chosen culture name.</param>
        /// <param name="returnUrl">The local page to return to.</param>
        /// <returns>A redirect back to the originating page.</returns>
        [HttpPost("identity/culture")]
        public IActionResult Set(string? culture, string? returnUrl)
        {
            // Only a language this deployment offers: the cookie is attacker-suppliable, and an
            // unoffered value would otherwise reach request localization unchallenged.
            if (languages.IsOffered(culture))
            {
                Response.Cookies.Append(
                    CookieRequestCultureProvider.DefaultCookieName,
                    CookieRequestCultureProvider.MakeCookieValue(new RequestCulture(culture!)),
                    new Microsoft.AspNetCore.Http.CookieOptions
                    {
                        // A year, so a returning user keeps their language; readable by no script,
                        // and not a credential, so it rides along on top-level navigations.
                        Expires = DateTimeOffset.UtcNow.AddYears(1),
                        IsEssential = true,
                        HttpOnly = true,
                        SameSite = Microsoft.AspNetCore.Http.SameSiteMode.Lax,
                        Secure = Request.IsHttps,
                    });
            }

            string fallback = Url.Page("/Account/Login", new { area = "Identity" })!;
            return LocalRedirect(ReturnUrlValidator.Sanitize(returnUrl, fallback));
        }
    }
}
