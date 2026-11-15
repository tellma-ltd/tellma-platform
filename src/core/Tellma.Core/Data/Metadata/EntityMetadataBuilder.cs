// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using System.Reflection;
using Tellma.Core.Abstractions.Composition;
using Tellma.Core.Abstractions.Data;
using Tellma.Core.Abstractions.Entities;
using Tellma.Core.Composition;
using Tellma.Core.Composition.Checks;

namespace Tellma.Core.Data.Metadata
{
    /// <summary>
    ///     Builds one <see cref="EntityMetadata" /> per mapped entity leaf from the runtime model and the
    ///     entity classes, and validates the model against the entity contract, collecting every problem
    ///     rather than stopping at the first.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         The build runs in phases, each reading only what the ones before it settled: the leaves
    ///         (which mapped types are entities); each leaf's own parts (table, properties and ownership,
    ///         multilingual groups, unique indexes, natural keys, blob references, tree, table type,
    ///         display name); the entity names and the lookup by base class; the parts that name other
    ///         entities (child collections, references, navigation names); the shadow properties of every
    ///         mapped type, entity or not; and the warnings. The provider is created only when no phase
    ///         reported a problem.
    ///     </para>
    ///     <para>
    ///         The conventions have already refused, by throwing, what no model can be built with. This
    ///         builder reports what the model can hold but the platform refuses, all at once, each problem
    ///         naming the entity and the member.
    ///     </para>
    /// </remarks>
    internal sealed partial class EntityMetadataBuilder
    {
        /// <summary>The open generic platform bases the entity lookup stops at.</summary>
        private static readonly HashSet<Type> PlatformGenericBases =
        [
            typeof(Entity<>), typeof(TopLevelEntity<>), typeof(ChildEntity<>), typeof(TreeEntity<>), typeof(ActivatableTreeEntity<>),
        ];

        /// <summary>The non-generic platform bases the entity lookup stops at.</summary>
        private static readonly HashSet<Type> PlatformBases =
        [
            typeof(TopLevelEntity), typeof(ChildEntity), typeof(TreeEntity), typeof(ActivatableTreeEntity),
        ];

        private readonly IModel _model;
        private readonly TellmaModelDefinition _definition;
        private readonly List<CompositionProblem> _problems = [];
        private readonly List<EntityMetadataWarning> _warnings = [];
        private readonly List<Leaf> _leaves = [];
        private readonly Dictionary<IReadOnlyEntityType, Leaf> _byEntityType = [];
        private readonly Dictionary<Type, EntityMetadata> _byType = [];

        /// <summary>Creates a builder over one model.</summary>
        /// <param name="model">The runtime model.</param>
        /// <param name="definition">What the composition put in the model.</param>
        private EntityMetadataBuilder(IModel model, TellmaModelDefinition definition)
        {
            _model = model;
            _definition = definition;
        }

        /// <summary>Builds and validates the metadata of a model.</summary>
        /// <param name="model">The runtime model.</param>
        /// <param name="definition">What the composition put in the model.</param>
        /// <returns>The provider when the model is sound, with every problem and warning found.</returns>
        public static EntityMetadataBuild Build(IModel model, TellmaModelDefinition definition)
        {
            ArgumentNullException.ThrowIfNull(model);
            ArgumentNullException.ThrowIfNull(definition);
            return new EntityMetadataBuilder(model, definition).Run();
        }

        /// <summary>Runs every phase.</summary>
        /// <returns>The outcome.</returns>
        private EntityMetadataBuild Run()
        {
            // --- Phase 1: which mapped types are entity leaves ---
            SelectLeaves();

            // --- Phase 2: each leaf's own parts ---
            foreach (Leaf leaf in _leaves)
            {
                DescribeLeaf(leaf);
            }

            List<Leaf> described = [.. _leaves.Where(static leaf => leaf.Metadata is not null)];

            // --- Phase 3: names, and the lookup from any class in a leaf's chain ---
            CheckNames(described);
            IndexByBase(described);

            // --- Phase 4: the parts that name other entities ---
            foreach (Leaf leaf in described)
            {
                PairChildren(leaf);
            }

            foreach (Leaf leaf in described)
            {
                CheckAggregate(leaf);
                CollectReferences(leaf);
            }

            foreach (Leaf leaf in described)
            {
                CheckNavigationNames(leaf, described);
            }

            // --- Phase 5: the model's shadow properties, over every mapped type ---
            CheckShadowProperties();

            // --- Phase 6: warnings ---
            WarnUnkeyedTargets(described);

            if (_problems.Count > 0)
            {
                return new EntityMetadataBuild(null, [.. _problems], [.. _warnings]);
            }

            // --- Freeze: every lazily built part is built now, so the metadata is immutable from here ---
            List<EntityMetadata> all = [.. described.Select(static leaf => leaf.Metadata!)];
            foreach (EntityMetadata metadata in all)
            {
                _ = metadata.Children;
                _ = metadata.References;
                _ = metadata.SchemaFingerprint;
            }

            EntityMetadataProvider provider = new(all, _byType, Fingerprints.Storage(_model));
            return new EntityMetadataBuild(provider, [], [.. _warnings]);
        }

