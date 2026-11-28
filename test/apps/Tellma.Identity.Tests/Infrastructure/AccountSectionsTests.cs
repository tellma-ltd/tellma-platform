// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Tellma.Identity.Infrastructure;

namespace Tellma.Identity.Tests.Infrastructure
{
    /// <summary>
    ///     The section names an application writes into its account links, and the page each one
    ///     opens. The names are a published contract, so each is pinned to its page here.
    /// </summary>
    public sealed class AccountSectionsTests
    {
        [Theory]
        [InlineData("profile", "/Manage/Index")]
        [InlineData("passkeys", "/Manage/Passkeys")]
        [InlineData("authenticator", "/Manage/EnableAuthenticator")]
        [InlineData("external-logins", "/Manage/ExternalLogins")]
        [InlineData("sessions", "/Manage/Sessions")]
        public void Each_section_opens_its_own_page(string section, string page)
        {
            Assert.Equal(page, AccountSections.PageOf(section));
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData(" ")]
        [InlineData("billing")]

        // Names are exact: a different spelling is an unknown section, not a guess at one.
        [InlineData("Passkeys")]
        [InlineData("external_logins")]
        [InlineData("sessions ")]

        // A page name or a path is not a section name, so neither can steer the redirect.
        [InlineData("/Manage/Sessions")]
        [InlineData("../Account/Logout")]
        public void A_missing_or_unknown_section_opens_the_profile(string? section)
        {
            Assert.Equal("/Manage/Index", AccountSections.PageOf(section));
        }
    }
}
