// Copyright (c) 2026 Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.EntityFrameworkCore.Metadata.Conventions;
using Microsoft.EntityFrameworkCore.Storage;
using System.Reflection;
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

        /// <summary>SQL Server's native JSON store type (2025), as EF's type mapping reports it.</summary>
        private const string JsonStoreType = "json";

        /// <summary>The on-disk UDTT wire form of a JSON column (see <see cref="NormalizeJsonColumn" />).</summary>
        private const string JsonColumnStoreType = "varchar(max)";

        /// <summary>
        ///     The memory-optimized UDTT wire form of a JSON column. A UTF-8 collation is rejected on
        ///     memory-optimized table types (SQL Server error 12356), as is the native <c>json</c> type
        ///     (10794), so the on-disk <c>varchar(max)</c> UTF-8 form cannot be used; <c>nvarchar(max)</c>
        ///     (UTF-16) keeps non-Latin text lossless and creates cleanly. Confirmed on SQL Server 2025.
        /// </summary>
        private const string JsonColumnStoreTypeMemoryOptimized = "nvarchar(max)";

        /// <summary>
        ///     The native <c>json</c> type's own UTF-8 collation, carried on an on-disk UDTT's
        ///     <c>varchar(max)</c> JSON columns so non-Latin text round-trips losslessly.
        /// </summary>
        private const string JsonColumnCollation = "Latin1_General_100_BIN2_UTF8";

        /// <inheritdoc />
        public virtual void ProcessModelFinalizing(
            IConventionModelBuilder modelBuilder,
            IConventionContext<IConventionModelBuilder> context)
        {
            ArgumentNullException.ThrowIfNull(modelBuilder);

            // Annotation key -> (json, source description), to detect duplicate type names with an
            // actionable message naming both contributors.
            Dictionary<string, (string Json, string Source)> definitions = [];

            // Definition keys this context declares but does not own (ExcludeFromMigrations): the
            // differ skips creating and keeping them; the metadata API still exposes them.
            HashSet<string> excludedKeys = [];

            foreach (IConventionEntityType entityType in modelBuilder.Metadata.GetEntityTypes().OrderBy(e => e.Name, StringComparer.Ordinal))
            {
                TableTypeConfig? config = ResolveConfig(entityType);
                if (config is null)
                {
                    continue;
                }

                TableTypeDefinition definition = DeriveDefinition(entityType, config);
                string key = AddDefinition(definitions, definition, $"entity type '{entityType.DisplayName()}'");
                if (config.ExcludeFromMigrations)
                {
                    excludedKeys.Add(key);
                }
            }

            ExpandStandaloneTypes(modelBuilder, definitions, excludedKeys);

            foreach ((string key, (string json, _)) in definitions)
            {
                modelBuilder.HasAnnotation(key, json);
            }

            // Record the excluded keys for the differ. Live-model only — filtered from snapshots,
            // since the source side never needs it.
            if (excludedKeys.Count > 0)
            {
                modelBuilder.HasAnnotation(
                    TableTypeAnnotationNames.ExcludedKeys,
                    TableTypeJson.SerializeStringList([.. excludedKeys.OrderBy(k => k, StringComparer.Ordinal)]));
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
            bool excludeFromMigrations = entityType.FindAnnotation(TableTypeAnnotationNames.ExcludeFromMigrations)?.Value as bool? ?? false;
            IReadOnlyList<string> grants = entityType.FindAnnotation(TableTypeAnnotationNames.Grants)?.Value is string grantsJson
                ? TableTypeJson.DeserializeStringList(grantsJson)
                : [];

            string resolvedName = name ?? (tableName + "List");
            ValidateLogicalNameLength(resolvedName, $"entity type '{entityType.DisplayName()}'");

            return new TableTypeConfig(
                Name: resolvedName,
                Schema: schema ?? entityType.GetSchema(),
                TableName: tableName,
                TableSchema: entityType.GetSchema(),
                ExcludeRowVersion: excludeRowVersion,
                MemoryOptimized: memoryOptimized,
                ExcludeFromMigrations: excludeFromMigrations,
                Grants: grants);
        }

        /// <summary>
        ///     Validates that a logical name fits the content-addressed physical-name budget
        ///     (<see cref="TableTypeNaming.MaxLogicalNameLength" />), since the physical name appends a
        ///     hash suffix and must fit SQL Server's 128-character identifier limit.
        /// </summary>
        private static void ValidateLogicalNameLength(string name, string source)
        {
            if (name.Length > TableTypeNaming.MaxLogicalNameLength)
            {
                throw new InvalidOperationException(
                    $"Table type name '{name}' from {source} is {name.Length} characters; the maximum is " +
                    $"{TableTypeNaming.MaxLogicalNameLength} so that the content-hash-versioned physical name fits SQL " +
                    "Server's 128-character identifier limit. Shorten the name with HasTableType(name, ...) or [TableType].");
            }
        }

        /// <summary>Derives the full definition (columns, order, PK, facets) for one opted-in entity type.</summary>
        private TableTypeDefinition DeriveDefinition(IConventionEntityType entityType, TableTypeConfig config)
        {
            var storeObject = StoreObjectIdentifier.Table(config.TableName, config.TableSchema);

            ValidateNoHiddenColumns(entityType, config);
            ValidateNoTphDerivedColumns(entityType, storeObject);

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

            // JSON container columns (ToJson() owned navigations / complex properties) are not
            // reachable through GetProperties(). EF appends them after every other column, ordered by
            // name (MigrationsModelDiffer.GetSortedColumns, issue #28539 — they are not injected at the
            // navigation's position), so the UDTT mirrors that tail to keep ordinal parity with the
            // table. Ordinal ordering (vs EF's culture-default) keeps the canonical JSON locale-stable;
            // the two agree for identifier names, and the column-order parity test pins any divergence.
            List<TableTypeColumnDefinition> jsonColumns = DeriveJsonContainerColumns(entityType, config.MemoryOptimized);

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
                Columns = [.. ordered.Select(c => c.Column), .. jsonColumns.OrderBy(c => c.Name, StringComparer.Ordinal)],
            };
        }

        /// <summary>
        ///     Rejects opt-ins whose mapped table carries columns the derivation cannot see —
        ///     <em>flattened</em> complex types and owned types mapped into the owner's table. A row
        ///     image missing columns its table has is the silent drift/truncation failure mode that
        ///     killed <c>ForSave</c> (spec 0001 §2), so it is made an impossible state, not a hazard.
        ///     A <c>ToJson()</c> mapping (owned or complex) is the exception: it contributes a single
        ///     container column that the derivation <em>does</em> materialize
        ///     (<see cref="DeriveJsonContainerColumns" />), so the image stays complete and it is allowed.
        /// </summary>
        private static void ValidateNoHiddenColumns(IConventionEntityType entityType, TableTypeConfig config)
        {
            // Flattened complex types spread scalar columns the derivation cannot reach; a JSON-mapped
            // complex type contributes only its (materialized) container column, so it is exempt.
            List<IConventionComplexProperty> hiddenComplex =
                [.. entityType.GetComplexProperties().Where(p => !p.ComplexType.IsMappedToJson())];
            if (hiddenComplex.Count > 0)
            {
                string names = string.Join(", ", hiddenComplex.Select(p => $"'{p.Name}'"));
                throw new InvalidOperationException(
                    $"Entity type '{entityType.DisplayName()}' opted into a table type but maps complex propert(y/ies) " +
                    $"({names}) whose columns the table-type derivation cannot see. The row image would silently omit " +
                    "them. Remove the opt-in, model these without a complex type, or map them to JSON with ToJson().");
            }

            foreach (IConventionNavigation navigation in entityType.GetNavigations())
            {
                IConventionEntityType target = navigation.TargetEntityType;

                // ToJson() owned navigations are materialized as a container column; non-owned
                // navigations and own-table owned types are handled below.
                if (!target.IsOwned() || target.IsMappedToJson())
                {
                    continue;
                }

                if (target.GetTableName() == config.TableName && target.GetSchema() == config.TableSchema)
                {
                    throw new InvalidOperationException(
                        $"Entity type '{entityType.DisplayName()}' opted into a table type but owned navigation " +
                        $"'{navigation.Name}' maps into the same table. Its columns are not part of the derived row " +
                        "image; map the owned type to its own table, map it to JSON with ToJson(), or remove the opt-in.");
                }
            }
        }

        /// <summary>
        ///     Rejects a TPH root opt-in when any derived type declares a mapped scalar column on the
        ///     shared table — the root's image would silently miss it (spec 0001 §2). A
        ///     pure-discriminator hierarchy (no derived-declared columns) passes, since its image is
        ///     complete. TPT is unaffected: derived types map to their own tables, so no derived
        ///     property maps to this store object.
        /// </summary>
        private static void ValidateNoTphDerivedColumns(IConventionEntityType entityType, in StoreObjectIdentifier storeObject)
        {
            StoreObjectIdentifier local = storeObject;
            foreach (IConventionEntityType derived in entityType.GetDerivedTypes())
            {
                static InvalidOperationException Reject(IConventionEntityType root, IConventionEntityType derived, string column)
                {
                    return new InvalidOperationException(
                        $"Entity type '{root.DisplayName()}' is the root of a table-per-hierarchy (TPH) mapping and opted " +
                        $"into a table type, but derived type '{derived.DisplayName()}' declares mapped column '{column}'. " +
                        "The root's row image would silently omit it. Use leaf-only mapping (with TPT for a shared root) " +
                        "for UDTT-saved aggregates, or opt out.");
                }

                foreach (IConventionProperty property in derived.GetDeclaredProperties())
                {
                    if (property.GetColumnName(local) is string scalar)
                    {
                        throw Reject(entityType, derived, scalar);
                    }
                }

                // A column a *derived* type declares on the shared table is invisible to the root's
                // property walk — the same hole ValidateNoHiddenColumns closes for the root itself.
                // This holds even for a JSON container column: a root-declared ToJson() mapping the
                // derivation does materialize, but a derived-declared one it never reaches, so derived
                // complex/owned mappings are rejected regardless of whether they map to JSON.
                if (derived.GetDeclaredComplexProperties().FirstOrDefault() is IConventionComplexProperty complex)
                {
                    throw Reject(entityType, derived, $"complex property '{complex.Name}'");
                }

                foreach (IConventionNavigation navigation in derived.GetDeclaredNavigations())
                {
                    IConventionEntityType target = navigation.TargetEntityType;
                    if (target.IsOwned()
                        && (target.IsMappedToJson()
                            || (target.GetTableName() == local.Name && target.GetSchema() == local.Schema)))
                    {
                        throw Reject(entityType, derived, $"owned navigation '{navigation.Name}'");
                    }
                }
            }
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

            // A native json column (e.g. a primitive collection or an explicit HasColumnType("json"))
            // is carried as the UDTT's JSON wire form and flagged IsJson (see remarks).
            (string columnStoreType, string? collation, bool isJson) =
                NormalizeJsonColumn(storeType, property.GetCollation(storeObject), config.MemoryOptimized);

            return new IncludedColumn(
                property,
                new TableTypeColumnDefinition
                {
                    Name = columnName,
                    StoreType = columnStoreType,
                    IsNullable = property.IsColumnNullable(storeObject),
                    MaxLength = property.GetMaxLength(storeObject),
                    Precision = property.GetPrecision(storeObject),
                    Scale = property.GetScale(storeObject),
                    Collation = collation,
                    IsRowVersion = false,
                    IsJson = isJson,
                },
                property.GetColumnOrder(storeObject),
                isPrimaryKey);
        }

        /// <summary>
        ///     Materializes the JSON container columns of an entity's <c>ToJson()</c> owned navigations
        ///     and complex properties. EF maps each such mapping to a single container column on the
        ///     owner's table (the <c>Relational:ContainerColumnName</c> annotation) that the property
        ///     walk in <see cref="DeriveColumn" /> cannot reach. Each is carried as one
        ///     <c>varchar(max)</c> UTF-8 column (see <see cref="NormalizeJsonColumn" />); nullability
        ///     mirrors EF's container-column rule. De-duplicated by name, since table splitting could
        ///     in principle route two mappings to one container column.
        /// </summary>
        private static List<TableTypeColumnDefinition> DeriveJsonContainerColumns(
            IConventionEntityType entityType,
            bool memoryOptimized)
        {
            Dictionary<string, TableTypeColumnDefinition> byName = new(StringComparer.OrdinalIgnoreCase);
            (string storeType, string? collation) = JsonColumnWireForm(memoryOptimized);

            void Add(string? name, bool isNullable)
            {
                if (!string.IsNullOrEmpty(name) && !byName.ContainsKey(name))
                {
                    byName[name] = new TableTypeColumnDefinition
                    {
                        Name = name,
                        StoreType = storeType,
                        IsNullable = isNullable,
                        Collation = collation,
                        IsRowVersion = false,
                        IsJson = true,
                    };
                }
            }

            foreach (IConventionNavigation navigation in entityType.GetNavigations())
            {
                IConventionEntityType target = navigation.TargetEntityType;
                if (target.IsOwned() && target.IsMappedToJson())
                {
                    // EF makes the container column nullable unless the owned type is a required,
                    // unique (single-reference) dependent declared on a root type
                    // (RelationalModel.CreateContainerColumn).
                    IConventionForeignKey ownership = navigation.ForeignKey;
                    bool isNullable = !ownership.IsRequiredDependent
                        || !ownership.IsUnique
                        || navigation.DeclaringEntityType.BaseType is not null;
                    Add(target.GetContainerColumnName(), isNullable);
                }
            }

            foreach (IConventionComplexProperty complex in entityType.GetComplexProperties())
            {
                if (complex.ComplexType.IsMappedToJson())
                {
                    bool isNullable = complex.IsNullable || entityType.BaseType is not null;
                    Add(complex.ComplexType.GetContainerColumnName(), isNullable);
                }
            }

            return [.. byName.Values];
        }

        /// <summary>
        ///     Rewrites SQL Server's native <c>json</c> store type to the form the bulk-save pipeline
        ///     actually transmits (see <see cref="JsonColumnWireForm" />) and reports that the column is
        ///     JSON. The native <c>json</c> type cannot be bound as a table-valued-parameter column by
        ///     the client driver (Microsoft.Data.SqlClient's <c>SqlMetaData</c> has no json entry), and
        ///     a UDTT is a transient parameter that gains nothing from native-json storage; SQL Server
        ///     implicitly converts the wire form back to the table's <c>json</c> (or <c>nvarchar(max)</c>)
        ///     column on the <c>INSERT … SELECT FROM @tvp</c> (spec 0001 §2). Non-JSON store types pass
        ///     through unchanged with <c>IsJson = false</c>.
        /// </summary>
        private static (string StoreType, string? Collation, bool IsJson) NormalizeJsonColumn(
            string storeType,
            string? collation,
            bool memoryOptimized)
        {
            if (!string.Equals(storeType, JsonStoreType, StringComparison.OrdinalIgnoreCase))
            {
                return (storeType, collation, false);
            }

            (string jsonStoreType, string? jsonCollation) = JsonColumnWireForm(memoryOptimized);
            return (jsonStoreType, jsonCollation, true);
        }

        /// <summary>
        ///     The store type and collation a JSON column takes in the UDTT: <c>varchar(max)</c> with the
        ///     json type's UTF-8 collation on-disk, or <c>nvarchar(max)</c> (no collation) on a
        ///     memory-optimized type, where UTF-8 collations are unsupported
        ///     (<see cref="JsonColumnStoreTypeMemoryOptimized" />). Both keep non-Latin text lossless.
        /// </summary>
        private static (string StoreType, string? Collation) JsonColumnWireForm(bool memoryOptimized)
        {
            return memoryOptimized
                ? (JsonColumnStoreTypeMemoryOptimized, null)
                : (JsonColumnStoreType, JsonColumnCollation);
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

            // The ordering PropertyInfo of a non-PK column. EF groups a *shadow* foreign-key column
            // under its dependent-to-principal navigation's property (ordered among CLR columns at
            // the navigation's declaration position, then by the column's index within the FK), and
            // only a non-FK shadow column lands in the true-shadow tail
            // (MigrationsModelDiffer.GetSortedProperties). A column with its own PropertyInfo uses
            // that. PK classification, in contrast, uses the raw PropertyInfo (a shadow PK — FK or
            // not — is a least-priority PK in EF), so this is consulted for non-PK columns only.
            static (PropertyInfo? Info, int FkIndex) OrderingProperty(IConventionProperty property)
            {
                if (property.PropertyInfo is not null)
                {
                    return (property.PropertyInfo, 0);
                }

                foreach (IConventionForeignKey foreignKey in property.GetContainingForeignKeys())
                {
                    PropertyInfo? navigation = foreignKey.DependentToPrincipal?.PropertyInfo;
                    if (navigation is not null)
                    {
                        int fkIndex = 0;
                        foreach (IConventionProperty fkProperty in foreignKey.Properties)
                        {
                            if (fkProperty == property)
                            {
                                break;
                            }

                            fkIndex++;
                        }

                        return (navigation, fkIndex);
                    }
                }

                return (null, 0);
            }

            // Non-PK columns grouped by their ordering PropertyInfo (CLR-declared or navigation-backed
            // shadow FK), ordered base-most type first then by declaration order, mirroring EF.
            List<IncludedColumn> clrNonPk = [.. included
                .Where(c => !c.IsPrimaryKey && OrderingProperty(c.Property).Info is not null)
                .OrderBy(c => ChainIndex(OrderingProperty(c.Property).Info!.DeclaringType!))
                .ThenBy(c => OrderingProperty(c.Property).Info!.DeclaringType!.FullName, StringComparer.Ordinal)
                .ThenBy(c => DeclarationIndex(OrderingProperty(c.Property).Info!))
                .ThenBy(c => OrderingProperty(c.Property).FkIndex)];

            // Primary-key columns: CLR-declared first (by declaration order), then shadow PKs —
            // EF's least-priority PK group, which holds shadow PKs regardless of FK membership.
            List<IncludedColumn> clrPk = [.. included
                .Where(c => c.IsPrimaryKey && c.Property.PropertyInfo is not null)
                .OrderBy(c => ChainIndex(c.Property.PropertyInfo!.DeclaringType!))
                .ThenBy(c => c.Property.PropertyInfo!.DeclaringType!.FullName, StringComparer.Ordinal)
                .ThenBy(c => DeclarationIndex(c.Property.PropertyInfo!))];
            List<IncludedColumn> shadowPk = [.. included.Where(c => c.IsPrimaryKey && c.Property.PropertyInfo is null)];

            // The true-shadow tail: non-PK shadow columns that are not navigation-backed FK columns.
            List<IncludedColumn> trueShadowNonPk =
                [.. included.Where(c => !c.IsPrimaryKey && OrderingProperty(c.Property).Info is null)];

            // "Own" = declared on the entity's CLR type itself (or a type derived from it); base-class
            // declarations go to the tail, exactly like EF's table ordering.
            bool IsOwnDeclaration(IncludedColumn column)
            {
                return entityType.ClrType.IsAssignableFrom(OrderingProperty(column.Property).Info!.DeclaringType!);
            }

            List<IncludedColumn> layout =
            [
                .. clrPk,
                .. shadowPk,
                .. clrNonPk.Where(IsOwnDeclaration),
                .. trueShadowNonPk,
                .. clrNonPk.Where(c => !IsOwnDeclaration(c)),
            ];

            // Explicit HasColumnOrder() trumps the structural layout, exactly as it does for the
            // table: ordered columns first (stable ascending), the rest keep the layout order.
            return [.. layout.Where(c => c.Order.HasValue).OrderBy(c => c.Order), .. layout.Where(c => !c.Order.HasValue)];
        }

        /// <summary>
        ///     Expands the model's standalone table types (spec 0001 §5,
        ///     <see cref="TableTypeAnnotationNames.StandalonePrefix" /> annotations) into definition
        ///     annotations, resolving store types from CLR types and facets through the provider's
        ///     type mapping unless a store type is explicit.
        /// </summary>
        private void ExpandStandaloneTypes(
            IConventionModelBuilder modelBuilder,
            Dictionary<string, (string Json, string Source)> definitions,
            HashSet<string> excludedKeys)
        {
            foreach (IConventionAnnotation annotation in modelBuilder.Metadata.GetAnnotations()
                .Where(a => a.Name.StartsWith(TableTypeAnnotationNames.StandalonePrefix, StringComparison.Ordinal))
                .OrderBy(a => a.Name, StringComparer.Ordinal))
            {
                if (annotation.Value is not string configJson)
                {
                    continue;
                }

                StandaloneTableTypeConfiguration config = TableTypeJson.DeserializeStandalone(configJson);
                string source = $"standalone table type '{config.Name}'";

                ValidateLogicalNameLength(config.Name, source);

                if (config.Columns.Count == 0)
                {
                    throw new InvalidOperationException(
                        $"Standalone table type '{config.Name}' has no columns. Add at least one column via the " +
                        "TableTypeBuilder, or public read-write properties on the registered class.");
                }

                if (config.Columns.GroupBy(c => c.Name, StringComparer.OrdinalIgnoreCase).Any(g => g.Count() > 1))
                {
                    throw new InvalidOperationException(
                        $"Standalone table type '{config.Name}' declares duplicate column names.");
                }

                foreach (string keyColumn in config.Key)
                {
                    // Case-insensitive, matching the duplicate-column check and SQL Server's default
                    // identifier collation.
                    if (!config.Columns.Any(c => string.Equals(c.Name, keyColumn, StringComparison.OrdinalIgnoreCase)))
                    {
                        throw new InvalidOperationException(
                            $"Standalone table type '{config.Name}' declares key column '{keyColumn}' which is not " +
                            "among its columns.");
                    }
                }

                TableTypeDefinition definition = new()
                {
                    Name = config.Name,
                    Schema = config.Schema,
                    IsMemoryOptimized = config.IsMemoryOptimized,
                    Grants = config.Grants,
                    PrimaryKey = config.Key,
                    Columns = [.. config.Columns.Select(c => ResolveStandaloneColumn(config, c))],
                };
                string key = AddDefinition(definitions, definition, source);
                if (config.ExcludeFromMigrations)
                {
                    excludedKeys.Add(key);
                }
            }
        }

        /// <summary>Resolves one standalone column's store type (explicit, or CLR type + facets through the type mapping).</summary>
        private TableTypeColumnDefinition ResolveStandaloneColumn(
            StandaloneTableTypeConfiguration config,
            StandaloneColumnConfiguration column)
        {
            string? storeType = column.StoreType;
            if (storeType is null)
            {
                // The CLR type was recorded by the fluent/class route in the same app domain, so
                // resolving it here (at model finalization, still inside the app) is safe.
                Type clrType = Type.GetType(column.ClrTypeName!, throwOnError: true)!;
                storeType = _typeMappingSource.FindMapping(
                        clrType,
                        storeTypeName: null,
                        keyOrIndex: false,
                        unicode: column.IsUnicode,
                        size: column.MaxLength,
                        rowVersion: null,
                        fixedLength: column.IsFixedLength,
                        precision: column.Precision,
                        scale: column.Scale)?.StoreType
                    ?? throw new InvalidOperationException(
                        $"Cannot resolve a SQL Server store type for column '{column.Name}' of standalone table type " +
                        $"'{config.Name}' from CLR type '{clrType.Name}'. Specify an explicit store type.");
            }

            // A standalone column typed json is carried as the JSON wire form, like a derived one.
            (string columnStoreType, string? collation, bool isJson) =
                NormalizeJsonColumn(storeType, column.Collation, config.IsMemoryOptimized);

            return new TableTypeColumnDefinition
            {
                Name = column.Name,
                StoreType = columnStoreType,
                IsNullable = column.IsNullable,
                MaxLength = column.MaxLength,
                Precision = column.Precision,
                Scale = column.Scale,
                Collation = collation,
                IsRowVersion = false,
                IsJson = isJson,
            };
        }

        /// <summary>
        ///     Adds a derived definition, failing with both contributors named on a duplicate type
        ///     name, and returns its definition-annotation key.
        /// </summary>
        private static string AddDefinition(
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
            return key;
        }

        /// <summary>The resolved per-entity configuration input of the derivation.</summary>
        private sealed record TableTypeConfig(
            string Name,
            string? Schema,
            string TableName,
            string? TableSchema,
            bool ExcludeRowVersion,
            bool MemoryOptimized,
            bool ExcludeFromMigrations,
            IReadOnlyList<string> Grants);

        /// <summary>A column included in the type, with the inputs the ordering rule needs.</summary>
        private sealed record IncludedColumn(
            IConventionProperty Property,
            TableTypeColumnDefinition Column,
            int? Order,
            bool IsPrimaryKey);
    }
}