        /// <summary>
        ///     Phase 1: every mapped type that is an entity leaf, in model order (the definition's entities,
        ///     then the ones Core's configurations add, by name). Reports a contributed class that is not an
        ///     entity, a replaced default the model still maps, and a type mapped as derived from another.
        /// </summary>
        private void SelectLeaves()
        {
            HashSet<Type> contributed = [.. _definition.EntityTypes];
            Dictionary<Type, List<Type>> defaultsOf = [];
            foreach ((Type replaced, Type leaf) in _definition.Leaves)
            {
                if (!defaultsOf.TryGetValue(leaf, out List<Type>? defaults))
                {
                    defaultsOf[leaf] = defaults = [];
                }

                defaults.Add(replaced);
            }

            IEnumerable<IEntityType> ordered = _definition.EntityTypes
                .Select(_model.FindEntityType)
                .OfType<IEntityType>()
                .Concat(_model.GetEntityTypes()
                    .Where(entityType => !contributed.Contains(entityType.ClrType))
                    .OrderBy(static entityType => entityType.Name, StringComparer.Ordinal))
                .Distinct();

            foreach (IEntityType entityType in ordered)
            {
                Type clrType = entityType.ClrType;
                if (entityType.IsOwned() || entityType.HasSharedClrType)
                {
                    continue;
                }

                // A replaced default is absent from the model; a configuration or navigation naming it
                // re-adds it, and its leaf then maps as a derived type under a discriminator.
                if (_definition.Leaves.TryGetValue(clrType, out Type? replacement))
                {
                    Report(
                        $"The model maps {TypeDisplay.Qualified(clrType)}, which UseEntity<{TypeDisplay.Short(clrType)}, {TypeDisplay.Short(replacement)}>() replaced: a model configuration or navigation names the default, which re-adds it beside its leaf.",
                        $"Write the configuration or navigation against the leaf: open over a type parameter constrained to {TypeDisplay.Short(clrType)}, which the model closes over {TypeDisplay.Short(replacement)}.");
                    continue;
                }

                if (entityType.BaseType is IEntityType baseType)
                {
                    // A leaf under a replaced default is the problem just reported.
                    if (!_definition.Leaves.ContainsKey(baseType.GetRootType().ClrType))
                    {
                        Report(
                            $"The model maps {TypeDisplay.Qualified(clrType)} as derived from {TypeDisplay.Qualified(baseType.ClrType)}; the platform maps leaves only, one class per table.",
                            $"Contribute only the leaf, and register it for its default with UseEntity<{TypeDisplay.Short(baseType.ClrType)}, {TypeDisplay.Short(clrType)}>() when it replaces one.");
                    }

                    continue;
                }

                if (!EntityShapes.IsKeyed(clrType))
                {
                    // Core's own migration-only tables are mapped but are not entities.
                    if (contributed.Contains(clrType))
                    {
                        Report(
                            $"The entity {TypeDisplay.Qualified(clrType)} does not derive from Entity<TKey>.",
                            "Derive the class from TopLevelEntity, ChildEntity, TreeEntity, ActivatableTreeEntity or Entity<TKey>.");
                    }

                    continue;
                }

                if (clrType.IsGenericType)
                {
                    Report(
                        $"The entity {TypeDisplay.Qualified(clrType)} is a generic class, which cannot name an entity.",
                        $"Close the generic in a non-generic leaf (class {clrType.Name.Split('`')[0]} : {TypeDisplay.Short(clrType)}) and contribute that.");
                    continue;
                }

                Type defaultType = clrType;
                if (defaultsOf.TryGetValue(clrType, out List<Type>? replaced))
                {
                    if (replaced.Count > 1)
                    {
                        Report(
                            $"The leaf {TypeDisplay.Qualified(clrType)} replaces {string.Join(" and ", replaced.Select(TypeDisplay.Qualified))}; a leaf replaces one default.",
                            "Register the leaf with one UseEntity<TDefault, TLeaf>() call, for the most derived default.");
                    }

                    defaultType = replaced[0];
                }

                Leaf added = new(entityType, defaultType);
                _leaves.Add(added);
                _byEntityType[entityType] = added;
            }

            // One class per table: a second class on a table would share its rows.
            foreach (IGrouping<(string?, string?), Leaf> table in _leaves
                .GroupBy(static leaf => (leaf.EntityType.GetSchema(), leaf.EntityType.GetTableName()))
                .Where(static group => group.Count() > 1))
            {
                Report(
                    $"The table [{table.Key.Item1}].[{table.Key.Item2}] is mapped by {string.Join(" and ", table.Select(static leaf => TypeDisplay.Qualified(leaf.ClrType)))}; a table has one leaf.",
                    "Give each class a table of its own with [Table], or register one as the other's leaf with UseEntity<TDefault, TLeaf>().");
            }
        }

