// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Tellma.Identity.Services.Invitations;

namespace Tellma.Identity.Tests.Services.Invitations
{
    /// <summary>
    ///     The origin comparison that decides where an accepted invitation may send a user.
    ///     <para>
    ///         This is the only place in the server where a redirect may leave the authority's own
    ///         origin, so it is the only place a mistake becomes an open redirect. Every case here
    ///         is a URL that reads like the registered origin at a glance and is not it.
    ///     </para>
    /// </summary>
    public sealed class InvitationReturnUrlValidatorTests
    {
        private const string Origin = "https://acme.app.tellma.com";

        [Theory]
        [InlineData("https://acme.app.tellma.com")]
        [InlineData("https://acme.app.tellma.com/")]
        [InlineData("https://acme.app.tellma.com/welcome")]
        [InlineData("https://acme.app.tellma.com/welcome?tab=1#top")]
        [InlineData("https://ACME.App.Tellma.Com/welcome")]
        public void A_destination_on_the_registered_origin_is_allowed(string returnUrl)
        {
            Assert.True(InvitationReturnUrlValidator.IsSameOrigin(returnUrl, Origin));
        }

        [Theory]
        // A different host that merely starts the same way — what a prefix comparison would accept.
        [InlineData("https://acme.app.tellma.com.evil.test/welcome")]
        [InlineData("https://acme.app.tellma.com.evil.test")]

        // Userinfo, so the registered origin appears before an @ and the real host after it.
        [InlineData("https://acme.app.tellma.com@evil.test/")]
        [InlineData("https://user:pass@acme.app.tellma.com/welcome")]

        // Same host, weaker or different transport.
        [InlineData("http://acme.app.tellma.com/welcome")]
        [InlineData("https://acme.app.tellma.com:8443/welcome")]

        // A neighbouring tenant of the same authority is still not this client's origin.
        [InlineData("https://other.app.tellma.com/welcome")]

        // Schemes a browser would hand to something other than a page.
        [InlineData("javascript:alert(1)")]
        [InlineData("data:text/html,<script>alert(1)</script>")]
        [InlineData("file:///etc/passwd")]

        // Protocol-relative and backslash forms, which some parsers read as a host.
        [InlineData("//evil.test/welcome")]
        [InlineData("https:/\\evil.test")]
        public void A_destination_anywhere_else_is_refused(string returnUrl)
        {
            Assert.False(InvitationReturnUrlValidator.IsSameOrigin(returnUrl, Origin));
        }

        [Fact]
        public void An_origin_that_is_not_a_usable_url_authorizes_nothing()
        {
            // A registration with a malformed origin must fail closed rather than comparing
            // loosely enough to let something through.
            Assert.False(InvitationReturnUrlValidator.IsSameOrigin("https://acme.app.tellma.com", "not-a-url"));
        }
    }
}
