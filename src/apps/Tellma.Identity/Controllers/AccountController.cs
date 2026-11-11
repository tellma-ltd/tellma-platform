// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Net.Http.Headers;
using Tellma.Identity.Infrastructure;

namespace Tellma.Identity.Controllers
{
    /// <summary>
    ///     The account entry: one stable address an application links to, which opens the account
    ///     page for a named section.
    ///     <para>
    ///         It only redirects. The account pages keep their own addresses and content, so they
    ///         can change without any application's link changing with them. The caller's client,
    ///         return address and language travel on to the page, which is where each of them is
    ///         checked and used.
    ///     </para>
    /// </summary>
    // The identity cookie: an anonymous caller is sent to sign in first and comes back here.
    [Authorize]
    public sealed class AccountController : Controller
    {
        /// <summary>Redirects to the account page of a section.</summary>
        /// <param name="section">
        ///     The section to open, one of the names in <see cref="AccountSections" />. A missing
        ///     or unknown name opens the profile.
        /// </param>
        /// <returns>A redirect to the section's page, carrying the caller's query values.</returns>
        [HttpGet(AccountSections.EntryRoute)]
        public IActionResult Index([FromQuery(Name = "section")] string? section)
        {
            string page = Url.Page(AccountSections.PageOf(section), new { area = TellmaIdentityConstants.AreaName })
                ?? throw new InvalidOperationException("The account page of the requested section cannot be addressed.");

            // The answer depends on who asks and on what they sent, so no cache may replay it.
            Response.Headers[HeaderNames.CacheControl] = "no-store";

            // The client, the return address and the language are read through the one reader
            // every page uses, so the page receives exactly the values it will itself accept.
            return Redirect(CarriedQuery.Read(Request).AppendTo(page));
        }
    }
}
