// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Routing;

namespace Tellma.Identity.Infrastructure
{
    /// <summary>
    ///     Adds the request's <see cref="CarriedQuery" /> to every redirect an identity page
    ///     answers with, when the redirect leads to another identity page. The counterpart of
    ///     <see cref="CarriedQueryTagHelper" /> for the hops a page makes on the server: between
    ///     the sign-in steps, back to an account page after a form post, and on to a fallback page
    ///     when nothing named a destination.
    ///     <para>
    ///         A redirect back to the authorization endpoint, to a client's callback or to an
    ///         external provider passes through untouched.
    ///     </para>
    /// </summary>
    /// <param name="links">Decides which addresses are identity pages, and adds the values.</param>
    public sealed class CarriedQueryRedirectFilter(IdentityPageLinks links) : IResultFilter
    {
        /// <inheritdoc />
        public void OnResultExecuting(ResultExecutingContext context)
        {
            ArgumentNullException.ThrowIfNull(context);

            HttpContext http = context.HttpContext;
            switch (context.Result)
            {
                // A redirect by page name has no address yet; the values join its route values and
                // routing writes them into the query when it builds the address.
                case RedirectToPageResult toPage:
                    Carry(toPage, CarriedQuery.Read(http.Request));
                    break;

                // A redirect by address is judged like a link: only an identity page is extended.
                case LocalRedirectResult local:
                    local.Url = links.Carry(http, local.Url);
                    break;

                case RedirectResult redirect:
                    redirect.Url = links.Carry(http, redirect.Url);
                    break;

                default:
                    break;
            }
        }

        /// <inheritdoc />
        public void OnResultExecuted(ResultExecutedContext context)
        {
            // Nothing to do once the response has been written.
        }

        /// <summary>Adds the carried values a page redirect does not state itself.</summary>
        /// <param name="redirect">The redirect to another page.</param>
        /// <param name="carried">The request's carried values.</param>
        private static void Carry(RedirectToPageResult redirect, CarriedQuery carried)
        {
            if (carried.IsEmpty)
            {
                return;
            }

            // An identity page only ever redirects to an identity page; a redirect that names
            // another area is not one of them, and gets nothing.
            if (redirect.RouteValues is { } stated
                && stated.TryGetValue("area", out object? area)
                && !string.Equals(area as string, TellmaIdentityConstants.AreaName, StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            RouteValueDictionary values = redirect.RouteValues ??= [];
            AddIfMissing(values, CarriedQuery.ClientIdName, carried.ClientId);
            AddIfMissing(values, CarriedQuery.ReturnUrlName, carried.ReturnUrl);
            AddIfMissing(values, CarriedQuery.UiLocalesName, carried.UiLocales);
        }

        /// <summary>Adds a value unless the redirect states one under that name already.</summary>
        private static void AddIfMissing(RouteValueDictionary values, string name, string? value)
        {
            if (value is not null)
            {
                values.TryAdd(name, value);
            }
        }
    }
}
