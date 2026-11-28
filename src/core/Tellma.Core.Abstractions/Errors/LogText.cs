// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using System.Globalization;

namespace Tellma.Core.Abstractions.Errors
{
    /// <summary>
    ///     Renders the lists inside the English log messages of the closed exception set, bounded so
    ///     that an outcome over ten thousand rows still logs one short line.
    /// </summary>
    internal static class LogText
    {
        /// <summary>How many items a rendered list shows before it summarises the rest.</summary>
        public const int MaxItems = 10;

        /// <summary>
        ///     Renders up to <see cref="MaxItems" /> items separated by commas, then the count of the
        ///     items left out: <c>5, 7, 9 and 12 more</c>.
        /// </summary>
        /// <typeparam name="T">The item type.</typeparam>
        /// <param name="items">The items.</param>
        /// <param name="render">Renders one item.</param>
        /// <returns>The rendered list; <c>none</c> when empty.</returns>
        public static string List<T>(IReadOnlyList<T> items, Func<T, string> render)
        {
            if (items.Count == 0)
            {
                return "none";
            }

            string shown = string.Join(", ", items.Take(MaxItems).Select(render));
            return items.Count <= MaxItems
                ? shown
                : string.Create(CultureInfo.InvariantCulture, $"{shown} and {items.Count - MaxItems} more");
        }
    }
}
