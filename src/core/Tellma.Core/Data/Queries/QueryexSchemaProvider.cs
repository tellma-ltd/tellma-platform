// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using System.Globalization;
using Tellma.Core.Abstractions.Data;
using Tellma.Core.Abstractions.Entities;
using Tellma.Core.Abstractions.Settings;
using Tellma.Core.Data.Conventions;
using Tellma.Core.Data.Metadata;
using Tellma.Core.Queryex;

namespace Tellma.Core.Data.Queries
{
    // The schema rules are spec 0012 §11.1's.
    /// <summary>
    ///     The query schemas of the composed model, one per multilingual shape, each built on first use
    ///     and kept for the process, with the maps the batch's query statements read beside them.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         Every mapped entity leaf, children included, is a root named after its metadata. Its
    ///         properties are every mapped scalar whose store type the language has a type for, less
    ///         the period columns, the JSON columns and the columns a platform entity keeps to itself;
    ///         the secondary and tertiary twins of a multilingual group are present only in the shapes
    ///         that admit them. Its navigations are its references and the one-to-one navigations its
    ///         siblings give it, which join outer. A tree's node is its hierarchy node.
    ///     </para>
    ///     <para>
    ///         The engine's caches key on schema identity, so one schema per shape is the smallest key
    ///         that neither leaks a twin to a tenant whose shape omits it nor rebuilds per request.
    ///     </para>
    /// </remarks>
    internal sealed class QueryexSchemaProvider : IQueryexSchemaProvider
    {
        /// <summary>The month table of the Umm al-Qura calendar, which the calendar functions read.</summary>
        public const string UmAlQuraMonths = "[core].[CalendarMonths]";

        /// <summary>The calendar code the month table serves.</summary>
        public const string UmAlQura = "uq";

        /// <summary>The columns a platform entity keeps out of every query, by entity name and property.</summary>
        private static readonly HashSet<(string Entity, string Property)> Withheld =
        [
            ("core.Blob", "StorageKey"),
            ("core.Blob", "Sha256"),
            ("core.Job", "StateJson"),
            ("core.Job", "LeaseToken"),
        ];

        /// <summary>The shape-independent plan of every entity, and the fingerprint over it.</summary>
        private readonly Lazy<Plan> _plan;

        /// <summary>The schema of each shape, by the shape's value less one.</summary>
        private readonly Lazy<QueryexSchemaMap>[] _maps;

        /// <summary>Creates the provider.</summary>
        /// <param name="model">The runtime model.</param>
        /// <param name="metadata">The model's entity metadata.</param>
        public QueryexSchemaProvider(IModel model, IEntityMetadataProvider metadata)
        {
            ArgumentNullException.ThrowIfNull(model);
            ArgumentNullException.ThrowIfNull(metadata);
            _plan = new Lazy<Plan>(() => Plan.Create(model, metadata), LazyThreadSafetyMode.ExecutionAndPublication);
            _maps =
            [
                .. new[] { MultilingualShape.Primary, MultilingualShape.PrimaryAndSecondary, MultilingualShape.All }
                    .Select(shape => new Lazy<QueryexSchemaMap>(() => Build(_plan.Value, shape), LazyThreadSafetyMode.ExecutionAndPublication)),
            ];
        }

        /// <inheritdoc />
        public string Fingerprint => _plan.Value.Fingerprint;

        /// <inheritdoc />
        public QueryexSchema GetSchema(MultilingualShape shape)
        {
            return Map(shape).Schema;
        }

        /// <summary>Whether a platform entity keeps a column out of every query.</summary>
        /// <param name="entity">The entity's name.</param>
        /// <param name="property">The property's name.</param>
        /// <returns>True when the column is withheld.</returns>
        internal static bool IsWithheld(string entity, string property)
        {
            return Withheld.Contains((entity, property));
        }

        /// <summary>The collation a column declares for itself, or null when it takes the database's.</summary>
        /// <param name="property">The column's property.</param>
        /// <returns>The collation name, or null.</returns>
        /// <remarks>
        ///     Read off the property's <see cref="CollationAttribute" />, which is how a tenant entity's
        ///     column gets a collation: the model the context runs on keeps no collation, so it cannot
        ///     be read back from there. A shadow column has no attribute and takes the database's.
        /// </remarks>
        private static string? Collation(IProperty property)
        {
            return property.PropertyInfo is null ? null : EntityShapes.GetAttribute<CollationAttribute>(property.PropertyInfo)?.Name;
        }

