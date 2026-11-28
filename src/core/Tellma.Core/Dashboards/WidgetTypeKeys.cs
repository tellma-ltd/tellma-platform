// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using System.Text.RegularExpressions;

namespace Tellma.Core.Dashboards
{
    /// <summary>
    ///     The dashboard widget type keys the platform registers, and the grammar every key follows:
    ///     the registering feature's name, a dot, then a lower-case name whose words are joined by
    ///     hyphens, such as <c>acme.cash-position</c>.
    /// </summary>
    internal static partial class WidgetTypeKeys
    {
        /// <summary>The open assignments waiting on the user.</summary>
        public const string AssignmentsToMe = "core.assignments-to-me";

        /// <summary>The open assignments the user handed to others.</summary>
        public const string AssignmentsByMe = "core.assignments-by-me";

        /// <summary>The recently visited pages.</summary>
        public const string Recents = "core.recents";

        /// <summary>The one type whose widgets show a stored or a coded report.</summary>
        public const string Report = "core.report";

        /// <summary>Whether <paramref name="key" /> follows the grammar.</summary>
        /// <param name="key">The candidate.</param>
        /// <returns>True when it does.</returns>
        public static bool IsWellFormed(string? key)
        {
            return key is not null && Grammar().IsMatch(key);
        }

        /// <summary>The key's first segment, which names the feature that registers it.</summary>
        /// <param name="key">A well-formed key.</param>
        /// <returns>The segment before the dot.</returns>
        public static string FeatureOf(string key)
        {
            ArgumentNullException.ThrowIfNull(key);
            int dot = key.IndexOf('.', StringComparison.Ordinal);
            return dot < 0 ? key : key[..dot];
        }

        /// <summary>The grammar: <c>feature.name</c>, the name's words joined by hyphens.</summary>
        /// <returns>The pattern.</returns>
        [GeneratedRegex(@"^[a-z][a-z0-9]*\.[a-z][a-z0-9]*(-[a-z0-9]+)*\z", RegexOptions.CultureInvariant, 1000)]
        private static partial Regex Grammar();
    }
}
