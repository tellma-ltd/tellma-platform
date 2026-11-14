// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Microsoft.CodeAnalysis;

namespace Tellma.Core.Analyzers.Entities
{
    /// <summary>One table a mapped entity type declares, as the compilation sees it.</summary>
    /// <param name="key">The table.</param>
    /// <param name="entity">The first entity type found mapped to it, for messages.</param>
    internal sealed class EntityTable(TableKey key, INamedTypeSymbol entity)
    {
        /// <summary>The table.</summary>
        public TableKey Key { get; } = key;

        /// <summary>The first entity type found mapped to it, for messages.</summary>
        public INamedTypeSymbol Entity { get; } = entity;

        /// <summary>Whether an entity mapped to it carries <c>[BumpsUserVersionTag]</c>.</summary>
        public bool BumpsUserTag { get; set; }

        /// <summary>The tables of the top-level entities whose stacks own it: itself for a root, its roots for a child table.</summary>
        public HashSet<TableKey> Owners { get; } = [];
    }

    /// <summary>
    ///     The tables of the mapped entity types a compilation can see, in its own sources and in the
    ///     assemblies that reference the contract: each entity's <c>[Table]</c>, inherited by a leaf,
    ///     and the tables of its child collections, each owned by the root it hangs from.
    /// </summary>
    internal sealed class EntityTables
    {
        /// <summary>The contract assembly, whose referencing assemblies hold the mapped entities.</summary>
        private const string ContractAssembly = "Tellma.Core.Abstractions";

        /// <summary>The schema SQL Server assumes when a name carries none.</summary>
        private const string DefaultSchema = "dbo";

        /// <summary>The deepest constraint chain followed from a type parameter.</summary>
        private const int MaximumDepth = 8;

        /// <summary>The tables, by key.</summary>
        private readonly Dictionary<TableKey, EntityTable> _tables = [];

        /// <summary>Indexes a compilation; <see cref="Build" /> is the entry point.</summary>
        private EntityTables()
        {
        }

        /// <summary>Indexes the mapped entity types a compilation can see.</summary>
        /// <param name="compilation">The compilation.</param>
        /// <returns>The index.</returns>
        public static EntityTables Build(Compilation compilation)
        {
            EntityTables index = new();

            // --- The entity types: the compilation's own, and those of every assembly built on the contract ---
            List<INamedTypeSymbol> entities = [];
            foreach (IAssemblySymbol assembly in RelevantAssemblies(compilation))
            {
                CollectEntities(assembly.GlobalNamespace, entities);
            }

            // --- Each entity's table, then its child tables under the root ---
            foreach (INamedTypeSymbol entity in entities)
            {
                if (TableOf(entity, 0) is not TableKey table)
                {
                    continue;
                }

                EntityTable entry = index.Entry(table, entity);
                entry.BumpsUserTag |= entity.FindInheritedAttribute(KnownNames.EntitiesNamespace, KnownNames.BumpsUserVersionTagAttribute) is not null;
                if (!entity.IsOrDerivesFrom(KnownNames.EntitiesNamespace, KnownNames.ChildEntity))
                {
                    _ = entry.Owners.Add(table);
                    index.AddChildren(entity, table, new HashSet<ITypeSymbol>(SymbolEqualityComparer.Default));
                }
            }

            return index;
        }

        /// <summary>The entry of a table, or null when no mapped entity declares it.</summary>
        /// <param name="table">The table.</param>
        /// <returns>The entry.</returns>
        public EntityTable? Find(TableKey table)
        {
            return _tables.TryGetValue(table, out EntityTable? entry) ? entry : null;
        }

        /// <summary>
        ///     The table an entity type maps to: its own or an inherited <c>[Table]</c>, or for a type
        ///     parameter the table of the first constraint that has one.
        /// </summary>
        /// <param name="type">The type.</param>
        /// <returns>The table, or null when it maps to none.</returns>
        public static TableKey? TableOf(ITypeSymbol type)
        {
            return TableOf(type, 0);
        }

        /// <summary>Finds a table of an entity type, following type-parameter constraints.</summary>
        /// <param name="type">The type.</param>
        /// <param name="depth">How many constraints were followed already.</param>
        /// <returns>The table, or null.</returns>
        private static TableKey? TableOf(ITypeSymbol type, int depth)
        {
            if (type is ITypeParameterSymbol parameter)
            {
                if (depth >= MaximumDepth)
                {
                    return null;
                }

                foreach (ITypeSymbol constraint in parameter.ConstraintTypes)
                {
                    if (TableOf(constraint, depth + 1) is TableKey constrained)
                    {
                        return constrained;
                    }
                }

                return null;
            }

            if (!type.IsOrDerivesFrom(KnownNames.EntitiesNamespace, KnownNames.Entity)
                || type.FindInheritedAttribute(KnownNames.SchemaNamespace, KnownNames.TableAttribute) is not AttributeData table
                || table.ConstructorArguments.Length == 0
                || table.ConstructorArguments[0].Value is not string name)
            {
                return null;
            }

            string schema = DefaultSchema;
            foreach (KeyValuePair<string, TypedConstant> argument in table.NamedArguments)
            {
                if (argument.Key == "Schema" && argument.Value.Value is string named && named.Length > 0)
                {
                    schema = named;
                }
            }

            return new TableKey(schema, name);
        }