        /// <summary>The schema of a shape with its maps.</summary>
        /// <param name="shape">The tenant's multilingual shape.</param>
        /// <returns>The map.</returns>
        /// <exception cref="ArgumentOutOfRangeException">The shape is not one of the defined shapes.</exception>
        public QueryexSchemaMap Map(MultilingualShape shape)
        {
            return Enum.IsDefined(shape)
                ? _maps[(int)shape - 1].Value
                : throw new ArgumentOutOfRangeException(nameof(shape), shape, "Not a multilingual shape.");
        }

        /// <summary>Builds the schema of one shape from the plan.</summary>
        /// <param name="plan">The plan.</param>
        /// <param name="shape">The shape.</param>
        /// <returns>The map.</returns>
        private static QueryexSchemaMap Build(Plan plan, MultilingualShape shape)
        {
            QueryexSchemaBuilder builder = new();
            List<(EntityMetadata Metadata, QueryexSchemaBuilder.EntityBuilder Builder, IReadOnlyList<PropertyMetadata> Json, IReadOnlyList<PropertyMetadata> Declared)> built = [];
            foreach (EntityPlan entity in plan.Entities)
            {
                // --- The entity, its key and the properties the shape admits ---
                QueryexSchemaBuilder.EntityBuilder declared = builder.Entity(entity.Metadata.Name, entity.Source);
                ColumnPlan key = entity.Key;
                _ = declared.Key(key.Name, key.Type, key.Column, key.Store);
                HashSet<PropertyMetadata> admitted = new(ReferenceEqualityComparer.Instance);
                if (key.Metadata is PropertyMetadata keyMetadata)
                {
                    _ = admitted.Add(keyMetadata);
                }

                foreach (ColumnPlan column in entity.Columns.Where(column => column.Shape <= shape))
                {
                    _ = declared.Property(column.Name, column.Type, column.Column, column.IsNotNull, column.IsUnique, column.Store, column.Collation);
                    if (column.Metadata is PropertyMetadata property)
                    {
                        _ = admitted.Add(property);
                    }
                }

                if (entity.TreeNode is string node)
                {
                    _ = declared.TreeNode(node);
                }

                built.Add((entity.Metadata, declared, entity.JsonColumns, [.. entity.Metadata.Properties.Where(admitted.Contains)]));
            }

            // --- The navigations, once every entity is declared ---
            for (int index = 0; index < built.Count; index++)
            {
                foreach (NavigationPlan navigation in plan.Entities[index].Navigations)
                {
                    _ = built[index].Builder.Navigation(navigation.Name, navigation.Target, navigation.ForeignKey, navigation.IsOptional);
                }
            }

            _ = builder.CalendarMap(UmAlQura, UmAlQuraMonths);
            QueryexSchema schema = builder.Build();
            return new QueryexSchemaMap(
                shape,
                schema,
                [.. built.Select(entity => (entity.Metadata, schema.FindEntity(entity.Metadata.Name)!, entity.Json, entity.Declared))]);
        }

        /// <summary>The shape-independent plan of the model's schemas.</summary>
        /// <param name="Entities">Every entity, in model order.</param>
        /// <param name="Fingerprint">The fingerprint over the plan of the widest shape.</param>
        private sealed record Plan(IReadOnlyList<EntityPlan> Entities, string Fingerprint)
        {
            /// <summary>Plans every entity of the model.</summary>
            /// <param name="model">The runtime model.</param>
            /// <param name="metadata">The model's entity metadata.</param>
            /// <returns>The plan.</returns>
            public static Plan Create(IModel model, IEntityMetadataProvider metadata)
            {
                // --- Each sibling's one-to-one navigation, by its owner ---
                Dictionary<EntityMetadata, List<NavigationPlan>> siblings = new(ReferenceEqualityComparer.Instance);
                foreach (EntityMetadata entity in metadata.All)
                {
                    if (EntityShapes.GetAttribute<SiblingAttribute>(entity.ClrType) is SiblingAttribute sibling)
                    {
                        EntityMetadata owner = metadata.Get(sibling.Owner);
                        if (!siblings.TryGetValue(owner, out List<NavigationPlan>? navigations))
                        {
                            siblings[owner] = navigations = [];
                        }

                        // Keyed by the owner's own key, so the key is never absent but the row may be.
                        navigations.Add(new NavigationPlan(sibling.Navigation, entity.Name, owner.Key.Name, IsOptional: true));
                    }
                }

                List<EntityPlan> entities = [.. metadata.All.Select(entity => EntityPlan.Create(model, entity, siblings.GetValueOrDefault(entity) ?? []))];
                return new Plan(entities, Fingerprinted(entities));
            }

