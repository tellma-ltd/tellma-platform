// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.Primitives;
using Tellma.Identity.Infrastructure;
using Tellma.Identity.Options;

namespace Tellma.Identity.Tests.Infrastructure
{
    /// <summary>
    ///     Which URLs receive the carried query values. The rule has to say yes to every page the
    ///     engine ships, wherever it is mounted, and no to everything else — above all to the
    ///     protocol endpoints, where an added parameter is a changed request, and to anything on
    ///     another origin. The same rule decides where a language the user chose replaces the
    ///     one an address states: on the page itself and in the address it returns to after
    ///     sign-in, but never inside a pending authorization request.
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

        [Theory]
        [InlineData("/account")]
        [InlineData("/account?section=passkeys&ui_locales=ar")]
        [InlineData("/account#main")]

        // Routing matches the path without case and with a closing slash.
        [InlineData("/Account")]
        [InlineData("/ACCOUNT/?section=sessions")]
        public void The_account_entry_is_recognized(string url)
        {
            Assert.True(Links().IsAccountEntry(PathString.Empty, url));
        }

        [Theory]

        // The entry is one address, not a folder and not a prefix.
        [InlineData("/accounts")]
        [InlineData("/account/passkeys")]
        [InlineData("/account//")]
        [InlineData("/x/account")]
        [InlineData("/Identity/Account")]
        [InlineData("/Identity/Account/Login")]
        [InlineData("/connect/authorize")]
        [InlineData("/")]

        // Another origin, a relative form, and paths a browser resolves elsewhere.
        [InlineData("https://evil.test/account")]
        [InlineData("//evil.test/account")]
        [InlineData("/\\evil.test/account")]
        [InlineData("account")]
        [InlineData("/account/..")]
        [InlineData("/x/%2e%2e/account")]
        [InlineData("/account;x=1")]
        [InlineData("")]
        [InlineData(null)]
        public void No_other_address_is_the_account_entry(string? url)
        {
            Assert.False(Links().IsAccountEntry(PathString.Empty, url));
        }

        [Theory]
        [InlineData("/tenant/id/account?section=sessions", true)]
        [InlineData("/tenant/ID/Account/", true)]

        // Without the host's path base or the engine's, the address is someone else's.
        [InlineData("/id/account", false)]
        [InlineData("/tenant/account", false)]
        [InlineData("/account", false)]
        [InlineData("/tenant/id/Identity/Account", false)]
        public void The_account_entry_lies_under_the_hosts_path_base_and_the_engines(string url, bool expected)
        {
            Assert.Equal(expected, Links(pathBase: "/id").IsAccountEntry(new PathString("/tenant"), url));
        }

        [Fact]
        public void A_chosen_language_replaces_the_one_the_challenged_account_entry_was_opened_in()
        {
            // What the cookie challenge of the entry builds: the entry's address, whole, as
            // the address to return to, and the language again as the sign-in page's own.
            const string Entry = "/account?section=sessions&client_id=acme-erp&ui_locales=ar&return_url=https%3A%2F%2Facme.test%2Forders%3Ftab%3Dlines";
            string login = QueryHelpers.AddQueryString(
                QueryHelpers.AddQueryString("/Identity/Account/Login", "ReturnUrl", Entry), "ui_locales", "ar");

            (string path, Dictionary<string, StringValues> query) = Split(Links().WithLanguage(PathString.Empty, login, "en"));

            // Both now state the choice, and nothing else about either address has changed:
            // the application's own return address still reads exactly as it did.
            Assert.Equal("/Identity/Account/Login", path);
            Assert.Equal(2, query.Count);
            Assert.Equal("en", (string?)query["ui_locales"]);
            Assert.Equal(
                "/account?section=sessions&client_id=acme-erp&ui_locales=en&return_url=https%3A%2F%2Facme.test%2Forders%3Ftab%3Dlines",
                (string?)query["ReturnUrl"]);
        }

        [Theory]

        // An account page whose session ran out, and a sign-in page further along the flow.
        [InlineData("/Identity/Account/Login", "/Identity/Manage/Passkeys?client_id=acme-erp&ui_locales=ar%20fr", "/Identity/Manage/Passkeys?client_id=acme-erp&ui_locales=en")]
        [InlineData("/Identity/Account/EmailCode", "/account?ui_locales=ar", "/account?ui_locales=en")]
        [InlineData("/identity/account/login", "/ACCOUNT/?UI_LOCALES=ar", "/ACCOUNT/?ui_locales=en")]
        public void A_chosen_language_replaces_the_one_inside_the_address_a_sign_in_page_returns_to(
            string page, string returnsTo, string expected)
        {
            // The page itself states no language here: the nested one is replaced all the same.
            string url = QueryHelpers.AddQueryString(page, "returnUrl", returnsTo);

            (string path, Dictionary<string, StringValues> query) = Split(Links().WithLanguage(PathString.Empty, url, "en"));

            Assert.Equal(page, path);
            Assert.Equal(expected, (string?)Assert.Single(query).Value);
        }

        [Theory]

        // A pending authorization request, pushed or on the URL, and the other protocol endpoints.
        [InlineData("/Identity/Account/Login?returnUrl=%2Fconnect%2Fauthorize%3Fclient_id%3Dacme-erp%26ui_locales%3Dar&ui_locales=ar")]
        [InlineData("/Identity/Account/Login?returnUrl=%2Fconnect%2Fendsession%3Fui_locales%3Dar&ui_locales=ar")]

