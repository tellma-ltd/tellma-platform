// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using AngleSharp.Dom;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Primitives;
using System.Net;
using Tellma.Identity.Infrastructure;
using Tellma.Identity.IntegrationTests.Infrastructure;
using Tellma.Identity.TestSupport;

namespace Tellma.Identity.IntegrationTests.Flows
{
    /// <summary>
    ///     The account entry an application links to, and what the account pages do with the
    ///     client, return address and language it hands them: the pages render in that language,
    ///     every step between them keeps all three, and the way back leads only to an address on
    ///     the client's own registered origin.
    /// </summary>
    [Collection(SqlServerCollectionDefinition.Name)]
    [Trait("Category", "Integration")]
    public sealed class AccountEntryTests(SqlServerFixture fixture)
    {
        /// <summary>The origin the provisioned first-party client is registered at.</summary>
        private const string Origin = "https://acme.app.tellma.com";

        /// <summary>A page in the client's application, as the client would name it.</summary>
        private const string ReturnUrl = "https://acme.app.tellma.com/orders/42?tab=lines&sort=date";

        /// <summary>The three values an application sends, as a query string without its question mark.</summary>
        private static readonly string Carried =
            "client_id=acme&return_url=" + Uri.EscapeDataString(ReturnUrl) + "&ui_locales=ar";

        /// <summary>Each section name with the page it opens; a missing or unknown name opens the profile.</summary>
        private static readonly (string? Section, string Page)[] Sections =
        [
            (null, "/Identity/Manage"),
            ("profile", "/Identity/Manage"),
            ("passkeys", "/Identity/Manage/Passkeys"),
            ("authenticator", "/Identity/Manage/EnableAuthenticator"),
            ("external-logins", "/Identity/Manage/ExternalLogins"),
            ("sessions", "/Identity/Manage/Sessions"),
            ("billing", "/Identity/Manage"),
        ];

        /// <summary>
        ///     An external provider, so the navigation offers every entry it can; and a third-party
        ///     client, which holds a registration but is not somewhere the pages send a user.
        /// </summary>
        private static readonly Dictionary<string, string?> Seed = new(StringComparer.Ordinal)
        {
            ["TellmaIdentity:ExternalProviders:Google:ClientId"] = "google-client-id",
            ["TellmaIdentity:ExternalProviders:Google:ClientSecret"] = "google-client-secret",
            ["TellmaIdentity:Seed:Clients:0:ClientId"] = "third-party",
            ["TellmaIdentity:Seed:Clients:0:DisplayName"] = "Third Party App",
            ["TellmaIdentity:Seed:Clients:0:Kind"] = "Native",
            ["TellmaIdentity:Seed:Clients:0:RequireConsent"] = "true",
            ["TellmaIdentity:Seed:Clients:0:ThirdParty"] = "true",
            ["TellmaIdentity:Seed:Clients:0:RedirectUris:0"] = "http://127.0.0.1/callback",
        };

        [Fact]
        public async Task A_signed_in_user_is_redirected_to_the_page_of_each_section()
        {
            using StandaloneFactory factory = DatabaseBackedFactory.CreateStandalone(fixture, "idacct", Seed);
            using OidcFlowClient flow = await SignedInAsync(factory, "entry@example.com");

            foreach ((string? section, string page) in Sections)
            {
                using HttpResponseMessage response = await GetAsync(flow, AccountUrl(section, Carried));

                Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
                Assert.True(response.Headers.CacheControl?.NoStore, $"The redirect for section '{section}' may be stored.");

                // The three values travel on; the section has done its work and does not.
                (string path, Dictionary<string, StringValues> query) = HtmlPage.Split(response.Headers.Location!.ToString());
                Assert.Equal(page, path);
                Assert.Equal(3, query.Count);
                Assert.Equal("acme", (string?)query["client_id"]);
                Assert.Equal(ReturnUrl, (string?)query["return_url"]);
                Assert.Equal("ar", (string?)query["ui_locales"]);

                // And the page it names is really there.
                using HttpResponseMessage destination = await GetAsync(flow, response.Headers.Location!.ToString());
                Assert.Equal(HttpStatusCode.OK, destination.StatusCode);
            }
        }

