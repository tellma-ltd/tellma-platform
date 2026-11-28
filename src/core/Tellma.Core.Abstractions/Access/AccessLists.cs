// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using System.Collections.ObjectModel;

namespace Tellma.Core.Abstractions.Access
{
    /// <summary>The defensive copies the access records take of the lists they are given.</summary>
    internal static class AccessLists
    {
        /// <summary>
        ///     Copies a list into a read-only one, refusing a null list or a null item, so a record
        ///     never changes after construction whatever its caller does with the list it passed.
        /// </summary>
        /// <typeparam name="T">The item type.</typeparam>
        /// <param name="items">The list.</param>
        /// <param name="parameterName">The caller's parameter name, for the exception.</param>
        /// <returns>The read-only copy.</returns>
        /// <exception cref="ArgumentNullException">The list is null.</exception>
        /// <exception cref="ArgumentException">An item is null.</exception>
        public static ReadOnlyCollection<T> Copy<T>(IReadOnlyList<T> items, string parameterName)
            where T : class
        {
            ArgumentNullException.ThrowIfNull(items, parameterName);
            if (items.Count == 0)
            {
                return [];
            }

            // --- Copied, then checked, so a list changed concurrently cannot slip a null past the check ---
            T[] copy = [.. items];
            foreach (T item in copy)
            {
                if (item is null)
                {
                    throw new ArgumentException("The list must not contain a null item.", parameterName);
                }
            }

            return Array.AsReadOnly(copy);
        }
    }
}
