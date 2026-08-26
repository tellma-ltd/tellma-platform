// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

namespace Tellma.Core.Queryex.Binding
{
    /// <summary>
    ///     The two properties of a type that gate operations, plus the mapping from a type to the
    ///     column families that can store it.
    /// </summary>
    internal static class QueryexTypeFacts
    {
        /// <summary>
        ///     Whether values of the type can be compared for equality, grouped, or tested for
        ///     membership.
        /// </summary>
        /// <param name="type">The type to test.</param>
        /// <returns>True for every type except the spatial one.</returns>
        /// <remarks>
        ///     The backend can neither equate nor group spatial values. Surfacing that as a
        ///     bind-time diagnostic is the whole point: the alternative is a backend error text
        ///     reaching a user who wrote a perfectly ordinary-looking filter.
        /// </remarks>
        internal static bool IsEquatable(QueryexType type)
        {
            return type != QueryexType.QxGeography;
        }

        /// <summary>Whether values of the type have a meaningful order.</summary>
        /// <param name="type">The type to test.</param>
        /// <returns>True for every type except the spatial one and the identifier one.</returns>
        /// <remarks>
        ///     Identifiers are equatable but not ordered: the backend's ordering of them is a
        ///     byte-shuffling accident rather than a meaning, so exposing it would invite reports
        ///     that sort in an order nobody can explain or rely on.
        /// </remarks>
        internal static bool IsOrdered(QueryexType type)
        {
            return type is not (QueryexType.QxGeography or QueryexType.QxGuid);
        }

        /// <summary>Whether a column family can store values of the given type.</summary>
        /// <param name="type">The property's type in the language.</param>
        /// <param name="family">The declared column family.</param>
        /// <returns>True when the pairing is coherent.</returns>
        internal static bool Admits(QueryexType type, QueryexStoreFamily family)
        {
            return type switch
            {
                QueryexType.QxBool => family is QueryexStoreFamily.QxBit,
                QueryexType.QxNumeric => family is QueryexStoreFamily.QxTinyInt
                    or QueryexStoreFamily.QxSmallInt
                    or QueryexStoreFamily.QxInt
                    or QueryexStoreFamily.QxBigInt
                    or QueryexStoreFamily.QxDecimal,
                QueryexType.QxString => family is QueryexStoreFamily.QxChar
                    or QueryexStoreFamily.QxVarChar
                    or QueryexStoreFamily.QxNChar
                    or QueryexStoreFamily.QxNVarChar,
                QueryexType.QxGuid => family is QueryexStoreFamily.QxUniqueIdentifier,
                QueryexType.QxDate => family is QueryexStoreFamily.QxDate,

                // The legacy datetime family is admitted alongside datetime2 because plenty of
                // existing tables still use it; it is never adopted for a parameter slot, though,
                // since binding a precise instant as one would round the probe value.
                QueryexType.QxDateTime => family is QueryexStoreFamily.QxDateTime
                    or QueryexStoreFamily.QxDateTime2,
                QueryexType.QxDateTimeOffset => family is QueryexStoreFamily.QxDateTimeOffset,
                QueryexType.QxHierarchyId => family is QueryexStoreFamily.QxHierarchyId,
                QueryexType.QxGeography => family is QueryexStoreFamily.QxGeography,
                _ => false,
            };
        }
    }
}
