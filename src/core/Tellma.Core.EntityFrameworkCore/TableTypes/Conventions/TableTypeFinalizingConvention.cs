// Copyright (c) 2026 Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using System.Reflection;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.EntityFrameworkCore.Metadata.Conventions;
using Microsoft.EntityFrameworkCore.Storage;
using Tellma.Core.EntityFrameworkCore.TableTypes.Json;

namespace Tellma.Core.EntityFrameworkCore.TableTypes.Conventions
{
    /// <summary>
    ///     An <see cref="IModelFinalizingConvention" /> that derives the full
    ///     <see cref="TableTypeDefinition" /> of every opted-in table — included columns in resolved
    ///     order, store types, facets, nullability, primary key, memory-optimized flag, grants —
    ///     and writes it as one canonical-JSON model annotation per type
    ///     (<see cref="TableTypeAnnotationNames.DefinitionPrefix" />).
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         The derivation runs at model-finalizing time using only public relational metadata
    ///         (never <c>IRelationalModel</c>, which does not exist yet at this point). Because the
    ///         serialized definition lands in the model snapshot verbatim, the differ compares
    ///         definitions annotation-to-annotation and never re-derives them from the snapshot
    ///         side — which is what makes a pure column reorder produce a correct diff even though
    ///         snapshots regenerate properties in alphabetical order.
    ///     </para>
    ///     <para>
    ///         Store types are resolved through the property's configuration when explicit, with the
    ///         injected <see cref="IRelationalTypeMappingSource" /> as fallback: EF computes property
    ///         type mappings lazily only after the model becomes read-only, so
    ///         <c>GetColumnType()</c> alone would come back null here for most properties.
    ///     </para>
    /// </remarks>
    /// <param name="typeMappingSource">The relational type mapping source of the current provider.</param>
    public class TableTypeFinalizingConvention(IRelationalTypeMappingSource typeMappingSource) : IModelFinalizingConvention
    {
        private readonly IRelationalTypeMappingSource _typeMappingSource = typeMappingSource;

        /// <inheritdoc />
        public virtual void ProcessModelFinalizing(
            IConventionModelBuilder modelBuilder,
            IConventionContext<IConventionModelBuilder> context)
        {
            ArgumentNullException.ThrowIfNull(modelBuilder);

            // Annotation key -> (json, source description), to detect duplicate type names with an
            // actionable message naming both contributors.
            Dictionary<string, (string Json, string Source)> definitions = [];

            foreach (IConventionEntityType entityType in modelBuilder.Metadata.GetEntityTypes().OrderBy(e => e.Name, StringComparer.Ordinal))
            {
                TableTypeConfig? config = ResolveConfig(entityType);
                if (config is null)
                {
                    continue;
                }

                TableTypeDefinition definition = DeriveDefinition(entityType, config);
                AddDefinition(definitions, definition, $"entity type '{entityType.DisplayName()}'");
            }

            ExpandBuiltInTypes(modelBuilder, definitions);

            foreach ((string key, (string json, _)) in definitions)
            {
                modelBuilder.HasAnnotation(key, json);
            }
        }

