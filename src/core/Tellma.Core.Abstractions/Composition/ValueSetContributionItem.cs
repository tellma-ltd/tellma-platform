// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Tellma.Core.Abstractions.Localization;

namespace Tellma.Core.Abstractions.Composition
{
    /// <summary>
    ///     A value set a feature contributes: a named enumeration over string properties, with its
    ///     values and their labels. The platform composes it into the value-set registry after its
    ///     own four sets, and the startup checks validate it.
    /// </summary>
    /// <param name="Descriptor">The set.</param>
    public sealed record ValueSetContributionItem(ValueSetDescriptor Descriptor) : FeatureContributionItem;
}