        /// <summary>The compilation's own assembly, the contract, and every referenced assembly that references the contract.</summary>
        /// <param name="compilation">The compilation.</param>
        /// <returns>The assemblies.</returns>
        private static IEnumerable<IAssemblySymbol> RelevantAssemblies(Compilation compilation)
        {
            yield return compilation.Assembly;
            foreach (IAssemblySymbol assembly in compilation.SourceModule.ReferencedAssemblySymbols)
            {
                if (string.Equals(assembly.Name, ContractAssembly, StringComparison.Ordinal)
                    || assembly.Modules.Any(static module => module.ReferencedAssemblies.Any(static reference => string.Equals(reference.Name, ContractAssembly, StringComparison.Ordinal))))
                {
                    yield return assembly;
                }
            }
        }

        /// <summary>Collects every class in a namespace tree that derives from the entity base, nested classes included.</summary>
        /// <param name="ns">The namespace.</param>
        /// <param name="entities">Where to add them.</param>
        private static void CollectEntities(INamespaceSymbol ns, List<INamedTypeSymbol> entities)
        {
            foreach (INamespaceOrTypeSymbol member in ns.GetMembers())
            {
                if (member is INamespaceSymbol child)
                {
                    CollectEntities(child, entities);
                }
                else if (member is INamedTypeSymbol type)
                {
                    CollectTypes(type, entities);
                }
            }
        }

        /// <summary>Collects a type when it is an entity class, then its nested types.</summary>
        /// <param name="type">The type.</param>
        /// <param name="entities">Where to add them.</param>
        private static void CollectTypes(INamedTypeSymbol type, List<INamedTypeSymbol> entities)
        {
            if (type.TypeKind == TypeKind.Class && type.IsOrDerivesFrom(KnownNames.EntitiesNamespace, KnownNames.Entity))
            {
                entities.Add(type);
            }

            foreach (INamedTypeSymbol nested in type.GetTypeMembers())
            {
                CollectTypes(nested, entities);
            }
        }

        /// <summary>The element type of a collection property's type, or null when it is no collection.</summary>
        /// <param name="type">The property's type.</param>
        /// <returns>The element type.</returns>
        private static ITypeSymbol? ElementOf(ITypeSymbol type)
        {
            return type.SpecialType == SpecialType.System_String
                ? null
                : type.FindInterfaces("System.Collections.Generic", "IEnumerable`1").FirstOrDefault()?.TypeArguments[0];
        }

        /// <summary>The entry of a table, created on first sight.</summary>
        /// <param name="table">The table.</param>
        /// <param name="entity">The entity type that declares it.</param>
        /// <returns>The entry.</returns>
        private EntityTable Entry(TableKey table, INamedTypeSymbol entity)
        {
            if (!_tables.TryGetValue(table, out EntityTable? entry))
            {
                entry = new EntityTable(table, entity);
                _tables[table] = entry;
            }

            return entry;
        }

        /// <summary>Adds the tables of a type's child collections, at every depth, as owned by a root's table.</summary>
        /// <param name="type">The entity type whose collections are read.</param>
        /// <param name="root">The root's table.</param>
        /// <param name="visited">The child types already followed, against cycles.</param>
        private void AddChildren(ITypeSymbol type, TableKey root, HashSet<ITypeSymbol> visited)
        {
            for (ITypeSymbol? current = type; current is not null; current = current.BaseType)
            {
                foreach (IPropertySymbol property in current.GetMembers().OfType<IPropertySymbol>())
                {
                    if (ElementOf(property.Type) is not INamedTypeSymbol child
                        || !child.IsOrDerivesFrom(KnownNames.EntitiesNamespace, KnownNames.ChildEntity)
                        || !visited.Add(child)
                        || TableOf(child, 0) is not TableKey table)
                    {
                        continue;
                    }

                    EntityTable entry = Entry(table, child);
                    entry.BumpsUserTag |= child.FindInheritedAttribute(KnownNames.EntitiesNamespace, KnownNames.BumpsUserVersionTagAttribute) is not null;
                    _ = entry.Owners.Add(root);
                    AddChildren(child, root, visited);
                }
            }
        }
    }
}
