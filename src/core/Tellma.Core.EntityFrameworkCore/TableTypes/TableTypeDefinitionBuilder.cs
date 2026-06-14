// Copyright (c) 2026 Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

namespace Tellma.Core.EntityFrameworkCore.TableTypes
{
    /// <summary>
    ///     Builds a complete, already-resolved <see cref="TableTypeDefinition" /> — the vocabulary
    ///     of <c>HasTableTypeDefinition(...)</c> calls in model snapshots.
    /// </summary>
    /// <remarks>
    ///     Unlike <see cref="TableTypeBuilder" /> (which collects raw input that the finalizing
    ///     convention resolves), this builder takes final store types verbatim: snapshots must
    ///     rebuild definitions byte-for-byte so the differ's verbatim comparison sees exactly what
    ///     the live model derived. Application code normally never calls this — table-derived types
    ///     come from <c>HasTableType()</c> on the entity and standalone types from
    ///     <c>HasTableType(...)</c> on the model builder.
    /// </remarks>
    public class TableTypeDefinitionBuilder
    {
        private readonly List<TableTypeColumnDefinition> _columns = [];
        private readonly List<string> _key = [];
        private readonly List<string> _grants = [];
        private string? _tableName;
        private string? _tableSchema;
        private bool _memoryOptimized;

        /// <summary>Records the table this type is derived from (absent for standalone types).</summary>
        /// <param name="name">The table's name.</param>
        /// <param name="schema">The table's schema, or <see langword="null" /> for the default schema.</param>
        /// <returns>The same builder instance so that multiple calls can be chained.</returns>
        public virtual TableTypeDefinitionBuilder ForTable(string name, string? schema = null)
        {
            ArgumentException.ThrowIfNullOrEmpty(name);

            _tableName = name;
            _tableSchema = schema;
            return this;
        }

        /// <summary>Adds a column with its final store type, in ordinal order.</summary>
        /// <param name="name">The column name.</param>
        /// <param name="storeType">The full SQL Server store type including facets.</param>
        /// <param name="nullable">Whether the column is nullable.</param>
        /// <param name="maxLength">The maximum length facet.</param>
        /// <param name="precision">The precision facet.</param>
        /// <param name="scale">The scale facet.</param>
        /// <param name="collation">The explicit collation, when one is configured.</param>
        /// <param name="rowVersion">Whether the column mirrors the table's rowversion column.</param>
        /// <param name="json">Whether the column holds a JSON document.</param>
        /// <returns>The same builder instance so that multiple calls can be chained.</returns>
        public virtual TableTypeDefinitionBuilder Column(
            string name,
            string storeType,
            bool nullable = false,
            int? maxLength = null,
            int? precision = null,
            int? scale = null,
            string? collation = null,
            bool rowVersion = false,
            bool json = false)
        {
            ArgumentException.ThrowIfNullOrEmpty(name);
            ArgumentException.ThrowIfNullOrEmpty(storeType);

            _columns.Add(new TableTypeColumnDefinition
            {
                Name = name,
                StoreType = storeType,
                IsNullable = nullable,
                MaxLength = maxLength,
                Precision = precision,
                Scale = scale,
                Collation = collation,
                IsRowVersion = rowVersion,
                IsJson = json,
            });
            return this;
        }

        /// <summary>Sets the type's primary key columns, in key order.</summary>
        /// <param name="columns">The key column names.</param>
        /// <returns>The same builder instance so that multiple calls can be chained.</returns>
        public virtual TableTypeDefinitionBuilder HasKey(params string[] columns)
        {
            ArgumentNullException.ThrowIfNull(columns);

            _key.Clear();
            _key.AddRange(columns);
            return this;
        }

        /// <summary>Sets the principals granted <c>EXECUTE</c> on the type after every (re)create.</summary>
        /// <param name="principals">The database principals.</param>
        /// <returns>The same builder instance so that multiple calls can be chained.</returns>
        public virtual TableTypeDefinitionBuilder HasGrants(params string[] principals)
        {
            ArgumentNullException.ThrowIfNull(principals);

            _grants.Clear();
            _grants.AddRange(principals);
            return this;
        }

        /// <summary>Marks the type as created with <c>MEMORY_OPTIMIZED = ON</c>.</summary>
        /// <param name="memoryOptimized">Whether the type is memory-optimized.</param>
        /// <returns>The same builder instance so that multiple calls can be chained.</returns>
        public virtual TableTypeDefinitionBuilder IsMemoryOptimized(bool memoryOptimized = true)
        {
            _memoryOptimized = memoryOptimized;
            return this;
        }

        /// <summary>Materializes the definition this builder collected.</summary>
        internal TableTypeDefinition Build(string name, string? schema)
        {
            return new TableTypeDefinition
            {
                Name = name,
                Schema = schema,
                TableName = _tableName,
                TableSchema = _tableSchema,
                IsMemoryOptimized = _memoryOptimized,
                Grants = [.. _grants],
                PrimaryKey = [.. _key],
                Columns = [.. _columns],
            };
        }
    }
}
