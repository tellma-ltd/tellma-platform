// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Tellma.Core.Abstractions.Calendars;

namespace Tellma.Core.Abstractions.Composition
{
    /// <summary>
    ///     What a feature adds in <see cref="ITellmaFeature.Contribute" />: data-only
    ///     <see cref="FeatureContributionItem" /> records, and nested features. Each method records an
    ///     item; nothing is registered until the platform realises the composition.
    /// </summary>
    /// <remarks>
    ///     The typed methods are sugar over <see cref="Add" />. Each spec that introduces an item kind
    ///     adds its sugar here or as an extension method; <see cref="Add" /> is the escape hatch for an
    ///     item kind that has none.
    /// </remarks>
    public sealed class FeatureContribution
    {
        private readonly List<FeatureContributionItem> _items = [];
        private readonly List<ITellmaFeature> _features = [];

        /// <summary>The items recorded so far, in the order they were added.</summary>
        public IReadOnlyList<FeatureContributionItem> Items => _items;

        /// <summary>The nested features recorded by <see cref="Feature" />, in the order they were added.</summary>
        public IReadOnlyList<ITellmaFeature> Features => _features;

        /// <summary>
        ///     Adds an item. An item whose exact runtime type no realizer handles is a composition
        ///     problem naming the item type.
        /// </summary>
        /// <param name="item">The item.</param>
        /// <returns>This contribution, for chaining.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="item" /> is null.</exception>
        public FeatureContribution Add(FeatureContributionItem item)
        {
            ArgumentNullException.ThrowIfNull(item);
            _items.Add(item);
            return this;
        }

        /// <summary>
        ///     Adds a nested feature. It is declared, validated and ordered with every other feature,
        ///     and it is realised before the feature that nests it, as if that feature required it.
        ///     Nesting an instance of a feature type that was already added is a no-op for the same
        ///     instance and a composition problem for a different one.
        /// </summary>
        /// <param name="feature">The nested feature.</param>
        /// <returns>This contribution, for chaining.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="feature" /> is null.</exception>
        public FeatureContribution Feature(ITellmaFeature feature)
        {
            ArgumentNullException.ThrowIfNull(feature);
            _features.Add(feature);
            return this;
        }

        /// <summary>Registers <typeparamref name="TImplementation" /> as a singleton <typeparamref name="TService" />.</summary>
        /// <typeparam name="TService">The service type.</typeparam>
        /// <typeparam name="TImplementation">The concrete implementation type.</typeparam>
        /// <returns>This contribution, for chaining.</returns>
        public FeatureContribution Singleton<TService, TImplementation>()
            where TService : class
            where TImplementation : class, TService
        {
            return Add(new ServiceContributionItem(typeof(TService), typeof(TImplementation), null, ServiceLifetimeKind.Singleton));
        }

        /// <summary>Registers <typeparamref name="TImplementation" /> as a scoped <typeparamref name="TService" />.</summary>
        /// <typeparam name="TService">The service type.</typeparam>
        /// <typeparam name="TImplementation">The concrete implementation type.</typeparam>
        /// <returns>This contribution, for chaining.</returns>
        public FeatureContribution Scoped<TService, TImplementation>()
            where TService : class
            where TImplementation : class, TService
        {
            return Add(new ServiceContributionItem(typeof(TService), typeof(TImplementation), null, ServiceLifetimeKind.Scoped));
        }

        /// <summary>Registers <typeparamref name="TImplementation" /> as a transient <typeparamref name="TService" />.</summary>
        /// <typeparam name="TService">The service type.</typeparam>
        /// <typeparam name="TImplementation">The concrete implementation type.</typeparam>
        /// <returns>This contribution, for chaining.</returns>
        public FeatureContribution Transient<TService, TImplementation>()
            where TService : class
            where TImplementation : class, TService
        {
            return Add(new ServiceContributionItem(typeof(TService), typeof(TImplementation), null, ServiceLifetimeKind.Transient));
        }

        /// <summary>
        ///     Contributes a standalone table-type class: the row type of a table-valued parameter
        ///     that is not an entity.
        /// </summary>
        /// <typeparam name="TRow">The row class.</typeparam>
        /// <returns>This contribution, for chaining.</returns>
        public FeatureContribution TableType<TRow>()
            where TRow : class
        {
            return Add(new TableTypeContributionItem(typeof(TRow)));
        }

        /// <summary>
        ///     Contributes a calendar system. The platform creates one instance of it, a singleton,
        ///     and composes it into the calendar registry by its code.
        /// </summary>
        /// <typeparam name="TCalendar">The calendar system class, with a public parameterless constructor.</typeparam>
        /// <returns>This contribution, for chaining.</returns>
        public FeatureContribution Calendar<TCalendar>()
            where TCalendar : class, ICalendarSystem
        {
            return Add(new CalendarContributionItem(typeof(TCalendar)));
        }

        /// <summary>
        ///     Contributes an entity with the platform's default service. The entity joins the tenant
        ///     model; a distribution's leaf replaces it when one is registered for it.
        /// </summary>
        /// <typeparam name="TEntity">The entity.</typeparam>
        /// <returns>This contribution, for chaining.</returns>
        public FeatureContribution Entity<TEntity>()
            where TEntity : class
        {
            return Add(new StackContributionItem(typeof(TEntity), null, null));
        }

        /// <summary>
        ///     Contributes an entity whose stack runs through <typeparamref name="TService" />. The entity
        ///     joins the tenant model; a distribution's leaf replaces it when one is registered for it.
        /// </summary>
        /// <typeparam name="TEntity">The entity.</typeparam>
        /// <typeparam name="TService">The service class of the entity's stack.</typeparam>
        /// <returns>This contribution, for chaining.</returns>
        public FeatureContribution Entity<TEntity, TService>()
            where TEntity : class
            where TService : class
        {
            return Add(new StackContributionItem(typeof(TEntity), typeof(TService), null));
        }

        /// <summary>
        ///     Requires the leaf registered for <typeparamref name="TEntity" /> to implement the
        ///     interface <typeparamref name="TShape" />. A leaf without it fails startup with the
        ///     interface's members listed as the remedy.
        /// </summary>
        /// <typeparam name="TEntity">The entity another feature owns.</typeparam>
        /// <typeparam name="TShape">The interface its leaf must implement.</typeparam>
        /// <returns>This contribution, for chaining.</returns>
        public FeatureContribution RequiresShape<TEntity, TShape>()
            where TEntity : class
            where TShape : class
        {
            return Add(new ShapeRequirementContributionItem(typeof(TEntity), typeof(TShape)));
        }
    }
}