        [Fact]
        public async Task The_entry_passes_on_only_what_it_was_given_and_could_read()
        {
            using StandaloneFactory factory = DatabaseBackedFactory.CreateStandalone(fixture, "idacctbare", Seed);
            using OidcFlowClient flow = await SignedInAsync(factory, "bare@example.com");

            // Nothing given, nothing added.
            using HttpResponseMessage bare = await GetAsync(flow, "/account");
            Assert.Equal(HttpStatusCode.Redirect, bare.StatusCode);
            Assert.Equal("/Identity/Manage", bare.Headers.Location!.ToString());
            Assert.True(bare.Headers.CacheControl?.NoStore);

            // A value given twice, a malformed language and a parameter of the caller's own are
            // all left behind; the one usable value still travels.
            using HttpResponseMessage partial = await GetAsync(
                flow, "/account?section=sessions&client_id=acme&client_id=other&ui_locales=%3Cb%3E&utm_source=mail&return_url="
                    + Uri.EscapeDataString(ReturnUrl));
            (string path, Dictionary<string, StringValues> query) = HtmlPage.Split(partial.Headers.Location!.ToString());
            Assert.Equal("/Identity/Manage/Sessions", path);
            Assert.Equal(ReturnUrl, (string?)Assert.Single(query).Value);
        }

        [Fact]
        public async Task An_anonymous_caller_is_challenged_in_the_language_asked_for_and_then_arrives()
        {
            using StandaloneFactory factory = DatabaseBackedFactory.CreateStandalone(fixture, "idacctanon", Seed);
            await TestData.ProvisionDistributionAsync(factory);
            await TestData.CreateActiveUserAsync(factory, "anon@example.com");
            using OidcFlowClient flow = factory.CreateFlowClient();

            string? loginUrl = null;
            foreach ((string? section, string _) in Sections)
            {
                string entry = AccountUrl(section, Carried);
                using HttpResponseMessage response = await GetAsync(flow, entry);

                Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
                Assert.True(response.Headers.CacheControl?.NoStore, $"The challenge for section '{section}' may be stored.");

                // The sign-in page is told the language as a value of its own; the entry URL,
                // whole, is where it returns to.
                loginUrl = response.Headers.Location!.ToString();
                (string path, Dictionary<string, StringValues> query) = HtmlPage.Split(loginUrl);
                Assert.Equal("/Identity/Account/Login", path);
                Assert.Equal(2, query.Count);
                Assert.Equal(entry, (string?)query["ReturnUrl"]);
                Assert.Equal("ar", (string?)query["ui_locales"]);
            }

            // A request that names no language adds none to the challenge.
            using (HttpResponseMessage plain = await GetAsync(flow, "/account?section=passkeys"))
            {
                (string _, Dictionary<string, StringValues> query) = HtmlPage.Split(plain.Headers.Location!.ToString());
                Assert.Equal("/account?section=passkeys", (string?)Assert.Single(query).Value);
            }

            // The sign-in page renders in the language the application asked for.
            using (HttpResponseMessage loginPage = await GetAsync(flow, loginUrl!))
            {
                IDocument login = await HtmlPage.ReadAsync(loginPage);
                Assert.Equal("ar", login.DocumentElement.GetAttribute("lang"));
                Assert.Equal("rtl", login.DocumentElement.GetAttribute("dir"));
            }

            // Signing in returns to the entry, which now opens the section — the last one asked
            // for — with everything the application sent.
            string afterSignIn = await flow.SignInWithEmailCodeAsync("anon@example.com", loginUrl!);
            Assert.Equal(AccountUrl("billing", Carried), afterSignIn);

            using HttpResponseMessage arrived = await GetAsync(flow, afterSignIn);
            Assert.Equal(HttpStatusCode.Redirect, arrived.StatusCode);
            Assert.Equal("/Identity/Manage?" + Carried, arrived.Headers.Location!.ToString());

            using HttpResponseMessage profile = await GetAsync(flow, arrived.Headers.Location!.ToString());
            IDocument page = await HtmlPage.ReadAsync(profile);
            Assert.Equal("ar", page.DocumentElement.GetAttribute("lang"));
            Assert.Equal(ReturnUrl, BackLink(page));
        }

