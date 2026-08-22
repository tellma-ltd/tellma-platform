// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using System.Text;
using Tellma.Core.Queryex.Binding;

namespace Tellma.Core.Queryex.Lowering
{
    /// <summary>
    ///     Decides the SQL type each parameter binds as.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         Two rules, and the second is the subtle one. A slot no column informs takes the
    ///         default for its type in the language. A slot whose value is compared against a column
    ///         may take that column's type <i>family</i>, and never its facets: a data provider
    ///         truncates a string to the slot's declared length and rounds a decimal to its declared
    ///         scale, so adopting a facet would compare a value the caller never supplied. A
    ///         parameter wider than its column costs nothing, because the backend converts only the
    ///         parameter side.
    ///     </para>
    ///     <para>
    ///         Adopting a family is worth doing at all because of what happens when the families
    ///         differ: comparing a Unicode parameter against a non-Unicode column converts the
    ///         <i>column</i>, which turns an index seek into a scan of the whole table.
    ///     </para>
    /// </remarks>
    internal static class StoreTypes
    {
        /// <summary>The widest a bounded Unicode parameter is declared.</summary>
        private const int UnicodeBound = 4000;

        /// <summary>The widest a bounded non-Unicode parameter is declared.</summary>
        private const int NarrowBound = 8000;

        /// <summary>The type a column of a given property has.</summary>
        /// <param name="property">The property.</param>
        /// <returns>Its declared type, or the default for the property's type in the language.</returns>
        internal static QueryexStoreType OfProperty(PropertyDescriptor property)
        {
            return property.StoreType ?? Default(BoundTypes.FromPublic(property.Type), null, 0, 0);
        }

        /// <summary>The type a slot binds as when no column informs it.</summary>
        /// <param name="type">The value's type in the language.</param>
        /// <param name="value">The value, when it is fixed at compile time.</param>
        /// <param name="precision">The written significant-digit count, for a number.</param>
        /// <param name="scale">The written scale, for a number.</param>
        /// <returns>The type to bind as.</returns>
        internal static QueryexStoreType Default(
            BoundType type,
            object? value,
            byte precision,
            byte scale)
        {
            return type switch
            {
                BoundType.Bool => QueryexStoreType.QxBit,

                // A written number binds at the precision and scale it was written with, which is
                // what keeps a trailing zero meaningful. A value the host supplies later has no
                // written form to read, so the facets are left for the provider to derive — the only
                // choice that cannot round what the caller passes.
                BoundType.Numeric => precision > 0
                    ? QueryexStoreType.QxDecimal(precision, scale)
                    : new QueryexStoreType(QueryexStoreFamily.QxDecimal),

                BoundType.String => QueryexStoreType.QxNVarChar(
                    value is string text && text.Length > UnicodeBound ? null : UnicodeBound),

                BoundType.Guid => QueryexStoreType.QxUniqueIdentifier,
                BoundType.Date => QueryexStoreType.QxDate,
                BoundType.DateTime => QueryexStoreType.QxDateTime2(7),
                BoundType.DateTimeOffset => QueryexStoreType.QxDateTimeOffset(7),
                BoundType.HierarchyId => QueryexStoreType.QxHierarchyId,
                BoundType.Geography => QueryexStoreType.QxGeography,

                // Neither the absent-value type nor the failure type is ever given a slot: the
                // absent value is written as the backend's own keyword, and nothing that failed to
                // bind reaches lowering at all.
                BoundType.Null or BoundType.Error => QueryexStoreType.QxNVarChar(UnicodeBound),
                _ => QueryexStoreType.QxNVarChar(UnicodeBound),
            };
        }

        /// <summary>The type a slot binds as, given the column its value is compared against.</summary>
        /// <param name="type">The value's type in the language.</param>
        /// <param name="value">The value, when it is fixed at compile time.</param>
        /// <param name="precision">The written significant-digit count, for a number.</param>
        /// <param name="scale">The written scale, for a number.</param>
        /// <param name="column">The column's type, when one informs the slot.</param>
        /// <returns>The type to bind as.</returns>
        internal static QueryexStoreType Adopted(
            BoundType type,
            object? value,
            byte precision,
            byte scale,
            QueryexStoreType? column)
        {
            QueryexStoreType fallback = Default(type, value, precision, scale);
            if (column is not QueryexStoreType declared || type != BoundType.String)
            {
                // Only the character families are ever adopted. Binding a fractional number as a
                // whole one or a precise instant as the legacy date type would round the value being
                // probed with, and the backend keeps the numeric and date families seekable across
                // widths anyway, so there is nothing to win there.
                return fallback;
            }

            if (declared.Family is not (QueryexStoreFamily.QxChar or QueryexStoreFamily.QxVarChar))
            {
                return fallback;
            }

            // The column's code page is not part of what a host declares, so the only values that
            // can be shown to survive it are the ones every code page agrees on. A value that might
            // not survive binds Unicode and accepts the scan.
            if (value is not string text || !Ascii.IsValid(text))
            {
                return fallback;
            }

            // Variable-width even against a fixed-width column: a fixed-width parameter would be
            // padded, and padding is a difference the matching functions can see. The two are one
            // family as far as seeking is concerned, so nothing is lost.
            return QueryexStoreType.QxVarChar(text.Length > NarrowBound ? null : NarrowBound);
        }
    }
}