        /// <summary>
        ///     Resolves the effective table-type configuration of an entity type, or
        ///     <see langword="null" /> when it is not opted in. Fluent annotations win over CLR
        ///     attributes; attributes are read with <c>inherit: true</c> so a leaf class inherits a
        ///     pack base class's opt-in and exclusions.
        /// </summary>
        private static TableTypeConfig? ResolveConfig(IConventionEntityType entityType)
        {
            bool? enabled = entityType.FindAnnotation(TableTypeAnnotationNames.Enabled)?.Value as bool?;
            TableTypeAttribute? attribute = entityType.ClrType.GetCustomAttribute<TableTypeAttribute>(inherit: true);

            if (enabled == false || (enabled is null && attribute is null))
            {
                return null;
            }

            // TPH: derived entity types share the root's table; only the type that owns the table
            // derives a definition (the root's row image covers the shared columns). An attribute
            // inherited onto a TPH-derived type is simply covered by the root; an explicit fluent
            // opt-in on the derived type is a configuration error.
            if (entityType.BaseType is not null
                && entityType.GetTableName() == entityType.BaseType.GetTableName()
                && entityType.GetSchema() == entityType.BaseType.GetSchema())
            {
                return enabled == true
                    ? throw new InvalidOperationException(
                        $"Entity type '{entityType.DisplayName()}' cannot have its own table type: it shares table " +
                        $"'{entityType.GetTableName()}' with its base type '{entityType.BaseType.DisplayName()}'. " +
                        "Configure the table type on the entity type that owns the table.")
                    : null;
            }

            if (entityType.IsOwned())
            {
                return enabled == true
                    ? throw new InvalidOperationException(
                        $"Owned entity type '{entityType.DisplayName()}' cannot have a table type: owned types map " +
                        "into their owner's table. Configure the table type on the owner instead.")
                    : null;
            }

            string? tableName = entityType.GetTableName();
            if (tableName is null)
            {
                return enabled == true
                    ? throw new InvalidOperationException(
                        $"Entity type '{entityType.DisplayName()}' opted into a table type but is not mapped to a table. " +
                        "Table types are derived row images of tables; map the entity to a table or remove HasTableType().")
                    : null;
            }

            string? name = entityType.FindAnnotation(TableTypeAnnotationNames.Name)?.Value as string ?? attribute?.Name;
            string? schema = entityType.FindAnnotation(TableTypeAnnotationNames.Schema)?.Value as string ?? attribute?.Schema;
            bool excludeRowVersion = entityType.FindAnnotation(TableTypeAnnotationNames.ExcludeRowVersion)?.Value as bool? ?? false;
            bool memoryOptimized = entityType.FindAnnotation(TableTypeAnnotationNames.MemoryOptimized)?.Value as bool? ?? false;
            IReadOnlyList<string> grants = entityType.FindAnnotation(TableTypeAnnotationNames.Grants)?.Value is string grantsJson
                ? TableTypeJson.DeserializeGrants(grantsJson)
                : [];

            return new TableTypeConfig(
                Name: name ?? (tableName + "List"),
                Schema: schema ?? entityType.GetSchema(),
                TableName: tableName,
                TableSchema: entityType.GetSchema(),
                ExcludeRowVersion: excludeRowVersion,
                MemoryOptimized: memoryOptimized,
                Grants: grants);
        }

        /// <summary>Derives the full definition (columns, order, PK, facets) for one opted-in entity type.</summary>
        private TableTypeDefinition DeriveDefinition(IConventionEntityType entityType, TableTypeConfig config)
        {
            var storeObject = StoreObjectIdentifier.Table(config.TableName, config.TableSchema);

            IConventionKey primaryKey = entityType.FindPrimaryKey()
                ?? throw new InvalidOperationException(
                    $"Entity type '{entityType.DisplayName()}' opted into a table type but has no primary key. " +
                    "The table type's primary key mirrors the table's; keyless entities cannot have table types.");

            List<IncludedColumn> included = [];
            foreach (IConventionProperty property in entityType.GetProperties())
            {
                IncludedColumn? column = DeriveColumn(entityType, property, primaryKey, config, storeObject);
                if (column is not null)
                {
                    included.Add(column);
                }
            }

            List<IncludedColumn> ordered = SortColumns(entityType, included);

            // The PK *constraint* lists columns in key declaration order (matching the table's
            // PRIMARY KEY clause), independent of the column layout order above.
            string[] primaryKeyColumns = [.. primaryKey.Properties.Select(p => p.GetColumnName(storeObject)!)];

            return new TableTypeDefinition
            {
                Name = config.Name,
                Schema = config.Schema,
                TableName = config.TableName,
                TableSchema = config.TableSchema,
                IsMemoryOptimized = config.MemoryOptimized,
                Grants = config.Grants,
                PrimaryKey = primaryKeyColumns,
                Columns = [.. ordered.Select(c => c.Column)],
            };
        }

