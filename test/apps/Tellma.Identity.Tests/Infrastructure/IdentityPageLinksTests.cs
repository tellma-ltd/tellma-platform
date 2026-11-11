// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Microsoft.AspNetCore.Http;
using Tellma.Identity.Infrastructure;
using Tellma.Identity.Options;

namespace Tellma.Identity.Tests.Infrastructure
{
    /// <summary>
    ///     Which URLs receive the carried query values. The rule has to say yes to every page the
    ///     engine ships, wherever it is mounted, and no to everything else — above all to the
    ///     protocol endpoints, where an added parameter is a changed request, and to anything on
    ///     another origin.
    /// </summary>
    public sealed class IdentityPageLinksTests
    {
        [Theory]
        [InlineData("/Identity/Account/Login")]
        [InlineData("/Identity/Account/EmailCode?email=a%40b.test&returnUrl=%2Fconnect%2Fauthorize")]
        [InlineData("/Identity/Account/RegisterPasskey#main")]
        [InlineData("/Identity/Manage")]
        [InlineData("/Identity/Manage?handler=Remove")]
        [InlineData("/Identity/Manage/Passkeys")]
        [InlineData("/Identity/Manage/ExternalLogins?handler=Link")]

        // Routing matches paths without case, so the rule does too.
        [InlineData("/identity/account/login")]
        [InlineData("/IDENTITY/MANAGE/SESSIONS")]
        public void A_sign_in_or_account_page_is_recognized(string url)
        {
            Assert.True(Links().IsIdentityPage(PathString.Empty, url));
        }

        [Theory]

        // Protocol endpoints: a parameter added to one of these changes the request.
        [InlineData("/connect/authorize?client_id=acme-erp&request_uri=urn%3Aabc")]
        [InlineData("/connect/endsession")]
        [InlineData("/connect/verify")]

        // The engine's other addresses, none of which is a page that reads the values.
        [InlineData("/account?section=passkeys")]
        [InlineData("/identity/culture")]
        [InlineData("/error")]
        [InlineData("/")]

        // A folder name is a whole segment, not a prefix.
        [InlineData("/Identity/Accounts/Login")]
        [InlineData("/Identity/ManageEverything")]
        [InlineData("/Identity")]
        [InlineData("/Identity/")]
        [InlineData("/IdentityX/Account/Login")]

        // Another origin, in every form a URL can name one.
        [InlineData("https://evil.test/Identity/Account/Login")]
        [InlineData("//evil.test/Identity/Account/Login")]
        [InlineData("/\\evil.test/Identity/Account/Login")]
        [InlineData("javascript:alert(1)")]

        // Relative forms resolve against wherever the page happens to be, so they are not judged.
        [InlineData("Identity/Account/Login")]
        [InlineData("./Login")]
        [InlineData("?handler=Remove")]
        [InlineData("#main")]
        [InlineData("")]
        [InlineData(null)]

        // Paths a browser resolves to somewhere other than where they read.
        [InlineData("/Identity/Account/../../connect/authorize")]
        [InlineData("/Identity/Account/%2e%2e/%2e%2e/connect/authorize")]
        [InlineData("/Identity/Account/..%2F..%2Fconnect/authorize")]
        [InlineData("/Identity/Account\\..\\..\\connect\\authorize")]
        [InlineData("/Identity/Account/Login;x=1")]
        public void Anything_else_is_not(string? url)
        {
            Assert.False(Links().IsIdentityPage(PathString.Empty, url));
        }

        [Theory]
        [InlineData("/id/Identity/Account/Login", true)]
        [InlineData("/id/Identity/Manage/Passkeys?handler=Remove", true)]
        [InlineData("/ID/identity/manage", true)]

        // Without the mount point the path is the host's own, whatever it is named.
        [InlineData("/Identity/Account/Login", false)]
        [InlineData("/idx/Identity/Account/Login", false)]
        [InlineData("/id", false)]
        [InlineData("/id/connect/authorize", false)]
        [InlineData("/id/account", false)]
        public void Mounted_under_a_path_base_the_pages_are_recognized_there_and_only_there(string url, bool expected)
        {
            Assert.Equal(expected, Links(pathBase: "/id").IsIdentityPage(PathString.Empty, url));
        }

        [Theory]
        [InlineData("/tenant/Identity/Account/Login", "", true)]
        [InlineData("/tenant/id/Identity/Account/Login", "/id", true)]
        [InlineData("/Identity/Account/Login", "", false)]
        [InlineData("/id/Identity/Account/Login", "/id", false)]
        [InlineData("/id/tenant/Identity/Account/Login", "/id", false)]
        public void A_host_served_under_its_own_path_base_is_stepped_over_first(string url, string pathBase, bool expected)
        {
            Assert.Equal(expected, Links(pathBase).IsIdentityPage(new PathString("/tenant"), url));
        }

        [Fact]
        public void A_link_to_an_identity_page_is_given_what_the_request_carried()
        {
            HttpContext http = Request("?client_id=acme-erp&return_url=https%3A%2F%2Facme.test%2Forders&ui_locales=ar&section=passkeys");

            // The three values, and only those: the request's other parameters are its own.
            Assert.Equal(
                "/Identity/Manage/Sessions?client_id=acme-erp&return_url=https%3A%2F%2Facme.test%2Forders&ui_locales=ar",
                Links().Carry(http, "/Identity/Manage/Sessions"));
        }

        [Theory]
        [InlineData("/connect/authorize?client_id=acme-erp&request_uri=urn%3Aabc")]
        [InlineData("https://acme.test/orders")]
        [InlineData("/identity/culture")]
        [InlineData("#tmi-main")]
        public void A_link_anywhere_else_is_returned_as_it_was(string url)
        {
            HttpContext http = Request("?client_id=acme-erp&return_url=https%3A%2F%2Facme.test%2Forders&ui_locales=ar");

            Assert.Same(url, Links().Carry(http, url));
        }

        [Fact]
        public void A_request_that_carries_nothing_changes_no_link()
        {
            const string Url = "/Identity/Manage/Sessions";

            Assert.Same(Url, Links().Carry(Request("?section=sessions"), Url));
        }

        /// <summary>Builds the rule for an engine mounted at a path base.</summary>
        /// <param name="pathBase">The engine's path base; empty when it is mounted at the root.</param>
        /// <returns>The rule.</returns>
        private static IdentityPageLinks Links(string pathBase = "")
        {
            return new IdentityPageLinks(Microsoft.Extensions.Options.Options.Create(new TellmaIdentityOptions { PathBase = pathBase }));
        }

        /// <summary>Builds a request with a query string.</summary>
        /// <param name="query">The query string, with its leading question mark.</param>
        /// <returns>The request's context.</returns>
        private static DefaultHttpContext Request(string query)
        {
            DefaultHttpContext http = new();
            http.Request.QueryString = new QueryString(query);
            return http;
        }
    }
}
