// Copyright (c) 2026 Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

namespace Tellma.Core.EntityFrameworkCore.TableTypes
{
    /// <summary>
    ///     Fluent builder of a standalone table type (spec 0001 §5) — a type paired with no table,
    ///     for operation-specific shapes such as bulk state updates or bulk assignments. Columns
    ///     appear in the type in the order they are added (the ordinal TVP-binding contract).
    /// </summary>
    /// <remarks>
    ///     Standalone types are not a backdoor to hand-maintained alternates of table row images
    ///     (the rejected <c>ForSave</c> pattern): if the shape is "this table's writable columns",
    ///     derive it from the table with <c>HasTableType()</c> on the entity instead.
    /// </remarks>
    public class TableTypeBuilder
    {
        private readonly List<StandaloneColumnConfiguration> _columns = [];
        private readonly List<string> _key = [];
        private readonly List<string> _grants = [];
        private bool _memoryOptimized;

        /// <summary>
        ///     Adds a column whose store type is resolved from <typeparamref name="T" /> and the
        ///     given facets through the provider's type mapping (e.g. <c>int</c> → <c>int</c>,
        ///     <c>string</c> + <paramref name="maxLength" /> 50 → <c>nvarchar(50)</c>).
        /// </summary>
        /// <typeparam name="T">The CLR type of the column's values; <c>T?</c> implies nullability.</typeparam>
        /// <param name="name">The column name.</param>
        /// <param name="nullable">Whether the column is nullable (forced for <c>Nullable&lt;T&gt;</c>).</param>
        /// <param name="maxLength">The maximum length facet.</param>
        /// <param name="precision">The precision facet.</param>
        /// <param name="scale">The scale facet.</param>
        /// <param name="unicode">Whether the column is Unicode, when relevant.</param>
        /// <param name="fixedLength">Whether the column is fixed length, when relevant.</param>
        /// <param name="collation">The explicit collation, when one is wanted.</param>
        /// <returns>The same builder instance so that multiple calls can be chained.</returns>
        public virtual TableTypeBuilder Column<T>(
            string name,
            bool nullable = false,
            int? maxLength = null,
            int? precision = null,
            int? scale = null,
            bool? unicode = null,
            bool? fixedLength = null,
            string? collation = null)
        {
            ArgumentException.ThrowIfNullOrEmpty(name);

            Type clrType = Nullable.GetUnderlyingType(typeof(T)) ?? typeof(T);
            _columns.Add(new StandaloneColumnConfiguration
            {
                Name = name,
                ClrTypeName = clrType.AssemblyQualifiedName,
                IsNullable = nullable || Nullable.GetUnderlyingType(typeof(T)) is not null,
                MaxLength = maxLength,
                Precision = precision,
                Scale = scale,
                IsUnicode = unicode,
                IsFixedLength = fixedLength,
                Collation = collation,
            });
            return this;
        }

        /// <summary>Adds a column with an explicit SQL Server store type, e.g. <c>decimal(19,4)</c>.</summary>
        /// <param name="name">The column name.</param>
        /// <param name="storeType">The full store type including facets.</param>
        /// <param name="nullable">Whether the column is nullable.</param>
        /// <param name="maxLength">The maximum length facet, carried into the metadata API.</param>
        /// <param name="precision">The precision facet, carried into the metadata API.</param>
        /// <param name="scale">The scale facet, carried into the metadata API.</param>
        /// <param name="collation">The explicit collation, when one is wanted.</param>
        /// <returns>The same builder instance so that multiple calls can be chained.</returns>
        public virtual TableTypeBuilder Column(
            string name,
            string storeType,
            bool nullable = false,
            int? maxLength = null,
            int? precision = null,
            int? scale = null,
            string? collation = null)
        {
            ArgumentException.ThrowIfNullOrEmpty(name);
            ArgumentException.ThrowIfNullOrEmpty(storeType);

            _columns.Add(new StandaloneColumnConfiguration
            {
                Name = name,
                StoreType = storeType,
                IsNullable = nullable,
                MaxLength = maxLength,
                Precision = precision,
                Scale = scale,
                Collation = collation,
            });
            return this;
        }

        /// <summary>
        ///     Sets the type's primary key (replacing any previously configured or
        ///     attribute-derived key). The PK enforces in-batch uniqueness and aids join plans.
        /// </summary>
        /// <param name="columns">The key column names, in key order.</param>
        /// <returns>The same builder instance so that multiple calls can be chained.</returns>
        public virtual TableTypeBuilder HasKey(params string[] columns)
        {
            ArgumentNullException.ThrowIfNull(columns);

            _key.Clear();
            _key.AddRange(columns);
            return this;
        }

        /// <summary>
        ///     Configures the database principals that receive <c>GRANT EXECUTE ON TYPE</c> after
        ///     every create/recreate of the type.
        /// </summary>
        /// <param name="principals">The database principals.</param>
        /// <returns>The same builder instance so that multiple calls can be chained.</returns>
        public virtual TableTypeBuilder HasGrants(params string[] principals)
        {
            ArgumentNullException.ThrowIfNull(principals);

            _grants.Clear();
            _grants.AddRange(principals);
            return this;
        }

        /// <summary>
        ///     Configures the type to be created with <c>MEMORY_OPTIMIZED = ON</c> (In-Memory
        ///     OLTP). The generated SQL pre-flights support and fails actionably on unsupported
        ///     tiers; there is deliberately no silent on-disk fallback.
        /// </summary>
        /// <param name="memoryOptimized">Whether the type is memory-optimized.</param>
        /// <returns>The same builder instance so that multiple calls can be chained.</returns>
        public virtual TableTypeBuilder IsMemoryOptimized(bool memoryOptimized = true)
        {
            _memoryOptimized = memoryOptimized;
            return this;
        }

        /// <summary>Adds an already-shaped column (used by the class-derivation route).</summary>
        internal void AddColumn(StandaloneColumnConfiguration column)
        {
            _columns.Add(column);
        }

        /// <summary>Appends a key column when no explicit key was configured (class-derivation route).</summary>
        internal void AddDerivedKeyColumn(string column)
        {
            _key.Add(column);
        }

        /// <summary>Whether an explicit or derived key exists yet.</summary>
        internal bool HasKeyColumns => _key.Count > 0;

        /// <summary>Materializes the configuration this builder collected.</summary>
        internal StandaloneTableTypeConfiguration Build(string name, string? schema)
        {
            return new StandaloneTableTypeConfiguration
            {
                Name = name,
                Schema = schema,
                IsMemoryOptimized = _memoryOptimized,
                Grants = [.. _grants],
                Key = [.. _key],
                Columns = [.. _columns],
            };
        }
    }
}