            /// <summary>
            ///     The hex SHA-256 of every entity's name, source, key, properties and navigations in
            ///     model order, every twin included, and of the calendar maps.
            /// </summary>
            /// <param name="entities">The entities.</param>
            /// <returns>The fingerprint.</returns>
            private static string Fingerprinted(List<EntityPlan> entities)
            {
                using FingerprintWriter writer = new();
                foreach (EntityPlan entity in entities)
                {
                    _ = writer.Add("entity").Add(entity.Metadata.Name).Add(entity.Source).Add(entity.Key.Name);
                    foreach (ColumnPlan column in (ColumnPlan[])[entity.Key, .. entity.Columns])
                    {
                        _ = writer.Add("property").Add(column.Name).Add(column.Column).Add(column.Type.ToString())
                            .Add(column.IsNotNull).Add(column.IsUnique).Add(column.Store.Family.ToString())
                            .Add(column.Store.Size?.ToString(CultureInfo.InvariantCulture)).Add(column.Store.Scale?.ToString(CultureInfo.InvariantCulture))
                            .Add(column.Collation);
                    }

                    _ = writer.Add("node").Add(entity.TreeNode);
                    foreach (NavigationPlan navigation in entity.Navigations)
                    {
                        _ = writer.Add("navigation").Add(navigation.Name).Add(navigation.Target).Add(navigation.ForeignKey).Add(navigation.IsOptional);
                    }
                }

                _ = writer.Add("calendar").Add(UmAlQura).Add(UmAlQuraMonths);
                return writer.Finish();
            }
        }

