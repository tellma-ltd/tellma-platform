// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Tellma.Core.Abstractions.Composition;

namespace Tellma.Core.Composition
{
    /// <summary>
    ///     Turns one kind of contribution item into registrations. The platform ships one realizer per
    ///     item type it understands, and finds the realizer for an item by the item's exact runtime
    ///     type: a realizer for a base record never handles a derived one.
    /// </summary>
    /// <remarks>
    ///     A realizer reports problems through <see cref="RealizationContext.Report" /> rather than
    ///     throwing, so every problem of a composition is found in one run.
    /// </remarks>
    /// <typeparam name="TItem">The item type realised.</typeparam>
    public interface IContributionRealizer<TItem>
        where TItem : FeatureContributionItem
    {
        /// <summary>Realises one item.</summary>
        /// <param name="item">The item.</param>
        /// <param name="owner">The feature that contributed it.</param>
        /// <param name="context">The realisation's services and problems.</param>
        void Realize(TItem item, ITellmaFeature owner, RealizationContext context);
    }
}