        /// <summary>
        ///     Derives a single column of the type from a property, or <see langword="null" /> when
        ///     the property's column does not participate (unmapped, computed, or excluded).
        /// </summary>
        private IncludedColumn? DeriveColumn(
            IConventionEntityType entityType,
            IConventionProperty property,
            IConventionKey primaryKey,
            TableTypeConfig config,
            in StoreObjectIdentifier storeObject)
        {
            string? columnName = property.GetColumnName(storeObject);
            if (columnName is null)
            {
                // Not mapped to this table — e.g. a TPT base property mapped to the base table.
                return null;
            }

            bool isPrimaryKey = primaryKey.Properties.Contains(property);

            // Effective exclusion: explicit fluent annotation wins; otherwise the CLR attribute
            // (inherited) applies.
            bool excluded = (property.FindAnnotation(TableTypeAnnotationNames.Excluded)?.Value as bool?)
                ?? (property.PropertyInfo?.GetCustomAttribute<ExcludeFromTableTypeAttribute>(inherit: true) is not null);
            if (excluded)
            {
                return isPrimaryKey
                    ? throw new InvalidOperationException(
                        $"Property '{entityType.DisplayName()}.{property.Name}' is part of the primary key and cannot be " +
                        "excluded from the table type: the type's primary key mirrors the table's.")
                    : null;
            }

            // Computed columns are not writable and are always excluded from the row image.
            if (property.GetComputedColumnSql(storeObject) is not null)
            {
                return null;
            }

            // Rowversion (concurrency token generated on add/update): included as nullable
            // binary(8) by default — insert rows carry no value, bulk UPDATEs carry the original
            // for optimistic-concurrency checks — and excludable per table.
            bool isRowVersion = property.IsConcurrencyToken && property.ValueGenerated == ValueGenerated.OnAddOrUpdate;
            if (isRowVersion)
            {
                return config.ExcludeRowVersion
                    ? null
                    : new IncludedColumn(
                        property,
                        new TableTypeColumnDefinition
                        {
                            Name = columnName,
                            StoreType = "binary(8)",
                            IsNullable = true,
                            MaxLength = 8,
                            IsRowVersion = true,
                        },
                        property.GetColumnOrder(storeObject),
                        isPrimaryKey);
            }

            // Store type: explicit configuration first; otherwise resolve through the type mapping
            // source. (EF computes property type mappings lazily only once the model is read-only,
            // so GetColumnType() alone would be null for most properties at this point.)
            RelationalTypeMapping? mapping = property is IProperty runtimeProperty
                ? _typeMappingSource.FindMapping(runtimeProperty)
                : null;
            string storeType = property.GetColumnType(storeObject)
                ?? mapping?.StoreType
                ?? throw new InvalidOperationException(
                    $"Cannot resolve a SQL Server store type for property '{entityType.DisplayName()}.{property.Name}' " +
                    "while deriving its table type. Configure an explicit column type with HasColumnType().");

            return new IncludedColumn(
                property,
                new TableTypeColumnDefinition
                {
                    Name = columnName,
                    StoreType = storeType,
                    IsNullable = property.IsColumnNullable(storeObject),
                    MaxLength = property.GetMaxLength(storeObject),
                    Precision = property.GetPrecision(storeObject),
                    Scale = property.GetScale(storeObject),
                    Collation = property.GetCollation(storeObject),
                    IsRowVersion = false,
                },
                property.GetColumnOrder(storeObject),
                isPrimaryKey);
        }

        /// <summary>
        ///     Orders the included columns to match the table's resolved column order, mirroring the
        ///     ordering EF's migrations differ applies when creating the table itself:
        ///     <list type="number">
        ///         <item><description>columns with an explicit <c>HasColumnOrder()</c> first, ascending (stable);</description></item>
        ///         <item><description>then primary-key columns (CLR-declared in structural order, then shadow PKs);</description></item>
        ///         <item><description>
        ///             then non-PK properties <b>declared on the entity's own CLR type</b>, in CLR
        ///             declaration order;
        ///         </description></item>
        ///         <item><description>then shadow properties;</description></item>
        ///         <item><description>
        ///             then properties declared on CLR <b>base</b> classes, base-most type first, in
        ///             CLR declaration order within each type. (This mirrors EF's quirk of filtering
        ///             on <c>ClrType.IsAssignableFrom(DeclaringType)</c>, which sends base-class
        ///             properties to the tail — a pack adding a column in a base class therefore
        ///             lands after the leaf's own columns, exactly like the table.)
        ///         </description></item>
        ///     </list>
        ///     An integration test pins UDTT order against the physical table's order, so any
        ///     divergence from EF's (private) sorting fails loudly there.
        /// </summary>
        private static List<IncludedColumn> SortColumns(IConventionEntityType entityType, List<IncludedColumn> included)
        {
            // The entity's CLR inheritance chain, base-most first, used to order base-class
            // property groups deterministically (unknown declaring types last, by name).
            List<Type> chain = [];
            for (Type? type = entityType.ClrType; type is not null; type = type.BaseType)
            {
                chain.Add(type);
            }

            chain.Reverse(); // base-most first

            int ChainIndex(Type declaringType)
            {
                int index = chain.IndexOf(declaringType);
                return index >= 0 ? index : int.MaxValue;
            }

            // CLR declaration order of a property within its declaring type.
            static int DeclarationIndex(PropertyInfo propertyInfo)
            {
                int index = 0;
                foreach (PropertyInfo declared in propertyInfo.DeclaringType!.GetTypeInfo().DeclaredProperties)
                {
                    if (declared.Name == propertyInfo.Name)
                    {
                        return index;
                    }

                    index++;
                }

                return int.MaxValue;
            }

            List<IncludedColumn> clrColumns = [.. included
                .Where(c => c.Property.PropertyInfo is not null)
                .OrderBy(c => ChainIndex(c.Property.PropertyInfo!.DeclaringType!))
                .ThenBy(c => c.Property.PropertyInfo!.DeclaringType!.FullName, StringComparer.Ordinal)
                .ThenBy(c => DeclarationIndex(c.Property.PropertyInfo!))];
            List<IncludedColumn> shadowColumns = [.. included.Where(c => c.Property.PropertyInfo is null)];

            // "Own" = declared on the entity's CLR type itself (or, theoretically, a type derived
            // from it); base-class declarations go to the tail, exactly like EF's table ordering.
            bool IsOwnDeclaration(IncludedColumn column)
            {
                return entityType.ClrType.IsAssignableFrom(column.Property.PropertyInfo!.DeclaringType!);
            }

            List<IncludedColumn> layout =
            [
                .. clrColumns.Where(c => c.IsPrimaryKey),
                .. shadowColumns.Where(c => c.IsPrimaryKey),
                .. clrColumns.Where(c => !c.IsPrimaryKey && IsOwnDeclaration(c)),
                .. shadowColumns.Where(c => !c.IsPrimaryKey),
                .. clrColumns.Where(c => !c.IsPrimaryKey && !IsOwnDeclaration(c)),
            ];

            // Explicit HasColumnOrder() trumps the structural layout, exactly as it does for the
            // table: ordered columns first (stable ascending), the rest keep the layout order.
            return [.. layout.Where(c => c.Order.HasValue).OrderBy(c => c.Order), .. layout.Where(c => !c.Order.HasValue)];
        }

