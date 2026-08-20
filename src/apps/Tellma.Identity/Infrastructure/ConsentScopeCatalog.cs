// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using System.Collections.Frozen;

namespace Tellma.Identity.Infrastructure
{
    /// <summary>One line on the consent screen: what an application is asking for, in words.</summary>
    /// <param name="Scope">The raw scope name, which is what an unknown one falls back to.</param>
    /// <param name="IconName">The glyph beside it.</param>
    /// <param name="TitleKey">Resource key for the heading, or null when the raw name is the heading.</param>
    /// <param name="DetailKey">Resource key for the sentence under it.</param>
    public sealed record ConsentScope(string Scope, string IconName, string? TitleKey, string DetailKey);

    /// <summary>
    ///     Turns the scopes an authorization asks for into something a person can weigh.
    ///     <para>
    ///         A scope name is an identifier for a protocol, not a description for a human, and
    ///         "openid profile tellma_api" tells a user nothing about what they are agreeing to.
    ///         Every scope the authority issues has an entry here; anything else is still listed,
    ///         under its own name, rather than hidden — a request the screen cannot explain is
    ///         exactly the one a user most needs to see.
    ///     </para>
    /// </summary>
    public static class ConsentScopeCatalog
    {
        /// <summary>The scopes this authority issues, in the order they read best.</summary>
        private static readonly FrozenDictionary<string, ConsentScope> Known =
            new Dictionary<string, ConsentScope>(StringComparer.Ordinal)
            {
                ["openid"] = new("openid", "user-round", "ScopeOpenIdTitle", "ScopeOpenIdDetail"),
                ["profile"] = new("profile", "user-round", "ScopeProfileTitle", "ScopeProfileDetail"),
                ["email"] = new("email", "mail", "ScopeEmailTitle", "ScopeEmailDetail"),
                ["offline_access"] = new("offline_access", "shield-check", "ScopeOfflineAccessTitle", "ScopeOfflineAccessDetail"),
                [TellmaIdentityConstants.ApiScope] = new(
                    TellmaIdentityConstants.ApiScope, "book-open", "ScopeApiTitle", "ScopeApiDetail"),
            }.ToFrozenDictionary(StringComparer.Ordinal);

        /// <summary>Describes each scope in a space-delimited request.</summary>
        /// <param name="scope">The request's <c>scope</c> value.</param>
        /// <returns>One entry per distinct scope, in the order requested.</returns>
        public static IReadOnlyList<ConsentScope> Describe(string? scope)
        {
            List<ConsentScope> described = [];
            foreach (string name in (scope ?? string.Empty).Split(' ', StringSplitOptions.RemoveEmptyEntries
                | StringSplitOptions.TrimEntries))
            {
                if (described.Exists(entry => string.Equals(entry.Scope, name, StringComparison.Ordinal)))
                {
                    continue;
                }

                described.Add(Known.TryGetValue(name, out ConsentScope? known)
                    ? known
                    : new ConsentScope(name, "circle-alert", TitleKey: null, "ScopeUnknownDetail"));
            }

            return described;
        }
    }
}