        [Fact]
        public async Task The_back_link_leads_to_the_return_address_only_on_the_clients_own_origin()
        {
            using StandaloneFactory factory = DatabaseBackedFactory.CreateStandalone(fixture, "idacctback", Seed);
            using OidcFlowClient flow = await SignedInAsync(factory, "back@example.com");

            // On the registered origin: the page the user left.
            Assert.Equal(ReturnUrl, await BackLinkAsync(flow, "acme", ReturnUrl));
            Assert.Equal(Origin + "/", await BackLinkAsync(flow, "acme", Origin + "/"));

            // Anywhere else: the origin's root, as if no address had been given.
            foreach (string elsewhere in (string[])
                [
                    "https://evil.test/orders/42",
                    "https://acme.app.tellma.com:8443/orders/42",
                    "https://acme.app.tellma.com@evil.test/orders/42",
                    "https://user:pass@acme.app.tellma.com/orders/42",
                    "http://acme.app.tellma.com/orders/42",
                    "https://acme.app.tellma.com.evil.test/",
                    "javascript:alert(document.domain)",
                    "//evil.test/orders/42",
                    "/orders/42",
                ])
            {
                Assert.Equal(Origin, await BackLinkAsync(flow, "acme", elsewhere));
            }

            Assert.Equal(Origin, await BackLinkAsync(flow, "acme", returnUrl: null));

            // A client that cannot vouch for an address — third-party, or not registered at all —
            // gets no link from one, and this deployment configures no application of its own.
            Assert.Null(await BackLinkAsync(flow, "third-party", "http://127.0.0.1/callback"));
            Assert.Null(await BackLinkAsync(flow, "no-such-client", ReturnUrl));
            Assert.Null(await BackLinkAsync(flow, clientId: null, ReturnUrl));
        }

        [Fact]
        public async Task Every_account_page_keeps_the_client_the_return_address_and_the_language()
        {
            using StandaloneFactory factory = DatabaseBackedFactory.CreateStandalone(fixture, "idacctnav", Seed);
            using OidcFlowClient flow = await SignedInAsync(factory, "carry@example.com");

            using HttpResponseMessage first = await GetAsync(flow, "/Identity/Manage?" + Carried);
            IDocument profile = await HtmlPage.ReadAsync(first);

            // The navigation offers every page it can, and each entry is followed below.
            string[] entries = [.. profile.QuerySelectorAll("nav a.tmi-nav-item").Select(static entry => entry.GetAttribute("href")!)];
            Assert.Equal(4, entries.Length);

            foreach (string entry in entries.Append("/Identity/Manage/EnableAuthenticator?" + Carried))
            {
                AssertCarries(entry);

                using HttpResponseMessage response = await GetAsync(flow, entry);
                Assert.Equal(HttpStatusCode.OK, response.StatusCode);
                IDocument page = await HtmlPage.ReadAsync(response);

                // Arabic, with the way back still leading to the page the user left.
                Assert.Equal("ar", page.DocumentElement.GetAttribute("lang"));
                Assert.Equal("rtl", page.DocumentElement.GetAttribute("dir"));
                Assert.Equal(ReturnUrl, BackLink(page));

                // Every link, form and button on the page that leads to another of the engine's
                // pages keeps all three — the navigation, sign-out and the page's own controls.
                IReadOnlyList<string> addresses = HtmlPage.IdentityPageAddresses(page);
                Assert.True(addresses.Count >= 6, $"{entry} renders {addresses.Count} internal addresses.");
                Assert.All(addresses, AssertCarries);

                // The language switch posts to its own endpoint, which is not a page; it is told
                // where to come back to instead.
                IElement picker = Assert.Single(page.QuerySelectorAll("form.tmi-lang-menu"));
                Assert.Equal("/identity/culture", picker.GetAttribute("action"));
                (string path, Dictionary<string, StringValues> query) =
                    HtmlPage.Split(picker.QuerySelector("input[name=returnUrl]")!.GetAttribute("value")!);
                Assert.Equal(HtmlPage.Split(entry).Path, path);
                Assert.Equal(ReturnUrl, (string?)query["return_url"]);
            }
        }

