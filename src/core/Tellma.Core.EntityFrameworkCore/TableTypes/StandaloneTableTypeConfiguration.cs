// Copyright (c) 2026 Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

namespace Tellma.Core.EntityFrameworkCore.TableTypes
{
    /// <summary>
    ///     The raw configuration of a standalone table type (spec 0001 §5) — a type paired with no
    ///     table, authored ad hoc through <see cref="TableTypeBuilder" /> or derived from a plain
    ///     CLR class. Stored as canonical JSON in a
    ///     <see cref="TableTypeAnnotationNames.StandalonePrefix" /> model annotation; the finalizing
    ///     convention resolves store types and expands it into an ordinary
    ///     <see cref="TableTypeDefinition" />.
    /// </summary>
    public sealed record StandaloneTableTypeConfiguration
    {
        /// <summary>The table type's name.</summary>
        public required string Name { get; init; }

        /// <summary>The table type's schema, or <see langword="null" /> for the database default.</summary>
        public string? Schema { get; init; }

        /// <summary>Whether the type is created with <c>MEMORY_OPTIMIZED = ON</c>.</summary>
        public bool IsMemoryOptimized { get; init; }

        /// <summary>
        ///     Whether this context declares the type for runtime binding but does not own it: another
        ///     context creates and sweeps the physical type (spec 0001 §3 → scoping). The type stays in
        ///     the metadata API; the differ emits no create and the sweep ignores it.
        /// </summary>
        public bool ExcludeFromMigrations { get; init; }

        /// <summary>Database principals granted <c>EXECUTE</c> on the type after every (re)create.</summary>
        public IReadOnlyList<string> Grants { get; init; } = [];

        /// <summary>The primary key column names, in key order. May be empty.</summary>
        public IReadOnlyList<string> Key { get; init; } = [];

        /// <summary>The columns, in declaration order (the ordinal TVP-binding contract).</summary>
        public IReadOnlyList<StandaloneColumnConfiguration> Columns { get; init; } = [];
    }

    /// <summary>
    ///     One column of a standalone table type, before store-type resolution. Either
    ///     <see cref="StoreType" /> is explicit, or the finalizing convention resolves it from
    ///     <see cref="ClrTypeName" /> plus the facets through the provider's type mapping source.
    /// </summary>
    public sealed record StandaloneColumnConfiguration
    {
        /// <summary>The column name.</summary>
        public required string Name { get; init; }

        /// <summary>
        ///     The assembly-qualified CLR type name the store type is resolved from, when
        ///     <see cref="StoreType" /> is not explicit.
        /// </summary>
        public string? ClrTypeName { get; init; }

        /// <summary>The explicit SQL Server store type, overriding CLR-based resolution.</summary>
        public string? StoreType { get; init; }

        /// <summary>Whether the column is nullable.</summary>
        public bool IsNullable { get; init; }

        /// <summary>The maximum length facet.</summary>
        public int? MaxLength { get; init; }

        /// <summary>The precision facet.</summary>
        public int? Precision { get; init; }

        /// <summary>The scale facet.</summary>
        public int? Scale { get; init; }

        /// <summary>Whether the column is Unicode (<c>nvarchar</c> vs <c>varchar</c>), when relevant.</summary>
        public bool? IsUnicode { get; init; }

        /// <summary>Whether the column is fixed length (<c>nchar</c>/<c>char</c>), when relevant.</summary>
        public bool? IsFixedLength { get; init; }

        /// <summary>The explicit collation of the column, when one is configured.</summary>
        public string? Collation { get; init; }
    }
}
