// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Localization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using OpenIddict.Abstractions;
using OpenIddict.Server;
using OpenIddict.Server.AspNetCore;
using System.Globalization;
using Tellma.Identity.Infrastructure;
using Tellma.Identity.Options;

namespace Tellma.Identity.Tests.Infrastructure
{
    /// <summary>
    ///     How a language list selects one of the offered languages, and when an authorization
    ///     request's own list decides the language of its pages. The list stands in for the
    ///     browser's preference; it never overrides a language chosen on the URL or by the user.
    /// </summary>
    public sealed class RequestedLanguageTests
    {
        [Theory]
        [InlineData("ar", "ar")]
        [InlineData("en", "en")]
        [InlineData("AR", "ar")]

        // A regional form selects the language it is a form of.
        [InlineData("ar-SA", "ar")]
        [InlineData("en-GB-oxendict", "en")]

        // The first tag that selects anything wins, and tags that select nothing are passed over.
        [InlineData("fr ar en", "ar")]
        [InlineData("fr-CA de ar-EG", "ar")]
        [InlineData("en ar", "en")]

        // Malformed entries are passed over like unknown ones.
        [InlineData("<b> ar", "ar")]
        public void A_language_list_selects_the_first_offered_language_it_names(string uiLocales, string expected)
        {
            Assert.Equal(expected, Catalog().Match(uiLocales));
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("  ")]
        [InlineData("fr")]
        [InlineData("fr-CA de")]

        // A tag is narrowed, never widened: a longer tag that merely starts like a language is not it.
        [InlineData("arabic")]
        [InlineData("a")]
        [InlineData("ar_SA")]
        public void A_list_that_names_no_offered_language_selects_nothing(string? uiLocales)
        {
            Assert.Null(Catalog().Match(uiLocales));
        }

        [Fact]
        public void A_language_the_deployment_does_not_offer_cannot_be_selected()
        {
            // Narrowing the offered languages narrows what a request can ask for as well.
            LanguageCatalog english = Catalog("en");

            Assert.Null(english.Match("ar"));
            Assert.Equal("en", english.Match("ar en"));
        }

        [Fact]
        public async Task A_pushed_requests_language_applies_when_only_the_default_decided()
        {
            Assert.Equal("ar", await RenderedLanguageAsync(requested: "ar-SA en", decidedBy: null));
        }

        [Fact]
        public async Task A_pushed_requests_language_outranks_the_browsers_preference()
        {
            Assert.Equal(
                "ar", await RenderedLanguageAsync(requested: "ar", decidedBy: new AcceptLanguageHeaderRequestCultureProvider()));
        }

        [Fact]
        public async Task A_language_the_user_picked_stands()
        {
            // The cookie is what the language picker writes. Overriding it would leave the picker
            // on the consent page doing nothing.
            Assert.Equal("en", await RenderedLanguageAsync(requested: "ar", decidedBy: new CookieRequestCultureProvider()));
        }

        [Fact]
        public async Task A_language_named_on_the_url_stands()
        {
            Assert.Equal("en", await RenderedLanguageAsync(requested: "ar", decidedBy: new UiLocalesRequestCultureProvider()));
        }

        [Theory]
        [InlineData(null)]
        [InlineData("fr de")]
        public async Task A_request_that_asks_for_nothing_usable_changes_nothing(string? requested)
        {
            Assert.Equal("en", await RenderedLanguageAsync(requested, decidedBy: null));
        }

        /// <summary>Builds the catalog of a deployment.</summary>
        /// <param name="languages">The configured languages; none configured offers everything shipped.</param>
        /// <returns>The catalog.</returns>
        private static LanguageCatalog Catalog(params string[] languages)
        {
            TellmaIdentityOptions options = new();
            foreach (string language in languages)
            {
                options.Ui.Languages.Add(language);
            }

            return new LanguageCatalog(Microsoft.Extensions.Options.Options.Create(options));
        }

        /// <summary>
        ///     Runs the filter around a stand-in for the rest of the request, and reports the
        ///     language that stand-in would have rendered in.
        /// </summary>
        /// <param name="requested">The authorization request's <c>ui_locales</c>.</param>
        /// <param name="decidedBy">What request localization says decided the language so far.</param>
        /// <returns>The UI culture in force while the rest of the request ran.</returns>
        private static async Task<string> RenderedLanguageAsync(string? requested, IRequestCultureProvider? decidedBy)
        {
            ServiceCollection services = new();
            services.AddSingleton(Catalog());
            await using ServiceProvider provider = services.BuildServiceProvider();

            DefaultHttpContext http = new() { RequestServices = provider };

            // Request localization has already run and settled on English, by whichever source.
            var english = CultureInfo.GetCultureInfo("en");
            http.Features.Set<IRequestCultureFeature>(new RequestCultureFeature(new RequestCulture(english), decidedBy));

            // The restored authorization request, where the protocol handler leaves it.
            http.Features.Set(new OpenIddictServerAspNetCoreFeature
            {
                Transaction = new OpenIddictServerTransaction { Request = new OpenIddictRequest { UiLocales = requested } },
            });

            ActionContext action = new(http, new RouteData(), new ActionDescriptor());
            ResourceExecutingContext executing = new(action, [], []);

            CultureInfo previous = CultureInfo.CurrentUICulture;
            CultureInfo.CurrentUICulture = english;
            try
            {
                string? rendered = null;
                string? formatted = null;
                await new RequestedLanguageAttribute().OnResourceExecutionAsync(executing, () =>
                {
                    rendered = CultureInfo.CurrentUICulture.Name;
                    formatted = CultureInfo.CurrentCulture.Name;
                    return Task.FromResult(new ResourceExecutedContext(action, []));
                });

                // The feature is what later components ask, so it agrees with what rendered; and
                // when the request's language applied, dates and numbers follow it too.
                Assert.Equal(rendered, http.Features.Get<IRequestCultureFeature>()!.RequestCulture.UICulture.Name);
                Assert.True(rendered == "en" || formatted == rendered);

                // The language was in force for the rest of the request and no longer.
                Assert.Same(english, CultureInfo.CurrentUICulture);
                return rendered!;
            }
            finally
            {
                CultureInfo.CurrentUICulture = previous;
            }
        }
    }
}