        /// <summary>One entity's place in every shape's schema.</summary>
        /// <param name="Metadata">The entity's metadata.</param>
        /// <param name="Source">The bracket-quoted table.</param>
        /// <param name="Key">The key column.</param>
        /// <param name="Columns">Every other column, in declaration order, the shadow ones last.</param>
        /// <param name="TreeNode">The hierarchy node's property, for a tree.</param>
        /// <param name="Navigations">The navigations: the references, then the siblings'.</param>
        /// <param name="JsonColumns">The JSON columns the schema omits.</param>
        private sealed record EntityPlan(
            EntityMetadata Metadata,
            string Source,
            ColumnPlan Key,
            IReadOnlyList<ColumnPlan> Columns,
            string? TreeNode,
            IReadOnlyList<NavigationPlan> Navigations,
            IReadOnlyList<PropertyMetadata> JsonColumns)
        {
            /// <summary>Plans one entity.</summary>
            /// <param name="model">The runtime model.</param>
            /// <param name="metadata">The entity's metadata.</param>
            /// <param name="siblings">The navigations its siblings give it.</param>
            /// <returns>The plan.</returns>
            public static EntityPlan Create(IModel model, EntityMetadata metadata, IReadOnlyList<NavigationPlan> siblings)
            {
                IEntityType entityType = model.FindEntityType(metadata.ClrType)
                    ?? throw new InvalidOperationException($"The model does not map {metadata.ClrType.Name}, whose metadata names it as the entity {metadata.Name}.");

                // --- The columns unique on their own: the key and every single-column unique index or key ---
                HashSet<IReadOnlyProperty> unique = [.. entityType.GetKeys().Where(static key => key.Properties.Count == 1).Select(static key => (IReadOnlyProperty)key.Properties[0])];
                unique.UnionWith(entityType.GetIndexes().Where(static index => index.IsUnique && index.Properties.Count == 1).Select(static index => (IReadOnlyProperty)index.Properties[0]));

                // --- The shape each twin needs ---
                Dictionary<string, MultilingualShape> twins = new(StringComparer.Ordinal);
                foreach (MultilingualGroup group in metadata.MultilingualGroups)
                {
                    if (group.Secondary is PropertyMetadata secondary)
                    {
                        twins[secondary.Name] = MultilingualShape.PrimaryAndSecondary;
                    }

                    if (group.Tertiary is PropertyMetadata tertiary)
                    {
                        twins[tertiary.Name] = MultilingualShape.All;
                    }
                }

                // --- The mapped scalars: the class's in declaration order, then the shadow ones ---
                // The period columns are the temporal convention's; the runtime model keeps no period annotation.
                string? periodStart = metadata.IsTemporal ? TemporalConvention.PeriodStart : null;
                string? periodEnd = metadata.IsTemporal ? TemporalConvention.PeriodEnd : null;
                ColumnPlan? key = null;
                List<ColumnPlan> columns = [];
                List<PropertyMetadata> json = [];
                IEnumerable<(IProperty Property, PropertyMetadata? Metadata)> candidates = metadata.Properties
                    .Select(property => (entityType.FindProperty(property.Name)!, (PropertyMetadata?)property))
                    .Concat(entityType.GetProperties()
                        .Where(static property => property.IsShadowProperty())
                        .Select(static property => (property, (PropertyMetadata?)null)));
                foreach ((IProperty property, PropertyMetadata? described) in candidates)
                {
                    // A JSON column is read by name, never through the language.
                    if (property.PropertyInfo is not null && EntityShapes.GetAttribute<JsonColumnAttribute>(property.PropertyInfo) is not null)
                    {
                        json.Add(described!);
                        continue;
                    }

                    bool omitted = property.Name == periodStart
                        || property.Name == periodEnd
                        || IsWithheld(metadata.Name, property.Name);
                    if (omitted || QueryexStoreTypes.Of(property.GetColumnType()) is not { } typed)
                    {
                        continue;
                    }

                    ColumnPlan column = new(
                        property.Name,
                        property.GetColumnName(),
                        typed.Type,
                        typed.Store,
                        !property.IsNullable,
                        unique.Contains(property),
                        twins.GetValueOrDefault(property.Name, MultilingualShape.Primary),
                        described,
                        Collation(property));
                    if (property.Name == metadata.Key.Name)
                    {
                        key = column;
                    }
                    else
                    {
                        columns.Add(column);
                    }
                }

                // --- The node, the references and the siblings' navigations ---
                string? node = metadata.Tree is TreeMetadata tree && columns.Any(column => column.Name == tree.NodeColumn) ? tree.NodeColumn : null;
                List<NavigationPlan> navigations = [.. metadata.References.Select(static reference => new NavigationPlan(reference.Navigation, reference.Target.Name, reference.ForeignKey.Name, IsOptional: false))];
                navigations.AddRange(siblings);

                return new EntityPlan(
                    metadata,
                    $"[{metadata.Table.Schema}].[{metadata.Table.Name}]",
                    key ?? throw new InvalidOperationException($"The key of the entity {metadata.Name}, {metadata.Key.Name}, has a store type the query language cannot read."),
                    columns,
                    node,
                    navigations,
                    json);
            }
        }

        /// <summary>One column of an entity.</summary>
        /// <param name="Name">The property name.</param>
        /// <param name="Column">The column name.</param>
        /// <param name="Type">The query type.</param>
        /// <param name="Store">The store type.</param>
        /// <param name="IsNotNull">Whether the column holds no nulls.</param>
        /// <param name="IsUnique">Whether the key or a single-column unique index covers it.</param>
        /// <param name="Shape">The narrowest shape that admits it.</param>
        /// <param name="Metadata">The property's metadata; null for a shadow column.</param>
        /// <param name="Collation">The collation the column declares for itself; null when it takes the database's.</param>
        private sealed record ColumnPlan(
            string Name,
            string Column,
            QueryexType Type,
            QueryexStoreType Store,
            bool IsNotNull,
            bool IsUnique,
            MultilingualShape Shape,
            PropertyMetadata? Metadata,
            string? Collation = null);

        /// <summary>One navigation of an entity.</summary>
        /// <param name="Name">The navigation's name.</param>
        /// <param name="Target">The target entity's name.</param>
        /// <param name="ForeignKey">The foreign key's property name on the declaring entity.</param>
        /// <param name="IsOptional">Whether the target row may be missing although the key holds a value.</param>
        private sealed record NavigationPlan(string Name, string Target, string ForeignKey, bool IsOptional);
    }
}
