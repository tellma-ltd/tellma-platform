// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Microsoft.EntityFrameworkCore.Migrations.Operations;

namespace Tellma.Core.EntityFrameworkCore.TableTypes.Operations
{
    /// <summary>
    ///     A <see cref="MigrationOperation" /> that creates a SQL Server table type (UDTT) under its
    ///     content-addressed <see cref="PhysicalName" />, stamps it with the owning scope, logical
    ///     name and definition hash, and grants <c>EXECUTE</c> to the configured principals.
    /// </summary>
    /// <remarks>
    ///     SQL Server has no <c>ALTER TYPE</c>, and none is needed: a definitional change yields a
    ///     different <see cref="PhysicalName" /> (spec 0001 §3 → Versioning), so the new version is
    ///     created <b>alongside</b> the old, which the cleanup sweep retires later. The generated
    ///     create is idempotent (keyed on the physical name) and completes the stamps of an aborted
    ///     prior create rather than failing.
    /// </remarks>
    public class CreateTableTypeOperation : MigrationOperation
    {
        /// <summary>The table type's logical (configured) name, e.g. <c>InvoicesList</c>.</summary>
        public string Name { get; set; } = null!;

        /// <summary>
        ///     The deployed physical name (<c>&lt;Name&gt;_&lt;hash8&gt;</c>) under which the type is
        ///     created — see <see cref="TableTypeNaming" />.
        /// </summary>
        public string PhysicalName { get; set; } = null!;

        /// <summary>
        ///     The table type's schema, or <see langword="null" /> to create it in the database's
        ///     default schema.
        /// </summary>
        public string? Schema { get; set; }

        /// <summary>
        ///     The owning context's sweep scope, stamped on the created type
        ///     (<see cref="TableTypeStampNames.Scope" />).
        /// </summary>
        public string Scope { get; set; } = null!;

        /// <summary>
        ///     The full SHA-256 of the definition's canonical JSON, stamped on the created type
        ///     (<see cref="TableTypeStampNames.DefinitionHash" />) for the create-time integrity check.
        /// </summary>
        public string DefinitionHash { get; set; } = null!;

        /// <summary>
        ///     The type's columns in ordinal order. The order is part of the type's contract
        ///     because TVP binding is ordinal.
        /// </summary>
        public List<TableTypeColumnDefinition> Columns { get; } = [];

        /// <summary>The primary key column names, in key declaration order. May be empty.</summary>
        public string[] PrimaryKey { get; set; } = [];

        /// <summary>
        ///     Whether the type is created with <c>MEMORY_OPTIMIZED = ON</c>. The generated SQL
        ///     pre-flights In-Memory OLTP support and throws an actionable error on unsupported
        ///     tiers; there is deliberately no silent fallback to an on-disk type. Memory-optimized
        ///     DDL also runs with the surrounding transaction suppressed.
        /// </summary>
        public bool IsMemoryOptimized { get; set; }

        /// <summary>
        ///     Database principals that receive <c>GRANT EXECUTE ON TYPE</c> immediately after the
        ///     type is created. Grants do not survive a drop, so they are emitted with every version
        ///     create.
        /// </summary>
        public string[] Grants { get; set; } = [];

        /// <summary>
        ///     Creates an operation for the given derived <paramref name="definition" />, computing
        ///     the physical name and definition hash from its <paramref name="canonicalJson" /> (the
        ///     exact annotation string, so the hash names exactly the bytes the differ compared).
        /// </summary>
        /// <param name="definition">The derived table-type definition.</param>
        /// <param name="canonicalJson">The canonical JSON annotation value of the definition.</param>
        /// <param name="scope">The owning context's sweep scope.</param>
        /// <returns>The create operation.</returns>
        public static CreateTableTypeOperation CreateFrom(TableTypeDefinition definition, string canonicalJson, string scope)
        {
            ArgumentNullException.ThrowIfNull(definition);
            ArgumentException.ThrowIfNullOrEmpty(canonicalJson);
            ArgumentException.ThrowIfNullOrWhiteSpace(scope);

            (string fullHash, string physicalName) = TableTypeNaming.Resolve(definition.Name, canonicalJson);

            CreateTableTypeOperation operation = new()
            {
                Name = definition.Name,
                PhysicalName = physicalName,
                Schema = definition.Schema,
                Scope = scope,
                DefinitionHash = fullHash,
                PrimaryKey = [.. definition.PrimaryKey],
                IsMemoryOptimized = definition.IsMemoryOptimized,
                Grants = [.. definition.Grants],
            };
            operation.Columns.AddRange(definition.Columns);
            return operation;
        }
    }
}
