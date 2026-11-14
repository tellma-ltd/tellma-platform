// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Microsoft.EntityFrameworkCore;
using System.Reflection;
using Tellma.Core.Composition;
using Tellma.Core.Jobs;

namespace Tellma.Core.Data
{
    /// <summary>
    ///     What one composition puts in the tenant model: the leaf entity classes, the standalone
    ///     table-type classes and Core's model configurations, each closed over the leaves, and the
    ///     built-in schedules the migrations seed. A
    ///     <see cref="TellmaDbContext" /> builds its model from exactly this, so the web host, the
    ///     workers and the migrator, composing the same features, build the same model.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         The entities are the contributed entities with each replaced default swapped for its
    ///         leaf, followed, depth first, by the child classes their <c>[NotMapped]</c> collections
    ///         carry (a child is never contributed: it is saved only with its owner), each swapped for
    ///         its leaf too.
    ///     </para>
    ///     <para>
    ///         An open model configuration (<c>UserConfiguration&lt;TUser&gt; where TUser : User</c>) is
    ///         closed over the leaf registered for its type parameter's class constraint. A
    ///         configuration that is neither a closed nor such an open entity type configuration is
    ///         left out here and reported by the <c>core.model-contributors</c> startup check.
    ///     </para>
    ///     <para>Instances compare by reference: the model cache keeps one model per definition.</para>
    /// </remarks>
    /// <param name="entityTypes">The leaf entity classes, children included, in model order.</param>
    /// <param name="tableTypes">The standalone table-type classes.</param>
    /// <param name="configurations">The closed model configuration classes, in the order they apply.</param>
    /// <param name="leaves">The leaf substitutions: each replaced default and the leaf that replaces it.</param>
    /// <param name="builtInSchedules">The built-in schedules the features declared, in realisation order; null for none.</param>
    internal sealed class TellmaModelDefinition(
        IReadOnlyList<Type> entityTypes,
        IReadOnlyList<Type> tableTypes,
        IReadOnlyList<Type> configurations,
        IReadOnlyDictionary<Type, Type> leaves,
        IReadOnlyList<BuiltInScheduleSeed>? builtInSchedules = null)
    {
        /// <summary>The leaf entity classes, children included, in model order.</summary>
        public IReadOnlyList<Type> EntityTypes { get; } = [.. entityTypes];

        /// <summary>
        ///     The leaf substitutions: each default a <c>UseEntity&lt;TDefault, TLeaf&gt;()</c> replaced and
        ///     the leaf that replaces it. A replaced default is absent from the model, and its leaf keeps the
        ///     default's entity name.
        /// </summary>
        public IReadOnlyDictionary<Type, Type> Leaves { get; } = new Dictionary<Type, Type>(leaves);

        /// <summary>The standalone table-type classes.</summary>
        public IReadOnlyList<Type> TableTypes { get; } = [.. tableTypes];

        /// <summary>The closed model configuration classes, in the order they apply.</summary>
        public IReadOnlyList<Type> Configurations { get; } = [.. configurations];

        /// <summary>The built-in schedules the migrations seed, in realisation order.</summary>
        public IReadOnlyList<BuiltInScheduleSeed> BuiltInSchedules { get; } = [.. builtInSchedules ?? []];

        /// <summary>The leaf registered for an entity class: its substitute, else the class itself.</summary>
        /// <param name="entityType">The entity class.</param>
        /// <returns>The leaf.</returns>
        public Type LeafOf(Type entityType)
        {
            ArgumentNullException.ThrowIfNull(entityType);
            return Leaves.TryGetValue(entityType, out Type? leaf) ? leaf : entityType;
        }

        /// <summary>The definition of a realised composition.</summary>
        /// <param name="record">The realised composition.</param>
        /// <returns>The definition.</returns>
        public static TellmaModelDefinition From(TellmaCompositionRecord record)
        {
            ArgumentNullException.ThrowIfNull(record);

            return Create(
                record.Entities.Select(static entity => entity.EntityType),
                record.TableTypes.Select(static tableType => tableType.Type),
                record.ModelConfigurations.Select(static configuration => configuration.Type),
                record.Leaves,
                [.. record.BuiltInSchedules.Select(static schedule => schedule.Seed)]);
        }

        /// <summary>A definition from contributed parts and the leaf substitutions.</summary>
        /// <param name="entities">The contributed entities, before leaf substitution.</param>
        /// <param name="tableTypes">The contributed standalone table-type classes.</param>
        /// <param name="configurations">The contributed model configurations, closed or open.</param>
        /// <param name="leaves">The leaf substitutions: each replaced default and the leaf that replaces it.</param>
        /// <param name="builtInSchedules">The built-in schedules the migrations seed; null for none.</param>
        /// <returns>The definition.</returns>
        public static TellmaModelDefinition Create(
            IEnumerable<Type> entities,
            IEnumerable<Type> tableTypes,
            IEnumerable<Type> configurations,
            IReadOnlyDictionary<Type, Type> leaves,
            IReadOnlyList<BuiltInScheduleSeed>? builtInSchedules = null)
        {
            ArgumentNullException.ThrowIfNull(entities);
            ArgumentNullException.ThrowIfNull(tableTypes);
            ArgumentNullException.ThrowIfNull(configurations);
            ArgumentNullException.ThrowIfNull(leaves);

            Type LeafOf(Type entity)
            {
                return leaves.TryGetValue(entity, out Type? leaf) ? leaf : entity;
            }

            // --- Entities, each followed by the children its collections carry, depth first ---
            List<Type> entityTypes = [];
            HashSet<Type> seen = [];
            foreach (Type entity in entities)
            {
                AddWithChildren(LeafOf(entity), entityTypes, seen, LeafOf);
            }

            // --- Configurations, closed over the leaves ---
            List<Type> closed = [];
            foreach (Type configuration in configurations)
            {
                if (Close(configuration, LeafOf) is Type closedConfiguration && !closed.Contains(closedConfiguration))
                {
                    closed.Add(closedConfiguration);
                }
            }

            return new TellmaModelDefinition(entityTypes, [.. tableTypes.Distinct()], closed, leaves, builtInSchedules);
        }

        /// <summary>
        ///     The entity type an entity type configuration configures: the type argument of the one
        ///     <see cref="IEntityTypeConfiguration{TEntity}" /> a class implements, or <see langword="null" />
        ///     when it implements none or several.
        /// </summary>
        /// <param name="configuration">The configuration class, closed or open.</param>
        /// <returns>The configured type, a generic parameter for an open configuration, or <see langword="null" />.</returns>
        public static Type? ConfiguredType(Type configuration)
        {
            ArgumentNullException.ThrowIfNull(configuration);

            Type[] interfaces = [.. configuration.GetInterfaces()
                .Where(static candidate => candidate.IsGenericType && candidate.GetGenericTypeDefinition() == typeof(IEntityTypeConfiguration<>))];

            return interfaces is [Type single] ? single.GetGenericArguments()[0] : null;
        }

        /// <summary>
        ///     Closes a model configuration over the leaves: a closed configuration as it is, and an open
        ///     one over the leaf of its one type parameter's class constraint. <see langword="null" /> for
        ///     a class that is neither a usable closed nor a usable open entity type configuration.
        /// </summary>
        /// <param name="configuration">The configuration class.</param>
        /// <param name="leafOf">Resolves an entity to the leaf registered for it, else itself.</param>
        /// <returns>The closed configuration, or <see langword="null" />.</returns>
        public static Type? Close(Type configuration, Func<Type, Type> leafOf)
        {
            ArgumentNullException.ThrowIfNull(configuration);
            ArgumentNullException.ThrowIfNull(leafOf);

            if (!configuration.IsClass || configuration.IsAbstract || ConfiguredType(configuration) is not Type configured)
            {
                return null;
            }

            if (!configuration.ContainsGenericParameters)
            {
                return HasParameterlessConstructor(configuration) ? configuration : null;
            }

            // An open configuration is generic over exactly the entity it configures, and the
            // parameter's class constraint names the replaceable default it is written against.
            if (!configuration.IsGenericTypeDefinition
                || configuration.GetGenericArguments() is not [Type parameter]
                || configured != parameter
                || DefaultOf(parameter) is not Type defaultType)
            {
                return null;
            }

            Type leaf = leafOf(defaultType);
            try
            {
                Type closedConfiguration = configuration.MakeGenericType(leaf);
                return HasParameterlessConstructor(closedConfiguration) ? closedConfiguration : null;
            }
            catch (ArgumentException)
            {
                // The leaf violates another constraint of the parameter (an interface, new()); the
                // startup check reports the configuration.
                return null;
            }
        }

        /// <summary>The replaceable default an open configuration's type parameter is constrained to: its one class constraint.</summary>
        /// <param name="parameter">The generic type parameter.</param>
        /// <returns>The class, or <see langword="null" /> when the parameter has no class constraint.</returns>
        public static Type? DefaultOf(Type parameter)
        {
            ArgumentNullException.ThrowIfNull(parameter);
            return parameter.GetGenericParameterConstraints().FirstOrDefault(static constraint => constraint.IsClass && !constraint.ContainsGenericParameters);
        }

        /// <summary>Whether a class can be created by the model, with a public parameterless constructor.</summary>
        /// <param name="type">The class.</param>
        /// <returns>True when it can.</returns>
        public static bool HasParameterlessConstructor(Type type)
        {
            return type.GetConstructor(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance, Type.EmptyTypes) is not null;
        }

        /// <summary>Adds a leaf and, recursively, the children its collections carry, each once.</summary>
        /// <param name="leaf">The leaf.</param>
        /// <param name="entityTypes">The list being built.</param>
        /// <param name="seen">The leaves already added.</param>
        /// <param name="leafOf">Resolves an entity to the leaf registered for it, else itself.</param>
        private static void AddWithChildren(Type leaf, List<Type> entityTypes, HashSet<Type> seen, Func<Type, Type> leafOf)
        {
            if (!seen.Add(leaf))
            {
                return;
            }

            entityTypes.Add(leaf);
            foreach ((PropertyInfo _, Type childType) in EntityShapes.ChildCollections(leaf))
            {
                AddWithChildren(leafOf(childType), entityTypes, seen, leafOf);
            }
        }
    }
}
