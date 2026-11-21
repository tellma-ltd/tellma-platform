// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

namespace Tellma.Core.Tenancy
{
    // The tier scheme is the identity server's (spec 0003 §9): a session holds the highest tier its
    // sign-in satisfied, a higher tier satisfies a lower one, and a list of requested tiers requires
    // its highest. The host judges sessions and forwards step-up requests by the same scheme, so a
    // bar the identity server can satisfy is one the host accepts once it is satisfied.
    /// <summary>
    ///     The platform's assurance tiers, ranked as the identity server ranks them: <c>aal1</c>, then
    ///     <c>aal2</c> (or its synonym <c>phr</c>), then <c>aal3</c> (or <c>phrh</c>).
    /// </summary>
    internal static class AssuranceTiers
    {
        /// <summary>Single-factor sign-in.</summary>
        public const string Aal1 = "urn:tellma:acr:aal1";

        /// <summary>Two distinct factors, or one multi-factor authenticator such as a passkey.</summary>
        public const string Aal2 = "urn:tellma:acr:aal2";

        /// <summary>A phishing-resistant, device-bound authenticator.</summary>
        public const string Aal3 = "urn:tellma:acr:aal3";

        /// <summary>The phishing-resistant synonym the identity server accepts for <see cref="Aal2" />.</summary>
        public const string PhishingResistant = "phr";

        /// <summary>The phishing-resistant hardware synonym the identity server accepts for <see cref="Aal3" />.</summary>
        public const string PhishingResistantHardware = "phrh";

        /// <summary>The rank of one class in the scheme.</summary>
        /// <param name="acr">The class.</param>
        /// <returns>1 to 3, or null when the class is outside the scheme.</returns>
        public static int? Rank(string? acr)
        {
            return acr switch
            {
                Aal1 => 1,
                Aal2 or PhishingResistant => 2,
                Aal3 or PhishingResistantHardware => 3,
                _ => null,
            };
        }

        /// <summary>
        ///     The rank a space-separated list of requested classes demands: its strongest member's, since
        ///     the identity server requires the highest tier a request names.
        /// </summary>
        /// <param name="values">The space-separated classes.</param>
        /// <returns>The strongest member's rank, or null when the list is empty or any member is outside the scheme.</returns>
        public static int? RankList(string? values)
        {
            int? strongest = null;
            foreach (string value in (values ?? string.Empty).Split(' ', StringSplitOptions.RemoveEmptyEntries))
            {
                if (Rank(value) is not int rank)
                {
                    return null;
                }

                strongest = strongest is int current && current > rank ? current : rank;
            }

            return strongest;
        }

        /// <summary>
        ///     Whether a session's class satisfies a required class: by rank when both are in the scheme,
        ///     a higher tier satisfying a lower one; otherwise only the very class required, as a
        ///     third-party authority's classes cannot be ranked.
        /// </summary>
        /// <param name="held">The session's class, or null when it states none.</param>
        /// <param name="required">The class required.</param>
        /// <returns>True when the session reaches the class.</returns>
        public static bool Satisfies(string? held, string required)
        {
            ArgumentNullException.ThrowIfNull(required);
            return Rank(held) is int heldRank && Rank(required) is int requiredRank
                ? heldRank >= requiredRank
                : string.Equals(held, required, StringComparison.Ordinal);
        }
    }
}