        /// <summary>Phase 3: entity names are unique.</summary>
        /// <param name="described">The leaves with metadata.</param>
        private void CheckNames(List<Leaf> described)
        {
            foreach (IGrouping<string, Leaf> group in described
                .GroupBy(static leaf => leaf.Metadata!.Name, StringComparer.OrdinalIgnoreCase)
                .Where(static group => group.Count() > 1))
            {
                Report(
                    $"The entities {string.Join(" and ", group.Select(static leaf => TypeDisplay.Qualified(leaf.ClrType)))} share the entity name '{group.Key}'.",
                    "Rename one class or move it to another schema: an entity's name is its schema and its default's class name.");
            }
        }

        /// <summary>
        ///     Phase 3: maps each leaf, and every class it derives from below the platform's bases, to its
        ///     metadata, and reports a class two leaves derive from, which would leave a lookup ambiguous.
        /// </summary>
        /// <param name="described">The leaves with metadata.</param>
        private void IndexByBase(List<Leaf> described)
        {
            Dictionary<Type, List<Leaf>> sharers = [];
            foreach (Leaf leaf in described)
            {
                for (Type? type = leaf.ClrType; type is not null && type != typeof(object) && !IsPlatformBase(type); type = type.BaseType)
                {
                    if (!sharers.TryGetValue(type, out List<Leaf>? leaves))
                    {
                        sharers[type] = leaves = [];
                    }

                    leaves.Add(leaf);
                }
            }

            // Report each shared class once, at the most derived class the same leaves share.
            HashSet<Type> reported = [];
            foreach ((Type type, List<Leaf> leaves) in sharers)
            {
                if (leaves.Count == 1)
                {
                    _byType[type] = leaves[0].Metadata!;
                    continue;
                }

                bool sharedBelow = sharers.Any(other => other.Key != type
                    && other.Value.Count == leaves.Count
                    && type.IsAssignableFrom(other.Key)
                    && other.Value.All(leaves.Contains));
                if (!sharedBelow && reported.Add(type))
                {
                    Report(
                        $"The leaves {string.Join(" and ", leaves.Select(static leaf => TypeDisplay.Qualified(leaf.ClrType)))} both derive from {TypeDisplay.Qualified(type)}, so it names no one leaf.",
                        $"Map one leaf per class: register the leaf that replaces {TypeDisplay.Short(type)} with UseEntity<{TypeDisplay.Short(type)}, TLeaf>(), or derive the others from a class of their own.");
                }
            }
        }

