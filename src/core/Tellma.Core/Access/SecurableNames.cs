// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Tellma.Core.Abstractions.Access;

namespace Tellma.Core.Access
{
    // The grammar is spec 0014 §2's.
    /// <summary>
    ///     The grammars of securable names. A resource is two or more segments of an ASCII letter
    ///     followed by ASCII letters and digits, joined by dots (<c>gl.Center</c>,
    ///     <c>core.Settings.General</c>); an action is one PascalCase identifier (<c>Save</c>). The
    ///     wildcard <c>*</c> of stored permissions matches neither.
    /// </summary>
    internal static class SecurableNames
    {
        /// <summary>The most characters a resource may have.</summary>
        public const int MaxResourceLength = 128;

        /// <summary>The most characters an action may have.</summary>
        public const int MaxActionLength = 32;

        /// <summary>Whether <paramref name="resource" /> is a well-formed resource.</summary>
        /// <param name="resource">The candidate.</param>
        /// <returns>True when it is.</returns>
        public static bool IsResource(string? resource)
        {
            if (string.IsNullOrEmpty(resource) || resource.Length > MaxResourceLength)
            {
                return false;
            }

            // --- Two or more segments, each a letter and then letters or digits ---
            string[] segments = resource.Split('.');
            return segments.Length >= 2 && segments.All(static segment => segment.Length > 0 && char.IsAsciiLetter(segment[0]) && segment.All(char.IsAsciiLetterOrDigit));
        }

        /// <summary>Whether <paramref name="action" /> is a well-formed action.</summary>
        /// <param name="action">The candidate.</param>
        /// <returns>True when it is.</returns>
        public static bool IsAction(string? action)
        {
            return !string.IsNullOrEmpty(action)
                && action.Length <= MaxActionLength
                && char.IsAsciiLetterUpper(action[0])
                && action.All(char.IsAsciiLetterOrDigit);
        }

        /// <summary>Whether <paramref name="name" /> is the wildcard, which no registered resource or action may be.</summary>
        /// <param name="name">The candidate.</param>
        /// <returns>True when it is.</returns>
        public static bool IsWildcard(string? name)
        {
            return string.Equals(name, AccessActions.Wildcard, StringComparison.Ordinal);
        }
    }
}
