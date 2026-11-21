// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Tellma.Core.Queryex;

namespace Tellma.Core.Abstractions.Access
{
    /// <summary>
    ///     The filters access shares: the one window predicate over role memberships, as a tree for
    ///     criteria and as SQL for raw statements, so the two can never disagree on when a membership
    ///     is current.
    /// </summary>
    public static class AccessFilters
    {
        /// <summary>The longest table alias <see cref="CurrentMembershipSql" /> accepts.</summary>
        private const int MaxAliasLength = 128;

        /// <summary>
        ///     The memberships current at the server's time: started at or before now, and not ended at
        ///     now. A tree over <c>core.RoleMembership</c>, of the current Queryex version; a criterion
        ///     that reads memberships composes it, such as
        ///     <c>And([Leaf("UserId = me()"), CurrentMembership])</c>.
        /// </summary>
        public static FilterTree CurrentMembership { get; } =
            FilterTree.Leaf("(StartsAt is null or StartsAt <= now()) and (EndsAt is null or EndsAt > now())");

        /// <summary>
        ///     The same window as <see cref="CurrentMembership" />, as SQL over
        ///     <c>SYSUTCDATETIME()</c> for a statement on <c>core.RoleMemberships</c>. The predicate is
        ///     parenthesized as a whole, so it composes with any connective.
        /// </summary>
        /// <param name="alias">
        ///     The alias of <c>core.RoleMemberships</c> in the statement: an ASCII letter or underscore
        ///     followed by letters, digits or underscores, at most 128 characters. It is bracketed in
        ///     the text.
        /// </param>
        /// <returns>The predicate.</returns>
        /// <exception cref="ArgumentException">The alias is not such an identifier.</exception>
        public static string CurrentMembershipSql(string alias)
        {
            // The alias reaches the SQL text, so it is held to a plain identifier's form: nothing in it
            // can close the bracket or end the predicate.
            string checkedAlias = IsIdentifier(alias)
                ? alias
                : throw new ArgumentException(
                    "A table alias is an ASCII letter or underscore followed by letters, digits or underscores, at most 128 characters.",
                    nameof(alias));

            return $"(([{checkedAlias}].[StartsAt] IS NULL OR [{checkedAlias}].[StartsAt] <= SYSUTCDATETIME()) AND ([{checkedAlias}].[EndsAt] IS NULL OR [{checkedAlias}].[EndsAt] > SYSUTCDATETIME()))";
        }

        /// <summary>Whether a string is a plain SQL identifier.</summary>
        /// <param name="alias">The string.</param>
        /// <returns>True for an ASCII letter or underscore followed by letters, digits or underscores, within the length limit.</returns>
        private static bool IsIdentifier(string? alias)
        {
            if (alias is null || alias.Length is 0 or > MaxAliasLength || !(char.IsAsciiLetter(alias[0]) || alias[0] == '_'))
            {
                return false;
            }

            foreach (char character in alias)
            {
                if (!(char.IsAsciiLetterOrDigit(character) || character == '_'))
                {
                    return false;
                }
            }

            return true;
        }
    }
}
