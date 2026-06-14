// Copyright (c) 2026 Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

namespace Tellma.Core.EntityFrameworkCore.TableTypes
{
    /// <summary>
    ///     A single column of a SQL Server table type (UDTT), as derived from the corresponding
    ///     table column of the EF relational model.
    /// </summary>
    /// <remarks>
    ///     The individual facets (<see cref="MaxLength" />, <see cref="Precision" />, <see cref="Scale" />)
    ///     are carried separately from the full <see cref="StoreType" /> string so that runtime TVP
    ///     binding can construct <c>SqlMetaData</c> without parsing store-type strings.
    ///     JSON property order follows declaration order and is part of the canonical-JSON
    ///     contract — do not reorder members (see <see cref="Json.TableTypeJson" />).
    /// </remarks>
    public sealed record TableTypeColumnDefinition
    {
        /// <summary>The column name, identical to the table column's name.</summary>
        public required string Name { get; init; }

        /// <summary>
        ///     The full SQL Server store type including facets, e.g. <c>nvarchar(255)</c> or
        ///     <c>decimal(19,4)</c>, byte-for-byte identical to the table column's store type
        ///     (except rowversion columns, which become <c>binary(8)</c> in the type).
        /// </summary>
        public required string StoreType { get; init; }

        /// <summary>Whether the column is nullable in the table type.</summary>
        public bool IsNullable { get; init; }

        /// <summary>The maximum length facet, when the store type carries one.</summary>
        public int? MaxLength { get; init; }

        /// <summary>The precision facet, when the store type carries one.</summary>
        public int? Precision { get; init; }

        /// <summary>The scale facet, when the store type carries one.</summary>
        public int? Scale { get; init; }

        /// <summary>The explicit collation of the column, when one is configured.</summary>
        public string? Collation { get; init; }

        /// <summary>
        ///     Whether this column mirrors the table's rowversion/concurrency-token column. In the
        ///     table type it is a nullable <c>binary(8)</c>: insert rows carry no value, while bulk
        ///     UPDATE payloads carry the original value for optimistic-concurrency checks.
        /// </summary>
        public bool IsRowVersion { get; init; }
    }
}