        [Fact]
        public async Task A_language_the_user_chooses_replaces_the_one_the_application_asked_for()
        {
            using StandaloneFactory factory = DatabaseBackedFactory.CreateStandalone(fixture, "idacctlang", Seed);
            using OidcFlowClient flow = await SignedInAsync(factory, "lang@example.com");

            // The language switch: the page comes back with the choice in place of the request,
            // and with the client and the way back untouched.
            using (HttpResponseMessage arabic = await GetAsync(flow, "/Identity/Manage/Sessions?" + Carried))
            {
                (string action, Dictionary<string, string> fields) =
                    await OidcFlowClient.ParseFormAsync(arabic, "form.tmi-lang-menu");
                fields["culture"] = "en";
                using HttpResponseMessage switched = await flow.PostFormAsync(action, fields);

                Assert.Equal(HttpStatusCode.Redirect, switched.StatusCode);
                string destination = switched.Headers.Location!.ToString();
                Assert.Equal(
                    "/Identity/Manage/Sessions?client_id=acme&return_url=" + Uri.EscapeDataString(ReturnUrl) + "&ui_locales=en",
                    destination);

                using HttpResponseMessage english = await GetAsync(flow, destination);
                IDocument page = await HtmlPage.ReadAsync(english);
                Assert.Equal("en", page.DocumentElement.GetAttribute("lang"));
                Assert.Equal(ReturnUrl, BackLink(page));
            }

            // The profile's own language field, saved from a page the application opened in
            // English: the page comes back in the saved language, the way back intact.
            using (HttpResponseMessage profile = await GetAsync(
                flow, "/Identity/Manage?" + Carried.Replace("ui_locales=ar", "ui_locales=en", StringComparison.Ordinal)))
            {
                (string action, Dictionary<string, string> fields) = await OidcFlowClient.ParseFormAsync(profile);
                fields["Locale"] = "ar";
                using HttpResponseMessage saved = await flow.PostFormAsync(action, fields);

                Assert.Equal(HttpStatusCode.Redirect, saved.StatusCode);
                (string path, Dictionary<string, StringValues> query) = HtmlPage.Split(saved.Headers.Location!.ToString());
                Assert.Equal("/Identity/Manage", path);
                Assert.Equal(3, query.Count);
                Assert.Equal("acme", (string?)query["client_id"]);
                Assert.Equal(ReturnUrl, (string?)query["return_url"]);
                Assert.Equal("ar", (string?)query["ui_locales"]);

                using HttpResponseMessage arabic = await GetAsync(flow, saved.Headers.Location!.ToString());
                IDocument page = await HtmlPage.ReadAsync(arabic);
                Assert.Equal("ar", page.DocumentElement.GetAttribute("lang"));
                Assert.Equal(ReturnUrl, BackLink(page));
            }
        }

