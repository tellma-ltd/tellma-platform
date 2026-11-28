// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

namespace Tellma.Core.Abstractions.Composition
{
    /// <summary>
    ///     A routed service: a class marked <c>[ApiRoute]</c> whose <c>[ApiAction]</c> methods are
    ///     projected under its route segment. The platform resolves it from each request's scope and
    ///     registers the securable of each of its actions.
    /// </summary>
    /// <param name="ServiceType">The service class.</param>
    public sealed record ApiServiceContributionItem(Type ServiceType) : FeatureContributionItem;
}
