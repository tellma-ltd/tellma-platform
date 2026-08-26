// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

namespace Tellma.Core.Queryex
{
    /// <summary>
    ///     The closed set of SQL Server type families a column may declare.
    /// </summary>
    /// <remarks>
    ///     Members carry a <c>Qx</c> prefix throughout. Most of these names are also platform type
    ///     names, which the repository's analyzers reject; prefixing every member rather than only
    ///     the colliding ones keeps the set readable as one thing.
    /// </remarks>
    public enum QueryexStoreFamily
    {
        /// <summary>The <c>bit</c> family.</summary>
        QxBit,

        /// <summary>The <c>tinyint</c> family.</summary>
        QxTinyInt,

        /// <summary>The <c>smallint</c> family.</summary>
        QxSmallInt,

        /// <summary>The <c>int</c> family.</summary>
        QxInt,

        /// <summary>The <c>bigint</c> family.</summary>
        QxBigInt,

        /// <summary>The <c>decimal</c> family.</summary>
        QxDecimal,

        /// <summary>The fixed-width non-Unicode character family.</summary>
        QxChar,

        /// <summary>The variable-width non-Unicode character family.</summary>
        QxVarChar,

        /// <summary>The fixed-width Unicode character family.</summary>
        QxNChar,

        /// <summary>The variable-width Unicode character family.</summary>
        QxNVarChar,

        /// <summary>The <c>uniqueidentifier</c> family.</summary>
        QxUniqueIdentifier,

        /// <summary>The <c>date</c> family.</summary>
        QxDate,

        /// <summary>The legacy <c>datetime</c> family.</summary>
        QxDateTime,

        /// <summary>The <c>datetime2</c> family.</summary>
        QxDateTime2,

        /// <summary>The <c>datetimeoffset</c> family.</summary>
        QxDateTimeOffset,

        /// <summary>The <c>hierarchyid</c> family.</summary>
        QxHierarchyId,

        /// <summary>The <c>geography</c> family.</summary>
        QxGeography,
    }

    /// <summary>
    ///     A SQL Server column type as a closed, structured value: a family plus its facets.
    /// </summary>
    /// <remarks>
    ///     Structured rather than textual, so no host-supplied type text can reach emitted SQL
    ///     through a new door. Consulted only to type the parameter slots whose values are compared
    ///     against a column; the language's own semantics never read it.
    /// </remarks>
    /// <param name="Family">The type family.</param>
    /// <param name="Size">
    ///     Length for the character families, where null means the unbounded form; precision for
    ///     <see cref="QueryexStoreFamily.QxDecimal" />; fractional-second scale for the time
    ///     families. Null where the family has no such facet.
    /// </param>
    /// <param name="Scale">The scale, for <see cref="QueryexStoreFamily.QxDecimal" /> only.</param>
    public readonly record struct QueryexStoreType(
        QueryexStoreFamily Family,
        int? Size = null,
        int? Scale = null)
    {
        /// <summary>The <c>bit</c> type.</summary>
        public static QueryexStoreType QxBit { get; } = new(QueryexStoreFamily.QxBit);

        /// <summary>The <c>tinyint</c> type.</summary>
        public static QueryexStoreType QxTinyInt { get; } = new(QueryexStoreFamily.QxTinyInt);

        /// <summary>The <c>smallint</c> type.</summary>
        public static QueryexStoreType QxSmallInt { get; } = new(QueryexStoreFamily.QxSmallInt);

        /// <summary>The <c>int</c> type.</summary>
        public static QueryexStoreType QxInt { get; } = new(QueryexStoreFamily.QxInt);

        /// <summary>The <c>bigint</c> type.</summary>
        public static QueryexStoreType QxBigInt { get; } = new(QueryexStoreFamily.QxBigInt);

        /// <summary>The <c>uniqueidentifier</c> type.</summary>
        public static QueryexStoreType QxUniqueIdentifier { get; } = new(QueryexStoreFamily.QxUniqueIdentifier);

        /// <summary>The <c>date</c> type.</summary>
        public static QueryexStoreType QxDate { get; } = new(QueryexStoreFamily.QxDate);

        /// <summary>The legacy <c>datetime</c> type.</summary>
        public static QueryexStoreType QxDateTime { get; } = new(QueryexStoreFamily.QxDateTime);

        /// <summary>The <c>hierarchyid</c> type.</summary>
        public static QueryexStoreType QxHierarchyId { get; } = new(QueryexStoreFamily.QxHierarchyId);

        /// <summary>The <c>geography</c> type.</summary>
        public static QueryexStoreType QxGeography { get; } = new(QueryexStoreFamily.QxGeography);

        /// <summary>A <c>decimal</c> type with an explicit precision and scale.</summary>
        /// <param name="precision">The total number of digits, from 1 to 38.</param>
        /// <param name="scale">The number of digits to the right of the point.</param>
        /// <returns>The described type.</returns>
        public static QueryexStoreType QxDecimal(int precision, int scale)
        {
            return new QueryexStoreType(QueryexStoreFamily.QxDecimal, precision, scale);
        }

        /// <summary>A <c>datetime2</c> type with an explicit fractional-second scale.</summary>
        /// <param name="scale">The fractional-second scale, from 0 to 7.</param>
        /// <returns>The described type.</returns>
        public static QueryexStoreType QxDateTime2(int scale)
        {
            return new QueryexStoreType(QueryexStoreFamily.QxDateTime2, scale);
        }

        /// <summary>A <c>datetimeoffset</c> type with an explicit fractional-second scale.</summary>
        /// <param name="scale">The fractional-second scale, from 0 to 7.</param>
        /// <returns>The described type.</returns>
        public static QueryexStoreType QxDateTimeOffset(int scale)
        {
            return new QueryexStoreType(QueryexStoreFamily.QxDateTimeOffset, scale);
        }

        /// <summary>A fixed-width non-Unicode character type.</summary>
        /// <param name="length">The width in characters.</param>
        /// <returns>The described type.</returns>
        public static QueryexStoreType QxChar(int length)
        {
            return new QueryexStoreType(QueryexStoreFamily.QxChar, length);
        }

        /// <summary>A variable-width non-Unicode character type.</summary>
        /// <param name="length">The maximum width, or null for the unbounded form.</param>
        /// <returns>The described type.</returns>
        public static QueryexStoreType QxVarChar(int? length)
        {
            return new QueryexStoreType(QueryexStoreFamily.QxVarChar, length);
        }

        /// <summary>A fixed-width Unicode character type.</summary>
        /// <param name="length">The width in characters.</param>
        /// <returns>The described type.</returns>
        public static QueryexStoreType QxNChar(int length)
        {
            return new QueryexStoreType(QueryexStoreFamily.QxNChar, length);
        }

        /// <summary>A variable-width Unicode character type.</summary>
        /// <param name="length">The maximum width, or null for the unbounded form.</param>
        /// <returns>The described type.</returns>
        public static QueryexStoreType QxNVarChar(int? length)
        {
            return new QueryexStoreType(QueryexStoreFamily.QxNVarChar, length);
        }
    }
}