        [Fact]
        public async Task A_language_chosen_on_the_sign_in_page_holds_once_the_user_has_signed_in()
        {
            using StandaloneFactory factory = DatabaseBackedFactory.CreateStandalone(fixture, "idacctswitch", Seed);
            await TestData.ProvisionDistributionAsync(factory);
            await TestData.CreateActiveUserAsync(factory, "switch@example.com");
            await TestData.CreateActiveUserAsync(factory, "expired@example.com");

            // Two ways an anonymous browser reaches the sign-in page with Arabic asked for: the
            // account entry, and an account page whose session has run out. Either way the
            // challenged address travels whole inside the sign-in page's own, Arabic included.
            foreach ((string email, string challenged, string page) in ((string Email, string Challenged, string Page)[])
                [
                    ("switch@example.com", AccountUrl("sessions", Carried), "/Identity/Manage/Sessions"),
                    ("expired@example.com", "/Identity/Manage/Passkeys?" + Carried, "/Identity/Manage/Passkeys"),
                ])
            {
                using OidcFlowClient flow = factory.CreateFlowClient();
                string inEnglish = challenged.Replace("ui_locales=ar", "ui_locales=en", StringComparison.Ordinal);

                using HttpResponseMessage challenge = await GetAsync(flow, challenged);
                Assert.Equal(HttpStatusCode.Redirect, challenge.StatusCode);

                // The user reads the Arabic sign-in page and switches to English.
                string englishLogin;
                using (HttpResponseMessage arabic = await GetAsync(flow, challenge.Headers.Location!.ToString()))
                {
                    (string action, Dictionary<string, string> fields) =
                        await OidcFlowClient.ParseFormAsync(arabic, "form.tmi-lang-menu");
                    Assert.Equal("ar", (await HtmlPage.ReadAsync(arabic)).DocumentElement.GetAttribute("lang"));

                    fields["culture"] = "en";
                    using HttpResponseMessage switched = await flow.PostFormAsync(action, fields);
                    Assert.Equal(HttpStatusCode.Redirect, switched.StatusCode);
                    englishLogin = switched.Headers.Location!.ToString();
                }

                // The sign-in page comes back stating English as its own language, and inside the
                // address it returns to. Nothing else about that address has changed.
                (string loginPath, Dictionary<string, StringValues> loginQuery) = HtmlPage.Split(englishLogin);
                Assert.Equal("/Identity/Account/Login", loginPath);
                Assert.Equal(2, loginQuery.Count);
                Assert.Equal("en", (string?)loginQuery["ui_locales"]);
                Assert.Equal(inEnglish, (string?)loginQuery["ReturnUrl"]);

                using (HttpResponseMessage english = await GetAsync(flow, englishLogin))
                {
                    Assert.Equal("en", (await HtmlPage.ReadAsync(english)).DocumentElement.GetAttribute("lang"));
                }

                // Signing in returns to the challenged address, now asking for English.
                string address = await flow.SignInWithEmailCodeAsync(email, englishLogin);
                Assert.Equal(inEnglish, address);

                // The entry redirects once more to its page; a page is already where it leads.
                using (HttpResponseMessage first = await GetAsync(flow, address))
                {
                    if (first.StatusCode == HttpStatusCode.Redirect)
                    {
                        address = first.Headers.Location!.ToString();
                    }
                }

                // The account page is in English, not in the language the application asked
                // for, and so is every page it leads to. The way back is untouched.
                (string path, Dictionary<string, StringValues> query) = HtmlPage.Split(address);
                Assert.Equal(page, path);
                Assert.Equal(3, query.Count);
                Assert.Equal("en", (string?)query["ui_locales"]);
                Assert.Equal("acme", (string?)query["client_id"]);
                Assert.Equal(ReturnUrl, (string?)query["return_url"]);

                using HttpResponseMessage arrived = await GetAsync(flow, address);
                Assert.Equal(HttpStatusCode.OK, arrived.StatusCode);
                IDocument document = await HtmlPage.ReadAsync(arrived);
                Assert.Equal("en", document.DocumentElement.GetAttribute("lang"));
                Assert.Equal("ltr", document.DocumentElement.GetAttribute("dir"));
                Assert.Equal(ReturnUrl, BackLink(document));

                IReadOnlyList<string> addresses = HtmlPage.IdentityPageAddresses(document);
                Assert.True(addresses.Count >= 6, $"{page} renders {addresses.Count} internal addresses.");
                Assert.All(addresses, static internalAddress =>
                    Assert.Equal("en", (string?)HtmlPage.Split(internalAddress).Query["ui_locales"]));
            }
        }

        [Fact]
        public async Task A_form_post_on_an_account_page_returns_with_the_way_back_intact()
        {
            using StandaloneFactory factory = DatabaseBackedFactory.CreateStandalone(fixture, "idacctpost", Seed);
            using OidcFlowClient flow = await SignedInAsync(factory, "post@example.com");

            // Signing out everywhere ends on the signed-out page; the redirect there is one a
            // page handler makes, and it keeps the language the pages were opened in.
            using HttpResponseMessage sessions = await GetAsync(flow, "/Identity/Manage/Sessions?" + Carried);
            (string action, Dictionary<string, string> fields) = await OidcFlowClient.ParseFormAsync(sessions);
            AssertCarries(action);

            using HttpResponseMessage signedOut = await flow.PostFormAsync(action, fields);
            Assert.Equal(HttpStatusCode.Redirect, signedOut.StatusCode);
            (string path, Dictionary<string, StringValues> query) = HtmlPage.Split(signedOut.Headers.Location!.ToString());
            Assert.Equal("/Identity/Account/LoggedOut", path);
            Assert.Equal("ar", (string?)query["ui_locales"]);
            Assert.Equal("acme", (string?)query["client_id"]);
            Assert.Equal(ReturnUrl, (string?)query["return_url"]);

            using HttpResponseMessage page = await GetAsync(flow, signedOut.Headers.Location!.ToString());
            Assert.Equal("ar", (await HtmlPage.ReadAsync(page)).DocumentElement.GetAttribute("lang"));
        }

