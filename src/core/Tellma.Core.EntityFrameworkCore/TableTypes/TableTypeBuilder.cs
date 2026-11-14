// Copyright (c) Tellma Ltd. All rights reserved.
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
        private readonly List<(string Column, string Collation)> _collations = [];
        private readonly List<ColumnTypeOverride> _columnTypes = [];
        private readonly List<string> _exclusions = [];
        private bool _memoryOptimized;
        private bool _excludeFromMigrations;

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
        /// <param name="collation">
        ///     The explicit collation, when one is wanted. Only a column that resolves to
        ///     <c>char</c>, <c>varchar</c>, <c>nchar</c> or <c>nvarchar</c> takes one; any other store
        ///     type, <c>json</c> included, fails model finalization. A <see cref="UseCollation" /> call
        ///     for the column, before or after this one, wins over this argument.
        /// </param>
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
        /// <param name="collation">
        ///     The explicit collation, when one is wanted. Only a <c>char</c>, <c>varchar</c>,
        ///     <c>nchar</c> or <c>nvarchar</c> store type takes one; any other, <c>json</c> included,
        ///     fails model finalization. A <see cref="UseCollation" /> call for the column, before or
        ///     after this one, wins over this argument.
        /// </param>
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
        ///     Sets the collation of a column, whether the registered class or
        ///     <see cref="Column{T}(string, bool, int?, int?, int?, bool?, bool?, string?)" /> added it —
        ///     the way to give a class-derived string column a collation, for example a
        ///     <c>Latin1_General_100_BIN2</c> key that a statement compares with an equally collated
        ///     table column (without it, SQL Server refuses the comparison with Msg 468).
        /// </summary>
        /// <remarks>
        ///     <para>
        ///         The collation is applied when the type is registered, after every column has been
        ///         added, so this call may come before or after the <c>Column(…)</c> call that adds
        ///         the column. The last call for a column wins, over an earlier call and over the
        ///         <c>collation</c> argument of <c>Column(…)</c>. Column names match
        ///         case-insensitively; a name that matches no column fails the registration with an
        ///         <see cref="InvalidOperationException" />.
        ///     </para>
        ///     <para>
        ///         Only <c>char</c>, <c>varchar</c>, <c>nchar</c> and <c>nvarchar</c> columns take a
        ///         collation; any other store type, <c>json</c> included, fails model finalization.
        ///         The collation name itself is not validated (SQL Server refuses an unknown name at
        ///         <c>CREATE TYPE</c>). Write its canonical spelling: the name is part of the type's
        ///         definition, so adding, changing, removing or re-casing it creates a new version of
        ///         the type.
        ///     </para>
        /// </remarks>
        /// <param name="column">The column name.</param>
        /// <param name="collation">The SQL Server collation name, e.g. <c>Latin1_General_100_BIN2</c>.</param>
        /// <returns>The same builder instance so that multiple calls can be chained.</returns>
        public virtual TableTypeBuilder UseCollation(string column, string collation)
        {
            ArgumentException.ThrowIfNullOrEmpty(column);
            ArgumentException.ThrowIfNullOrWhiteSpace(collation);

            _collations.Add((column, collation));
            return this;
        }

        /// <summary>
        ///     Sets the store type of a column, whether the registered class or <c>Column(…)</c> added
        ///     it, replacing the type its CLR type and facets would resolve to — for example a
        ///     <c>varchar(16)</c> for an enum property a model stores by name.
        /// </summary>
        /// <remarks>
        ///     Applied when the type is registered, after every column has been added, so the call's
        ///     position against <c>Column(…)</c> does not matter, and before the
        ///     <see cref="UseCollation" /> calls, so a collation applies to the type set here. The last
        ///     call for a column wins, also over a class's <c>[Column(TypeName = …)]</c>. The facets
        ///     given here replace the column's own (an omitted facet clears it); they are carried into
        ///     the metadata API for binding and are not checked against the store type. Column names
        ///     match case-insensitively; a name that matches no column fails the registration with an
        ///     <see cref="InvalidOperationException" />.
        /// </remarks>
        /// <param name="column">The column name.</param>
        /// <param name="storeType">The full store type including facets, e.g. <c>varchar(16)</c>.</param>
        /// <param name="maxLength">The maximum length facet, carried into the metadata API.</param>
        /// <param name="precision">The precision facet, carried into the metadata API.</param>
        /// <param name="scale">The scale facet, carried into the metadata API.</param>
        /// <returns>The same builder instance so that multiple calls can be chained.</returns>
        public virtual TableTypeBuilder HasColumnType(
            string column,
            string storeType,
            int? maxLength = null,
            int? precision = null,
            int? scale = null)
        {
            ArgumentException.ThrowIfNullOrEmpty(column);
            ArgumentException.ThrowIfNullOrWhiteSpace(storeType);

            _columnTypes.Add(new ColumnTypeOverride(column, storeType, maxLength, precision, scale));
            return this;
        }

        /// <summary>
        ///     Leaves a column out of the type, whether the registered class or <c>Column(…)</c> added
        ///     it — the way a model that marks exclusions with an attribute of its own drops a
        ///     class-derived column.
        /// </summary>
        /// <remarks>
        ///     Applied when the type is registered, after every other call of this builder, so the
        ///     column's position, type and collation calls still name it. A key column cannot be
        ///     excluded, and a name that matches no column fails the registration with an
        ///     <see cref="InvalidOperationException" />; names match case-insensitively.
        /// </remarks>
        /// <param name="column">The column name.</param>
        /// <returns>The same builder instance so that multiple calls can be chained.</returns>
        public virtual TableTypeBuilder ExcludeColumn(string column)
        {
            ArgumentException.ThrowIfNullOrEmpty(column);

            _exclusions.Add(column);
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

        /// <summary>
        ///     Declares this type for runtime binding without this context owning it: another context
        ///     creates and sweeps the physical type (spec 0001 §3 → scoping). Use when two contexts
        ///     share one database and both need the same shape (e.g. <c>IdList</c>) — one owns it, the
        ///     others exclude it. The type stays in the metadata API; the differ emits no create for it
        ///     and the sweep ignores it.
        /// </summary>
        /// <param name="excluded">Whether the type is excluded from this context's migrations.</param>
        /// <returns>The same builder instance so that multiple calls can be chained.</returns>
        public virtual TableTypeBuilder ExcludeFromMigrations(bool excluded = true)
        {
            _excludeFromMigrations = excluded;
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

        /// <summary>
        ///     Materializes the configuration this builder collected, applying the
        ///     <see cref="HasColumnType" />, <see cref="UseCollation" /> and <see cref="ExcludeColumn" />
        ///     calls, in that order, to the finished column list.
        /// </summary>
        /// <exception cref="InvalidOperationException">
        ///     A <see cref="HasColumnType" />, <see cref="UseCollation" /> or <see cref="ExcludeColumn" />
        ///     call names no column, or an <see cref="ExcludeColumn" /> call names a key column.
        /// </exception>
        internal StandaloneTableTypeConfiguration Build(string name, string? schema)
        {
            List<StandaloneColumnConfiguration> columns = [.. _columns];
            ApplyColumnTypes(columns, name, schema);
            ApplyCollations(columns, name, schema);
            ApplyExclusions(columns, name, schema);

            return new StandaloneTableTypeConfiguration
            {
                Name = name,
                Schema = schema,
                IsMemoryOptimized = _memoryOptimized,
                ExcludeFromMigrations = _excludeFromMigrations,
                Grants = [.. _grants],
                Key = [.. _key],
                Columns = columns,
            };
        }

        /// <summary>
        ///     Applies every <see cref="HasColumnType" /> call, in call order, so the last call for a
        ///     column wins over earlier calls and over the column's own store type.
        /// </summary>
        private void ApplyColumnTypes(List<StandaloneColumnConfiguration> columns, string name, string? schema)
        {
            foreach (ColumnTypeOverride columnType in _columnTypes)
            {
                Apply(
                    columns,
                    columnType.Column,
                    column => column with
                    {
                        StoreType = columnType.StoreType,
                        MaxLength = columnType.MaxLength,
                        Precision = columnType.Precision,
                        Scale = columnType.Scale,
                    },
                    () => $"give store type '{columnType.StoreType}'",
                    "HasColumnType",
                    name,
                    schema);
            }
        }

        /// <summary>
        ///     Applies every <see cref="UseCollation" /> call, in call order, so the last call for a
        ///     column wins over earlier calls and over the column's own <c>collation</c> argument.
        ///     Applying at build time, after every column exists, is what makes the call's position
        ///     against <c>Column(…)</c> irrelevant.
        /// </summary>
        private void ApplyCollations(List<StandaloneColumnConfiguration> columns, string name, string? schema)
        {
            foreach ((string column, string collation) in _collations)
            {
                Apply(
                    columns,
                    column,
                    existing => existing with { Collation = collation },
                    () => $"give collation '{collation}'",
                    "UseCollation",
                    name,
                    schema);
            }
        }

        /// <summary>
        ///     Removes the columns named by <see cref="ExcludeColumn" /> calls. Runs last, so the other
        ///     calls of the builder still find every column by name.
        /// </summary>
        private void ApplyExclusions(List<StandaloneColumnConfiguration> columns, string name, string? schema)
        {
            foreach (string column in _exclusions)
            {
                // A key column cannot leave the type: the key would name a column the type lacks.
                if (_key.Contains(column, StringComparer.OrdinalIgnoreCase))
                {
                    throw new InvalidOperationException(
                        $"Standalone table type {DisplayName(name, schema)} cannot exclude column '{column}': it is part " +
                        "of the type's primary key.");
                }

                int removed = columns.RemoveAll(c => string.Equals(c.Name, column, StringComparison.OrdinalIgnoreCase));
                if (removed == 0)
                {
                    throw UnknownColumn(columns, column, "exclude", "ExcludeColumn", name, schema);
                }
            }
        }

        /// <summary>
        ///     Replaces every column named <paramref name="column" /> (case-insensitively) with
        ///     <paramref name="change" />'s result, failing when none matches. A duplicate pair of names
        ///     fails finalization anyway; until then the change lands on both.
        /// </summary>
        private static void Apply(
            List<StandaloneColumnConfiguration> columns,
            string column,
            Func<StandaloneColumnConfiguration, StandaloneColumnConfiguration> change,
            Func<string> action,
            string method,
            string name,
            string? schema)
        {
            bool matched = false;
            for (int i = 0; i < columns.Count; i++)
            {
                if (string.Equals(columns[i].Name, column, StringComparison.OrdinalIgnoreCase))
                {
                    columns[i] = change(columns[i]);
                    matched = true;
                }
            }

            if (!matched)
            {
                throw UnknownColumn(columns, column, action(), method, name, schema);
            }
        }

        /// <summary>The failure of a builder call that names no column of the type.</summary>
        private static InvalidOperationException UnknownColumn(
            List<StandaloneColumnConfiguration> columns,
            string column,
            string action,
            string method,
            string name,
            string? schema)
        {
            string known = columns.Count == 0
                ? "it has no columns"
                : "its columns are " + string.Join(", ", columns.Select(c => $"'{c.Name}'"));
            return new InvalidOperationException(
                $"Standalone table type {DisplayName(name, schema)} has no column '{column}' to {action} ({known}). " +
                $"{method} must name a column the class or Column(...) adds; names match case-insensitively.");
        }

        /// <summary>The bracket-delimited display name of the type, for messages.</summary>
        private static string DisplayName(string name, string? schema)
        {
            return schema is null ? $"[{name}]" : $"[{schema}].[{name}]";
        }

        /// <summary>One <see cref="HasColumnType" /> call.</summary>
        /// <param name="Column">The column name.</param>
        /// <param name="StoreType">The store type.</param>
        /// <param name="MaxLength">The maximum length facet.</param>
        /// <param name="Precision">The precision facet.</param>
        /// <param name="Scale">The scale facet.</param>
        private sealed record ColumnTypeOverride(string Column, string StoreType, int? MaxLength, int? Precision, int? Scale);
    }
}
