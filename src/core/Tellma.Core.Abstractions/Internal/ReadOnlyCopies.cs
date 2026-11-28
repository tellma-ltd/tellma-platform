// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using System.Collections.ObjectModel;

namespace Tellma.Core.Abstractions.Internal
{
    /// <summary>
    ///     Read-only copies of the collections a contract value captures, so that a caller who keeps
    ///     the collection it passed in cannot change the value afterwards.
    /// </summary>
    internal static class ReadOnlyCopies
    {
        /// <summary>
        ///     Copies a sequence into a list that cannot be cast back to a mutable collection.
        /// </summary>
        /// <typeparam name="T">The element type.</typeparam>
        /// <param name="items">The items, in order.</param>
        /// <returns>A read-only copy holding the same items in the same order.</returns>
        public static ReadOnlyCollection<T> Of<T>(IEnumerable<T> items)
        {
            return Array.AsReadOnly([.. items]);
        }

        /// <summary>
        ///     Copies a list a constructor received, refusing a null list, a null entry and, when
        ///     <paramref name="requireOne" />, an empty list.
        /// </summary>
        /// <typeparam name="T">The entry type.</typeparam>
        /// <param name="items">The list.</param>
        /// <param name="requireOne">Whether the list must hold at least one entry.</param>
        /// <param name="parameterName">The constructor parameter the list came from.</param>
        /// <returns>A read-only copy holding the same entries in the same order.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="items" /> is null.</exception>
        /// <exception cref="ArgumentException">The list holds a null entry, or is empty when one is required.</exception>
        public static ReadOnlyCollection<T> OfEntries<T>(IReadOnlyList<T> items, bool requireOne, string parameterName)
            where T : class
        {
            ArgumentNullException.ThrowIfNull(items, parameterName);

            T[] copy = [.. items];
            if (requireOne && copy.Length == 0)
            {
                throw new ArgumentException("The list must hold at least one entry.", parameterName);
            }

            foreach (T item in copy)
            {
                if (item is null)
                {
                    throw new ArgumentException("The list must not hold a null entry.", parameterName);
                }
            }

            return Array.AsReadOnly(copy);
        }

        /// <summary>
        ///     Copies named arguments into a read-only dictionary keyed by ordinal comparison, which is
        ///     how every reader of an argument bag (message formatting, the problem body) looks names
        ///     up.
        /// </summary>
        /// <param name="arguments">The arguments.</param>
        /// <returns>A read-only copy; the shared empty instance when there are none.</returns>
        public static IReadOnlyDictionary<string, object?> Of(IReadOnlyDictionary<string, object?> arguments)
        {
            return arguments.Count == 0
                ? ReadOnlyDictionary<string, object?>.Empty
                : new ReadOnlyDictionary<string, object?>(new Dictionary<string, object?>(arguments, StringComparer.Ordinal));
        }
    }
}
