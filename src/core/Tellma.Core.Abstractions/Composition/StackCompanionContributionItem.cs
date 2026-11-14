// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

namespace Tellma.Core.Abstractions.Composition
{
    /// <summary>
    ///     A stack companion: a class, generic over the entity, whose <c>[ApiAction]</c> methods are
    ///     projected under the stack's segment and resource exactly as the service's own. The platform
    ///     closes it over the stack's leaf and resolves it from each request's scope.
    /// </summary>
    /// <param name="EntityType">The entity whose stack the companion joins.</param>
    /// <param name="CompanionType">The companion class, closed over <paramref name="EntityType" />.</param>
    public sealed record StackCompanionContributionItem(Type EntityType, Type CompanionType) : FeatureContributionItem;
}
