// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using System.Text.Json.Serialization;
using Tellma.Core.EntityFrameworkCore.TableTypes.Json;

namespace Tellma.Core.EntityFrameworkCore.TableTypes
{
    /// <summary>
    ///     The complete derived definition of a SQL Server table type (UDTT): the row image of the
    ///     paired table, or the resolved shape of a standalone type. This is both the diffing
    ///     contract — serialized as canonical JSON into a model annotation — and the runtime
    ///     metadata surface consumed by dynamic SQL generation and TVP binding.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         <b>Column order is part of the contract.</b> TVP binding (<c>SqlDataRecord</c> /
    ///         <c>DataTable</c>) is ordinal, so <see cref="Columns" /> preserves the table's resolved
    ///         column order and a pure reorder is a definitional change (drop + recreate).
    ///     </para>
    ///     <para>
    ///         JSON property order follows declaration order and is part of the canonical-JSON
    ///         contract — do not reorder members (see <see cref="TableTypeJson" />).
    ///     </para>
    /// </remarks>
    public sealed record TableTypeDefinition
    {
        /// <summary>The table type's name, e.g. <c>InvoicesList</c>.</summary>
        public required string Name { get; init; }

        /// <summary>
        ///     The table type's schema, e.g. <c>gl</c>, or <see langword="null" /> to use the
        ///     database's default schema.
        /// </summary>
        public string? Schema { get; init; }

        /// <summary>
        ///     The name of the table this type is derived from, or <see langword="null" /> for
        ///     standalone types, which pair with no table.
        /// </summary>
        public string? TableName { get; init; }

        /// <summary>
        ///     The schema of the table this type is derived from, or <see langword="null" /> when
        ///     <see cref="TableName" /> is <see langword="null" /> or the table uses the default schema.
        /// </summary>
        public string? TableSchema { get; init; }

        /// <summary>
        ///     Whether the type is created with <c>MEMORY_OPTIMIZED = ON</c>. Memory-optimized and
        ///     on-disk declarations differ structurally (index kinds), so this flag is definitional.
        /// </summary>
        public bool IsMemoryOptimized { get; init; }

        /// <summary>
        ///     Database principals that receive <c>GRANT EXECUTE ON TYPE</c> after every
        ///     create/recreate of the type (grants do not survive a drop).
        /// </summary>
        public IReadOnlyList<string> Grants { get; init; } = [];

        /// <summary>
        ///     The primary key column names, mirroring the table's primary key in key declaration
        ///     order. IDs are app-assigned and always present, so the PK enforces in-batch
        ///     uniqueness and aids join plans.
        /// </summary>
        public IReadOnlyList<string> PrimaryKey { get; init; } = [];

        /// <summary>
        ///     The type's columns, in the table's resolved column order. The order is ordinal-binding
        ///     contract — see the remarks on this type.
        /// </summary>
        public IReadOnlyList<TableTypeColumnDefinition> Columns { get; init; } = [];

        /// <summary>
        ///     The schema-qualified, bracket-delimited display name of the type, e.g.
        ///     <c>[gl].[InvoicesList]</c>, for diagnostics and error messages. Uses the logical
        ///     <see cref="Name" />.
        /// </summary>
        [JsonIgnore]
        public string DisplayName => Schema is null ? $"[{Name}]" : $"[{Schema}].[{Name}]";

        /// <summary>
        ///     The deployed <b>physical</b> name (<c>&lt;Name&gt;_&lt;hash8&gt;</c>) — the content-addressed
        ///     name the type is created under and that runtime TVP binding must address (spec 0001 §3
        ///     → Versioning). Derived from the canonical JSON of this definition, so it matches the
        ///     name the differ computed. Not part of the canonical JSON (it is derived from it).
        /// </summary>
        [JsonIgnore]
        public string PhysicalName => TableTypeNaming.PhysicalName(Name, TableTypeNaming.ComputeHash(TableTypeJson.Serialize(this)));
    }
}
