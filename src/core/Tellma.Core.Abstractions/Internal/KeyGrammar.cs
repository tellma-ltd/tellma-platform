// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

namespace Tellma.Core.Abstractions.Internal
{
    /// <summary>
    ///     The lowercase key grammar the platform's registered names share: job handler keys,
    ///     notification type keys and client event names are dotted keys, and notification categories
    ///     and channel keys are plain keys.
    /// </summary>
    /// <remarks>
    ///     A segment is one lowercase ASCII letter followed by any number of lowercase ASCII letters,
    ///     digits and hyphens (<c>[a-z][a-z0-9-]*</c>). A plain key is one segment; a dotted key is two
    ///     or more segments joined by dots (<c>^[a-z][a-z0-9-]*(\.[a-z][a-z0-9-]*)+$</c>). Matched
    ///     character by character rather than by a regular expression, so the check costs nothing and
    ///     its meaning is visible here.
    /// </remarks>
    internal static class KeyGrammar
    {
        /// <summary>Whether <paramref name="key" /> is a dotted key of at most <paramref name="maxLength" /> characters.</summary>
        /// <param name="key">The candidate; null is never a key.</param>
        /// <param name="maxLength">The most characters the key may have.</param>
        /// <returns>True when it is.</returns>
        public static bool IsDottedKey(string? key, int maxLength)
        {
            if (key is null || key.Length == 0 || key.Length > maxLength)
            {
                return false;
            }

            // Every segment between dots must be well formed, and there must be at least one dot.
            int segments = 0;
            foreach (Range range in key.AsSpan().Split('.'))
            {
                if (!IsSegment(key.AsSpan()[range]))
                {
                    return false;
                }

                segments++;
            }

            return segments >= 2;
        }

        /// <summary>Whether <paramref name="key" /> is a plain key of at most <paramref name="maxLength" /> characters.</summary>
        /// <param name="key">The candidate; null is never a key.</param>
        /// <param name="maxLength">The most characters the key may have.</param>
        /// <returns>True when it is.</returns>
        public static bool IsPlainKey(string? key, int maxLength)
        {
            return key is not null && key.Length <= maxLength && IsSegment(key);
        }

        /// <summary>Whether <paramref name="segment" /> is one segment: a lowercase letter, then lowercase letters, digits and hyphens.</summary>
        /// <param name="segment">The candidate segment.</param>
        /// <returns>True when it is.</returns>
        private static bool IsSegment(ReadOnlySpan<char> segment)
        {
            if (segment.IsEmpty || segment[0] is not (>= 'a' and <= 'z'))
            {
                return false;
            }

            foreach (char c in segment[1..])
            {
                if (c is not ((>= 'a' and <= 'z') or (>= '0' and <= '9') or '-'))
                {
                    return false;
                }
            }

            return true;
        }
    }
}
