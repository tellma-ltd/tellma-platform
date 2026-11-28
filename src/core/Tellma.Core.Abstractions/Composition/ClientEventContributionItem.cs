// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

namespace Tellma.Core.Abstractions.Composition
{
    /// <summary>
    ///     A client event a feature registers, once, by the feature whose code publishes it. The
    ///     platform composes it into the client event registry in realisation order, and the startup
    ///     gate validates it.
    /// </summary>
    /// <param name="Name">The event's name, such as <c>inbox.changed</c>.</param>
    public sealed record ClientEventContributionItem(string Name) : FeatureContributionItem;
}
