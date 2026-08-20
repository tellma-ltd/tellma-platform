// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Microsoft.AspNetCore.Http;
using Tellma.Identity.Infrastructure;

namespace Tellma.Identity.Tests.Infrastructure
{
    /// <summary>
    ///     The per-response widening of the <c>form-action</c> directive: what it turns a registered
    ///     URI into, and that two widenings on one page both survive.
    /// </summary>
    public sealed class CspFormActionTests
    {
        [Fact]
        public void Nothing_is_allowed_until_a_page_asks()
        {
            Assert.Null(CspFormAction.Allowed(new DefaultHttpContext()));
        }

        [Fact]
        public void Successive_calls_accumulate()
        {
            // The sign-in page needs both at once — the provider origins behind its federated
            // buttons and the callback of the client whose authorization it is finishing. A second
            // call replacing the first would silently disable whichever ran earlier, and the
            // symptom is a button that does nothing with a clean server log behind it.
            DefaultHttpContext context = new();
            CspFormAction.Allow(context, ["https://accounts.google.com"]);
            CspFormAction.Allow(context, ["https://acme.app.example.com/callback"]);

            Assert.Equal(
                ["https://accounts.google.com", "https://acme.app.example.com/callback"],
                CspFormAction.Allowed(context));
        }

        [Fact]
        public void Duplicates_across_calls_are_recorded_once()
        {
            DefaultHttpContext context = new();
            CspFormAction.Allow(context, ["https://acme.example.com/callback"]);
            CspFormAction.Allow(context, ["https://acme.example.com/callback"]);

            Assert.Equal(["https://acme.example.com/callback"], CspFormAction.Allowed(context));
        }

        [Fact]
        public void A_private_scheme_becomes_a_bare_scheme_source()
        {
            // A native client's callback is an opaque URI that CSP's host-source grammar cannot
            // describe, and the navigation hands off to the operating system rather than to a page.
            DefaultHttpContext context = new();
            CspFormAction.Allow(context, ["com.acme.app:/oauth/callback"]);

            Assert.Equal(["com.acme.app:"], CspFormAction.Allowed(context));
        }

        [Theory]
        [InlineData("")]
        [InlineData("   ")]
        [InlineData("not a uri")]
        [InlineData("https://acme.example.com/callback; script-src *")]
        [InlineData("https://acme.example.com/'unsafe-inline'")]
        public void A_uri_that_could_rewrite_the_policy_is_dropped(string uri)
        {
            DefaultHttpContext context = new();
            CspFormAction.Allow(context, [uri]);

            Assert.Null(CspFormAction.Allowed(context));
        }
    }
}