        [Fact]
        public async Task Mounted_in_a_host_the_entry_and_its_challenge_carry_the_path_base()
        {
            using InProcFactory factory = DatabaseBackedFactory.CreateInProc(fixture, "idacctinproc");
            using HttpClient browser = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

            const string Entry = "/id/account?section=sessions&client_id=acme&ui_locales=ar";
            using HttpResponseMessage response = await browser.GetAsync(
                new Uri(Entry, UriKind.Relative), TestContext.Current.CancellationToken);

            Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
            Assert.True(response.Headers.CacheControl?.NoStore);
            (string path, Dictionary<string, StringValues> query) = HtmlPage.Split(response.Headers.Location!.ToString());
            Assert.Equal("/id/Identity/Account/Login", path);
            Assert.Equal(Entry, (string?)query["ReturnUrl"]);
            Assert.Equal("ar", (string?)query["ui_locales"]);

            // The sign-in page there renders in the language, and its own links stay under the
            // path base with the language on them.
            using HttpResponseMessage loginPage = await browser.GetAsync(
                response.Headers.Location, TestContext.Current.CancellationToken);
            IDocument login = await HtmlPage.ReadAsync(loginPage);
            Assert.Equal("ar", login.DocumentElement.GetAttribute("lang"));
            IReadOnlyList<string> addresses = HtmlPage.IdentityPageAddresses(login, "/id");
            Assert.NotEmpty(addresses);
            Assert.All(addresses, static address => Assert.Equal("ar", (string?)HtmlPage.Split(address).Query["ui_locales"]));

            // Outside the path base the address is the host's own, and the host has no such route.
            using HttpResponseMessage unprefixed = await browser.GetAsync(
                new Uri("/account?section=sessions", UriKind.Relative), TestContext.Current.CancellationToken);
            Assert.Equal(HttpStatusCode.NotFound, unprefixed.StatusCode);
        }

        [Fact]
        public async Task Mounted_in_a_host_a_page_handlers_redirect_keeps_what_the_page_was_given()
        {
            using InProcFactory factory = DatabaseBackedFactory.CreateInProc(fixture, "idacctinprocpost");
            using HttpClient browser = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

            // The sign-out page answers anyone, signed in or not, so it shows what a page
            // handler's redirect does in a host that composes the engine into its own services,
            // without a session. The form's own address already keeps the values.
            using HttpResponseMessage signOut = await browser.GetAsync(
                new Uri("/id/Identity/Account/Logout?ui_locales=ar&client_id=acme", UriKind.Relative),
                TestContext.Current.CancellationToken);
            Assert.Equal(HttpStatusCode.OK, signOut.StatusCode);
            (string action, Dictionary<string, string> fields) = await OidcFlowClient.ParseFormAsync(signOut);
            (string actionPath, Dictionary<string, StringValues> actionQuery) = HtmlPage.Split(action);
            Assert.Equal("/id/Identity/Account/Logout", actionPath);
            Assert.Equal("ar", (string?)actionQuery["ui_locales"]);
            Assert.Equal("acme", (string?)actionQuery["client_id"]);

            // The handler redirects to a page by name. The address it is sent to is under the
            // path base and states the language and the client the page was opened with.
            using HttpResponseMessage signedOut = await browser.PostAsync(
                new Uri(action, UriKind.RelativeOrAbsolute),
                new FormUrlEncodedContent(fields),
                TestContext.Current.CancellationToken);
            Assert.Equal(HttpStatusCode.Redirect, signedOut.StatusCode);
            (string path, Dictionary<string, StringValues> query) = HtmlPage.Split(signedOut.Headers.Location!.ToString());
            Assert.Equal("/id/Identity/Account/LoggedOut", path);
            Assert.Equal(2, query.Count);
            Assert.Equal("ar", (string?)query["ui_locales"]);
            Assert.Equal("acme", (string?)query["client_id"]);

            using HttpResponseMessage page = await browser.GetAsync(
                signedOut.Headers.Location, TestContext.Current.CancellationToken);
            Assert.Equal("ar", (await HtmlPage.ReadAsync(page)).DocumentElement.GetAttribute("lang"));
        }

