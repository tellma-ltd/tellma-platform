// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Tellma.Core.Abstractions.Access;

namespace Tellma.Core.Abstractions.Composition
{
    /// <summary>
    ///     Securables a feature registers by hand: those no stack declares, such as settings
    ///     categories and the pairs of hand-mapped endpoints. The platform runs
    ///     <paramref name="Configure" /> once, when the securable registry is composed, and attributes
    ///     what it adds to the contributing feature.
    /// </summary>
    /// <param name="Configure">Registers the securables.</param>
    public sealed record SecurablesContributionItem(Action<SecurableRegistryBuilder> Configure) : FeatureContributionItem;
}
