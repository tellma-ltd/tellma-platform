// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

namespace Tellma.Core.Abstractions.Composition
{
    /// <summary>
    ///     The base of every data-only item a feature contributes. The platform realises each item
    ///     through the realizer registered for the item's exact runtime type; an item that no realizer
    ///     handles is a composition problem naming the item type, which is what a pack built against a
    ///     newer platform than the one it runs on produces.
    /// </summary>
    /// <remarks>
    ///     Items are records so a composition can be compared, reflected over and, later, serialised
    ///     into a manifest. Each spec that introduces an item kind adds the record, its sugar on
    ///     <see cref="FeatureContribution" /> and its realizer together.
    /// </remarks>
    public abstract record FeatureContributionItem;
}