        /// <summary>
        ///     Phase 5: the platform declares every column of the model that no property of a class
        ///     carries. Any other is implicit: EF Core adds a shadow foreign key for a navigation that has
        ///     no foreign-key property, a join table for a many-to-many pair of collections, and a table of
        ///     shadow keys for an owned type, and a model configuration can map a column by name alone.
        ///     Covers every mapped type, the platform's own tables included. A join table and an owned type
        ///     are reported once each, naming the navigations behind them; any other column is reported
        ///     once, on the type that declares it.
        /// </summary>
        private void CheckShadowProperties()
        {
            // --- The join tables of many-to-many navigations, with the navigations through each ---
            Dictionary<IEntityType, List<ISkipNavigation>> joins = [];
            foreach (ISkipNavigation skip in _model.GetEntityTypes().SelectMany(static entityType => entityType.GetDeclaredSkipNavigations()))
            {
                if (!joins.TryGetValue(skip.JoinEntityType, out List<ISkipNavigation>? through))
                {
                    joins[skip.JoinEntityType] = through = [];
                }

                through.Add(skip);
            }

            foreach (IEntityType entityType in _model.GetEntityTypes())
            {
                // --- A join table or an owned type is one problem; its keys follow from it ---
                if (joins.TryGetValue(entityType, out List<ISkipNavigation>? navigations))
                {
                    ReportJoin(entityType, navigations);
                    continue;
                }

                if (entityType.IsOwned())
                {
                    ReportOwned(entityType);
                    continue;
                }

                // --- Every other column no property carries: shadow, or an indexer property of a property bag ---
                foreach (IProperty property in entityType.GetDeclaredProperties())
                {
                    if (!(property.IsShadowProperty() || property.IsIndexerProperty()) || PlatformShadowColumns.IsDeclared(entityType, property.Name))
                    {
                        continue;
                    }

                    // A hierarchy's discriminator is the shadow column EF Core adds for a derived type,
                    // which phase 1 already refuses with the type named.
                    if (property == entityType.FindDiscriminatorProperty() && entityType.GetDerivedTypes().Any())
                    {
                        continue;
                    }

                    ReportShadow(entityType, property);
                }
            }
        }

        /// <summary>Reports the join table EF Core adds for a many-to-many pair of collections, naming them.</summary>
        /// <param name="join">The join entity type.</param>
        /// <param name="navigations">The many-to-many navigations through it.</param>
        private void ReportJoin(IEntityType join, List<ISkipNavigation> navigations)
        {
            string named = string.Join(" and ", navigations.Select(static skip => $"{TypeDisplay.Short(skip.DeclaringEntityType.ClrType)}.{skip.Name}"));
            string entities = string.Join(" and ", navigations.Select(static skip => TypeDisplay.Qualified(skip.DeclaringEntityType.ClrType)).Distinct());
            Report(
                $"The model maps {named} of {entities} as a many-to-many link, for which EF Core adds the join table {join.GetTableName()} with columns no class carries.",
                $"Mark {named} [NotMapped], and store the link as a child entity with a [ParentKey] to one side and a [References] to the other: an entity carries foreign keys, never navigations.");
        }

        /// <summary>Reports an owned type, naming the navigation that owns it.</summary>
        /// <param name="owned">The owned entity type.</param>
        private void ReportOwned(IEntityType owned)
        {
            string type = TypeDisplay.Short(owned.ClrType);
            IForeignKey? ownership = owned.FindOwnership();
            string navigation = ownership?.PrincipalToDependent is INavigation toOwned
                ? $"{TypeDisplay.Short(ownership.PrincipalEntityType.ClrType)}.{toOwned.Name}"
                : type;
            string owner = ownership is null ? "The model" : $"The entity {TypeDisplay.Qualified(ownership.PrincipalEntityType.ClrType)}";
            Report(
                $"{owner} maps {TypeDisplay.Qualified(owned.ClrType)} as an owned type through {navigation}, whose keys no class carries; the platform maps no owned types.",
                $"Mark {navigation} [NotMapped] and remove [Owned] or the OwnsOne/OwnsMany that makes {type} owned; to store it, make {type} a child entity with a [ParentKey] to its owner.");
        }

