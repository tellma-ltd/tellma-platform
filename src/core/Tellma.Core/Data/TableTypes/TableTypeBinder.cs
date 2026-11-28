// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Microsoft.Data.SqlClient;
using Microsoft.Data.SqlClient.Server;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using System.Collections.Concurrent;
using System.Linq.Expressions;
using System.Reflection;
using Tellma.Core.Abstractions.Entities;
using Tellma.Core.Data.Conventions;
using Tellma.Core.EntityFrameworkCore.TableTypes;

namespace Tellma.Core.Data.TableTypes
{
    /// <summary>
    ///     Binds rows to table-valued parameters from the model's table-type definitions, never by a
    ///     hard-coded ordinal. The one place that builds <see cref="SqlDataRecord" />s: each type's
    ///     <see cref="SqlMetaData" /> comes from its definition's ordered columns, and each row's
    ///     values from compiled accessors matched to the columns by name, so a column a migration adds
    ///     or reorders changes nothing here.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         An entity's rows bind to its table's row-image type: each column reads the property
    ///         mapped to it and writes its provider value, so an enum binds through its string
    ///         conversion. The shadow node and period columns are absent from every row-image type. A
    ///         standalone class's rows bind to the type registered for it, its enums by member name.
    ///     </para>
    ///     <para>
    ///         Bindings are built on first use and cached for the life of the model; the binder is a
    ///         singleton and thread-safe.
    ///     </para>
    /// </remarks>
    /// <param name="model">The tenant model.</param>
    [TableTypeBinder]
    internal sealed class TableTypeBinder(IModel model)
    {
        private readonly ConcurrentDictionary<Type, TableTypeBinding> _bindings = new();

        /// <summary>The binding of a row type: an entity's row image, or a standalone class's type.</summary>
        /// <param name="rowType">The row type: an entity leaf, a default a leaf replaces, or a standalone table-type class.</param>
        /// <returns>The binding.</returns>
        /// <exception cref="InvalidOperationException">The model has no table type for the row type, or a column has no member to read.</exception>
        /// <exception cref="NotSupportedException">A column's store type cannot be bound.</exception>
        public TableTypeBinding Binding(Type rowType)
        {
            ArgumentNullException.ThrowIfNull(rowType);
            return _bindings.GetOrAdd(rowType, Build);
        }

        /// <summary>The rows of a list as records of their type's table type, created lazily.</summary>
        /// <typeparam name="TRow">The row type.</typeparam>
        /// <param name="rows">The rows.</param>
        /// <returns>The records.</returns>
        public IEnumerable<SqlDataRecord> Records<TRow>(IReadOnlyList<TRow> rows)
            where TRow : class
        {
            ArgumentNullException.ThrowIfNull(rows);
            return Binding(typeof(TRow)).Records(rows);
        }

        /// <summary>A structured parameter carrying the rows of a list.</summary>
        /// <typeparam name="TRow">The row type.</typeparam>
        /// <param name="name">The parameter's name, with its leading <c>@</c>.</param>
        /// <param name="rows">The rows.</param>
        /// <returns>The parameter.</returns>
        public SqlParameter Parameter<TRow>(string name, IReadOnlyList<TRow> rows)
            where TRow : class
        {
            ArgumentNullException.ThrowIfNull(rows);
            return Binding(typeof(TRow)).Parameter(name, rows);
        }

        /// <summary>Builds the binding of a row type.</summary>
        /// <param name="rowType">The row type.</param>
        /// <returns>The binding.</returns>
        private TableTypeBinding Build(Type rowType)
        {
            return EntityTypeOf(rowType) is IEntityType entityType
                ? BuildForEntity(entityType)
                : BuildForStandalone(rowType);
        }

        /// <summary>The entity type of a row type: the type itself, else the one leaf that replaces it.</summary>
        /// <param name="rowType">The row type.</param>
        /// <returns>The entity type, or <see langword="null" /> when the row type is not an entity.</returns>
        private IEntityType? EntityTypeOf(Type rowType)
        {
            if (model.FindEntityType(rowType) is IEntityType exact)
            {
                return exact;
            }

            List<IEntityType> leaves = [.. model.GetEntityTypes()
                .Where(entityType => entityType.BaseType is null && !entityType.HasSharedClrType && rowType.IsAssignableFrom(entityType.ClrType))];

            return leaves is [IEntityType leaf] ? leaf : null;
        }

