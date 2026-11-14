// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Tellma.Core.Data.TableTypes;
using Tellma.Core.Queryex;

namespace Tellma.Core.Data.Queries
{
    // The mapping follows spec 0012 §11.1: a column the query language cannot type is absent from
    // the schema rather than declared under a type that would misread it.
    /// <summary>
    ///     Maps a column's SQL Server store type, as the model states it, to the query language's type
    ///     and its structured store type.
    /// </summary>
    internal static class QueryexStoreTypes
    {
        /// <summary>The query type and store type of a column, or null for a store type the language has no type for.</summary>
        /// <param name="storeType">The store type, such as <c>nvarchar(255)</c> or <c>decimal(19,4)</c>.</param>
        /// <returns>
        ///     The pair; null for <c>varbinary</c>, <c>time</c>, <c>float</c>, <c>real</c> and every
        ///     other type the language does not read.
        /// </returns>
        public static (QueryexType Type, QueryexStoreType Store)? Of(string storeType)
        {
            ArgumentNullException.ThrowIfNull(storeType);
            (string baseType, int? first, int? second, bool max) = StoreTypeMetadata.Parse(storeType);
            return baseType switch
            {
                // --- Truth values and numbers ---
                "bit" => (QueryexType.QxBool, QueryexStoreType.QxBit),
                "tinyint" => (QueryexType.QxNumeric, QueryexStoreType.QxTinyInt),
                "smallint" => (QueryexType.QxNumeric, QueryexStoreType.QxSmallInt),
                "int" => (QueryexType.QxNumeric, QueryexStoreType.QxInt),
                "bigint" => (QueryexType.QxNumeric, QueryexStoreType.QxBigInt),

                // SQL Server's own defaults for an unstated precision and scale.
                "decimal" or "numeric" => (QueryexType.QxNumeric, QueryexStoreType.QxDecimal(first ?? 18, second ?? 0)),

                // --- Text: an unstated length is one character, as SQL Server reads it ---
                "char" => (QueryexType.QxString, QueryexStoreType.QxChar(first ?? 1)),
                "varchar" => (QueryexType.QxString, QueryexStoreType.QxVarChar(max ? null : first ?? 1)),
                "nchar" => (QueryexType.QxString, QueryexStoreType.QxNChar(first ?? 1)),
                "nvarchar" => (QueryexType.QxString, QueryexStoreType.QxNVarChar(max ? null : first ?? 1)),

                // --- Identifiers, dates and instants: an unstated fractional scale is seven digits ---
                "uniqueidentifier" => (QueryexType.QxGuid, QueryexStoreType.QxUniqueIdentifier),
                "date" => (QueryexType.QxDate, QueryexStoreType.QxDate),
                "datetime" => (QueryexType.QxDateTime, QueryexStoreType.QxDateTime),
                "datetime2" => (QueryexType.QxDateTime, QueryexStoreType.QxDateTime2(first ?? 7)),
                "datetimeoffset" => (QueryexType.QxDateTimeOffset, QueryexStoreType.QxDateTimeOffset(first ?? 7)),

                // --- Positions and shapes ---
                "hierarchyid" => (QueryexType.QxHierarchyId, QueryexStoreType.QxHierarchyId),
                "geography" => (QueryexType.QxGeography, QueryexStoreType.QxGeography),
                _ => null,
            };
        }
    }
}