        /// <summary>
        ///     Expands the model's built-in primitive types opt-in
        ///     (<see cref="TableTypeAnnotationNames.BuiltIn" />) into ordinary definition annotations
        ///     so they flow through the same differ, operations and SQL as table-derived types.
        /// </summary>
        private static void ExpandBuiltInTypes(
            IConventionModelBuilder modelBuilder,
            Dictionary<string, (string Json, string Source)> definitions)
        {
            if (modelBuilder.Metadata.FindAnnotation(TableTypeAnnotationNames.BuiltIn)?.Value is not string configJson)
            {
                return;
            }

            BuiltInTableTypesConfiguration config = TableTypeJson.DeserializeBuiltIn(configJson);
            foreach ((BuiltInTableTypes flag, string name, string storeType, int? maxLength) in new[]
            {
                (BuiltInTableTypes.IdList, "IdList", "int", (int?)null),
                (BuiltInTableTypes.BigIdList, "BigIdList", "bigint", null),
                (BuiltInTableTypes.GuidList, "GuidList", "uniqueidentifier", null),
                (BuiltInTableTypes.StringList, "StringList", "nvarchar(450)", 450),
            })
            {
                if (!config.Types.HasFlag(flag))
                {
                    continue;
                }

                TableTypeDefinition definition = new()
                {
                    Name = name,
                    Schema = config.Schema,
                    IsMemoryOptimized = false,
                    Grants = config.Grants,
                    PrimaryKey = ["Id"],
                    Columns =
                    [
                        new TableTypeColumnDefinition
                        {
                            Name = "Id",
                            StoreType = storeType,
                            IsNullable = false,
                            MaxLength = maxLength,
                        },
                    ],
                };
                AddDefinition(definitions, definition, $"built-in table type '{name}'");
            }
        }

        /// <summary>Adds a derived definition, failing with both contributors named on a duplicate type name.</summary>
        private static void AddDefinition(
            Dictionary<string, (string Json, string Source)> definitions,
            TableTypeDefinition definition,
            string source)
        {
            string key = TableTypeAnnotationNames.DefinitionPrefix + (definition.Schema ?? string.Empty) + "." + definition.Name;
            if (definitions.TryGetValue(key, out (string Json, string Source) existing))
            {
                throw new InvalidOperationException(
                    $"Duplicate table type {definition.DisplayName}: defined by both {existing.Source} and {source}. " +
                    "Table type names must be unique; override the name or schema with HasTableType(name, schema) or [TableType].");
            }

            definitions.Add(key, (TableTypeJson.Serialize(definition), source));
        }

        /// <summary>The resolved per-entity configuration input of the derivation.</summary>
        private sealed record TableTypeConfig(
            string Name,
            string? Schema,
            string TableName,
            string? TableSchema,
            bool ExcludeRowVersion,
            bool MemoryOptimized,
            IReadOnlyList<string> Grants);

        /// <summary>A column included in the type, with the inputs the ordering rule needs.</summary>
        private sealed record IncludedColumn(
            IConventionProperty Property,
            TableTypeColumnDefinition Column,
            int? Order,
            bool IsPrimaryKey);
    }
}