        /// <summary>Reports one shadow property the platform does not declare, with the cause when it is a foreign key.</summary>
        /// <param name="entityType">The type that declares the property.</param>
        /// <param name="property">The property.</param>
        private void ReportShadow(IEntityType entityType, IProperty property)
        {
            string entity = entityType.HasSharedClrType ? entityType.Name : TypeDisplay.Qualified(entityType.ClrType);
            string owner = entityType.HasSharedClrType ? entityType.Name : TypeDisplay.Short(entityType.ClrType);
            string name = property.Name;

            // --- A shadow foreign key: from a navigation without a key property, or a configuration naming the column ---
            if (property.GetContainingForeignKeys().FirstOrDefault() is IForeignKey foreignKey)
            {
                string target = TypeDisplay.Qualified(foreignKey.PrincipalEntityType.ClrType);
                string principal = TypeDisplay.Short(foreignKey.PrincipalEntityType.ClrType);
                string declare = $"declare {name} as a public property of {owner}, marked [References(typeof({principal}))] or, for a child's parent, [ParentKey]";

                // The navigation on the dependent: the reference it stands for is the property.
                if (foreignKey.DependentToPrincipal is INavigation toPrincipal)
                {
                    Report(
                        $"The entity {entity} maps {name}, a shadow foreign key to {target} that EF Core adds for the navigation {owner}.{toPrincipal.Name}, since no property of the class carries it.",
                        $"Remove the navigation {owner}.{toPrincipal.Name} and {declare}: an entity carries foreign keys, never navigations.");
                    return;
                }

                // The navigation on the principal: a collection of children is never mapped.
                if (foreignKey.PrincipalToDependent is INavigation toDependent)
                {
                    Report(
                        $"The entity {entity} maps {name}, a shadow foreign key to {target} that EF Core adds for the navigation {principal}.{toDependent.Name}, since no property of the class carries it.",
                        $"Mark {principal}.{toDependent.Name} [NotMapped], as every child collection is, and {declare}: an entity carries foreign keys, never navigations.");
                    return;
                }

                Report(
                    $"The entity {entity} maps {name}, a shadow foreign key to {target} that a model configuration declares by name, which no property of the class carries.",
                    $"Declare {name} as a public property of {owner}, and name that property in the configuration's HasForeignKey.");
                return;
            }

            // --- Any other shadow column ---
            Report(
                $"The entity {entity} maps {name}, a shadow property that no property of the class carries.",
                $"Declare {name} as a public property of {owner}, or remove the model configuration that maps it: the platform's shadow columns are the period columns of a temporal entity, the node of a tree and the model-only columns of its own job and blob tables.");
        }

        /// <summary>Whether a class is one of the platform's entity bases, where the entity lookup stops.</summary>
        /// <param name="type">The class.</param>
        /// <returns>True for a platform base.</returns>
        private static bool IsPlatformBase(Type type)
        {
            return type.IsGenericType
                ? PlatformGenericBases.Contains(type.GetGenericTypeDefinition())
                : PlatformBases.Contains(type);
        }

        /// <summary>Records a problem.</summary>
        /// <param name="problem">What is wrong, naming the entity and the member.</param>
        /// <param name="fix">The remedy.</param>
        private void Report(string problem, string fix)
        {
            _problems.Add(new CompositionProblem(CoreCheckNames.EntityMetadata, problem, fix));
        }

        /// <summary>One entity leaf of the model and what the phases learn about it.</summary>
        /// <param name="entityType">The leaf's entity type.</param>
        /// <param name="defaultType">The default the leaf replaces, else the leaf itself.</param>
        private sealed class Leaf(IEntityType entityType, Type defaultType)
        {
            /// <summary>The leaf's entity type.</summary>
            public IEntityType EntityType { get; } = entityType;

            /// <summary>The leaf's class.</summary>
            public Type ClrType { get; } = entityType.ClrType;

            /// <summary>The default the leaf replaces, else the leaf itself; its class name names the entity.</summary>
            public Type DefaultType { get; } = defaultType;

            /// <summary>The leaf's metadata; null when its own parts could not be described.</summary>
            public EntityMetadata? Metadata { get; set; }

            /// <summary>The leaf's properties' metadata, by property name.</summary>
            public Dictionary<string, PropertyMetadata> Properties { get; } = new(StringComparer.Ordinal);

            /// <summary>The CLR property behind each of the leaf's properties, by property name.</summary>
            public Dictionary<string, PropertyInfo> Members { get; } = new(StringComparer.Ordinal);

            /// <summary>The store type of each of the leaf's properties, by property name.</summary>
            public Dictionary<string, string> StoreTypes { get; } = new(StringComparer.Ordinal);

            /// <summary>The child's one parent-key property; null for a non-child or one that declares none or several.</summary>
            public PropertyInfo? ParentKey { get; set; }

            /// <summary>The parent whose collection carries this child, once paired.</summary>
            public Leaf? Parent { get; set; }

            /// <summary>The names of the capability members a leaf of this shape owns by derivation.</summary>
            public HashSet<string> ServerOwnedByDerivation { get; } = new(StringComparer.Ordinal);

            /// <summary>The property implementing <see cref="IJobEntity.JobId" />, when the leaf implements it.</summary>
            public PropertyInfo? JobIdMember { get; set; }

            /// <summary>The child collections, built in phase 4.</summary>
            public List<ChildCollectionMetadata> Collections { get; } = [];

            /// <summary>The references, built in phase 4.</summary>
            public List<ReferenceMetadata> References { get; } = [];

            /// <summary>The references a natural key of the target would serve, by target, with the navigation's description.</summary>
            public List<(Leaf Target, string Description)> KeyedReferences { get; } = [];
        }
    }
}