        /// <summary>The binding of an entity's rows to its table's row-image type.</summary>
        /// <param name="entityType">The entity type.</param>
        /// <returns>The binding.</returns>
        private static TableTypeBinding BuildForEntity(IEntityType entityType)
        {
            TableTypeDefinition definition = entityType.GetTableType()
                ?? throw new InvalidOperationException(
                    $"The entity {entityType.DisplayName()} has no table type, so its rows cannot be bound to a table-valued parameter. Declare [TableType] on the entity.");

            // The table's column mappings, by column name: what each column of the row image reads.
            Dictionary<string, IProperty> byColumn = new(StringComparer.OrdinalIgnoreCase);
            foreach (ITableMapping mapping in entityType.GetTableMappings())
            {
                if (mapping.Table.Name != definition.TableName || mapping.Table.Schema != definition.TableSchema)
                {
                    continue;
                }

                foreach (IColumnMapping column in mapping.ColumnMappings)
                {
                    byColumn[column.Column.Name] = column.Property;
                }
            }

            List<SqlMetaData> metadata = [];
            List<Func<object, object?>> accessors = [];
            foreach (TableTypeColumnDefinition column in definition.Columns)
            {
                IProperty property = byColumn.TryGetValue(column.Name, out IProperty? mapped) && mapped.PropertyInfo is not null
                    ? mapped
                    : throw new InvalidOperationException(
                        $"Column '{column.Name}' of table type {definition.DisplayName} maps to no member of {entityType.DisplayName()}, so it cannot be bound. A shadow column must be excluded from the table type.");

                metadata.Add(StoreTypeMetadata.For(definition, column));
                accessors.Add(Accessor(entityType.ClrType, property.PropertyInfo!, property.GetTypeMapping().Converter, enumByName: false));
            }

            return new TableTypeBinding(entityType.ClrType, definition, [.. metadata], [.. accessors]);
        }

        /// <summary>The binding of a standalone class's rows to the type registered for it.</summary>
        /// <param name="rowType">The row class.</param>
        /// <returns>The binding.</returns>
        private TableTypeBinding BuildForStandalone(Type rowType)
        {
            string name = TableTypeConvention.StandaloneName(rowType);
            string schema = TableTypeConvention.StandaloneSchemaOf(rowType);
            TableTypeDefinition definition = model.GetTableTypes()
                    .FirstOrDefault(candidate => candidate.TableName is null && candidate.Name == name && candidate.Schema == schema)
                ?? throw new InvalidOperationException(
                    $"The model has no table type [{schema}].[{name}] for rows of {rowType.Name}. Contribute the class with contribution.TableType<{rowType.Name}>(), or its entity with contribution.Entity<{rowType.Name}>().");

            Dictionary<string, PropertyInfo> byName = new(StringComparer.OrdinalIgnoreCase);
            foreach (PropertyInfo property in TableTypeConvention.ColumnProperties(rowType))
            {
                byName[property.Name] = property;
            }

            List<SqlMetaData> metadata = [];
            List<Func<object, object?>> accessors = [];
            foreach (TableTypeColumnDefinition column in definition.Columns)
            {
                PropertyInfo property = byName.TryGetValue(column.Name, out PropertyInfo? found)
                    ? found
                    : throw new InvalidOperationException(
                        $"Column '{column.Name}' of table type {definition.DisplayName} maps to no property of {rowType.Name}, so it cannot be bound.");

                metadata.Add(StoreTypeMetadata.For(definition, column));
                accessors.Add(Accessor(rowType, property, converter: null, enumByName: true));
            }

            return new TableTypeBinding(rowType, definition, [.. metadata], [.. accessors]);
        }

        /// <summary>
        ///     A compiled accessor of one column: reads the property of a row and turns its value into
        ///     the value the record takes — through the model's converter when the property has one,
        ///     an enum by member name for a standalone class, and the date and time types the record
        ///     field accepts.
        /// </summary>
        /// <param name="rowType">The row's CLR type.</param>
        /// <param name="property">The property the column reads.</param>
        /// <param name="converter">The model's provider conversion, or <see langword="null" />.</param>
        /// <param name="enumByName">Whether an enum without a converter binds by member name.</param>
        /// <returns>The accessor.</returns>
        private static Func<object, object?> Accessor(Type rowType, PropertyInfo property, ValueConverter? converter, bool enumByName)
        {
            // --- (object row) => (object)((TRow)row).Property ---
            ParameterExpression row = Expression.Parameter(typeof(object), "row");
            Expression member = Expression.Property(Expression.Convert(row, rowType), property);
            Func<object, object?> getter = Expression.Lambda<Func<object, object?>>(Expression.Convert(member, typeof(object)), row).Compile();

            Func<object?, object?>? convert = converter?.ConvertToProvider;
            return target =>
            {
                object? raw = getter(target);
                if (raw is null)
                {
                    return null;
                }

                if (convert is not null)
                {
                    raw = convert(raw);
                }
                else if (enumByName && raw is Enum enumValue)
                {
                    raw = enumValue.ToString();
                }

                return raw switch
                {
                    DateOnly date => date.ToDateTime(TimeOnly.MinValue),
                    TimeOnly time => time.ToTimeSpan(),
                    _ => raw,
                };
            };
        }
    }
}
