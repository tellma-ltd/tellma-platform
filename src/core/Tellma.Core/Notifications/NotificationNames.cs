// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Tellma.Core.Abstractions.Internal;

namespace Tellma.Core.Notifications
{
    /// <summary>
    ///     The grammars of notification type keys, categories, channel keys and client event names.
    ///     A dotted name (a type key or a client event name) is two or more segments separated by dots;
    ///     a plain name (a category or a channel key) is one segment. A segment is lowercase ASCII
    ///     letters, digits and hyphens, starting with a letter: the grammar job handler keys follow
    ///     too, so both read it from one place.
    /// </summary>
    internal static class NotificationNames
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
            return KeyGrammar.IsDottedKey(key, MaxTypeKeyLength);
        }

        /// <summary>Whether <paramref name="category" /> is a well-formed notification category.</summary>
        /// <param name="category">The candidate.</param>
        /// <returns>True when it is.</returns>
        public static bool IsCategory(string? category)
        {
            return KeyGrammar.IsPlainKey(category, MaxCategoryLength);
        }

        /// <summary>Whether <paramref name="key" /> is a well-formed notification channel key.</summary>
        /// <param name="key">The candidate.</param>
        /// <returns>True when it is.</returns>
        public static bool IsChannelKey(string? key)
        {
            return KeyGrammar.IsPlainKey(key, MaxChannelKeyLength);
        }

        /// <summary>Whether <paramref name="name" /> is a well-formed client event name.</summary>
        /// <param name="name">The candidate.</param>
        /// <returns>True when it is.</returns>
        public static bool IsClientEventName(string? name)
        {
            return KeyGrammar.IsDottedKey(name, MaxClientEventNameLength);
        }
    }
}