        [Fact]
        public void Every_page_the_engine_ships_is_one_the_values_are_carried_to()
        {
            using StandaloneFactory standalone = DatabaseBackedFactory.CreateStandalone(fixture, "idacctpages", Seed);
            using InProcFactory inProc = DatabaseBackedFactory.CreateInProc(fixture, "idacctpagesin");

            // The rule recognizes the engine's pages by where they are routed. A page added
            // somewhere it does not look would silently drop the language and the way back, so
            // the routed pages themselves are held against it, in both hosting shapes.
            foreach (IServiceProvider services in (IServiceProvider[])[standalone.Services, inProc.Services])
            {
                IdentityPageLinks links = services.GetRequiredService<IdentityPageLinks>();
                string[] pages =
                [
                    .. services.GetRequiredService<EndpointDataSource>().Endpoints
                        .OfType<RouteEndpoint>()
                        .Where(static endpoint => endpoint.Metadata.GetMetadata<PageActionDescriptor>() is { AreaName: "Identity" })
                        .Select(static endpoint => "/" + endpoint.RoutePattern.RawText!.TrimStart('/'))
                        .Distinct(StringComparer.Ordinal),
                ];

                // Every page under the two folders, the profile page under both of its routes.
                Assert.True(pages.Length >= 18, $"Only {pages.Length} identity pages are routed.");
                Assert.All(pages, page => Assert.True(
                    links.IsIdentityPage(PathString.Empty, page), $"{page} is not recognized."));
            }
        }

        /// <summary>Provisions the first-party client and signs a fresh user in.</summary>
        /// <param name="factory">The host under test.</param>
        /// <param name="email">The user's email.</param>
        /// <returns>The signed-in browser.</returns>
        private static async Task<OidcFlowClient> SignedInAsync(StandaloneFactory factory, string email)
        {
            await TestData.ProvisionDistributionAsync(factory);
            await TestData.CreateActiveUserAsync(factory, email);

            OidcFlowClient flow = factory.CreateFlowClient();
            await flow.SignInWithEmailCodeAsync(email, "/Identity/Account/Login");
            return flow;
        }

        /// <summary>Requests an address with the browser, following nothing.</summary>
        /// <param name="flow">The browser.</param>
        /// <param name="address">The address, relative or on the test host.</param>
        /// <returns>The response.</returns>
        private static Task<HttpResponseMessage> GetAsync(OidcFlowClient flow, string address)
        {
            return flow.Browser.GetAsync(new Uri(address, UriKind.RelativeOrAbsolute), TestContext.Current.CancellationToken);
        }

        /// <summary>Builds an account entry address.</summary>
        /// <param name="section">The section to name, or null to name none.</param>
        /// <param name="carried">The values the application sends, as a query string.</param>
        /// <returns>The address.</returns>
        private static string AccountUrl(string? section, string carried)
        {
            return section is null ? "/account?" + carried : "/account?section=" + section + "&" + carried;
        }

        /// <summary>Opens the profile page as an application would link it and reads the back link.</summary>
        /// <param name="flow">The signed-in browser.</param>
        /// <param name="clientId">The client the link names, when it names one.</param>
        /// <param name="returnUrl">The return address the link names, when it names one.</param>
        /// <returns>The back link's address, or null when the page offers none.</returns>
        private static async Task<string?> BackLinkAsync(OidcFlowClient flow, string? clientId, string? returnUrl)
        {
            Dictionary<string, string?> query = [];
            if (clientId is not null)
            {
                query["client_id"] = clientId;
            }

            if (returnUrl is not null)
            {
                query["return_url"] = returnUrl;
            }

            using HttpResponseMessage response = await GetAsync(
                flow, QueryHelpers.AddQueryString("/Identity/Manage", query));
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            return BackLink(await HtmlPage.ReadAsync(response));
        }

        /// <summary>Reads the address of a page's link back to the application.</summary>
        /// <param name="page">An account page.</param>
        /// <returns>The link's address exactly as written, or null when the page offers none.</returns>
        private static string? BackLink(IDocument page)
        {
            return page.QuerySelector("a.tmi-topbar-back")?.GetAttribute("href");
        }

        /// <summary>Asserts that an address states the three values the application sent.</summary>
        /// <param name="address">An address on one of the engine's pages.</param>
        private static void AssertCarries(string address)
        {
            Dictionary<string, StringValues> query = HtmlPage.Split(address).Query;
            Assert.True(
                (string?)query.GetValueOrDefault("client_id") == "acme"
                && (string?)query.GetValueOrDefault("return_url") == ReturnUrl
                && (string?)query.GetValueOrDefault("ui_locales") == "ar",
                $"{address} does not carry the client, the return address and the language.");
        }
    }
}
