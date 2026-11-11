// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Routing;
using Tellma.Identity.Infrastructure;
using Tellma.Identity.Options;

namespace Tellma.Identity.Tests.Infrastructure
{
    /// <summary>
    ///     The filter that keeps the carried query values on the redirects an identity page
    ///     answers with. A redirect to another identity page gains them; a redirect back into the
    ///     protocol, or out to a client, must pass through exactly as the page wrote it.
    /// </summary>
    public sealed class CarriedQueryRedirectFilterTests
    {
        /// <summary>A request that carries all three values.</summary>
        private const string CarryingQuery = "?client_id=acme-erp&return_url=https%3A%2F%2Facme.test%2Forders&ui_locales=ar";

        [Fact]
        public void A_redirect_to_a_page_gains_the_carried_values_as_route_values()
        {
            RedirectToPageResult redirect = new("EmailCode", new { email = "a@b.test", returnUrl = "/connect/authorize" });

            Run(redirect, CarryingQuery);

            Assert.NotNull(redirect.RouteValues);
            Assert.Equal("a@b.test", redirect.RouteValues["email"]);
            Assert.Equal("/connect/authorize", redirect.RouteValues["returnUrl"]);
            Assert.Equal("acme-erp", redirect.RouteValues["client_id"]);
            Assert.Equal("https://acme.test/orders", redirect.RouteValues["return_url"]);
            Assert.Equal("ar", redirect.RouteValues["ui_locales"]);
        }

        [Fact]
        public void A_redirect_to_the_same_page_gains_them_too()
        {
            // The redirect after a form post names nothing at all, and is the common case.
            RedirectToPageResult redirect = new(pageName: null);

            Run(redirect, "?ui_locales=ar");

            Assert.NotNull(redirect.RouteValues);
            Assert.Equal("ar", Assert.Single(redirect.RouteValues).Value);
        }

        [Fact]
        public void A_value_the_redirect_states_itself_is_kept()
        {
            // A page that has just changed the language says so, and is not contradicted.
            RedirectToPageResult redirect = new(pageName: null, new RouteValueDictionary { ["ui_locales"] = "en" });

            Run(redirect, CarryingQuery);

            Assert.Equal("en", redirect.RouteValues!["ui_locales"]);
            Assert.Equal("acme-erp", redirect.RouteValues["client_id"]);
        }

        [Fact]
        public void A_redirect_to_a_page_of_another_area_gains_nothing()
        {
            RedirectToPageResult redirect = new("/Index", new { area = "Host" });

            Run(redirect, CarryingQuery);

            Assert.Equal("Host", Assert.Single(redirect.RouteValues!).Value);
        }

        [Fact]
        public void A_redirect_that_names_the_identity_area_gains_them()
        {
            RedirectToPageResult redirect = new("/Account/LoggedOut", new { area = "Identity" });

            Run(redirect, "?ui_locales=ar");

            Assert.Equal("ar", redirect.RouteValues!["ui_locales"]);
        }

        [Fact]
        public void A_local_redirect_to_an_identity_page_is_extended()
        {
            LocalRedirectResult redirect = new("/Identity/Manage/Passkeys");

            Run(redirect, CarryingQuery);

            Assert.Equal(
                "/Identity/Manage/Passkeys?client_id=acme-erp&return_url=https%3A%2F%2Facme.test%2Forders&ui_locales=ar", redirect.Url);
        }

        [Fact]
        public void A_local_redirect_back_to_the_authorization_endpoint_is_untouched()
        {
            // The return to the protocol after sign-in: the request it resumes is the client's,
            // and nothing may be added to it.
            const string Authorize = "/connect/authorize?client_id=acme-erp&request_uri=urn%3Aietf%3Aparams%3Aoauth%3Arequest_uri%3Aabc";
            LocalRedirectResult redirect = new(Authorize);

            Run(redirect, "?returnUrl=%2Fconnect%2Fauthorize&ui_locales=ar&client_id=other");

            Assert.Same(Authorize, redirect.Url);
        }

        [Fact]
        public void A_redirect_to_another_origin_is_untouched()
        {
            // An accepted invitation's sealed destination in the client's application.
            const string Destination = "https://acme.test/welcome";
            RedirectResult redirect = new(Destination);

            Run(redirect, CarryingQuery);

            Assert.Same(Destination, redirect.Url);
        }

        [Fact]
        public void A_plain_redirect_to_an_identity_page_is_extended()
        {
            RedirectResult redirect = new("/Identity/Account/Login?returnUrl=%2Fconnect%2Fauthorize");

            Run(redirect, "?ui_locales=ar");

            Assert.Equal("/Identity/Account/Login?returnUrl=%2Fconnect%2Fauthorize&ui_locales=ar", redirect.Url);
        }

        [Fact]
        public void A_request_that_carries_nothing_changes_no_redirect()
        {
            RedirectToPageResult toPage = new("Login");
            LocalRedirectResult local = new("/Identity/Manage");

            Run(toPage, "?handler=Remove");
            Run(local, "?handler=Remove");

            Assert.Null(toPage.RouteValues);
            Assert.Equal("/Identity/Manage", local.Url);
        }

        [Fact]
        public void A_result_that_is_not_a_redirect_is_left_alone()
        {
            ContentResult content = new() { Content = "/Identity/Manage" };

            Run(content, CarryingQuery);

            Assert.Equal("/Identity/Manage", content.Content);
        }

        /// <summary>Runs the filter over a result, as it runs just before the result executes.</summary>
        /// <param name="result">The result a page handler returned.</param>
        /// <param name="query">The query string of the request being answered.</param>
        private static void Run(IActionResult result, string query)
        {
            DefaultHttpContext http = new();
            http.Request.QueryString = new QueryString(query);

            CarriedQueryRedirectFilter filter = new(
                new IdentityPageLinks(Microsoft.Extensions.Options.Options.Create(new TellmaIdentityOptions())));

            filter.OnResultExecuting(new ResultExecutingContext(
                new ActionContext(http, new RouteData(), new ActionDescriptor()), [], result, controller: new object()));
        }
    }
}
