// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Tellma.Core.Abstractions.Composition;

namespace Tellma.Core.Composition.Realizers
{
    /// <summary>
    ///     The realizers of one composition, keyed by the exact item type each handles. A set is
    ///     created per composition, so a realizer may keep state across the items it realises (the
    ///     service realizer tracks what to replace).
    /// </summary>
    internal sealed class ContributionRealizerSet
    {
        private readonly Dictionary<Type, Action<FeatureContributionItem, ITellmaFeature, RealizationContext>> _realizers = [];

        /// <summary>Creates the set of every realizer the platform ships.</summary>
        /// <returns>A new set.</returns>
        public static ContributionRealizerSet CreateBuiltIn()
        {
            // Each spec that adds an item kind adds its realizer here.
            return new ContributionRealizerSet()
                .Add(new ServiceContributionRealizer())
                .Add(new TableTypeContributionRealizer())
                .Add(new ShapeRequirementContributionRealizer())
                .Add(new ModelContributionRealizer())
                .Add(new CalendarContributionRealizer())
                .Add(new ValueSetContributionRealizer())
                .Add(new StackContributionRealizer());
        }

        /// <summary>Adds the realizer of <typeparamref name="TItem" />.</summary>
        /// <typeparam name="TItem">The item type.</typeparam>
        /// <param name="realizer">The realizer.</param>
        /// <returns>This set, for chaining.</returns>
        /// <exception cref="InvalidOperationException">A realizer for <typeparamref name="TItem" /> is already in the set.</exception>
        public ContributionRealizerSet Add<TItem>(IContributionRealizer<TItem> realizer)
            where TItem : FeatureContributionItem
        {
            ArgumentNullException.ThrowIfNull(realizer);

            return _realizers.TryAdd(typeof(TItem), (item, owner, context) => realizer.Realize((TItem)item, owner, context))
                ? this
                : throw new InvalidOperationException($"A realizer for {TypeDisplay.Qualified(typeof(TItem))} is already registered.");
        }

        /// <summary>Whether a realizer handles items of exactly <paramref name="itemType" />.</summary>
        /// <param name="itemType">The item's runtime type.</param>
        /// <returns>True when one does.</returns>
        public bool Handles(Type itemType)
        {
            return _realizers.ContainsKey(itemType);
        }

        /// <summary>Realises one item with the realizer of its exact runtime type.</summary>
        /// <param name="item">The item.</param>
        /// <param name="owner">The feature that contributed it.</param>
        /// <param name="context">The realisation's context.</param>
        /// <exception cref="InvalidOperationException">No realizer handles the item's type; the graph gate reports that before realisation.</exception>
        public void Realize(FeatureContributionItem item, ITellmaFeature owner, RealizationContext context)
        {
            // Base types are deliberately not consulted: an item kind a newer pack derives from an
            // older one carries meaning the older realizer cannot know, so it must surface as unknown.
            if (!_realizers.TryGetValue(item.GetType(), out Action<FeatureContributionItem, ITellmaFeature, RealizationContext>? realize))
            {
                throw new InvalidOperationException($"No realizer handles {TypeDisplay.Qualified(item.GetType())}.");
            }

            realize(item, owner, context);
        }
    }
}
