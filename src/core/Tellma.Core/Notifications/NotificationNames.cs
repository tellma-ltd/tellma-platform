// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using System.Text.RegularExpressions;

namespace Tellma.Core.Notifications
{
    /// <summary>
    ///     The grammars of notification type keys, categories, channel keys and client event names.
    ///     A dotted name (a type key or a client event name) is two or more segments separated by dots;
    ///     a plain name (a category or a channel key) is one segment. A segment is lowercase ASCII
    ///     letters, digits and hyphens, starting with a letter.
    /// </summary>
    internal static partial class NotificationNames
    {
        /// <summary>The most characters a notification type key may have: the width of the rows' type column.</summary>
        public const int MaxTypeKeyLength = 64;

        /// <summary>The most characters a notification category may have.</summary>
        public const int MaxCategoryLength = 32;

        /// <summary>The most characters a notification channel key may have: the width of the preferences' channel column.</summary>
        public const int MaxChannelKeyLength = 16;

        /// <summary>The most characters a client event name may have.</summary>
        public const int MaxClientEventNameLength = 64;

        /// <summary>Whether <paramref name="key" /> is a well-formed notification type key.</summary>
        /// <param name="key">The candidate.</param>
        /// <returns>True when it is.</returns>
        public static bool IsTypeKey(string? key)
        {
            return key is { Length: > 0 and <= MaxTypeKeyLength } && DottedName().IsMatch(key);
        }

        /// <summary>Whether <paramref name="category" /> is a well-formed notification category.</summary>
        /// <param name="category">The candidate.</param>
        /// <returns>True when it is.</returns>
        public static bool IsCategory(string? category)
        {
            return category is { Length: > 0 and <= MaxCategoryLength } && PlainName().IsMatch(category);
        }

        /// <summary>Whether <paramref name="key" /> is a well-formed notification channel key.</summary>
        /// <param name="key">The candidate.</param>
        /// <returns>True when it is.</returns>
        public static bool IsChannelKey(string? key)
        {
            return key is { Length: > 0 and <= MaxChannelKeyLength } && PlainName().IsMatch(key);
        }

        /// <summary>Whether <paramref name="name" /> is a well-formed client event name.</summary>
        /// <param name="name">The candidate.</param>
        /// <returns>True when it is.</returns>
        public static bool IsClientEventName(string? name)
        {
            return name is { Length: > 0 and <= MaxClientEventNameLength } && DottedName().IsMatch(name);
        }

        /// <summary>Two or more dot-separated segments.</summary>
        /// <returns>The expression.</returns>
        [GeneratedRegex(@"^[a-z][a-z0-9-]*(\.[a-z][a-z0-9-]*)+\z", RegexOptions.CultureInvariant, 1000)]
        private static partial Regex DottedName();

        /// <summary>One segment.</summary>
        /// <returns>The expression.</returns>
        [GeneratedRegex(@"^[a-z][a-z0-9-]*\z", RegexOptions.CultureInvariant, 1000)]
        private static partial Regex PlainName();
    }
}