        // Another origin, and paths a browser resolves somewhere other than where they read.
        [InlineData("/Identity/Account/Login?returnUrl=https%3A%2F%2Fevil.test%2Faccount%3Fui_locales%3Dar&ui_locales=ar")]
        [InlineData("/Identity/Account/Login?returnUrl=%2F%2Fevil.test%2Faccount%3Fui_locales%3Dar&ui_locales=ar")]
        [InlineData("/Identity/Account/Login?returnUrl=%2FIdentity%2FManage%2F..%2F..%2Fconnect%2Fauthorize%3Fui_locales%3Dar&ui_locales=ar")]

        // A return address given twice: there is no one address to rewrite.
        [InlineData("/Identity/Account/Login?returnUrl=%2Faccount%3Fui_locales%3Dar&ReturnUrl=%2Faccount%3Fui_locales%3Dar&ui_locales=ar")]

        // The application's own return address is never looked into, whatever it holds.
        [InlineData("/Identity/Manage?return_url=%2Faccount%3Fui_locales%3Dar&ui_locales=ar")]
        public void A_chosen_language_never_reaches_into_any_other_nested_address(string url)
        {
            // Only the page's own value changes; the rest of the URL is as it was written.
            Assert.Equal(
                string.Concat(url.AsSpan(0, url.Length - "ui_locales=ar".Length), "ui_locales=en"),
                Links().WithLanguage(PathString.Empty, url, "en"));
        }

        [Fact]
        public void A_return_address_on_an_authorization_request_is_the_requests_own_parameter()
        {
            // Only the engine's pages hold a return address under that name. The same name on
            // a protocol endpoint is a parameter of that request, and it is not rewritten even
            // when it names an account page.
            const string Url = "/connect/authorize?client_id=acme-erp&returnUrl=%2FIdentity%2FManage%3Fui_locales%3Dar&ui_locales=ar";

            Assert.Equal(
                "/connect/authorize?client_id=acme-erp&returnUrl=%2FIdentity%2FManage%3Fui_locales%3Dar&ui_locales=en",
                Links().WithLanguage(PathString.Empty, Url, "en"));
        }

        [Theory]
        [InlineData("/Identity/Account/Login")]
        [InlineData("/Identity/Account/Login?returnUrl=%2Faccount%3Fsection%3Dsessions")]
        [InlineData("/Identity/Account/Login?returnUrl=%2Fconnect%2Fauthorize%3Fui_locales%3Dar")]
        [InlineData("/connect/authorize?client_id=acme-erp&request_uri=urn%3Aabc")]
        public void A_url_that_states_no_language_it_may_replace_is_returned_as_it_was(string url)
        {
            Assert.Same(url, Links().WithLanguage(PathString.Empty, url, "en"));
        }

        [Fact]
        public void A_chosen_language_follows_return_addresses_a_few_levels_deep_and_no_further()
        {
            // Six sign-in pages, each returning to the next and each stating Arabic. A page
            // that holds a return address and is then challenged makes two levels; this many
            // only a hand-written URL has, and the work done on one is bounded.
            string url = "/Identity/Account/Login?ui_locales=ar";
            for (int level = 0; level < 5; level++)
            {
                url = QueryHelpers.AddQueryString("/Identity/Account/Login?ui_locales=ar", "returnUrl", url);
            }

            List<string?> languages = [];
            string? current = Links().WithLanguage(PathString.Empty, url, "en");
            while (current is not null)
            {
                Dictionary<string, StringValues> query = Split(current).Query;
                languages.Add((string?)query["ui_locales"]);
                current = (string?)query.GetValueOrDefault("returnUrl");
            }

            Assert.Equal((string?[])["en", "en", "en", "en", "ar", "ar"], languages);
        }

        [Fact]
        public void Mounted_in_a_host_a_chosen_language_follows_the_return_address_under_the_path_base()
        {
            IdentityPageLinks links = Links(pathBase: "/id");
            PathString host = new("/tenant");

            // The entry under both path bases is followed into.
            string mounted = QueryHelpers.AddQueryString(
                "/tenant/id/Identity/Account/Login?ui_locales=ar", "ReturnUrl", "/tenant/id/account?ui_locales=ar");
            Dictionary<string, StringValues> query = Split(links.WithLanguage(host, mounted, "en")).Query;
            Assert.Equal("en", (string?)query["ui_locales"]);
            Assert.Equal("/tenant/id/account?ui_locales=en", (string?)query["ReturnUrl"]);

            // An address of the same name outside them is the host's own page.
            string hosts = QueryHelpers.AddQueryString(
                "/tenant/id/Identity/Account/Login?ui_locales=ar", "ReturnUrl", "/tenant/account?ui_locales=ar");
            query = Split(links.WithLanguage(host, hosts, "en")).Query;
            Assert.Equal("en", (string?)query["ui_locales"]);
            Assert.Equal("/tenant/account?ui_locales=ar", (string?)query["ReturnUrl"]);
        }

        /// <summary>Splits a URL into its path and its decoded query values.</summary>
        /// <param name="url">The URL.</param>
        /// <returns>The path and the query.</returns>
        private static (string Path, Dictionary<string, StringValues> Query) Split(string url)
        {
            int mark = url.IndexOf('?', StringComparison.Ordinal);
            return mark < 0
                ? (url, new Dictionary<string, StringValues>(StringComparer.Ordinal))
                : (url[..mark], QueryHelpers.ParseQuery(url[mark..]));
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
