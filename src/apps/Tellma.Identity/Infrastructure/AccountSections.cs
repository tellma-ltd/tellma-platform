// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

namespace Tellma.Identity.Infrastructure
{
    /// <summary>
    ///     The sections of the account pages an application may link to by name, and the page
    ///     each one opens.
    ///     <para>
    ///         A closed list: the names are what a distribution writes into its own links, so they
    ///         stay stable while the pages behind them are free to move. A name outside the list
    ///         opens the profile rather than failing, so a link written for a newer server still
    ///         lands the user on their account.
    ///     </para>
    /// </summary>
    public static class AccountSections
    {
        /// <summary>The profile: name, language and how to address the user. Also the default.</summary>
        public const string Profile = "profile";

        /// <summary>The passkey list.</summary>
        public const string Passkeys = "passkeys";

        /// <summary>Authenticator-app enrollment.</summary>
        public const string Authenticator = "authenticator";

        /// <summary>The linked external logins.</summary>
        public const string ExternalLogins = "external-logins";

        /// <summary>The active sessions.</summary>
        public const string Sessions = "sessions";

        /// <summary>The page a section opens, as a page name within the identity area.</summary>
        /// <param name="section">The section name a caller gave, exactly as written.</param>
        /// <returns>The section's page; the profile page for a missing or unknown name.</returns>
        public static string PageOf(string? section)
        {
            return section switch
            {
                Passkeys => "/Manage/Passkeys",
                Authenticator => "/Manage/EnableAuthenticator",
                ExternalLogins => "/Manage/ExternalLogins",
                Sessions => "/Manage/Sessions",
                _ => "/Manage/Index",
            };
        }
    }
}
